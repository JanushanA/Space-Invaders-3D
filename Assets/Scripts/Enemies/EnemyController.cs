using Game.Combat;
using Game.Core;
using Game.Player;
using UnityEngine;
namespace Game.Enemies
{
    // Closes on the player, weaves, matches altitude and shoots.
    [RequireComponent(typeof(Health))]

    // Controls a fighter enemy approaching, strafing, moving vertically, aiming and firing.
    public class EnemyController : MonoBehaviour
    {
        // Selects how an enemy changes height while it attacks the player.
        public enum VerticalBehaviour
        {
            Hold = 0,
            MatchTarget = 1,
            Weave = 2,
            MatchAndWeave = 3
        }
        public float stoppingDistance = 13f;
        public float minPlanarRange = 12f;
        public float advanceSpeed = 3f;
        public float strafeDistance = 2.5f;
        public float strafeFrequency = 0.35f;
        public VerticalBehaviour verticalBehaviour = VerticalBehaviour.MatchAndWeave;
        public float verticalSpeed = 2.2f;
        public float targetHeightOffset = 0.5f;
        public float weaveHeight = 1.4f;
        public float weaveFrequency = 0.3f;
        public Weapon weapon;

        // Randomised so a row does not fire in unison.
        public Vector2 secondsBetweenShots = new Vector2(1.75f, 3.25f);
        private Health health;
        private float strafePhase;
        private float weavePhase;
        private float spawnHeight;
        private float nextFireTime;
        private Transform target;
        private Vector3 lastForward;
        private float bankRoll;
        private Game.AI.AcePilot ace;
        private Game.AI.SquadronMember squadron;

        private void Awake()
        {
            // Caches shared combat components once because the enemy uses them repeatedly while alive.
            // Looking them up every frame would add unnecessary work during large waves.
            health = GetComponent<Health>();
            if (weapon == null)
            {
                weapon = GetComponentInChildren<Weapon>();
            }

            if (GetComponent<ShipSetup>() == null)
            {
                gameObject.AddComponent<ShipSetup>();
            }
        }

        private void Start()
        {
            Game.Graphics.GraphicsSettingsManager.PrepareHierarchy(gameObject);
        }

        private void OnEnable()
        {
            Game.World.FloatingOrigin.Shifted += ShiftOrigin;

            // Resets movement and firing timers whenever a fighter enters a new wave.
            // This prevents pooled enemies continuing an old strafe or shot delay.
            health.ResetHealth();
            spawnHeight = transform.position.y;

            // Offset phases, so the group does not move as one block.
            strafePhase = Random.value * Mathf.PI * 2f;
            weavePhase = Random.value * Mathf.PI * 2f;
            lastForward = transform.forward;
            ScheduleNextShot();
            health.Died += HandleDied;
        }

        private void OnDisable()
        {
            Game.World.FloatingOrigin.Shifted -= ShiftOrigin;
            health.Died -= HandleDied;
        }

        private void ShiftOrigin(Vector3 offset)
        {
            spawnHeight += offset.y;
        }

        private void Update()
        {
            // Only the host runs enemy AI in multiplayer so every client receives one authoritative result.
            if ((Game.Net.NetSession.Active && !Game.Net.NetSession.IsHost) || !health.IsAlive)
            {
                return;
            }

            target = ResolvePlayerTransform();

            // Finds a live player first so enemies automatically retarget when a pilot is destroyed.
            if (target == null)
            {
                return;
            }

            if (ace == null)
            {
                ace = GetComponent<Game.AI.AcePilot>();
            }

            if (squadron == null)
            {
                squadron = GetComponent<Game.AI.SquadronMember>();
            }

            Vector3 specialAim;
            if ((ace != null && ace.Tick(target, Time.deltaTime, weapon, out specialAim)) ||
                (squadron != null && squadron.Tick(target, Time.deltaTime, out specialAim)))
            {
                AimWeaponAt(specialAim);
                if (ace != null && ace.Ready)
                {
                    if (weapon != null && weapon.TryFire())
                    {
                        ace.Fired();
                    }
                }
                else
                {
                    TryShoot();
                }

                return;
            }

            Advance();
            ApplyVerticalMovement();

            // Handles horizontal and vertical motion separately so weaving does not change strafe speed.
            ShipClearance.PushAway(transform, target, minPlanarRange, ShipClearance.FighterHeight);

            // Aimed after moving, so shots leave from where the enemy now is.
            AimWeaponAt(target.position);
            TryShoot();
        }

        private void Advance()
        {
            // Flattens the player direction so pursuit and strafing stay on the main combat plane.
            // Height changes are calculated in the separate vertical movement block.
            // Combines pursuit and sideways motion so enemies approach without flying in a straight line.
            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            // Uses this distance to decide whether the enemy should close in or hold its attack range.
            if (distance < 0.01f)
            {
                return;
            }

            Vector3 forward = toTarget / distance;

            // Dividing by distance normalises the vector, keeping only direction for movement and rotation.
            float deltaTime2 = Time.deltaTime;
            if (deltaTime2 > 0.0001f)
            {
                float yawDegrees = Vector3.SignedAngle(lastForward, forward, Vector3.up);
                float targetRoll = Mathf.Clamp(-yawDegrees / deltaTime2 * 0.35f, -18f, 18f);

                // Converts turn speed into a capped visual roll so sharper turns bank more strongly.
                bankRoll = Mathf.Lerp(bankRoll, targetRoll, 1f - Mathf.Exp(-8f * deltaTime2));
            }

            lastForward = forward;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, 0f, bankRoll);
            Vector3 velocity = Vector3.zero;
            if (distance > stoppingDistance)
            {
                // Adds forward speed only when the player is outside this fighter's preferred range.
                velocity += forward * advanceSpeed;
            }

            if (strafeDistance > 0f && strafeFrequency > 0f)
            {
                // A cross product gives a perpendicular direction; cosine changes its side-to-side strength smoothly.
                // Across the approach direction, so it reads as evasive from any angle.
                strafePhase += strafeFrequency * Mathf.PI * 2f * Time.deltaTime;

                // Advances through a cosine cycle so sideways movement changes direction smoothly.
                velocity += Vector3.Cross(Vector3.up, forward) * (Mathf.Cos(strafePhase) * strafeDistance);
            }

            // Altitude is handled separately.
            Vector3 position = transform.position + velocity * Time.deltaTime;
            position.y = transform.position.y;
            Game.World.SweptMovement.Move(transform, position - transform.position);
        }

        // Following the player's altitude stops climbing being an escape.
        private void ApplyVerticalMovement()
        {
            // Combines player-height tracking and optional sine-wave weaving into a desired altitude.
            // Builds the target height from tracking and optional sine-wave weaving, then approaches it smoothly.
            if (verticalBehaviour == VerticalBehaviour.Hold)
            {
                return;
            }

            bool matchesTarget = verticalBehaviour == VerticalBehaviour.MatchTarget ||
                                 verticalBehaviour == VerticalBehaviour.MatchAndWeave;
            bool weaves = verticalBehaviour == VerticalBehaviour.Weave ||
                          verticalBehaviour == VerticalBehaviour.MatchAndWeave;
            float desiredHeight = spawnHeight;
            if (matchesTarget)
            {
                // Following player height prevents climbing above the fighter from removing all pressure.
                desiredHeight = target.position.y + targetHeightOffset;
            }

            if (weaves && weaveHeight > 0f && weaveFrequency > 0f)
            {
                weavePhase += weaveFrequency * Mathf.PI * 2f * Time.deltaTime;

                // Sine moves smoothly between -1 and 1, creating a natural vertical weave.
                desiredHeight += Mathf.Sin(weavePhase) * weaveHeight;
            }

            Vector3 position = transform.position;
            position.y = Mathf.MoveTowards(position.y, desiredHeight, verticalSpeed * Time.deltaTime);

            // Limits climb speed so altitude changes remain visible and possible to react to.
            Game.World.SweptMovement.Move(transform, position - transform.position);
        }

        // The body turns flat, so only the muzzle can aim up and down.
        private void AimWeaponAt(Vector3 worldPoint)
        {
            // Keeps the flat ship hull stable while allowing its muzzle to aim vertically at the player.
            if (weapon == null || weapon.muzzle == null || weapon.muzzle == transform)
            {
                return;
            }

            Vector3 toPoint = worldPoint - weapon.muzzle.position;
            if (toPoint.sqrMagnitude < 0.0001f)
            {
                return;
            }

            weapon.muzzle.rotation = Quaternion.LookRotation(toPoint.normalized, Vector3.up);
        }

        private Transform ResolvePlayerTransform()
        {
            // Uses the multiplayer roster first, then falls back to the tagged single-player ship.
            Transform roster = Game.Net.PlayerRoster.Nearest(transform.position);
            if (roster != null)
            {
                return roster;
            }

            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                return player.transform;
            }

            return null;
        }

        private void TryShoot()
        {
            // Requires both the AI timer and the weapon cooldown to allow the next projectile.
            if (weapon == null || Time.time < nextFireTime)
            {
                return;
            }

            if (weapon.TryFire())
            {
                ScheduleNextShot();
            }
        }

        private void ScheduleNextShot()
        {
            // Randomises the next delay so a wave does not fire in identical predictable rhythms.
            // Random delays stop a group of enemies firing in identical, predictable rhythms.
            float minimum = Mathf.Max(0.05f, Mathf.Min(secondsBetweenShots.x, secondsBetweenShots.y));
            float maximum = Mathf.Max(minimum, secondsBetweenShots.y);
            nextFireTime = Time.time + Random.Range(minimum, maximum);
        }

        private void HandleDied(Health source, DamageInfo info)
        {
            if (info.Instigator == null)
            {
                return;
            }

            if (Game.Net.NetSession.Active && Game.Net.NetSession.IsHost && Game.Net.NetRunner.Instance != null)
            {
                Game.Net.NetRunner.Instance.AwardKill(info.Instigator, 20, true);
                return;
            }

            PlayerProgression progression = info.Instigator.GetComponentInParent<PlayerProgression>();
            if (progression != null)
            {
                progression.AddKillXp();
            }
        }
    }
}
