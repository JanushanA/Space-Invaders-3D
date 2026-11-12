using Game.Core;
using Game.Net;
using Game.Player;
using UnityEngine;
namespace Game.Combat
{
    // Fires a WeaponDefinition. Shared by the player and enemies.
    // Controls fire timing, projectile spreads and muzzle placement for a ship weapon.
    public class Weapon : MonoBehaviour
    {
        public WeaponDefinition definition;
        public Transform muzzle;
        public DamageSource damageSource = DamageSource.EnemyProjectile;

        // Leave empty to derive from the damage source.
        public LayerMask hitMaskOverride;
        public int extraProjectiles;
        public float extraSpread;
        public float fireRateMul = 1f;
        public float damageMul = 1f;
        public float speedMul = 1f;
        private float nextAllowedFireTime;
        private MeshFilter shipMesh;
        private PlayerSkills pilotSkills;
        private readonly System.Collections.Generic.List<RadarContact> acquisition = new System.Collections.Generic.List<RadarContact>();

        public Health AcquiredTarget
        {
            get; private set;
        }

        private void Awake()
        {
            // Uses a child muzzle when the ship has one, otherwise falls back to the weapon transform.
            // This allows simple enemy prefabs and detailed ships to use the same firing code.
            // Uses a child muzzle when one exists, but still works with simple ship prefabs.
            if (muzzle == null)
            {
                muzzle = transform;
            }

            shipMesh = GetComponentInParent<MeshFilter>();
            pilotSkills = GetComponentInParent<PlayerSkills>();
        }

        private void LateUpdate()
        {
            SnapMuzzleToFront();
        }

        private void OnEnable()
        {
            // Starts the fire timer after enabling so pooled enemies cannot shoot the instant they appear.
            // The delay comes from the weapon definition, keeping timing consistent for each weapon type.
            // Stops a reactivated shooter firing instantly.
            float delay = 0f;
            if (definition != null)
            {
                delay = definition.SecondsBetweenShots;
            }

            nextAllowedFireTime = Time.time + delay;
        }

        // True only if shots actually left the barrel.
        public bool TryFire()
        {
            // Rejects the request until the previous shot's cooldown has finished.
            // This makes firing speed depend on seconds rather than how many frames the game renders.
            // Rejects input until the cooldown expires. This makes fire rate independent of frame rate.
            if (Time.time < nextAllowedFireTime)
            {
                return false;
            }

            ProjectilePool pool = GetPool();

            // Stops safely when no projectile pool or prefab is available instead of creating broken shots.
            if (pool == null)
            {
                return false;
            }

            Vector3 shotPosition;
            Vector3 shotForward;
            GetShotPose(out shotPosition, out shotForward);
            if (damageSource == DamageSource.PlayerProjectile)
            {
                shotForward = AcquireDirection(shotPosition, shotForward);
            }

            ProjectileLaunchData launchData = BuildLaunchData();
            int shotCount = Mathf.Max(1, definition.projectilesPerShot + extraProjectiles);
            float spread = definition.spreadAngle + extraSpread;

            // Calculates one evenly spaced direction for every bullet in the fan.
            // The first and last shots sit on the spread edges, with remaining shots between them.
            // Evenly spaces each projectile across the requested angle to create fan-shaped shots.
            for (int shotIndex = 0; shotIndex < shotCount; shotIndex++)
            {
                Vector3 direction = FanSpread(shotForward, shotIndex, shotCount, spread);
                LaunchOne(pool, shotPosition, direction, launchData);
            }

            float shotInterval = definition.SecondsBetweenShots / Mathf.Max(0.05f, fireRateMul);

            // Dividing by the rate multiplier makes higher fire-rate upgrades reduce the time between shots.
            nextAllowedFireTime = Time.time + shotInterval;
            return true;
        }

        // Ignores the fire rate, for the player's critical attack.
        public void FireSpray(int bulletCount, float spreadAngle)
        {
            // Fires a skill-controlled burst without waiting for normal weapon cooldown.
            // The critical skill controls its own timer, so this method only creates the individual bullets.
            // Bypasses the normal cooldown because the critical skill owns its own duration and cooldown.
            ProjectilePool pool = GetPool();
            if (pool == null)
            {
                return;
            }

            Vector3 shotPosition;
            Vector3 shotForward;
            GetShotPose(out shotPosition, out shotForward);
            if (damageSource == DamageSource.PlayerProjectile)
            {
                shotForward = AcquireDirection(shotPosition, shotForward);
            }

            ProjectileLaunchData launchData = BuildLaunchData();

            for (int bulletIndex = 0; bulletIndex < bulletCount; bulletIndex++)
            {
                // Picks a different yaw and pitch offset for each bullet to create a messy spray cone.
                Vector3 direction = RandomSpread(shotForward, spreadAngle);
                LaunchOne(pool, shotPosition, direction, launchData);
            }
        }

        public Vector3 AcquireDirection(Vector3 origin, Vector3 forward)
        {
            acquisition.Clear();
            AcquiredTarget = null;

            // PlayerController may attach PlayerSkills after this child weapon's Awake.
            if (pilotSkills == null)
            {
                pilotSkills = GetComponentInParent<PlayerSkills>();
            }

            float range = pilotSkills != null && pilotSkills.SensorRange > 150 ? 150 : 100;
            RadarIndex.Query(origin, range, RadarKind.Hostile, acquisition);
            float best = Mathf.Cos(3f * Mathf.Deg2Rad);

            foreach (RadarContact contact in acquisition)
            {
                if (contact.Health == null)
                {
                    continue;
                }

                Vector3 delta = contact.transform.position - origin;
                float alignment = Vector3.Dot(forward.normalized, delta.normalized);
                if (alignment <= best || !RadarIndex.LineOfSight(origin, contact))
                {
                    continue;
                }

                best = alignment;
                AcquiredTarget = contact.Health;
            }

            return AcquiredTarget != null ? (AcquiredTarget.transform.position - origin).normalized : forward;
        }

        private void GetShotPose(out Vector3 position, out Vector3 direction)
        {
            FlightView view = FlightView.Instance;
            if (view != null && view.OwnsWeapon(this))
            {
                view.GetShotPose(this, out position, out direction);
                return;
            }

            Transform origin = GetMuzzle();
            position = origin.position;
            direction = origin.forward;
        }

        private void LaunchOne(ProjectilePool pool, Vector3 position, Vector3 direction, ProjectileLaunchData launchData)
        {
            // Requests a reusable projectile and passes its complete launch data in one object.
            // Pooling avoids repeated Instantiate and Destroy calls during rapid firing.
            // Gets a reusable bullet and gives it all movement and damage values for this shot.
            Projectile projectile = pool.Get(definition.projectilePrefab);
            if (projectile != null)
            {
                projectile.Launch(position, direction, launchData, pool);
            }

            if (NetRunner.HasInstance)
            {
                // Reports the shot to networking after launching the local visual projectile.
                // Remote machines can then recreate the same fire event from its origin and direction.
                // Tells multiplayer peers about the shot so they can show the same visual projectile.
                NetRunner.Instance.NoteFire(position, direction, launchData.Source, projectile);
            }
        }

        // Null whenever this weapon cannot shoot at all.
        private ProjectilePool GetPool()
        {
            if (definition == null || definition.projectilePrefab == null)
            {
                return null;
            }

            return ProjectilePool.Instance;
        }

        private Transform GetMuzzle()
        {
            // Returns a valid transform even if the prefab does not have a separate muzzle child.
            // This keeps weapons functional while artists are still building a ship model.
            if (muzzle != null)
            {
                return muzzle;
            }

            return transform;
        }

        // Sits just past the mesh nose, along the current aim.
        private void SnapMuzzleToFront()
        {
            FlightView view = FlightView.Instance;
            if (view != null && view.OwnsWeapon(this))
            {
                Vector3 position;
                Vector3 direction;
                view.GetShotPose(this, out position, out direction);

                // Keep the aim pivot rotation independent of convergence and fan spread.
                if (muzzle != null && muzzle != transform.root)
                {
                    muzzle.position = position;
                }

                return;
            }

            // Reads the hull mesh bounds so the muzzle follows the visible nose of different ship models.
            // This prevents bullets appearing from the centre of a long ship.
            // Finds the visible hull so shots begin at its nose instead of the ship's centre.
            MeshFilter filter = shipMesh;
            if (filter == null)
            {
                Transform hull = transform.root.Find("Hull");
                if (hull != null)
                {
                    filter = hull.GetComponent<MeshFilter>();
                }

                if (filter == null)
                {
                    filter = GetComponentInParent<MeshFilter>();
                }

                shipMesh = filter;
            }

            if (filter == null || filter.sharedMesh == null)
            {
                return;
            }

            Transform ship = filter.transform;
            Transform origin = GetMuzzle();
            float scaleZ = Mathf.Abs(ship.lossyScale.z);
            if (scaleZ < 0.0001f)
            {
                scaleZ = 1f;
            }

            float noseDistance = filter.sharedMesh.bounds.max.z * scaleZ + 0.2f;

            // Converts the mesh's local front edge into world scale, then adds a small visual gap.
            // Limits unusual imported mesh bounds so a bad model cannot place bullets far away.
            if (noseDistance < 0.3f)
            {
                noseDistance = 0.3f;
            }

            if (noseDistance > 8f)
            {
                // Caps bad imported bounds so one unusually scaled mesh cannot launch bullets far away.
                noseDistance = 8f;
            }

            origin.position = transform.root.position + origin.forward * noseDistance;
        }

        private ProjectileLaunchData BuildLaunchData()
        {
            // Selects the layers this projectile is allowed to hit from the damage source.
            // A custom mask can override that rule for special weapons.
            // Chooses the target layers from the weapon owner, unless this weapon has a custom mask.
            LayerMask hitMask = hitMaskOverride;
            if (hitMask.value == 0)
            {
                hitMask = GameLayers.HitMaskFor(damageSource);
            }

            return new ProjectileLaunchData(
                definition.projectileSpeed * Mathf.Max(0.2f, speedMul),
                definition.damagePerProjectile * Mathf.Max(0.1f, damageMul),
                definition.projectileRadius,
                definition.projectileLifetime,
                damageSource,
                gameObject,
                hitMask);
        }

        // Fans shot i of total across the spread angle.
        private Vector3 FanSpread(Vector3 forward, int index, int total, float spread)
        {
            if (total <= 1 || spread <= 0f)
            {
                return forward;
            }

            float angle = -spread * 0.5f + spread / (total - 1) * index;

            // Rotates around world-up so the fan stays horizontal even when the ship aims up or down.
            // Rotates around world-up so the spread remains horizontal even when the ship is pitched.
            return Quaternion.AngleAxis(angle, Vector3.up) * forward;
        }

        // Anywhere inside a cone, so a spray looks messy.
        private static Vector3 RandomSpread(Vector3 forward, float spreadAngle)
        {
            if (spreadAngle <= 0f)
            {
                return forward;
            }

            float half = spreadAngle * 0.5f;
            float yawDegrees = Random.Range(-half, half);
            float pitch = Random.Range(-half, half);

            // Builds a rotation pointing forward first, then applies random vertical and horizontal offsets.
            // This creates directions inside a cone rather than only along a flat line.
            // Builds a rotation from the shot direction, then offsets both axes inside the spray cone.
            Quaternion aimRotation = Quaternion.LookRotation(forward, Vector3.up);
            return aimRotation * Quaternion.Euler(pitch, yawDegrees, 0f) * Vector3.forward;
        }
    }
}
