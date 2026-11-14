using System.Collections.Generic;
using Game.Combat;
using Game.Core;
using Game.Player;
using UnityEngine;
using UnityEngine.UI;
namespace Game.Enemies
{
    // Sector 2 mothership. Big hull, telegraphed attacks, then a weak window.
    // Runs the mother ship boss movement, attack states, minion phases and health UI.
    public class MotherShipBoss : MonoBehaviour
    {
        // Represents the attack or recovery state currently selected by the boss.
        private enum Move
        {
            Intro = 0,
            AimedLaserWarn = 1,
            AimedLaserFire = 2,
            SweepWarn = 3,
            SweepFire = 4,
            Volley = 5,
            Minions = 6,
            Nova = 7,
            ChargeWarn = 8,
            Charge = 9,
            Down = 10,
            StageShift = 11
        }
        public const float OutgoingDamageScale = 0.55f;
        private readonly Game.AI.BossPlanner planner = new Game.AI.BossPlanner(234);
        private Vector3 previousPlayerPosition;
        private Vector3 playerVelocity;
        private bool hasPlayerSample;
        private static readonly Move[] PlannedMoves = { Move.AimedLaserWarn, Move.SweepWarn, Move.Minions, Move.ChargeWarn, Move.Nova, Move.Volley };
        private Health health;
        private Weapon weapon;
        private EnemyWaveSpawner spawner;
        private GameObject minionPrefab;
        private Transform player;
        private float strafePhase;
        private float weavePhase;
        private Move move;
        private float moveTime;
        private int patternStep = -1;
        private bool enraged;
        private bool shifting;
        private MeshRenderer[] hullParts;
        private Color[] hullColours;
        private Vector3 baseScale;
        private Vector3 beamDir;
        private Vector3 beamOrigin;
        private Vector3 sweepStart;
        private Vector3 sweepEnd;
        private Vector3 chargeTarget;
        private bool beamHit;
        private bool chargeHit;
        private Game.Campaign.BattleTimeline timeline;
        private readonly List<GameObject> escorts = new List<GameObject>();
        private LineRenderer beam;
        private Image bossFill;
        private RectTransform bossCap;
        private Text bossLabel;
        private float bossBarWidth = 640f;
        private Color hullColour = Color.white;
        private bool presentationTinted;
        private Color presentationTint;

        public Game.Net.BossPresentationState CapturePresentation()
        {
            return new Game.Net.BossPresentationState
            {
                Phase = (byte)move,
                Stage = (byte)(enraged ? 2 : 1),
                Invulnerable = health != null && health.invulnerable,
                BeamVisible = beam != null && beam.enabled,
                BeamFiring = move == Move.AimedLaserFire || move == Move.SweepFire,
                BeamLocked = move == Move.AimedLaserWarn && moveTime >= LaserTrackUntil(),
                BeamOrigin = beam != null && beam.enabled ? beam.GetPosition(0) : transform.position,
                BeamDirection = beamDir,
                HullTinted = presentationTinted,
                HullColor = presentationTint
            };
        }

        public float PlanarRange
        {
            get
            {
                if (move == Move.Charge)
                {
                    // Allows a slightly closer range during a ram so the charge can actually reach the player.
                    return ShipClearance.BossRamRange;
                }

                return ShipClearance.BossRange;
            }
        }

        public void Bind(GameObject prefab, EnemyWaveSpawner owner)
        {
            // Receives the wave spawner's fighter prefab so boss escorts use the same normal enemy setup.
            minionPrefab = prefab;
            spawner = owner;
        }

        private void Awake()
        {
            // Caches boss-specific systems and removes normal fighter AI from this upgraded enemy prefab.
            health = GetComponent<Health>();
            weapon = GetComponentInChildren<Weapon>();
            EnemyController fighter = GetComponent<EnemyController>();
            if (fighter != null)
            {
                // Disables and removes the basic enemy controller so only the boss state machine moves this ship.
                fighter.enabled = false;
                Destroy(fighter);
            }

            MeshRenderer hull = GetComponent<MeshRenderer>();
            if (hull != null && hull.material != null)
            {
                hullColour = hull.material.color;
            }

            CacheHull();

            // Saves original materials and scale so flashing and pulses can be restored after attacks.
            baseScale = transform.localScale;
        }

        private void CacheHull()
        {
            // Stores every child renderer colour because the boss may use several model pieces.
            hullParts = GetComponentsInChildren<MeshRenderer>(true);
            hullColours = new Color[hullParts.Length];
            int hullPartIndex;

            for (hullPartIndex = 0; hullPartIndex < hullParts.Length; hullPartIndex++)
            {
                // Records each original colour once so temporary warning flashes do not permanently recolour the model.
                if (hullParts[hullPartIndex] != null && hullParts[hullPartIndex].material != null)
                {
                    hullColours[hullPartIndex] = hullParts[hullPartIndex].material.color;
                    if (hullPartIndex == 0)
                    {
                        hullColour = hullColours[hullPartIndex];
                    }
                }
                else
                {
                    hullColours[hullPartIndex] = Color.white;
                }
            }
        }

        private void OnEnable()
        {
            Game.World.FloatingOrigin.Shifted += ShiftOrigin;

            // Listens for health changes only while active so the boss HUD and death logic stay synchronised.
            if (health != null)
            {
                health.Died += HandleDied;
                health.HealthChanged += HandleHealthChanged;
            }
        }

        private void OnDisable()
        {
            Game.World.FloatingOrigin.Shifted -= ShiftOrigin;
            if (timeline != null)
            {
                timeline.Clear();
            }

            if (health != null)
            {
                health.Died -= HandleDied;
                health.HealthChanged -= HandleHealthChanged;
            }
        }

        private void Start()
        {
            Game.Graphics.GraphicsSettingsManager.PrepareHierarchy(gameObject);

            // Starts in front of the player and randomises movement phases so encounters do not look identical.
            FindPlayer();
            strafePhase = Random.value * Mathf.PI * 2f;
            weavePhase = Random.value * Mathf.PI * 2f;
            if (player != null)
            {
                Vector3 spawn = player.position + Flatten(player.forward) * 36f;

                // Uses the player's flat forward direction so the boss appears ahead without inheriting camera pitch.
                spawn.y = player.position.y;
                transform.position = Game.World.SweptMovement.Resolve(spawn, 6);
            }

            BuildBeam();
            BuildHud();

            // Creates one reusable beam and health HUD before beginning the scripted attack sequence.
            Begin(Move.Intro);
        }

        private void ShiftOrigin(Vector3 offset)
        {
            previousPlayerPosition += offset;
            beamOrigin += offset;
            chargeTarget += offset;
        }

        private void Update()
        {
            // Advances the active boss state, movement and phase visuals while the boss is alive.
            if ((Game.Net.NetSession.Active && !Game.Net.NetSession.IsHost) || health == null || !health.IsAlive)
            {
                HideBeam();
                return;
            }

            FindPlayer();
            if (player != null)
            {
                playerVelocity = hasPlayerSample && Time.deltaTime > 0.0001f ? Vector3.ClampMagnitude((player.position - previousPlayerPosition) / Time.deltaTime, 50) : Vector3.zero;
                previousPlayerPosition = player.position;
                hasPlayerSample = true;
            }

            moveTime += Time.deltaTime;

            // Uses a seconds timer shared by every boss state for warnings, damage windows and exits.
            TickMove();
            if (move != Move.StageShift)
            {
                // Pauses normal movement during the phase transition so the visual change is clearly readable.
                ChasePlayer(Time.deltaTime);
                FaceForMove();
            }

            KeepClearOfPlayer();
            TickStageFlash();
        }

        private void TickMove()
        {
            // Each state owns its warning, damage and exit timing so attacks remain readable.
            if (move == Move.StageShift)
            {
                TickStageShift();
                return;
            }

            if (move == Move.Intro)
            {
                if (moveTime >= 1.8f)
                {
                    NextMove();
                }

                return;
            }

            if (move == Move.AimedLaserWarn)
            {
                // Tracks the player during the warning, then freezes aim shortly before the laser fires.
                float trackUntil = LaserTrackUntil();
                float fireAt = LaserFireAt();
                if (moveTime < trackUntil)
                {
                    float follow = 130f;
                    if (enraged)
                    {
                        // Raises tracking speed in phase two so the player has less time to stay still.
                        follow = 200f;
                    }

                    TrackBeam(follow);
                }

                ShowBeam(false);
                if (moveTime >= trackUntil)
                {
                    // Announces the locked aim point so players know the beam will no longer follow them.
                    SetBossLabel("LASER LOCK");
                }

                if (moveTime >= fireAt)
                {
                    Begin(Move.AimedLaserFire);
                }

                return;
            }

            if (move == Move.AimedLaserFire)
            {
                ShowBeam(true);
                TickBeamDamage();
                float laserTime = 1.25f;
                if (enraged)
                {
                    laserTime = 1.55f;
                }

                if (moveTime >= laserTime)
                {
                    HideBeam();
                    NextMove();
                }

                return;
            }

            if (move == Move.SweepWarn)
            {
                // Interpolates from one sweep edge to the other during the warning to show the upcoming path.
                float warn = 2.4f;
                if (enraged)
                {
                    warn = 1.55f;
                }

                float interpolationFraction = Mathf.Clamp01(moveTime / warn);
                beamDir = Vector3.Slerp(sweepStart, sweepEnd, interpolationFraction).normalized;

                // Slerp rotates smoothly between directions; normalising keeps beam length consistent.
                ShowBeam(false);
                if (moveTime >= warn)
                {
                    Begin(Move.SweepFire);
                }

                return;
            }

            if (move == Move.SweepFire)
            {
                float fire = 1.5f;
                if (enraged)
                {
                    fire = 1.15f;
                }

                float interpolationFraction2 = Mathf.Clamp01(moveTime / fire);
                beamDir = Vector3.Slerp(sweepStart, sweepEnd, interpolationFraction2).normalized;
                ShowBeam(true);
                TickBeamDamage();
                if (moveTime >= fire)
                {
                    HideBeam();
                    NextMove();
                }

                return;
            }

            if (move == Move.Volley)
            {
                // Barrage shots and their shared exit are dispatched by the battle timeline.
                return;
            }

            if (move == Move.Minions)
            {
                // Keeps the boss invulnerable while active escorts remain, creating a required target switch.
                if (EscortsAlive())
                {
                    SetBossLabel("DESTROY ESCORTS");
                    float pulse = 0.45f + Mathf.Abs(Mathf.Sin(Time.time * 6f)) * 0.55f;

                    // Uses sine to pulse between dim and bright blue without abrupt colour changes.
                    PulseHull(new Color(0.25f, 0.55f * pulse, 1f));
                    return;
                }

                if (health != null)
                {
                    health.invulnerable = false;
                }

                RestoreHull();
                NextMove();
                return;
            }

            if (move == Move.Nova)
            {
                return; // Scheduled detonation and recovery.

            } // Scheduled detonation and recovery.

            if (move == Move.ChargeWarn)
            {
                PulseHull(new Color(1f, 0.35f, 0.2f));
                float warn = 1.05f;
                if (enraged)
                {
                    warn = 0.65f;
                }

                if (moveTime >= warn)
                {
                    Begin(Move.Charge);
                }

                return;
            }

            if (move == Move.Charge)
            {
                // Moves directly toward the locked target for a short high-speed ram attack.
                float ramSpeed = 38f;
                float ramTime = 1.15f;
                if (enraged)
                {
                    ramSpeed = 56f;
                    ramTime = 0.95f;
                }

                Game.World.SweptMovement.Move(transform, Vector3.MoveTowards(
                    transform.position, chargeTarget, ramSpeed * Time.deltaTime) - transform.position, 6);

                // Multiplies ram speed by delta time so the charge covers the same distance at any frame rate.
                TickChargeDamage();
                if (moveTime >= ramTime || (transform.position - chargeTarget).sqrMagnitude < 0.4f)
                {
                    RestoreHull();
                    NextMove();
                }

                return;
            }

            if (move == Move.Down)
            {
                // Opens a limited double-damage window after an attack sequence to reward player timing.
                float pulse = 0.55f + Mathf.Abs(Mathf.Sin(Time.time * 8f)) * 0.45f;
                PulseHull(new Color(1f, 0.82f * pulse, 0.2f));
                SetBossLabel("OVERLOAD  -  DOUBLE DAMAGE");
                if (moveTime >= 2.4f)
                {
                    health.incomingDamageMul = BaseIncoming();
                    RestoreHull();
                    NextMove();
                }
            }
        }

        private void Begin(Move next)
        {
            if (timeline == null)
            {
                timeline = Game.Campaign.BattleTimeline.For(gameObject);
            }

            timeline.Clear();

            // Clears values left by the last attack before setting up the newly selected state.
            move = next;
            moveTime = 0f;
            beamHit = false;
            chargeHit = false;
            HideBeam();
            RestoreHull();
            if (health != null)
            {
                // Restores normal vulnerability first; special states below can then override it deliberately.
                health.incomingDamageMul = BaseIncoming();
                health.invulnerable = false;
            }

            if (next == Move.StageShift)
            {
                // Locks damage while the half-health transition flashes and prepares phase two.
                shifting = true;
                if (health != null)
                {
                    health.invulnerable = true;
                    health.incomingDamageMul = 0f;
                }

                SetBossLabel("CORE OVERLOAD");
                timeline.After(1.15f, () =>
                {
                    if (AttackActive(Move.StageShift))
                    {
                        FireNova();
                    }
                });
            }
            else if (next == Move.AimedLaserWarn)
            {
                LockBeamAtPlayer();
                SetBossLabel("CHARGING LASER");
            }
            else if (next == Move.AimedLaserFire)
            {
                SetBossLabel("LASER");
            }
            else if (next == Move.SweepWarn)
            {
                LockSweep();
                SetBossLabel("SWEEP LASER");
            }
            else if (next == Move.SweepFire)
            {
                beamHit = false;
                SetBossLabel("SWEEP LASER");
            }
            else if (next == Move.Volley)
            {
                SetBossLabel("BARRAGE");
                timeline.After(0, () =>
                {
                    if (AttackActive(Move.Volley))
                    {
                        FireFan();
                    }
                });
                if (enraged)
                {
                    timeline.After(0.32f, () =>
                    {
                        if (AttackActive(Move.Volley))
                        {
                            FireFan();
                        }
                    });
                }

                timeline.After(enraged ? 0.7f : 0.9f, () =>
                {
                    if (AttackActive(Move.Volley))
                    {
                        NextMove();
                    }
                });
            }
            else if (next == Move.Minions)
            {
                // Spawns escorts first, then protects the boss only when at least one escort was created.
                SetBossLabel("LAUNCHING ESCORTS");
                SpawnMinions();
                timeline.After(SprayGap(), CoordinatedEscortFire);
                if (health != null && EscortsAlive())
                {
                    health.invulnerable = true;
                    SetBossLabel("DESTROY ESCORTS");
                }
            }
            else if (next == Move.Nova)
            {
                SetBossLabel("NOVA CHARGING");
                timeline.After(0.45f, () =>
                {
                    if (AttackActive(Move.Nova))
                    {
                        FireNova();
                        SetBossLabel("NOVA");
                    }
                });
                timeline.After(1.2f, () =>
                {
                    if (AttackActive(Move.Nova))
                    {
                        NextMove();
                    }
                });
            }
            else if (next == Move.ChargeWarn)
            {
                LockCharge();
                SetBossLabel("RAMMING");
            }
            else if (next == Move.Down)
            {
                // Doubles all incoming damage through Health so every player attack benefits from the weak window.
                if (health != null)
                {
                    health.incomingDamageMul = 2f;
                }

                SetBossLabel("OVERLOAD  -  DOUBLE DAMAGE");
            }
            else if (next == Move.Intro)
            {
                SetBossLabel("MOTHERSHIP");
            }
        }

        private bool AttackActive(Move expected) => this != null && isActiveAndEnabled && health != null && health.IsAlive && move == expected && (!Game.Net.NetSession.Active || Game.Net.NetSession.IsHost);

        private void CoordinatedEscortFire()
        {
            if (!AttackActive(Move.Minions) || !EscortsAlive())
            {
                return;
            }

            FireFan();
            timeline.After(SprayGap(), CoordinatedEscortFire);
        }

        private void NextMove()
        {
            patternStep++;

            // A guaranteed recovery window remains readable even when planning favors pressure.
            if (patternStep % 4 == 3)
            {
                Begin(Move.Down);
                return;
            }

            Vector3 offset = player != null ? player.position - transform.position : Vector3.forward * 30;
            float lateral = Vector3.Dot(playerVelocity, Vector3.Cross(Vector3.up, offset.normalized));
            int choice = planner.Choose(offset.magnitude, lateral, health != null ? health.CurrentHealth / Mathf.Max(1, health.maxHealth) : 1, enraged);
            Begin(PlannedMoves[choice]);
        }

        // Same idea as regular fighters: close in, strafe, and match altitude.
        private void ChasePlayer(float deltaTime)
        {
            if (player == null || move == Move.Charge || move == Move.StageShift)
            {
                return;
            }

            if (move == Move.Minions)
            {
                RetreatBehindEscorts(deltaTime);
                return;
            }

            Vector3 toTarget = player.position - transform.position;
            toTarget.y = 0f;

            // Uses flat distance for preferred range so vertical dodging does not change horizontal pursuit.
            float distance = toTarget.magnitude;
            Vector3 flatForward = transform.forward;
            flatForward.y = 0f;
            if (distance > 0.01f)
            {
                flatForward = toTarget / distance;
            }

            float hold = 26f;
            float advance = 11f;
            float vertical = 8f;
            float strafe = 7f;
            float strafeSpin = 0.28f;
            float weaveSpin = 0.4f;
            if (enraged)
            {
                // Makes phase two faster and more aggressive by reducing hold range and increasing weave values.
                hold = 22f;
                advance = 20f;
                vertical = 14f;
                strafe = 12f;
                strafeSpin = 0.55f;
                weaveSpin = 0.7f;
            }

            Vector3 velocity = Vector3.zero;
            if (distance > hold)
            {
                // Closes the gap when the player is outside the preferred firing distance.
                velocity += flatForward * advance;
            }
            else if (distance < hold - 4f)
            {
                // Backs away inside a small buffer so the boss does not sit directly on the player model.
                velocity -= flatForward * (advance * 0.45f);
            }

            strafePhase += strafeSpin * Mathf.PI * 2f * deltaTime;

            // Advances a cosine cycle used to create smooth side-to-side strafing.
            velocity += Vector3.Cross(Vector3.up, flatForward) * (Mathf.Cos(strafePhase) * strafe);
            Vector3 position = transform.position + velocity * deltaTime;
            weavePhase += weaveSpin * Mathf.PI * 2f * deltaTime;
            float desiredHeight = player.position.y + Mathf.Sin(weavePhase) * 2.4f;

            // Adds a sine-wave offset around player height so the boss is harder to track vertically.
            position.y = Mathf.MoveTowards(position.y, desiredHeight, vertical * deltaTime);
            Game.World.SweptMovement.Move(transform, position - transform.position, 6);
        }

        private void RetreatBehindEscorts(float deltaTime)
        {
            // Hold beyond the party's occupied radius, keeping the invulnerable hull
            // behind the fighters even when another pilot approaches from the side.
            Vector3 center = Vector3.zero;
            int count = 0;

            for (int pilotSlot = 0; pilotSlot < Game.Net.PlayerRoster.Count; pilotSlot++)
            {
                Transform ship = Game.Net.PlayerRoster.At(pilotSlot);
                if (!LivingShip(ship))
                {
                    continue;
                }

                center += ship.position;
                count++;
            }

            center = count > 0 ? center / count : player.position;
            float partyRadius = 0f;

            for (int checkedPilotSlot = 0; checkedPilotSlot < Game.Net.PlayerRoster.Count; checkedPilotSlot++)
            {
                Transform ship = Game.Net.PlayerRoster.At(checkedPilotSlot);
                if (LivingShip(ship))
                {
                    partyRadius = Mathf.Max(partyRadius, Flatten(ship.position - center).magnitude);
                }
            }

            Vector3 away = Flatten(transform.position - center);
            if (away.sqrMagnitude < 0.01f)
            {
                away = -Flatten(player.forward);
            }

            if (away.sqrMagnitude < 0.01f)
            {
                away = Vector3.forward;
            }

            Vector3 destination = center + away.normalized * (64f + partyRadius) + Vector3.up * 12f;

            // Do not increase PlanarRange: collision separation would teleport the pilot.
            Game.World.SweptMovement.Move(transform, Vector3.MoveTowards(transform.position, destination, 36f * Mathf.Max(0f, deltaTime)) - transform.position, 6);
        }

        private static bool LivingShip(Transform ship)
        {
            if (ship == null || !ship.gameObject.activeInHierarchy)
            {
                return false;
            }

            Health shipHealth = ship.GetComponent<Health>();
            return shipHealth == null || shipHealth.IsAlive;
        }

        private void KeepClearOfPlayer()
        {
            // Applies the shared planar clearance rule after movement so the boss cannot overlap a player hull.
            if (player == null || move == Move.StageShift)
            {
                return;
            }

            ShipClearance.PushAway(transform, player, PlanarRange, ShipClearance.BossHeight);
        }

        private void FaceForMove()
        {
            // Rotates only during normal movement; warning and firing states preserve their locked attack aim.
            if (player == null)
            {
                return;
            }

            if (move == Move.Charge || move == Move.AimedLaserFire || move == Move.StageShift)
            {
                return;
            }

            if (move == Move.AimedLaserWarn && moveTime >= LaserTrackUntil())
            {
                return;
            }

            Vector3 destination = Flatten(player.position - transform.position);
            if (destination.sqrMagnitude < 0.01f)
            {
                return;
            }

            Quaternion want = Quaternion.LookRotation(destination.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-3.5f * Time.deltaTime));

            // Exponential smoothing gives a stable turn speed without frame-rate dependent snapping.
        }

        private void LockBeamAtPlayer()
        {
            // Saves the player's current direction at the start of a laser warning for the initial beam aim.
            beamOrigin = Nose();
            Vector3 aimDirection = PlayerAimPoint() - beamOrigin;
            if (aimDirection.sqrMagnitude < 0.01f)
            {
                aimDirection = transform.forward;
            }

            beamDir = aimDirection.normalized;
        }

        private void TrackBeam(float degreesPerSecond)
        {
            // Rotates toward the player at a capped rate so the warning laser can still be dodged.
            beamOrigin = Nose();
            Vector3 want = PlayerAimPoint() - beamOrigin;
            if (want.sqrMagnitude < 0.01f)
            {
                return;
            }

            want.Normalize();
            if (beamDir.sqrMagnitude < 0.01f)
            {
                beamDir = want;
                return;
            }

            beamDir = Vector3.RotateTowards(beamDir, want, degreesPerSecond * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;
        }

        private void LockSweep()
        {
            // Builds two directions either side of the player to define a sweep laser arc.
            beamOrigin = Nose();
            Vector3 aimDirection = PlayerAimPoint() - beamOrigin;
            if (aimDirection.sqrMagnitude < 0.01f)
            {
                aimDirection = transform.forward;
            }

            aimDirection.Normalize();
            Vector3 axis = Vector3.Cross(aimDirection, Vector3.up);

            // The cross product creates an axis perpendicular to the aim direction for rotating the sweep.
            if (axis.sqrMagnitude < 0.01f)
            {
                axis = Vector3.right;
            }

            axis.Normalize();
            float span = 42f;
            if (enraged)
            {
                span = 55f;
            }

            sweepStart = Quaternion.AngleAxis(-span, axis) * aimDirection;
            sweepEnd = Quaternion.AngleAxis(span, axis) * aimDirection;

            // Rotates the initial aim in both directions to create the two sweep endpoints.
            beamDir = sweepStart;
        }

        private void LockCharge()
        {
            // Locks a target beyond the player so the boss charges through their current location.
            Vector3 aimDirection = PlayerAimPoint();
            Vector3 direction = aimDirection - transform.position;
            if (direction.sqrMagnitude < 0.01f)
            {
                direction = transform.forward;
            }

            direction.Normalize();
            chargeTarget = aimDirection + direction * 16f;

            // Extending past the player stops the ram from ending exactly on top of their ship.
        }

        private void TickBeamDamage()
        {
            // Checks each player against the beam line and applies damage only during the firing state.
            if (beamHit)
            {
                return;
            }

            beamOrigin = Nose();
            RaycastHit hit;
            float radius = 2.1f;
            if (!Physics.SphereCast(
                beamOrigin,
                radius,
                beamDir,
                out hit,
                90f,
                GameLayers.EnemyWeaponHitMask,
                QueryTriggerInteraction.Ignore))
            {
                // Casts a thick beam path so close calls still count as a dangerous laser hit.
                return;
            }

            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();

            // Reads damage from the ship root because the hit collider may belong to a child model piece.
            if (target == null || !target.IsAlive)
            {
                return;
            }

            if (hit.collider.GetComponentInParent<MotherShipBoss>() != null)
            {
                return;
            }

            target.ApplyDamage(new DamageInfo(LaserDamage(), DamageSource.EnemyProjectile, gameObject, hit.point, -beamDir));

            // Marks this laser state as spent so a continuous beam cannot damage every frame.
            beamHit = true;
        }

        private void TickChargeDamage()
        {
            // Allows at most one ram hit and checks the player's planar clearance cylinder during the charge.
            if (chargeHit || player == null)
            {
                return;
            }

            if (!ShipClearance.PlanarReach(
                player.position,
                transform.position,
                ShipClearance.BossRamRange + 1f,
                ShipClearance.BossHeight))
            {
                return;
            }

            Health playerHealth = player.GetComponent<Health>();
            if (playerHealth == null || !playerHealth.IsAlive)
            {
                return;
            }

            playerHealth.ApplyDamage(new DamageInfo(
                RamDamage(), DamageSource.EnemyProjectile, gameObject, player.position, Vector3.up));

            // Uses normal Health damage handling so shields and death rules also apply to a ram.
            chargeHit = true;
        }

        private void FireFan()
        {
            // Creates a predictable projectile fan that the player can weave through.
            if (weapon == null)
            {
                return;
            }

            AimMuzzleAtPlayer();
            int shots = 4;
            float spread = 28f;
            if (enraged)
            {
                // Increases both shot count and cone width in stage two, making the barrage harder to dodge.
                shots = 7;
                spread = 40f;
            }

            float oldMul = weapon.damageMul;
            weapon.damageMul = 0.5f * OutgoingDamageScale;

            // Temporarily reduces barrage damage, then restores the original multiplier after firing.
            weapon.FireSpray(shots, spread);
            weapon.damageMul = oldMul;
        }

        private void FireNova()
        {
            // Launches a radial projectile pattern across several pitch angles to fill three-dimensional space.
            if (weapon == null || weapon.definition == null || weapon.definition.projectilePrefab == null)
            {
                return;
            }

            ProjectilePool pool = ProjectilePool.Instance;
            if (pool == null)
            {
                return;
            }

            int count = 14;
            if (enraged)
            {
                count = 22;
            }

            Vector3 origin = Nose();
            ProjectileLaunchData data = new ProjectileLaunchData(
                weapon.definition.projectileSpeed * 0.85f,
                weapon.definition.damagePerProjectile * OutgoingDamageScale,
                weapon.definition.projectileRadius,
                weapon.definition.projectileLifetime,
                DamageSource.EnemyProjectile,
                gameObject,
                GameLayers.EnemyWeaponHitMask);
            float[] pitches = { -35f, 0f, 35f };

            for (int pitchIndex = 0; pitchIndex < pitches.Length; pitchIndex++)
            {
                // Creates a full horizontal ring for each vertical angle, producing the nova shell.
                for (int projectileIndex = 0; projectileIndex < count; projectileIndex++)
                {
                    float yawDegrees = 360f / count * projectileIndex;

                    // Divides the full circle into equal angles so the ring has evenly spaced projectiles.
                    Vector3 direction = Quaternion.Euler(pitches[pitchIndex], yawDegrees, 0f) * Vector3.forward;
                    Projectile shot = pool.Get(weapon.definition.projectilePrefab);
                    if (shot != null)
                    {
                        shot.Launch(origin, direction, data, pool);
                        Game.Net.NetRunner.Instance?.NoteFire(origin, direction, data.Source, shot);
                    }
                }
            }
        }

        private void SpawnMinions()
        {
            // Spawns escorts around the boss and locks its health behind a minion-clear phase.
            if (minionPrefab == null)
            {
                return;
            }

            int count = 3 * GameSession.PartySize;

            // Scales escort count by lobby size so additional players still have meaningful pressure.
            if (enraged)
            {
                count = 6 * GameSession.PartySize;
            }

            Vector3 anchor = player != null ? player.position : transform.position + Flatten(transform.forward) * 26f;
            Vector3 towardBoss = Flatten(transform.position - anchor);
            float bossDistance = towardBoss.magnitude;
            if (bossDistance < 0.01f)
            {
                towardBoss = -Flatten(transform.forward);
            }

            if (towardBoss.sqrMagnitude < 0.01f)
            {
                towardBoss = Vector3.forward;
            }

            towardBoss.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, towardBoss);
            float foregroundDistance = Mathf.Clamp(bossDistance - 16f, 12f, 28f);

            for (int escortIndex = 0; escortIndex < count; escortIndex++)
            {
                float side = -1f + 2f * escortIndex / Mathf.Max(1, count - 1);

                // Maps each index from -1 to 1 so escorts spread evenly across the boss's width.
                Vector3 position2 = anchor + towardBoss * foregroundDistance + right * (side * Mathf.Min(14f, count * 2.5f));
                position2.y = anchor.y + Random.Range(-1.5f, 1.5f);
                GameObject minion = Instantiate(minionPrefab, position2, transform.rotation);
                minion.name = "Escort_" + (escortIndex + 1);
                Weapon escortWeapon = minion.GetComponentInChildren<Weapon>();
                if (escortWeapon != null)
                {
                    escortWeapon.damageMul *= OutgoingDamageScale;
                }

                escorts.Add(minion);
                if (spawner != null)
                {
                    spawner.TrackEnemy(minion);
                }
            }
        }

        private bool EscortsAlive()
        {
            // Removes dead or inactive entries while scanning so the list remains a reliable shield check.
            for (int entryIndex = escorts.Count - 1; entryIndex >= 0; entryIndex--)
            {
                GameObject escort = escorts[entryIndex];
                if (escort == null || !escort.activeSelf)
                {
                    escorts.RemoveAt(entryIndex);
                }
            }

            return escorts.Count > 0;
        }

        private void AimMuzzleAtPlayer()
        {
            // Turns only the muzzle toward the target because the boss hull can be moving independently.
            if (weapon == null || weapon.muzzle == null || player == null)
            {
                return;
            }

            Vector3 destination = PlayerAimPoint() - weapon.muzzle.position;
            if (destination.sqrMagnitude < 0.0001f)
            {
                return;
            }

            weapon.muzzle.rotation = Quaternion.LookRotation(destination.normalized, Vector3.up);
        }

        private Vector3 Nose()
        {
            // Uses mesh bounds and world scale to place lasers and novas at the visible front of the boss.
            MeshFilter filter = GetComponent<MeshFilter>();
            float distance = 3f;
            if (filter != null && filter.sharedMesh != null)
            {
                float scaleZ = Mathf.Abs(transform.lossyScale.z);
                if (scaleZ < 0.0001f)
                {
                    scaleZ = 1f;
                }

                distance = filter.sharedMesh.bounds.max.z * scaleZ + 0.6f;
            }

            return transform.position + transform.forward * distance;
        }

        private Vector3 PlayerAimPoint()
        {
            // Uses a point ahead of the boss if no player exists, keeping attack calculations safe during cleanup.
            if (player == null)
            {
                return transform.position + transform.forward * 20f;
            }

            return player.position;
        }

        private void ShowBeam(bool firing)
        {
            // Positions a two-point line from the boss nose to its current beam direction.
            if (beam == null)
            {
                return;
            }

            beam.enabled = true;
            beamOrigin = Nose();
            Vector3 end = beamOrigin + beamDir.normalized * 90f;
            beam.SetPosition(0, beamOrigin);
            beam.SetPosition(1, end);
            if (firing)
            {
                // Makes the damaging beam wide and red so it is clearly different from its warning state.
                beam.startWidth = 2.4f;
                beam.endWidth = 2.8f;
                beam.startColor = new Color(1f, 0.12f, 0.08f, 1f);
                beam.endColor = new Color(1f, 0.45f, 0.15f, 0.85f);
            }
            else
            {
                bool locked = move == Move.AimedLaserWarn && moveTime >= LaserTrackUntil();

                // Changes warning colour once tracking has stopped to show the player the aim is now fixed.
                if (locked)
                {
                    beam.startWidth = 0.9f;
                    beam.endWidth = 1.2f;
                    beam.startColor = new Color(1f, 1f, 0.85f, 1f);
                    beam.endColor = new Color(1f, 0.25f, 0.1f, 0.95f);
                }
                else
                {
                    float pulse = 0.35f + Mathf.Abs(Mathf.Sin(Time.time * 10f)) * 0.65f;

                    // Pulses warning alpha with sine so the charge-up feels active without changing gameplay.
                    beam.startWidth = 0.35f;
                    beam.endWidth = 0.7f;
                    beam.startColor = new Color(1f, 0.82f, 0.15f, pulse);
                    beam.endColor = new Color(1f, 0.35f, 0.1f, pulse * 0.7f);
                }
            }
        }

        private void HideBeam()
        {
            if (beam != null)
            {
                beam.enabled = false;
            }
        }

        private void BuildBeam()
        {
            // Creates one LineRenderer and disables shadows because the laser is a screen effect, not geometry.
            GameObject host = new GameObject("BossLaser");
            host.transform.SetParent(transform, false);
            beam = host.AddComponent<LineRenderer>();
            beam.enabled = false;
            beam.positionCount = 2;
            beam.useWorldSpace = true;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.material = VisualUtil.MakeAdditive(new Color(1f, 0.2f, 0.1f, 1f));
            beam.numCapVertices = 4;
        }

        private void BuildHud()
        {
            // The local flight HUD presents both authoritative and network boss state.
            if (FindObjectOfType<GameHud>() != null)
            {
                return;
            }

            // Builds the boss health UI in code so every boss prefab receives the same screen-space HUD.
            GameObject canvasObject = new GameObject("BossHud");
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            GameObject rootObject = new GameObject("BossBar");
            rootObject.transform.SetParent(canvasObject.transform, false);
            RectTransform root = rootObject.AddComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 1f);
            root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -18f);
            root.sizeDelta = new Vector2(980f, 110f);
            Image plate = UiChrome.MakeBox(root, "Plate", UiChrome.MetalDark);
            plate.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            plate.rectTransform.sizeDelta = new Vector2(320f, 34f);
            Image leftBracket = UiChrome.MakeBox(root, "LeftBracket", UiChrome.NeonRed);
            leftBracket.rectTransform.anchoredPosition = new Vector2(-186f, -8f);
            leftBracket.rectTransform.sizeDelta = new Vector2(18f, 22f);
            leftBracket.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 18f);
            Image rightBracket = UiChrome.MakeBox(root, "RightBracket", UiChrome.NeonRed);
            rightBracket.rectTransform.anchoredPosition = new Vector2(186f, -8f);
            rightBracket.rectTransform.sizeDelta = new Vector2(18f, 22f);
            rightBracket.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -18f);
            bossLabel = UiChrome.MakeText(plate.transform, "Label", "MOTHERSHIP", 22, TextAnchor.MiddleCenter, UiChrome.Gold);
            UiChrome.Stretch(bossLabel.rectTransform);
            Image leftCap = UiChrome.MakeBox(root, "LeftCap", UiChrome.Metal);
            leftCap.rectTransform.anchoredPosition = new Vector2(-430f, -58f);
            leftCap.rectTransform.sizeDelta = new Vector2(54f, 46f);
            Image leftGlow = UiChrome.MakeBox(leftCap.transform, "Glow", UiChrome.NeonRed);
            UiChrome.Stretch(leftGlow.rectTransform);
            leftGlow.rectTransform.offsetMin = new Vector2(6f, 6f);
            leftGlow.rectTransform.offsetMax = new Vector2(-32f, -6f);
            Image rightCap = UiChrome.MakeBox(root, "RightCap", UiChrome.Metal);
            rightCap.rectTransform.anchoredPosition = new Vector2(430f, -58f);
            rightCap.rectTransform.sizeDelta = new Vector2(54f, 46f);
            Image rightGlow = UiChrome.MakeBox(rightCap.transform, "Glow", UiChrome.NeonRed);
            UiChrome.Stretch(rightGlow.rectTransform);
            rightGlow.rectTransform.offsetMin = new Vector2(32f, 6f);
            rightGlow.rectTransform.offsetMax = new Vector2(-6f, -6f);
            Image frame = UiChrome.MakeBox(root, "Frame", UiChrome.MetalDark);
            frame.rectTransform.anchoredPosition = new Vector2(0f, -58f);
            frame.rectTransform.sizeDelta = new Vector2(780f, 28f);
            Image well = UiChrome.MakeBox(frame.transform, "Well", UiChrome.Ink);
            UiChrome.Stretch(well.rectTransform);
            well.rectTransform.offsetMin = new Vector2(8f, 4f);
            well.rectTransform.offsetMax = new Vector2(-8f, -4f);
            bossBarWidth = 760f;
            bossFill = UiChrome.MakeImage(well.transform, "Fill", UiChrome.Slant(), UiChrome.NeonRed);
            bossFill.type = Image.Type.Filled;
            bossFill.fillMethod = Image.FillMethod.Horizontal;
            bossFill.fillOrigin = 0;
            bossFill.fillAmount = 1f;

            // Uses Unity's horizontal filled image mode so health can update without resizing layout objects.
            UiChrome.Stretch(bossFill.rectTransform);
            bossFill.rectTransform.offsetMin = new Vector2(2f, 2f);
            bossFill.rectTransform.offsetMax = new Vector2(-2f, -2f);
            Image image = UiChrome.MakeImage(well.transform, "Cap", UiChrome.Slant(), Color.white);
            bossCap = image.rectTransform;
            bossCap.anchorMin = new Vector2(0f, 0.5f);
            bossCap.anchorMax = new Vector2(0f, 0.5f);
            bossCap.pivot = new Vector2(0.5f, 0.5f);
            bossCap.sizeDelta = new Vector2(16f, 22f);
            Image tick = UiChrome.MakeImage(root, "Tick", UiChrome.Chevron(), UiChrome.NeonRed);
            tick.rectTransform.anchoredPosition = new Vector2(0f, -84f);
            tick.rectTransform.sizeDelta = new Vector2(22f, 16f);
            if (health != null)
            {
                HandleHealthChanged(health.CurrentHealth, health.maxHealth);
            }
        }

        private void SetBossLabel(string text)
        {
            if (bossLabel != null)
            {
                bossLabel.text = text;
            }
        }

        private void HandleHealthChanged(float current, float max)
        {
            // Converts health into a 0-to-1 fraction used by the filled UI image and moving health cap.
            if (bossFill != null && max > 0f)
            {
                float healthFraction = Mathf.Clamp01(current / max);
                bossFill.fillAmount = healthFraction;
                if (bossCap != null)
                {
                    bossCap.anchoredPosition = new Vector2(8f + healthFraction * bossBarWidth, 0f);
                    bossCap.gameObject.SetActive(healthFraction > 0.02f && healthFraction < 0.98f);
                }
            }

            if (!enraged && !shifting && health != null && health.IsAlive && max > 0f && current / max <= 0.5f)
            {
                // Starts stage two once at half health and blocks repeated transition calls with state flags.
                Begin(Move.StageShift);
            }
        }

        private void HandleDied(Health source, DamageInfo info)
        {
            if (timeline != null)
            {
                timeline.Clear();
            }

            // Clears temporary visuals and awards boss XP to the player who supplied the killing blow.
            HideBeam();
            RestoreHull();
            transform.localScale = baseScale;
            SetBossLabel("MOTHERSHIP DESTROYED");
            if (health != null)
            {
                health.incomingDamageMul = 1f;
            }

            if (Game.Net.NetSession.Active && Game.Net.NetSession.IsHost && Game.Net.NetRunner.Instance != null)
            {
                Game.Net.NetRunner.Instance.AwardKill(info.Instigator, 120, false);
            }
            else if (info.Instigator != null)
            {
                PlayerProgression progression = info.Instigator.GetComponentInParent<PlayerProgression>();
                if (progression != null)
                {
                    progression.AddXp(120);
                }
            }
        }

        private void PulseHull(Color colour)
        {
            presentationTinted = true;
            presentationTint = colour;

            // Applies a temporary warning colour to every saved hull renderer.
            if (hullParts == null)
            {
                return;
            }

            int hullPartIndex;

            for (hullPartIndex = 0; hullPartIndex < hullParts.Length; hullPartIndex++)
            {
                if (hullParts[hullPartIndex] != null && hullParts[hullPartIndex].material != null)
                {
                    hullParts[hullPartIndex].material.color = colour;
                }
            }
        }

        private void RestoreHull()
        {
            presentationTinted = false;

            // Restores each renderer's original material colour after a warning or phase flash.
            if (hullParts == null)
            {
                return;
            }

            int hullPartIndex;

            for (hullPartIndex = 0; hullPartIndex < hullParts.Length; hullPartIndex++)
            {
                if (hullParts[hullPartIndex] != null && hullParts[hullPartIndex].material != null)
                {
                    hullParts[hullPartIndex].material.color = hullColours[hullPartIndex];
                }
            }
        }

        private void TickStageShift()
        {
            // Runs the protected half-health transformation before enabling the stronger stage-two pattern.
            if (health != null)
            {
                health.invulnerable = true;
            }

            float flash = Mathf.PingPong(Time.time * 22f, 1f);

            // PingPong repeatedly moves from 0 to 1, creating alternating white and dark phase flashes.
            if (flash > 0.5f)
            {
                PulseHull(Color.white);
            }
            else
            {
                PulseHull(new Color(0.35f, 0.35f, 0.4f, 1f));
            }

            float swell = 1f + Mathf.Abs(Mathf.Sin(moveTime * 16f)) * 0.08f;

            // Pulses scale by a small amount to make the transition feel like the core is overloading.
            transform.localScale = baseScale * swell;
            SetBossLabel("CORE OVERLOAD");
            if (moveTime < 2.35f)
            {
                return;
            }

            transform.localScale = baseScale;
            RestoreHull();
            shifting = false;
            enraged = true;

            // Switches all later pattern, damage and movement checks to their stage-two values.
            if (health != null)
            {
                health.invulnerable = false;
                health.incomingDamageMul = BaseIncoming();
            }

            if (bossFill != null)
            {
                bossFill.color = new Color(1f, 0.42f, 0.08f, 1f);
            }

            if (bossLabel != null)
            {
                bossLabel.color = UiChrome.NeonRed;
            }

            SetBossLabel("STAGE 2");
            FireNova();
            NextMove();
        }

        private void TickStageFlash()
        {
            // Adds a lightweight phase-two flash outside states that already own a special hull colour.
            if (!enraged || shifting)
            {
                return;
            }

            if (move == Move.ChargeWarn || move == Move.Down || move == Move.Minions || move == Move.StageShift)
            {
                return;
            }

            float flash = Mathf.PingPong(Time.time * 7f, 1f);
            if (flash > 0.55f)
            {
                PulseHull(Color.white);
            }
            else
            {
                RestoreHull();
            }
        }

        private float LaserTrackUntil()
        {
            // Returns an earlier lock time in phase two, shortening the safe reaction window.
            if (enraged)
            {
                return 1.4f;
            }

            return 2.1f;
        }

        private float LaserFireAt()
        {
            if (enraged)
            {
                return 1.8f;
            }

            return 2.6f;
        }

        private float SprayGap()
        {
            if (enraged)
            {
                return 1.5f;
            }

            return 2.4f;
        }

        private float LaserDamage()
        {
            if (enraged)
            {
                return 3f * OutgoingDamageScale;
            }

            return 2f * OutgoingDamageScale;
        }

        private float RamDamage()
        {
            if (enraged)
            {
                return 2f * OutgoingDamageScale;
            }

            return 1f * OutgoingDamageScale;
        }

        private float BaseIncoming()
        {
            // Gives stage two a small permanent damage resistance after its transition.
            if (enraged)
            {
                return 0.8f;
            }

            return 1f;
        }

        private void FindPlayer()
        {
            // Targets the nearest roster ship in multiplayer, then supports the normal tagged player fallback.
            Transform roster = Game.Net.PlayerRoster.Nearest(transform.position);
            if (roster != null)
            {
                player = roster;
                return;
            }

            GameObject found = GameObject.FindWithTag("Player");
            if (found != null)
            {
                player = found.transform;
            }
        }

        private static Vector3 Flatten(Vector3 value)
        {
            // Removes vertical height when a calculation needs a direction on the main combat plane.
            value.y = 0f;
            return value;
        }

        private void OnDestroy()
        {
            HideBeam();
        }
    }
}
