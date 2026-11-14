using Game.Combat;
using Game.Core;
using Game.Net;
using UnityEngine;
namespace Game.AI
{
    // One offline-trained elite. The existing controller retains lifecycle, XP and firing.
    public sealed class AcePilot : MonoBehaviour
    {
        readonly System.Random random = new System.Random(234);
        AcePolicy policy;
        readonly float[] observation = new float[12];
        Vector3 previousTarget;
        Vector3 targetVelocity;
        Vector3 velocity;
        Transform previousTransform;
        float nextDecision;
        float lastSample;
        float shotReady;
        int action = 3;
        Health health;
        public float MobilityMultiplier = 1;

        public bool Ready
        {
            get
            {
                return policy != null;
            }
        }

        public static AcePilot Attach(GameObject fighter)
        {
            if (fighter == null || NetSession.Active && !NetSession.IsHost)
            {
                return null;
            }

            var acePilot = fighter.GetComponent<AcePilot>();
            if (acePilot == null)
            {
                acePilot = fighter.AddComponent<AcePilot>();
            }

            return acePilot;
        }

        void Awake()
        {
            policy = AcePolicy.Load();
            health = GetComponent<Health>();
            if (policy == null)
            {
                Debug.LogWarning("Ace policy unavailable; retaining scripted controller", this);
            }
        }

        void OnEnable()
        {
            Game.World.FloatingOrigin.Shifted += Shift;
        }

        void OnDisable()
        {
            Game.World.FloatingOrigin.Shifted -= Shift;
        }

        void Shift(Vector3 delta)
        {
            previousTarget += delta;
        }

        public bool Tick(Transform target, float deltaTime, Weapon weapon, out Vector3 aimDirection)
        {
            aimDirection = target.position;
            if (policy == null || target == null || NetSession.Active && !NetSession.IsHost)
            {
                return false;
            }

            Vector3 targetOffset = target.position - transform.position;
            targetOffset.y = 0;
            float range = targetOffset.magnitude;
            Vector3 forward = range > 0.01f ? targetOffset / range : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (Time.time >= nextDecision)
            {
                // Estimate target motion only across consecutive samples of the same target.
                float sampleElapsed = Time.time - lastSample;
                targetVelocity = previousTransform == target && sampleElapsed > 0.001f
                    ? Vector3.ClampMagnitude((target.position - previousTarget) / sampleElapsed, 30)
                    : Vector3.zero;
                previousTransform = target;
                previousTarget = target.position;
                lastSample = Time.time;
                nextDecision = Time.time + 0.2f;
                Projectile incoming = null;
                float nearestDistanceSquared = 40 * 40;

                // Existing pooled shots only; bounded scan, no FindObjects allocation or private reflection.
                var projectilePool = ProjectilePool.Instance;
                if (projectilePool != null)
                {
                    for (int projectileIndex = 0; projectileIndex < Mathf.Min(512, projectilePool.transform.childCount); projectileIndex++)
                    {
                        var candidate = projectilePool.transform.GetChild(projectileIndex).GetComponent<Projectile>();
                        if (candidate == null || !candidate.isActiveAndEnabled
                            || !candidate.InFlight || candidate.Source != DamageSource.PlayerProjectile)
                        {
                            continue;
                        }

                        Vector3 relative = candidate.transform.position - transform.position;
                        float distanceSquared = relative.sqrMagnitude;
                        if (distanceSquared < nearestDistanceSquared && Vector3.Dot(relative, candidate.Velocity - velocity) < 0)
                        {
                            nearestDistanceSquared = distanceSquared;
                            incoming = candidate;
                        }
                    }
                }

                // Match the twelve channels and normalization used by the training simulator.
                observation[0] = range / 40;
                observation[1] = Vector3.Dot(targetVelocity, right) / 8;
                observation[2] = Vector3.Dot(targetVelocity, forward) / 8;
                observation[3] = Vector3.Dot(velocity, right) / 8;
                observation[4] = Vector3.Dot(velocity, forward) / 8;
                Vector3 projectileRelative = incoming == null ? Vector3.zero : incoming.transform.position - transform.position;
                Vector3 projectileVelocity = incoming == null ? Vector3.zero : incoming.Velocity;
                observation[5] = Vector3.Dot(projectileRelative, right) / 30;
                observation[6] = Vector3.Dot(projectileRelative, forward) / 30;
                observation[7] = Vector3.Dot(projectileVelocity, right) / 50;
                observation[8] = Vector3.Dot(projectileVelocity, forward) / 50;
                observation[9] = incoming == null ? 0 : 1;
                observation[10] = health == null ? 1 : health.CurrentHealth / Mathf.Max(1, health.maxHealth);
                observation[11] = Mathf.Max(0, shotReady - Time.time);
                action = policy.Decide(observation, (float)random.NextDouble());
            }

            // Three lateral choices combine with three lead-aim choices to make nine actions.
            Vector3 desired = right * ((action / 3 - 1) * 7) + forward * Mathf.Clamp((range - 18) * 0.8f, -4, 4);
            velocity = Vector3.MoveTowards(velocity, desired * Mathf.Clamp(MobilityMultiplier, 0.2f, 1), 12 * deltaTime);
            Vector3 movement = velocity * deltaTime;
            movement.y = Mathf.Clamp(target.position.y - transform.position.y, -3 * deltaTime, 3 * deltaTime);
            Game.World.SweptMovement.Move(transform, movement);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(forward), 110 * deltaTime);
            float projectileSpeed = weapon != null && weapon.definition != null ? weapon.definition.projectileSpeed : 20;
            aimDirection = target.position + targetVelocity * (range / Mathf.Max(1, projectileSpeed) * (action % 3) * 0.5f);
            return true;
        }

        public void Fired()
        {
            shotReady = Time.time + 1;
        }
    }
}
