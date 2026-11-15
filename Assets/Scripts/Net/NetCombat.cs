using System.Collections.Generic;
using System.IO;
using Game.Combat;
using Game.Core;
using Game.Enemies;
using Game.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Game.Net
{
    // Connects network messages to the current combat scene. The host runs enemies and guest health;
    // clients render enemy ghosts and remote shots from the host's updates.
    public class NetCombat : MonoBehaviour
    {
        public static NetCombat Instance;

        public int Round
        {
            get; private set;
        }
        private static readonly Color[] PilotColours =
        {
            new Color(0.35f, 0.85f, 1f),
            new Color(0.86f, 0.72f, 0.42f),
            new Color(0.45f, 0.9f, 0.55f),
            new Color(0.72f, 0.5f, 1f),
            new Color(1f, 0.55f, 0.3f)
        };
        private PlayerController localPilot;
        private Health localHealth;
        private readonly GameObject[] avatars = new GameObject[NetSession.MaxPlayers];
        private readonly Dictionary<int, GameObject> ghosts = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> hostEnemies = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Vector3> ghostTargetPos = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, Quaternion> ghostTargetRot = new Dictionary<int, Quaternion>();
        private int nextEnemyId = 1;
        private GameObject enemyPrefab;
        private float nextPose;
        private float nextSnap;
        private bool failSent;
        private WeaponDefinition playerWeaponDef;
        private WeaponDefinition enemyWeaponDef;
        private readonly Dictionary<uint, Projectile> networkProjectiles = new Dictionary<uint, Projectile>();
        private readonly SortedSet<uint> cancelledProjectiles = new SortedSet<uint>();
        private uint retiredProjectileId;

        public int CancelledProjectileCount => cancelledProjectiles.Count;

        public static void BootIfNeeded()
        {
            // Creates one combat synchroniser only after a multiplayer match scene has loaded.
            if (!NetSession.Active || !NetSession.InMatch)
            {
                return;
            }

            if (Instance != null)
            {
                return;
            }

            GameObject sceneObject = new GameObject("NetCombat");
            Instance = sceneObject.AddComponent<NetCombat>();
        }

        private void Awake()
        {
            Game.World.FloatingOrigin.Shifted += ShiftOrigin;

            // Finds the local ship, places party members by slot, and prevents clients spawning their own enemies.
            Instance = this;
            Round = NetRunner.Instance != null ? NetRunner.Instance.MatchRound : 0;
            PlayerRoster.Clear();

            // Rebuilds the roster from this scene so old lobby or previous-match transforms cannot be targeted.
            localPilot = FindObjectOfType<PlayerController>();
            Vector3 home = new Vector3(0f, 1f, -6f);
            if (localPilot != null)
            {
                home = localPilot.transform.position;
                localHealth = localPilot.GetComponent<Health>();
                localPilot.transform.position = SlotStart(home, NetSession.LocalSlot);

                // Offsets each pilot by lobby slot so ships do not spawn inside one another.
                PlayerRoster.Add(localPilot.transform);
                CacheWeaponDefs(localPilot);
            }

            EnemyWaveSpawner spawner = FindObjectOfType<EnemyWaveSpawner>();
            if (spawner != null)
            {
                enemyPrefab = spawner.enemyPrefab;
                if (enemyPrefab != null)
                {
                    CacheEnemyDef(enemyPrefab);
                }

                if (!NetSession.IsHost)
                {
                    // Disables client spawning because the host is the only machine allowed to create real enemies.
                    spawner.enabled = false;
                }
            }

            int pilotSlot;

            for (pilotSlot = 0; pilotSlot < NetSession.MaxPlayers; pilotSlot++)
            {
                // Creates visual-only avatars for occupied remote slots while keeping the local ship player-controlled.
                if (pilotSlot == NetSession.LocalSlot)
                {
                    continue;
                }

                if (NetSession.SlotTaken(pilotSlot))
                {
                    avatars[pilotSlot] = MakeAvatar(pilotSlot, home);
                }
            }

            if (NetRunner.Instance != null)
            {
                NetRunner.Instance.MatchFailed += HandleMatchFailed;
            }
        }

        private void OnDestroy()
        {
            networkProjectiles.Clear();
            cancelledProjectiles.Clear();
            retiredProjectileId = 0;
            Game.World.FloatingOrigin.Shifted -= ShiftOrigin;
            if (Instance == this)
            {
                Instance = null;
            }

            if (NetRunner.Instance != null)
            {
                NetRunner.Instance.MatchFailed -= HandleMatchFailed;
            }

            PlayerRoster.Clear();
        }

        private void ShiftOrigin(Vector3 delta)
        {
            var enemyIds = new List<int>(ghostTargetPos.Keys);

            foreach (int enemyId in enemyIds)
            {
                ghostTargetPos[enemyId] += delta;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void HookScene()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            BootIfNeeded();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BootIfNeeded();
        }

        private void Update()
        {
            // Sends player poses often; the host also broadcasts enemy snapshots for every client.
            if (!NetSession.Active || !NetSession.InMatch)
            {
                return;
            }

            if (Time.unscaledTime >= nextPose)
            {
                // Sends player pose twenty times per second using unscaled time so menus do not slow networking.
                nextPose = Time.unscaledTime + 0.05f;
                SendPose();
            }

            if (NetSession.IsHost && Time.unscaledTime >= nextSnap)
            {
                // Sends authoritative enemy state twenty-five times per second and checks for a party wipe.
                nextSnap = Time.unscaledTime + 0.04f;
                SendEnemySnap();

                for (int pilotSlot = 1; pilotSlot < avatars.Length; pilotSlot++)
                {
                    if (avatars[pilotSlot] == null || NetRunner.Instance == null)
                    {
                        continue;
                    }

                    Health health = avatars[pilotSlot].GetComponent<Health>();
                    if (health != null)
                    {
                        NetRunner.Instance.NoteHealth(pilotSlot, health.CurrentHealth);
                    }
                }

                CheckWipe();
            }

            if (!NetSession.IsHost)
            {
                SmoothGhosts();
            }
        }

        public void RegisterEnemy(GameObject enemy)
        {
            // Gives each host enemy a stable ID so clients can update the matching ghost later.
            if (!NetSession.IsHost || enemy == null)
            {
                return;
            }

            int enemyId = nextEnemyId;
            nextEnemyId++;
            hostEnemies[enemyId] = enemy;
        }

        public void ApplyPose(int slot, Vector3 position2, float yawDegrees, float pitch, float bank, bool alive)
        {
            // Updates a remote pilot visual without giving that player control over this local ship.
            if (slot == NetSession.LocalSlot)
            {
                return;
            }

            if (slot < 0 || slot >= avatars.Length || !NetSession.SlotTaken(slot))
            {
                return;
            }

            if (avatars[slot] == null)
            {
                // Creates a missing remote avatar if a late join or packet arrives before the lobby visual did.
                Vector3 home = new Vector3(0f, 1f, -6f);
                if (localPilot != null)
                {
                    home = localPilot.transform.position - new Vector3(NetSession.LocalSlot * 16f, 0f, 0f);
                }

                avatars[slot] = MakeAvatar(slot, home);
            }

            GameObject avatar = avatars[slot];
            if (avatar == null)
            {
                return;
            }

            avatar.transform.position = position2;
            avatar.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f) * Quaternion.Euler(0f, 0f, bank);

            // Rebuilds hull yaw and bank from packet values without changing the local camera or controller.
            if (!NetSession.IsHost && !alive && avatar.activeSelf)
            {
                Health health = avatar.GetComponent<Health>();
                if (health != null && health.IsAlive)
                {
                    health.SetNetworkHealth(0f);
                }
            }
        }

        public bool PilotAlive(int slot)
        {
            if (slot == NetSession.LocalSlot)
            {
                return localHealth == null || localHealth.IsAlive;
            }

            if (slot < 0 || slot >= avatars.Length || avatars[slot] == null)
            {
                return false;
            }

            Health health = avatars[slot].GetComponent<Health>();
            return health != null && health.IsAlive;
        }

        public void RemovePilot(int slot)
        {
            if (slot < 0 || slot >= avatars.Length || avatars[slot] == null)
            {
                return;
            }

            PlayerRoster.Remove(avatars[slot].transform);
            Destroy(avatars[slot]);
            avatars[slot] = null;
        }

        public void ApplySnapshot(BinaryReader packetReader)
        {
            // Reads the host enemy list and creates a ghost only when its network ID is first seen.
            if (NetSession.IsHost)
            {
                return;
            }

            int count = packetReader.ReadUInt16();

            // Reads the host's leading enemy count so the client knows exactly how many records follow.
            HashSet<int> seen = new HashSet<int>();
            int snapshotEntryIndex;

            for (snapshotEntryIndex = 0; snapshotEntryIndex < count; snapshotEntryIndex++)
            {
                // Deserialises one complete enemy record in the same field order used by SendEnemySnap.
                int enemyId = packetReader.ReadInt32();
                Vector3 enemyPosition = Game.World.FloatingOrigin.ToLocalPosition(new Vector3(packetReader.ReadSingle(), packetReader.ReadSingle(), packetReader.ReadSingle()));
                float yRot = packetReader.ReadSingle();
                float scale = packetReader.ReadSingle();
                bool alive = packetReader.ReadByte() == 1;
                float hpCurrent = packetReader.ReadSingle();
                float hpMax = packetReader.ReadSingle();
                bool boss = packetReader.ReadByte() == 1;
                BossPresentationState presentation = boss ? ReadBoss(packetReader) : default(BossPresentationState);
                seen.Add(enemyId);
                GameObject ghost;
                if (!ghosts.TryGetValue(enemyId, out ghost) || ghost == null)
                {
                    // Creates one visual ghost for a newly seen network ID instead of simulating local enemy AI.
                    ghost = MakeGhost(scale);
                    ghosts[enemyId] = ghost;
                    if (ghost != null)
                    {
                        ghost.transform.position = enemyPosition;
                        ghost.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
                    }
                }

                if (ghost == null)
                {
                    continue;
                }

                ghost.SetActive(alive);
                if (!alive)
                {
                    ghostTargetPos.Remove(enemyId);
                    ghostTargetRot.Remove(enemyId);
                    continue;
                }

                ghostTargetPos[enemyId] = enemyPosition;

                // Stores snapshot targets separately so movement can be smoothed between packet arrivals.
                ghostTargetRot[enemyId] = Quaternion.Euler(0f, yRot, 0f);
                ghost.transform.localScale = new Vector3(scale, scale, scale);
                Health health = ghost.GetComponent<Health>();
                if (health != null)
                {
                    if (hpMax > 0.1f)
                    {
                        health.maxHealth = hpMax;
                    }

                    health.SetNetworkHealth(hpCurrent);
                }

                if (boss)
                {
                    NetBossPresentation view = ghost.GetComponent<NetBossPresentation>();
                    if (view == null)
                    {
                        view = ghost.AddComponent<NetBossPresentation>();
                    }

                    view.Apply(presentation);
                }
            }

            List<int> drop = new List<int>();

            foreach (KeyValuePair<int, GameObject> pair in ghosts)
            {
                // Finds ghosts missing from this host snapshot so despawned enemies are removed on the client too.
                if (!seen.Contains(pair.Key))
                {
                    drop.Add(pair.Key);
                }
            }

            for (snapshotEntryIndex = 0; snapshotEntryIndex < drop.Count; snapshotEntryIndex++)
            {
                int enemyId2 = drop[snapshotEntryIndex];
                GameObject ghost = ghosts[enemyId2];
                ghosts.Remove(enemyId2);
                ghostTargetPos.Remove(enemyId2);
                ghostTargetRot.Remove(enemyId2);
                if (ghost != null)
                {
                    Destroy(ghost);
                }
            }
        }

        private void SmoothGhosts()
        {
            // Blends each ghost toward its latest host snapshot to hide visible packet-by-packet teleporting.
            float blend = 1f - Mathf.Exp(-18f * Time.deltaTime);

            // Exponential interpolation gives a stable smoothing rate regardless of the current frame rate.
            foreach (KeyValuePair<int, GameObject> pair in ghosts)
            {
                GameObject ghost = pair.Value;
                if (ghost == null || !ghost.activeSelf)
                {
                    continue;
                }

                Vector3 targetPos;
                if (!ghostTargetPos.TryGetValue(pair.Key, out targetPos))
                {
                    continue;
                }

                ghost.transform.position = Vector3.Lerp(ghost.transform.position, targetPos, blend);

                // Moves only partway toward the latest position every frame, creating smooth remote movement.
                Quaternion targetRot;
                if (ghostTargetRot.TryGetValue(pair.Key, out targetRot))
                {
                    ghost.transform.rotation = Quaternion.Slerp(ghost.transform.rotation, targetRot, blend);
                }
            }
        }

        public void SpawnNetFire(int slot, Vector3 origin, Vector3 direction, DamageSource source)
        {
            SpawnNetProjectile(slot, origin, direction, source, 0, null);
        }

        public void SpawnIdentifiedEnemyFire(Vector3 origin, Vector3 direction, uint projectileId, float? hostileSpeed = null)
        {
            if (NetSession.IsHost || !CanSpawnNetworkProjectile(projectileId))
            {
                return;
            }

            SpawnNetProjectile(255, origin, direction, DamageSource.EnemyProjectile, projectileId, hostileSpeed);
        }

        private bool CanSpawnNetworkProjectile(uint projectileId)
        {
            // Suppress repeated launches and launches cancelled before their packet arrived.
            if (projectileId == 0 || projectileId <= retiredProjectileId || cancelledProjectiles.Contains(projectileId))
            {
                return false;
            }

            if (!networkProjectiles.TryGetValue(projectileId, out Projectile shot))
            {
                return true;
            }

            // Pooled objects can be reused for a different shot, so the reference alone is not enough.
            if (shot != null && shot.InFlight && shot.NetworkId == projectileId)
            {
                return false;
            }

            networkProjectiles.Remove(projectileId);
            return true;
        }

        public bool RegisterNetworkProjectile(uint projectileId, Projectile shot)
        {
            if (NetSession.IsHost || shot == null || !CanSpawnNetworkProjectile(projectileId))
            {
                return false;
            }

            shot.NetworkId = projectileId;
            networkProjectiles[projectileId] = shot;
            return true;
        }

        public void ForgetNetworkProjectile(Projectile shot)
        {
            // Remove only the current owner of the ID; a stale pooled reference must not remove its replacement.
            if (shot != null && shot.NetworkId != 0 && networkProjectiles.TryGetValue(shot.NetworkId, out Projectile known) && known == shot)
            {
                networkProjectiles.Remove(shot.NetworkId);
            }
        }

        public void CancelNetworkProjectile(uint projectileId)
        {
            if (NetSession.IsHost || projectileId == 0)
            {
                return;
            }

            if (networkProjectiles.TryGetValue(projectileId, out Projectile shot))
            {
                networkProjectiles.Remove(projectileId);
                if (shot != null && shot.NetworkId == projectileId)
                {
                    shot.Despawn();
                }
            }

            if (projectileId <= retiredProjectileId)
            {
                return;
            }

            cancelledProjectiles.Add(projectileId);

            // Remember cancelled IDs even when no shot existed yet, so a late launch stays cancelled.
            // When the set fills, reject the retired ID range without storing every old ID separately.
            if (cancelledProjectiles.Count > 4096)
            {
                retiredProjectileId = cancelledProjectiles.Min;
                cancelledProjectiles.Remove(retiredProjectileId);
            }
        }

        private void SpawnNetProjectile(int slot, Vector3 origin, Vector3 fireDirection, DamageSource source, uint projectileId, float? hostileSpeed)
        {
            // Recreates remote projectile visuals while keeping real player-projectile damage on the host.
            if (slot == NetSession.LocalSlot)
            {
                return;
            }

            if (source == DamageSource.EnemyProjectile && NetSession.IsHost)
            {
                return;
            }

            WeaponDefinition weaponDefinition = playerWeaponDef;
            if (source == DamageSource.EnemyProjectile)
            {
                if (enemyWeaponDef == null && enemyPrefab != null)
                {
                    CacheEnemyDef(enemyPrefab);
                }

                weaponDefinition = enemyWeaponDef;
            }
            else if (weaponDefinition == null && localPilot != null)
            {
                CacheWeaponDefs(localPilot);
                weaponDefinition = playerWeaponDef;
            }

            if (weaponDefinition == null || weaponDefinition.projectilePrefab == null)
            {
                return;
            }

            bool damaging = NetSession.IsHost && source == DamageSource.PlayerProjectile;
            RemotePilotAuthority authority = slot >= 0 && slot < avatars.Length && avatars[slot] != null ? avatars[slot].GetComponent<RemotePilotAuthority>() : null;

            // Gives only host-created player shots damage so clients cannot decide enemy health themselves.
            GameObject instigator = InstigatorFor(slot);
            LayerMask collisionLayers = GameLayers.HitMaskFor(source);
            float damage = 0f;
            if (damaging)
            {
                damage = weaponDefinition.damagePerProjectile * (authority != null ? authority.DamageMultiplier : 1f);
            }

            ProjectileLaunchData data = new ProjectileLaunchData(
                hostileSpeed ?? weaponDefinition.projectileSpeed * (authority != null ? authority.SpeedMultiplier : 1f),
                damage,
                weaponDefinition.projectileRadius,
                weaponDefinition.projectileLifetime,
                source,
                instigator,
                collisionLayers);
            NetRunner.VisualOnlyFire = true;

            // Prevents this recreated shot from sending another network fire packet and causing a loop.
            try
            {
                ProjectilePool pool = ProjectilePool.Instance;
                if (pool != null)
                {
                    Projectile shot = pool.Get(weaponDefinition.projectilePrefab);
                    if (shot != null)
                    {
                        shot.Launch(origin, fireDirection, data, pool);
                        if (projectileId != 0 && !RegisterNetworkProjectile(projectileId, shot))
                        {
                            shot.Despawn();
                        }
                    }
                }
            }
            finally
            {
                NetRunner.VisualOnlyFire = false;
            }
        }

        public void ApplyRemoteHealth(float current, float maximum = 0f)
        {
            // Updates the local health display from a host packet without replaying a local collision.
            if (localHealth == null)
            {
                return;
            }

            if (!localHealth.IsAlive && current > 0f)
            {
                return; // Death is final until the next match scene.

            } // Death is final until the next match scene.

            if (maximum > 0f)
            {
                localHealth.maxHealth = maximum;
            }

            localHealth.SetNetworkHealth(current);
        }

        public float PilotMaximum(int slot)
        {
            if (slot == NetSession.LocalSlot)
            {
                return localHealth != null ? localHealth.maxHealth : 3f;
            }

            Health health = slot >= 0 && slot < avatars.Length && avatars[slot] != null ? avatars[slot].GetComponent<Health>() : null;
            return health != null ? health.maxHealth : 3f;
        }

        public bool HasLoadout(int slot)
        {
            RemotePilotAuthority authority = Authority(slot);
            return authority != null && authority.HasLoadout;
        }

        public void ApplyLoadout(int slot, uint unlockedSkills)
        {
            if (!NetSession.IsHost)
            {
                return;
            }

            RemotePilotAuthority authority = Authority(slot);
            if (authority != null)
            {
                authority.ApplyLoadout(unlockedSkills);
            }
        }

        public void ApplySkill(int slot, SkillId skill)
        {
            if (!NetSession.IsHost)
            {
                return;
            }

            RemotePilotAuthority authority = Authority(slot);
            if (authority != null)
            {
                authority.TryUse(skill);
            }
        }

        public bool AdmitShot(int slot, Vector3 origin, Vector3 direction)
        {
            // A guest requests a shot; the host checks its origin, direction, and available firing allowance.
            RemotePilotAuthority authority = Authority(slot);
            return authority != null && (origin - authority.transform.position).sqrMagnitude < 32f * 32f &&
                direction.sqrMagnitude > 0.5f && direction.sqrMagnitude < 1.5f && authority.TryConsumeShot(playerWeaponDef);
        }

        private RemotePilotAuthority Authority(int slot)
        {
            return slot > 0 && slot < avatars.Length && avatars[slot] != null ? avatars[slot].GetComponent<RemotePilotAuthority>() : null;
        }

        public void ApplyReward(int experiencePoints, bool countKill)
        {
            if (NetSession.IsHost || localPilot == null)
            {
                return;
            }

            PlayerProgression progression = localPilot.GetComponent<PlayerProgression>();
            if (progression == null)
            {
                return;
            }

            if (countKill)
            {
                GameSession.NoteKill(experiencePoints);
            }

            progression.AddXp(experiencePoints);
        }

        private void SendPose()
        {
            // Sends position plus yaw, pitch and bank so remote avatars match both movement and ship banking.
            if (localPilot == null || NetRunner.Instance == null)
            {
                return;
            }

            PlayerSkills skills = localPilot.GetComponent<PlayerSkills>();
            if (skills != null)
            {
                NetRunner.Instance.SendLoadout(skills.NetworkLoadout());
            }

            bool alive = true;
            if (localHealth != null)
            {
                alive = localHealth.IsAlive;
            }

            float yawDegrees;
            float pitch;
            float bank;
            localPilot.GetAimPose(out yawDegrees, out pitch, out bank);
            NetRunner.Instance.SendLocalPose(localPilot.transform.position, yawDegrees, pitch, bank, alive);
        }

        private void SendEnemySnap()
        {
            // Serialises every host enemy into one compact snapshot packet for all clients.
            if (NetRunner.Instance == null)
            {
                return;
            }

            List<int> dead = new List<int>();
            MemoryStream packetStream = NetRunner.Instance.OpenSnapStream();
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            List<int> enemyIds = new List<int>(hostEnemies.Keys);
            ushort count = 0;
            long countPos = packetStream.Position;
            packetWriter.Write((ushort)0);

            // Reserves the first two bytes, then fills the true record count after dead entries are skipped.
            int enemyEntryIndex;

            for (enemyEntryIndex = 0; enemyEntryIndex < enemyIds.Count; enemyEntryIndex++)
            {
                int enemyId = enemyIds[enemyEntryIndex];
                GameObject enemy = hostEnemies[enemyId];
                if (enemy == null)
                {
                    // Collects destroyed entries for cleanup after packet creation rather than changing the dictionary mid-loop.
                    dead.Add(enemyId);
                    continue;
                }

                bool alive = enemy.activeSelf;
                Health health = enemy.GetComponent<Health>();
                if (health != null && !health.IsAlive)
                {
                    alive = false;
                }

                packetWriter.Write(enemyId);
                Vector3 globalPosition = Game.World.FloatingOrigin.ToGlobalPosition(enemy.transform.position);
                packetWriter.Write(globalPosition.x);
                packetWriter.Write(globalPosition.y);
                packetWriter.Write(globalPosition.z);
                packetWriter.Write(enemy.transform.eulerAngles.y);
                packetWriter.Write(enemy.transform.localScale.x);
                byte live = 0;
                if (alive)
                {
                    live = 1;
                }

                packetWriter.Write(live);
                float hpCurrent = 0f;
                float hpMax = 1f;
                if (health != null)
                {
                    hpCurrent = health.CurrentHealth;
                    hpMax = health.maxHealth;
                }

                packetWriter.Write(hpCurrent);
                packetWriter.Write(hpMax);
                MotherShipBoss boss = enemy.GetComponent<MotherShipBoss>();
                packetWriter.Write((byte)(boss != null ? 1 : 0));
                if (boss != null)
                {
                    WriteBoss(packetWriter, boss.CapturePresentation());
                }

                count++;
            }

            long end = packetStream.Position;
            packetStream.Position = countPos;
            packetWriter.Write(count);

            // Rewinds to replace the placeholder count once the number of written records is known.
            packetStream.Position = end;
            NetRunner.Instance.SendSnapshot(packetStream.ToArray());

            for (enemyEntryIndex = 0; enemyEntryIndex < dead.Count; enemyEntryIndex++)
            {
                hostEnemies.Remove(dead[enemyEntryIndex]);
            }
        }

        private static void WriteBoss(BinaryWriter packetWriter, BossPresentationState state)
        {
            // Keep this 43-byte layout paired with ReadBoss and NetRunner.ValidPacket.
            // Flags pack shield, beam visibility/firing/lock, and hull tint into one byte.
            state.BeamOrigin = Game.World.FloatingOrigin.ToGlobalPosition(state.BeamOrigin);
            packetWriter.Write(state.Phase);
            packetWriter.Write(state.Stage);
            packetWriter.Write((byte)((state.Invulnerable ? 1 : 0) | (state.BeamVisible ? 2 : 0) | (state.BeamFiring ? 4 : 0) | (state.BeamLocked ? 8 : 0) | (state.HullTinted ? 16 : 0)));
            packetWriter.Write(state.BeamOrigin.x);
            packetWriter.Write(state.BeamOrigin.y);
            packetWriter.Write(state.BeamOrigin.z);
            packetWriter.Write(state.BeamDirection.x);
            packetWriter.Write(state.BeamDirection.y);
            packetWriter.Write(state.BeamDirection.z);
            packetWriter.Write(state.HullColor.r);
            packetWriter.Write(state.HullColor.g);
            packetWriter.Write(state.HullColor.b);
            packetWriter.Write(state.HullColor.a);
        }

        private static BossPresentationState ReadBoss(BinaryReader packetReader)
        {
            // Positions cross the wire in global space; each client converts to its own floating origin.
            var state = new BossPresentationState { Phase = packetReader.ReadByte(), Stage = packetReader.ReadByte() };
            byte flags = packetReader.ReadByte();
            state.Invulnerable = (flags & 1) != 0;
            state.BeamVisible = (flags & 2) != 0;
            state.BeamFiring = (flags & 4) != 0;
            state.BeamLocked = (flags & 8) != 0;
            state.HullTinted = (flags & 16) != 0;
            state.BeamOrigin = Game.World.FloatingOrigin.ToLocalPosition(new Vector3(packetReader.ReadSingle(), packetReader.ReadSingle(), packetReader.ReadSingle()));
            state.BeamDirection = new Vector3(packetReader.ReadSingle(), packetReader.ReadSingle(), packetReader.ReadSingle());
            state.HullColor = new Color(packetReader.ReadSingle(), packetReader.ReadSingle(), packetReader.ReadSingle(), packetReader.ReadSingle());
            return state;
        }

        private void CheckWipe()
        {
            // Sends the failure event once when no rostered ships remain alive.
            if (failSent)
            {
                return;
            }

            if (PlayerRoster.AliveCount() > 0)
            {
                return;
            }

            failSent = true;
            if (NetRunner.Instance != null)
            {
                NetRunner.Instance.BroadcastFail();
            }
        }

        private void HandleMatchFailed()
        {
            // Finds local death UI and opens the party-wipe screen after the host broadcasts failure.
            DeathFlow death = FindObjectOfType<DeathFlow>();
            if (death != null)
            {
                death.OpenForPartyWipe();
            }
        }

        private GameObject MakeAvatar(int slot, Vector3 home)
        {
            // Copies the local ship's visible mesh into a remote visual avatar without adding player input.
            GameObject sceneObject = new GameObject("Pilot_" + (slot + 1));
            sceneObject.layer = LayerMask.NameToLayer(GameLayers.Player);
            MeshFilter srcFilter = null;
            MeshRenderer srcRend = null;
            Quaternion hullSpin = Quaternion.identity;
            Vector3 hullScale = Vector3.one;
            Vector3 hullPosition = Vector3.zero;
            if (localPilot != null)
            {
                // Prefers the manually configured Hull child so multiplayer does not change the player's model setup.
                Transform hull = localPilot.transform.Find("Hull");
                if (hull != null)
                {
                    srcFilter = hull.GetComponent<MeshFilter>();
                    srcRend = hull.GetComponent<MeshRenderer>();
                    hullSpin = hull.localRotation;
                    hullScale = hull.localScale;
                    hullPosition = hull.localPosition;
                }

                if (srcFilter == null)
                {
                    srcFilter = localPilot.GetComponentInChildren<MeshFilter>();
                }

                if (srcRend == null && srcFilter != null)
                {
                    srcRend = srcFilter.GetComponent<MeshRenderer>();
                }
            }

            GameObject hullGo = new GameObject("Hull");
            hullGo.transform.SetParent(sceneObject.transform, false);
            hullGo.transform.localRotation = hullSpin;
            hullGo.transform.localScale = hullScale;
            hullGo.transform.localPosition = hullPosition;
            hullGo.layer = sceneObject.layer;
            MeshFilter filter = hullGo.AddComponent<MeshFilter>();
            MeshRenderer rend = hullGo.AddComponent<MeshRenderer>();
            if (srcFilter != null)
            {
                filter.sharedMesh = srcFilter.sharedMesh;
            }

            if (srcRend != null && srcRend.sharedMaterial != null)
            {
                // Creates a per-avatar material instance and tints it by slot to distinguish pilots visually.
                rend.material = srcRend.sharedMaterial;
                Color tint = PilotColours[slot % PilotColours.Length];
                rend.material.color = tint;
            }

            // Imported player hulls have nested scale/orientation mounts. Copying only their
            // first mesh loses those transforms and creates oversized, reversed avatars.
            var playerVisual = Resources.Load<GameObject>("Ships/Player0");
            if (playerVisual != null)
            {
                hullGo.SetActive(false);
                if (Application.isPlaying)
                {
                    Destroy(hullGo);
                }
                else
                {
                    DestroyImmediate(hullGo);
                }

                var visual = Instantiate(playerVisual, sceneObject.transform);
                visual.name = "Hull";

                foreach (var child in visual.GetComponentsInChildren<Transform>())
                {
                    child.gameObject.layer = sceneObject.layer;
                }
            }

            BoxCollider collider = sceneObject.AddComponent<BoxCollider>();
            BoxCollider sourceCollider = localPilot != null ? localPilot.GetComponent<BoxCollider>() : null;
            collider.center = sourceCollider != null ? sourceCollider.center : Vector3.zero;
            collider.size = sourceCollider != null ? sourceCollider.size : new Vector3(1.2f, 1.2f, 2.2f);
            Health health = sceneObject.AddComponent<Health>();
            health.maxHealth = localHealth != null ? localHealth.maxHealth : 3f;
            if (localHealth != null)
            {
                health.regenPerSecond = localHealth.regenPerSecond;
                health.regenDelay = localHealth.regenDelay;
                health.incomingDamageMul = localHealth.incomingDamageMul;
            }

            health.deactivateOnDeath = false;
            health.ResetHealth();
            int captured = slot;
            health.HealthChanged += delegate (float current, float max)
            {
                // Lets the host forward remote avatar health changes as packets for client UI consistency.
                if (NetRunner.Instance != null && NetSession.IsHost)
                {
                    NetRunner.Instance.NoteHealth(captured, current);
                }
            };
            if (NetSession.IsHost)
            {
                sceneObject.AddComponent<RemotePilotAuthority>().Slot = slot;
            }

            PlayerRoster.Add(sceneObject.transform);
            sceneObject.transform.position = SlotStart(home, slot);
            Game.Graphics.GraphicsSettingsManager.PrepareHierarchy(sceneObject);
            return sceneObject;
        }

        private GameObject MakeGhost(float scale)
        {
            // Instantiates an enemy visual with AI, weapon and local damage disabled for host-driven clients.
            if (enemyPrefab == null)
            {
                return null;
            }

            GameObject ghost = Instantiate(enemyPrefab);
            ghost.name = "NetGhost";
            EnemyController enemyController = ghost.GetComponent<EnemyController>();
            if (enemyController != null)
            {
                // Prevents a client ghost from independently moving or firing.
                enemyController.enabled = false;
            }

            Weapon weapon = ghost.GetComponentInChildren<Weapon>();
            if (weapon != null)
            {
                weapon.enabled = false;
            }

            Health health = ghost.GetComponent<Health>();
            if (health != null)
            {
                // Makes ghost health display-only because the host owns all real damage decisions.
                health.invulnerable = true;
                health.deactivateOnDeath = false;
            }

            HitFeedback feedback = ghost.GetComponent<HitFeedback>();
            if (feedback == null)
            {
                feedback = ghost.AddComponent<HitFeedback>();
            }

            feedback.EnsureWorldBar();
            ghost.transform.localScale = new Vector3(scale, scale, scale);
            CacheEnemyDef(ghost);
            Game.Graphics.GraphicsSettingsManager.PrepareHierarchy(ghost);
            return ghost;
        }

        private void CacheWeaponDefs(PlayerController pilot)
        {
            // Saves the local player weapon definition so remote player shots can use matching visual values.
            if (pilot.weapon != null)
            {
                playerWeaponDef = pilot.weapon.definition;
            }
        }

        private void CacheEnemyDef(GameObject enemy)
        {
            // Caches the first enemy weapon definition because all standard enemy ghosts share that projectile type.
            if (enemyWeaponDef != null)
            {
                return;
            }

            Weapon weapon = enemy.GetComponentInChildren<Weapon>();
            if (weapon != null)
            {
                enemyWeaponDef = weapon.definition;
            }
        }

        private static Vector3 SlotStart(Vector3 home, int slot)
        {
            // Spaces lobby slots along X so party members start separated but remain near the same combat origin.
            return home + new Vector3(slot * 16f, 0f, 0f);
        }

        private GameObject InstigatorFor(int slot)
        {
            // Resolves the firing ship for hit ownership and effects, falling back safely if an avatar is missing.
            if (slot == NetSession.LocalSlot && localPilot != null)
            {
                return localPilot.gameObject;
            }

            if (slot >= 0 && slot < avatars.Length && avatars[slot] != null)
            {
                return avatars[slot];
            }

            return gameObject;
        }
    }
}
