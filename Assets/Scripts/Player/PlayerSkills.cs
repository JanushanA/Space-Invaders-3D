using System.Collections.Generic;
using Game.Combat;
using Game.Core;
using Game.Net;
using UnityEngine;
namespace Game.Player
{
    // Hotbar, active skills, and stat bonuses from the tree.
    // Applies skill-tree bonuses and runs the active abilities assigned to the skill bar.
    public class PlayerSkills : MonoBehaviour
    {
        public const int SlotCount = 3;
        public static readonly KeyCode[] BindChoices =
        {
            KeyCode.R, KeyCode.T, KeyCode.X, KeyCode.Z, KeyCode.C, KeyCode.B
        };
        private readonly SkillId[] slots = new SkillId[SlotCount];
        private readonly KeyCode[] binds = new KeyCode[SlotCount];
        private readonly float[] readyTime = new float[SlotCount];
        private SkillTree tree;
        private PlayerController player;
        private Weapon weapon;
        private Health health;
        private float criticalTimeLeft;
        private float nextCriticalShotTime;
        private float shieldUntil;
        private float boostUntil;
        private float baseMoveSpeed;
        private float baseVerticalSpeed;
        private int healthRanks;
        private int speedRanks;
        private int damageRanks;
        private GameObject shieldBubble;
        private float nextIntercept;
        private readonly List<RadarContact> supportContacts = new List<RadarContact>();
        private readonly List<RadarContact> seekerContacts = new List<RadarContact>();

        public float SensorRange => SkillSupportEffects.SensorRange(NetworkLoadout());

        public float TractorRange => SkillSupportEffects.TractorRange(NetworkLoadout());

        public float CooldownSeconds(SkillId skillId) => SkillSupportEffects.Cooldown(NetworkLoadout(), skillId);
        public event System.Action HotbarChanged;

        public static bool MenuOpen
        {
            get; set;
        }

        public bool BlockingFire
        {
            get
            {
                return criticalTimeLeft > 0f;
            }
        }

        private void Awake()
        {
            // Caches player systems and assigns a usable default critical skill bar for a new pilot.
            tree = GetComponent<SkillTree>();
            player = GetComponent<PlayerController>();
            health = GetComponent<Health>();
            weapon = GetComponentInChildren<Weapon>();
            slots[0] = SkillId.Critical;
            slots[1] = SkillId.None;
            slots[2] = SkillId.None;
            binds[0] = KeyCode.R;
            binds[1] = KeyCode.T;
            binds[2] = KeyCode.X;
        }

        private void Start()
        {
            // Records unmodified speeds once so temporary boosts can be removed without losing tree upgrades.
            if (player != null)
            {
                baseMoveSpeed = player.moveSpeed;
                baseVerticalSpeed = player.verticalSpeed;
            }

            if (tree != null)
            {
                tree.Changed += ApplyTreeBonuses;
            }

            LoadSavedHotbar();
            ApplyTreeBonuses();
            SaveHotbarNow();
        }

        private void OnDestroy()
        {
            SaveHotbarNow();
            if (tree != null)
            {
                tree.Changed -= ApplyTreeBonuses;
            }
        }

        private void Update()
        {
            // Updates timed skill effects, then checks each configurable hotbar bind for activation.
            if (MenuOpen)
            {
                return;
            }

            if (health != null && !health.IsAlive)
            {
                return;
            }

            UpdateShield();
            UpdateBoost();
            UpdateCriticalSpray();
            SkillSupportEffects.Intercept(gameObject, NetworkLoadout(), ref nextIntercept, supportContacts);

            // Updates timed effects before reading new keys so an expired effect ends cleanly on this frame.
            for (int abilitySlot = 0; abilitySlot < SlotCount; abilitySlot++)
            {
                // Tests each selected key separately because every hotbar slot can use a different bind.
                if (WasBindPressed(binds[abilitySlot]))
                {
                    TryUseSlot(abilitySlot);
                }
            }
        }

        public SkillId GetSlot(int index)
        {
            if (index < 0 || index >= SlotCount)
            {
                return SkillId.None;
            }

            return slots[index];
        }

        public KeyCode GetBind(int index)
        {
            if (index < 0 || index >= SlotCount)
            {
                return KeyCode.None;
            }

            return binds[index];
        }

        public float Cooldown01(int index)
        {
            // Returns remaining cooldown as a 0-to-1 fraction for the HUD's grey overlay.
            if (index < 0 || index >= SlotCount)
            {
                return 0f;
            }

            if (slots[index] == SkillId.None)
            {
                return 0f;
            }

            if (Time.time >= readyTime[index])
            {
                return 0f;
            }

            float cooldownSeconds = CooldownSeconds(slots[index]);
            if (cooldownSeconds <= 0.01f)
            {
                return 0f;
            }

            return Mathf.Clamp01((readyTime[index] - Time.time) / cooldownSeconds);

            // Divides remaining seconds by total cooldown to give the HUD a 1-to-0 fill amount.
        }

        public bool AssignToSlot(int index, SkillId skill)
        {
            // Allows only unlocked active skills and swaps duplicates instead of putting one skill in two slots.
            if (index < 0 || index >= SlotCount)
            {
                return false;
            }

            if (skill != SkillId.None)
            {
                // Allows only active skills already unlocked in the tree to be placed on the hotbar.
                if (tree == null || !tree.IsUnlocked(skill) || !tree.IsActiveSkill(skill))
                {
                    return false;
                }
            }

            for (int abilitySlot = 0; abilitySlot < SlotCount; abilitySlot++)
            {
                // Swaps duplicates into the old slot so one ability cannot be equipped twice.
                if (abilitySlot != index && slots[abilitySlot] == skill && skill != SkillId.None)
                {
                    slots[abilitySlot] = slots[index];
                }
            }

            slots[index] = skill;
            NotifyHotbar();
            return true;
        }

        public void CycleBind(int index)
        {
            // Finds the next allowed key that is not already assigned to another hotbar slot.
            if (index < 0 || index >= SlotCount)
            {
                return;
            }

            int bindingIndex = 0;

            for (int bindingChoiceIndex = 0; bindingChoiceIndex < BindChoices.Length; bindingChoiceIndex++)
            {
                if (BindChoices[bindingChoiceIndex] == binds[index])
                {
                    bindingIndex = bindingChoiceIndex;
                    break;
                }
            }

            for (int step = 1; step <= BindChoices.Length; step++)
            {
                // Wraps through the allowed keys until it finds one not used by another slot.
                KeyCode next = BindChoices[(bindingIndex + step) % BindChoices.Length];
                if (!BindTaken(next, index))
                {
                    binds[index] = next;
                    NotifyHotbar();
                    return;
                }
            }
        }

        public void ApplyTreeBonuses()
        {
            // Recalculates modifiers from unlocked ranks so the skill tree is the single source of upgrade effects.
            if (tree == null)
            {
                return;
            }

            int newHealth = CountUnlocked(SkillId.Health1, SkillId.Health2, SkillId.Health3, SkillId.Health4);

            // Counts health ranks so an upgrade applies only once even when the tree refreshes many times.
            if (health != null && newHealth > healthRanks)
            {
                health.AddMaxHealth(newHealth - healthRanks);
            }

            healthRanks = newHealth;
            if (health != null)
            {
                health.regenPerSecond = tree.IsUnlocked(SkillId.Health4) ? 0.12f : 0f;
            }

            int newSpeed = CountUnlocked(SkillId.Speed1, SkillId.Speed2, SkillId.Speed3, SkillId.Speed4);
            speedRanks = newSpeed;
            if (player != null)
            {
                // Rebuilds movement stats from base values and ranks instead of stacking upgrades repeatedly.
                player.moveSpeed = (baseMoveSpeed + speedRanks * 2f) * Game.Progression.PilotEquipment.CruiseMultiplier;
                player.verticalSpeed = (baseVerticalSpeed + speedRanks * 1.2f) * Game.Progression.PilotEquipment.VerticalMultiplier;
            }

            damageRanks = CountUnlocked(SkillId.Damage1, SkillId.Damage2, SkillId.Damage3, SkillId.Damage4);
            if (weapon != null)
            {
                // Resets weapon modifiers first, making the current unlock list the single source of final values.
                weapon.extraProjectiles = 0;
                weapon.extraSpread = 0f;
                weapon.fireRateMul = 1f;
                weapon.damageMul = 1f + damageRanks * 0.25f;
                weapon.speedMul = 1f;
                if (tree.IsUnlocked(SkillId.TwinShot))
                {
                    // Adds one projectile and a little spread so the extra shot remains visible.
                    weapon.extraProjectiles += 1;
                    weapon.extraSpread += 6f;
                }

                if (tree.IsUnlocked(SkillId.QuadShot))
                {
                    weapon.extraProjectiles += 2;
                    weapon.extraSpread += 8f;
                }

                if (tree.IsUnlocked(SkillId.RapidFire))
                {
                    weapon.fireRateMul *= 1.8f;
                }

                if (tree.IsUnlocked(SkillId.Overclock))
                {
                    weapon.fireRateMul *= 1.6f;
                }

                if (tree.IsUnlocked(SkillId.HeavyShot))
                {
                    // Trades fire rate and projectile speed for a much stronger individual hit.
                    weapon.damageMul *= 2f;
                    weapon.fireRateMul *= 0.5f;
                    weapon.speedMul *= 0.85f;
                }

                if (tree.IsUnlocked(SkillId.Cannon))
                {
                    weapon.damageMul *= 2f;
                    weapon.fireRateMul *= 0.55f;
                    weapon.speedMul *= 0.75f;
                }

                if (tree.IsUnlocked(SkillId.SpreadShot))
                {
                    weapon.extraProjectiles += 2;
                    weapon.extraSpread += 18f;
                }
            }

            AutoEquipUnlocked();
        }

        private void AutoEquipUnlocked()
        {
            // Fills empty slots with newly unlocked active skills without replacing the player's chosen skills.
            SkillId[] actives =
            {
                SkillId.Critical, SkillId.Dash, SkillId.Afterburner, SkillId.Shield, SkillId.Barrier,
                SkillId.Bomb, SkillId.PlasmaBomb, SkillId.Repair, SkillId.HomingBurst
            };

            for (int abilityIndex = 0; abilityIndex < actives.Length; abilityIndex++)
            {
                if (tree == null || !tree.IsUnlocked(actives[abilityIndex]))
                {
                    continue;
                }

                bool already = false;
                int empty = -1;

                // Scans once for both a duplicate and the first available hotbar position.
                for (int abilitySlot = 0; abilitySlot < SlotCount; abilitySlot++)
                {
                    if (slots[abilitySlot] == actives[abilityIndex])
                    {
                        already = true;
                    }

                    if (empty < 0 && slots[abilitySlot] == SkillId.None)
                    {
                        empty = abilitySlot;
                    }
                }

                if (!already && empty >= 0)
                {
                    slots[empty] = actives[abilityIndex];
                    NotifyHotbar();
                }
            }
        }

        private void TryUseSlot(int index)
        {
            // Rejects empty, locked or cooling-down slots before starting the selected ability.
            SkillId skill = slots[index];
            if (skill == SkillId.None)
            {
                return;
            }

            if (Time.time < readyTime[index])
            {
                return;
            }

            if (!UseSkill(skill))
            {
                return;
            }

            readyTime[index] = Time.time + CooldownSeconds(skill);

            // Stores an absolute ready time, making cooldown checks simple even when frame rate changes.
            NotifyHotbar();
        }

        private bool UseSkill(SkillId skill)
        {
            if (tree == null || !tree.IsUnlocked(skill))
            {
                return false;
            }

            if (NetSession.Active && !NetSession.IsHost)
            {
                if (NetRunner.Instance == null || !NetRunner.Instance.RequestSkill(skill, NetworkLoadout()))
                {
                    return false;
                }

                // Damage, healing and protection are executed on the host avatar.
                if (skill == SkillId.Repair)
                {
                    SpawnBurst(new Color(0.3f, 1f, 0.4f), 16, 3f);
                    return true;
                }

                if (skill == SkillId.Bomb || skill == SkillId.PlasmaBomb)
                {
                    SpawnBurst(new Color(1f, 0.45f, 0.1f), 40, skill == SkillId.PlasmaBomb ? 11.9f : 7.7f);
                    return true;
                }

                if (skill == SkillId.Shield || skill == SkillId.Barrier)
                {
                    shieldUntil = Time.time + (skill == SkillId.Barrier ? 4f : 2.2f);
                    ShowShield(shieldUntil - Time.time);
                    return true;
                }
            }

            // Dispatches each active skill to its matching behaviour and reports whether it actually activated.
            if (skill == SkillId.Critical)
            {
                // Starts a timed spray state instead of firing all critical bullets in a single frame.
                if (weapon == null)
                {
                    return false;
                }

                criticalTimeLeft = 1f;
                nextCriticalShotTime = Time.time;
                return true;
            }

            if (skill == SkillId.Dash)
            {
                // Uses the player's aim direction so dash remains useful while flying up or down.
                if (player != null)
                {
                    player.DashAlongAim(18f);
                }
                else
                {
                    transform.position += AimDir() * 18f;
                }

                SpawnBurst(new Color(0.4f, 1f, 0.75f), 18, 8f);
                return true;
            }

            if (skill == SkillId.Afterburner)
            {
                // Sets an expiry time, then applies the faster movement values immediately.
                boostUntil = Time.time + 3f;
                ApplyBoostSpeed();
                SpawnBurst(new Color(0.4f, 0.85f, 1f), 24, 6f);
                return true;
            }

            if (skill == SkillId.Shield || skill == SkillId.Barrier)
            {
                // Shares shield logic while changing only duration for the upgraded Barrier skill.
                float time = 2.2f;
                if (skill == SkillId.Barrier)
                {
                    time = 4f;
                }

                shieldUntil = Time.time + time;
                if (health != null)
                {
                    health.invulnerable = true;
                }

                ShowShield(time);
                return true;
            }

            if (skill == SkillId.Bomb || skill == SkillId.PlasmaBomb)
            {
                // Shares area damage logic while using a wider radius for the upgraded bomb.
                float radius = 22f;
                if (skill == SkillId.PlasmaBomb)
                {
                    radius = 34f;
                }

                Detonate(radius);
                SpawnBurst(new Color(1f, 0.45f, 0.1f), 40, radius * 0.35f);
                return true;
            }

            if (skill == SkillId.Repair)
            {
                // Routes the repair through Health so it follows normal maximum-health limits and UI updates.
                if (health != null)
                {
                    SkillSupportEffects.Repair(gameObject, health, NetworkLoadout());
                }

                SpawnBurst(new Color(0.3f, 1f, 0.4f), 16, 3f);
                return true;
            }

            if (skill == SkillId.HomingBurst)
            {
                FireSeekers();
                return true;
            }

            return false;
        }

        private void UpdateCriticalSpray()
        {
            // Fires repeated random-spread shots for the critical duration instead of one instant burst.
            if (criticalTimeLeft <= 0f || weapon == null)
            {
                return;
            }

            criticalTimeLeft -= Time.deltaTime;

            // Counts down in seconds so the one-second critical attack has the same length on every machine.
            if (Time.time >= nextCriticalShotTime)
            {
                // Fires small bursts on a short interval to make the attack look like continuous spray fire.
                weapon.FireSpray(3, 20f);
                nextCriticalShotTime = Time.time + 0.05f;
            }
        }

        private void UpdateShield()
        {
            // Keeps the Health invulnerability flag in sync with the shield's expiry time.
            if (health == null)
            {
                return;
            }

            if (shieldUntil > 0f && Time.time >= shieldUntil)
            {
                // Clears invulnerability exactly when the expiry time is reached and hides the shield visual.
                health.invulnerable = false;
                shieldUntil = 0f;
                if (shieldBubble != null)
                {
                    shieldBubble.SetActive(false);
                }
            }
        }

        private void UpdateBoost()
        {
            // Restores normal movement values when the temporary afterburner ends.
            if (boostUntil <= 0f)
            {
                return;
            }

            if (Time.time >= boostUntil)
            {
                boostUntil = 0f;
                ApplyTreeBonuses();
            }
        }

        public void RefreshEquipmentBonuses()
        {
            ApplyTreeBonuses();
            if (boostUntil > Time.time)
            {
                ApplyBoostSpeed();
            }
        }

        private void ApplyBoostSpeed()
        {
            // Rebuilds boosted speed from base plus ranks so an afterburner never multiplies itself repeatedly.
            if (player == null)
            {
                return;
            }

            player.moveSpeed = (baseMoveSpeed + speedRanks * 2f) * 1.7f * Game.Progression.PilotEquipment.CruiseMultiplier;
            player.verticalSpeed = (baseVerticalSpeed + speedRanks * 1.2f) * 1.7f * Game.Progression.PilotEquipment.VerticalMultiplier;
        }

        private void Detonate(float radius)
        {
            DetonateAt(transform.position, radius, gameObject);
        }

        public static void DetonateAt(Vector3 origin, float radius, GameObject instigator)
        {
            if (NetSession.Active && !NetSession.IsHost)
            {
                return;
            }

            var targets = new List<RadarContact>();
            RadarIndex.Query(origin, radius, RadarKind.Hostile, targets);

            foreach (var contact in targets)
            {
                Health target = contact.Health;
                if (target == null || !RadarIndex.LineOfSight(origin, contact))
                {
                    continue;
                }

                target.ApplyDamage(new DamageInfo(3f, DamageSource.PlayerProjectile, instigator, target.transform.position, Vector3.up));
            }
        }

        public uint NetworkLoadout() => tree != null ? tree.NetworkMask : 0u;

        private void ShowShield(float time)
        {
            // Creates one translucent bubble on first use, then only toggles it for later shields.
            if (shieldBubble == null)
            {
                shieldBubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shieldBubble.name = "ShieldBubble";
                shieldBubble.transform.SetParent(transform, false);
                shieldBubble.transform.localScale = Vector3.one * 2.4f;
                Collider collider = shieldBubble.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                MeshRenderer renderer = shieldBubble.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = VisualUtil.MakeAdditive(new Color(0.35f, 0.75f, 1f, 0.7f));
                VisualUtil.HideShadows(renderer);
            }

            shieldBubble.SetActive(true);
        }

        private void SpawnBurst(Color colour, int count, float speed)
        {
            // Builds a short one-shot particle system for skill feedback at the ship's current position.
            GameObject host = new GameObject("SkillBurst");
            host.transform.position = transform.position;
            ParticleSystem particleSystem = host.AddComponent<ParticleSystem>();
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particleSystem.main;
            main.loop = false;
            main.playOnAwake = true;
            main.duration = 0.4f;
            main.startLifetime = 0.45f;
            main.startSpeed = speed;
            main.startSize = 0.25f;
            main.startColor = colour;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count;
            main.gravityModifier = 0f;
            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });
            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.material = VisualUtil.MakeAdditive(colour);
            VisualUtil.HideShadows(renderer);
            particleSystem.Play();
            Destroy(host, 1.2f);
        }

        private void FireSeekers()
        {
            // Chooses nearby enemies and launches shots toward their positions for a simple homing burst.
            if (weapon == null)
            {
                return;
            }

            seekerContacts.Clear();
            RadarIndex.Query(transform.position, 70, RadarKind.Hostile, seekerContacts);
            seekerContacts.Sort((firstContact, secondContact) => (firstContact.transform.position - transform.position).sqrMagnitude.CompareTo((secondContact.transform.position - transform.position).sqrMagnitude));
            int fired = 0;

            foreach (var contact in seekerContacts)
            {
                if (!RadarIndex.LineOfSight(transform.position, contact))
                {
                    continue;
                }

                Vector3 targetOffset = contact.transform.position - transform.position;
                ProjectilePool pool = ProjectilePool.Instance;
                if (pool == null || weapon.definition == null)
                {
                    return;
                }

                Projectile shot = pool.Get(weapon.definition.projectilePrefab);
                if (shot != null)
                {
                    // Normalises the target vector so the seeker projectile receives direction, not distance.
                    shot.Launch(transform.position, targetOffset.normalized, weaponBuild(), pool);
                    if (NetRunner.HasInstance)
                    {
                        NetRunner.Instance.NoteFire(transform.position, targetOffset.normalized, DamageSource.PlayerProjectile);
                    }
                }

                fired += 1;
                if (fired >= 6)
                {
                    return;
                }
            }
        }

        private ProjectileLaunchData weaponBuild()
        {
            // Creates launch data for seeker shots using current player damage modifiers and collision rules.
            return new ProjectileLaunchData(
                35f,
                Mathf.Max(1f, weapon.definition.damagePerProjectile * weapon.damageMul),
                0.25f,
                3f,
                DamageSource.PlayerProjectile,
                gameObject,
                GameLayers.PlayerWeaponHitMask);
        }

        private Vector3 AimDir()
        {
            // Uses the pitching aim pivot when available so skills follow the camera rather than only hull yaw.
            if (player != null && player.aimPivot != null)
            {
                return player.aimPivot.forward;
            }

            return transform.forward;
        }

        private int CountUnlocked(SkillId firstSkill, SkillId secondSkill, SkillId thirdSkill, SkillId fourthSkill)
        {
            // Counts a fixed upgrade branch to turn four individual unlock flags into one stat rank.
            int unlockedCount = 0;
            if (tree.IsUnlocked(firstSkill))
            {
                unlockedCount += 1;
            }

            if (tree.IsUnlocked(secondSkill))
            {
                unlockedCount += 1;
            }

            if (tree.IsUnlocked(thirdSkill))
            {
                unlockedCount += 1;
            }

            if (tree.IsUnlocked(fourthSkill))
            {
                unlockedCount += 1;
            }

            return unlockedCount;
        }

        private bool BindTaken(KeyCode key, int skip)
        {
            // Checks every other slot so changing one bind cannot create duplicate key assignments.
            for (int abilitySlot = 0; abilitySlot < SlotCount; abilitySlot++)
            {
                if (abilitySlot != skip && binds[abilitySlot] == key)
                {
                    return true;
                }
            }

            return false;
        }

        private void NotifyHotbar()
        {
            // Saves the new slot setup before telling the HUD to redraw it.
            SaveHotbarNow();
            if (HotbarChanged != null)
            {
                HotbarChanged();
            }
        }

        private void LoadSavedHotbar()
        {
            // Restores each saved slot independently so one missing record does not affect the other slots.
            if (!GameSession.HasPilotSave())
            {
                return;
            }

            for (int abilitySlot = 0; abilitySlot < SlotCount; abilitySlot++)
            {
                int skillId;
                int bindCode;
                bool found;
                GameSession.LoadHotbarSlot(abilitySlot, out skillId, out bindCode, out found);
                if (!found)
                {
                    continue;
                }

                SkillId skill = (SkillId)skillId;
                if (skill != SkillId.None)
                {
                    // Removes a saved skill if this version of the tree no longer considers it usable.
                    if (tree == null || !tree.IsUnlocked(skill) || !tree.IsActiveSkill(skill))
                    {
                        skill = SkillId.None;
                    }
                }

                slots[abilitySlot] = skill;
                KeyCode bind = (KeyCode)bindCode;
                if (bind != KeyCode.None)
                {
                    binds[abilitySlot] = bind;
                }
            }

            // V now belongs to FlightView. Read every saved slot before choosing
            // replacements so a later user's binding cannot be stolen by migration.
            for (int abilitySlot2 = 0; abilitySlot2 < SlotCount; abilitySlot2++)
            {
                if (binds[abilitySlot2] != KeyCode.V)
                {
                    continue;
                }

                foreach (KeyCode candidate in BindChoices)
                {
                    if (BindTaken(candidate, abilitySlot2))
                    {
                        continue;
                    }

                    binds[abilitySlot2] = candidate;
                    break;
                }
            }

            // Start's normal SaveHotbarNow persists this through GameSession's
            // existing SuppressSave guard; loading itself never writes preferences.
        }

        private void SaveHotbarNow()
        {
            // Writes every slot together so skill IDs and their matching key binds stay paired.
            for (int abilitySlot = 0; abilitySlot < SlotCount; abilitySlot++)
            {
                GameSession.SaveHotbar(abilitySlot, (int)slots[abilitySlot], (int)binds[abilitySlot]);
            }
        }

        public static float CooldownFor(SkillId skill)
        {
            // Centralises cooldown choices so both activation and HUD fill use exactly the same duration.
            if (skill == SkillId.Critical)
            {
                return 6f;
            }

            if (skill == SkillId.Dash)
            {
                return 2.5f;
            }

            if (skill == SkillId.Afterburner)
            {
                return 8f;
            }

            if (skill == SkillId.Shield)
            {
                return 8f;
            }

            if (skill == SkillId.Barrier)
            {
                return 10f;
            }

            if (skill == SkillId.Bomb)
            {
                return 7f;
            }

            if (skill == SkillId.PlasmaBomb)
            {
                return 9f;
            }

            if (skill == SkillId.Repair)
            {
                return 12f;
            }

            if (skill == SkillId.HomingBurst)
            {
                return 6f;
            }

            return 5f;
        }

        private static bool WasBindPressed(KeyCode key)
        {
            if (key == KeyCode.V)
            {
                return false;
            }

            // Uses the new Input System when present, while preserving the old Input Manager fallback.
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                return KeyboardPressed(key);
            }

            return Input.GetKeyDown(key);
        }

        private static bool KeyboardPressed(KeyCode key)
        {
            // Maps only allowed hotbar keys to their new Input System button states.
            if (UnityEngine.InputSystem.Keyboard.current == null)
            {
                return false;
            }

            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (key == KeyCode.R)
            {
                return keyboard.rKey.wasPressedThisFrame;
            }

            if (key == KeyCode.T)
            {
                return keyboard.tKey.wasPressedThisFrame;
            }

            if (key == KeyCode.X)
            {
                return keyboard.xKey.wasPressedThisFrame;
            }

            if (key == KeyCode.Z)
            {
                return keyboard.zKey.wasPressedThisFrame;
            }

            if (key == KeyCode.C)
            {
                return keyboard.cKey.wasPressedThisFrame;
            }

            if (key == KeyCode.B)
            {
                return keyboard.bKey.wasPressedThisFrame;
            }

            return false;
        }
    }
}
