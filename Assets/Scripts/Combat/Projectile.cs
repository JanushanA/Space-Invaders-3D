using Game.Core;
using UnityEngine;
namespace Game.Combat
{
    // Everything a shot needs at launch.
    // Groups the data a weapon gives a projectile when it is launched.
    public struct ProjectileLaunchData
    {
        public float Speed;
        public float Damage;
        public float Radius;
        public float Lifetime;
        public DamageSource Source;
        public GameObject Instigator;
        public LayerMask HitMask;

        public ProjectileLaunchData(
            float speed,
            float damage,
            float radius,
            float lifetime,
            DamageSource source,
            GameObject instigator,
            LayerMask hitMask)
        {
            Speed = speed;
            Damage = damage;
            Radius = radius;
            Lifetime = lifetime;
            Source = source;
            Instigator = instigator;
            HitMask = hitMask;
        }
    }

    // A single shot. Sphere casts its whole path, so it cannot tunnel through a target.
    // Moves a projectile, checks its full travel path for hits, then returns it to its pool.
    public class Projectile : MonoBehaviour
    {
        public bool InFlight => inFlight;

        public uint NetworkId
        {
            get; internal set;
        }

        public DamageSource Source => source;

        public Vector3 Velocity => direction * speed;
        private Vector3 direction = Vector3.forward;
        private float speed;
        private float damage;
        private float radius;
        private float lifeRemaining;
        private DamageSource source;
        private GameObject instigator;
        private LayerMask hitMask;
        private ProjectilePool owningPool;
        private bool inFlight;
        private TrailRenderer trail;
        private ParticleSystem sparks;
        private bool vfxReady;

        public void Launch(Vector3 position, Vector3 travelDirection, ProjectileLaunchData data, ProjectilePool pool)
        {
            // Resets every value that may remain from the projectile's last pooled use.
            // This prevents an old owner, speed, damage value, or trail state affecting the next shot.
            // Resets a pooled projectile with fresh data so its previous shot cannot affect this one.
            Game.Net.NetCombat.Instance?.ForgetNetworkProjectile(this);
            NetworkId = 0;
            EnsureVfx();
            transform.position = position;
            if (travelDirection.sqrMagnitude > 0.0001f)
            {
                // Normalising removes distance from the supplied vector so it acts as direction only.
                direction = travelDirection.normalized;
            }
            else
            {
                // Uses the object's current forward direction as a safe fallback for an invalid aim vector.
                direction = transform.forward;
            }

            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            speed = Mathf.Max(0.1f, data.Speed);
            damage = Mathf.Max(0f, data.Damage);
            radius = Mathf.Max(0.01f, data.Radius);
            lifeRemaining = Mathf.Max(0.1f, data.Lifetime);
            source = data.Source;
            RadarContact.Track(gameObject, RadarKind.Missile);
            instigator = data.Instigator;
            hitMask = data.HitMask;
            owningPool = pool;
            inFlight = true;
            ColourVfx();
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }

            if (sparks != null)
            {
                sparks.Play(true);
            }
        }

        private void Update()
        {
            if (!inFlight)
            {
                return;
            }

            lifeRemaining -= Time.deltaTime;

            // Counts down in seconds so projectile lifetime stays the same at all frame rates.
            if (lifeRemaining <= 0f)
            {
                Despawn();
                return;
            }

            float stepDistance = speed * Time.deltaTime;

            // Calculates how far this projectile should travel during exactly this rendered frame.
            Vector3 origin = transform.position;
            RaycastHit hit;

            // Tests the entire distance travelled this frame. The sphere radius catches fast shots
            // that could otherwise jump through a target between two frames.
            if (Physics.SphereCast(origin, radius, direction, out hit, stepDistance, hitMask, QueryTriggerInteraction.Ignore))
            {
                // Tests the whole travel path rather than only the final position.
                // The sphere radius stops fast bullets from passing through thin colliders between frames.
                // A cast starting inside a collider reports distance 0 and a useless point.
                Vector3 impactPoint = hit.point;
                if (hit.distance <= 0f)
                {
                    // Uses the current position when the cast starts inside a collider and has no useful hit point.
                    impactPoint = origin;
                }

                Vector3 impactNormal = hit.normal;
                if (impactNormal.sqrMagnitude < 0.0001f)
                {
                    // Falls back to the opposite travel direction when Unity cannot provide a usable surface normal.
                    impactNormal = -direction;
                }

                transform.position = impactPoint;
                SpawnHitSpark(impactPoint);
                HandleImpact(hit.collider, impactPoint, impactNormal);
                return;
            }

            transform.position = origin + direction * stepDistance;

            // Moves only after confirming there was no collision along this frame's travel path.
        }

        private void HandleImpact(Collider hitCollider, Vector3 point, Vector3 normal)
        {
            // Searches upward from a child collider because Health is normally placed on the ship root.
            // This lets detailed models use many colliders without duplicating health components.
            // The collider often sits on a child of the object holding the health.
            IDamageable target = hitCollider.GetComponentInParent<IDamageable>();
            if (target != null && target.IsAlive)
            {
                // Sends a single damage message that works for players, fighters and bosses.
                target.ApplyDamage(new DamageInfo(damage, source, instigator, point, normal));
            }

            Despawn();
        }

        public void Despawn()
        {
            // Stops effects before returning the projectile to its owner pool for a later shot.
            // Falling back to Destroy still supports projectiles created outside a pool.
            // Stops visual effects before returning this projectile to the pool for a later shot.
            if (!inFlight)
            {
                return;
            }

            inFlight = false;
            Game.Net.NetCombat.Instance?.ForgetNetworkProjectile(this);
            NetworkId = 0;
            if (trail != null)
            {
                trail.emitting = false;
                trail.Clear();
            }

            if (sparks != null)
            {
                sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (owningPool != null)
            {
                owningPool.Release(this);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDisable()
        {
            Game.Net.NetCombat.Instance?.ForgetNetworkProjectile(this);
            NetworkId = 0;
            inFlight = false;
        }

        private void EnsureVfx()
        {
            // Creates trail and particle components only once for each pooled projectile.
            // Reusing those components avoids allocations every time a weapon fires.
            // Creates effects once per pooled object instead of creating new renderers every launch.
            if (vfxReady)
            {
                return;
            }

            VisualUtil.HideShadows(gameObject);
            Color colour = ShotColour();
            Material shotMat = VisualUtil.MakeAdditive(colour);
            if (shotMat == null)
            {
                return;
            }

            trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.14f;
            trail.minVertexDistance = 0.05f;
            trail.startWidth = 0.18f;
            trail.endWidth = 0f;
            trail.material = shotMat;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            GameObject sparkObject = new GameObject("ShotSparks");
            sparkObject.transform.SetParent(transform, false);
            sparks = sparkObject.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = sparks.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 0.12f;
            main.startSpeed = 0.4f;
            main.startSize = 0.08f;
            main.startColor = colour;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;
            main.gravityModifier = 0f;
            ParticleSystem.EmissionModule emission = sparks.emission;
            emission.rateOverTime = 40f;
            ParticleSystem.ShapeModule shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.02f;
            ParticleSystemRenderer renderer = sparkObject.GetComponent<ParticleSystemRenderer>();
            renderer.material = VisualUtil.MakeAdditive(colour);
            VisualUtil.HideShadows(renderer);
            vfxReady = true;
        }

        private void ColourVfx()
        {
            // Matches the trail colour to the damage source so player and enemy shots stay readable.
            Color colour = ShotColour();
            if (trail != null)
            {
                trail.startColor = colour;
                trail.endColor = new Color(colour.r, colour.g, colour.b, 0f);
            }
        }

        private Color ShotColour()
        {
            if (source == DamageSource.EnemyProjectile)
            {
                return new Color(1f, 0.25f, 0.15f, 1f);
            }

            return new Color(0.35f, 0.85f, 1f, 1f);
        }

        private void SpawnHitSpark(Vector3 point)
        {
            // Creates a very short particle burst where a shot collides to confirm the impact.
            // Unlike bullets, this effect is cheap and short enough to destroy after playing once.
            // Uses a short-lived particle burst for impacts because it does not need to be reused.
            GameObject burst = new GameObject("HitSpark");
            burst.transform.position = point;
            ParticleSystem particleSystem = burst.AddComponent<ParticleSystem>();
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particleSystem.main;
            main.loop = false;
            main.playOnAwake = true;
            main.duration = 0.2f;
            main.startLifetime = 0.2f;
            main.startSpeed = 4f;
            main.startSize = 0.1f;
            main.startColor = ShotColour();
            main.gravityModifier = 0f;
            main.maxParticles = 16;
            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 12) });
            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
            ParticleSystemRenderer renderer = burst.GetComponent<ParticleSystemRenderer>();
            renderer.material = VisualUtil.MakeAdditive(ShotColour());
            VisualUtil.HideShadows(renderer);
            Destroy(burst, 0.4f);
        }
    }
}
