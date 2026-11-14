using System.Collections.Generic;
using Game.Combat;
using Game.Net;
using UnityEngine;
namespace Game.AI
{
    public sealed class SquadronCoordinator : MonoBehaviour
    {
        public SquadronMember[] Members
        {
            get; private set;
        }
        readonly Collider[] overlaps = new Collider[48];
        readonly Collider[] obstacles = new Collider[48];
        Vector3[] slots = new Vector3[0];
        int[] assignment = new int[0];
        float nextScan;
        float born;
        Transform target;

        public static SquadronCoordinator Create(IList<GameObject> fighters)
        {
            if (NetSession.Active && !NetSession.IsHost || fighters == null || fighters.Count < 2)
            {
                return null;
            }

            var coordinatorObject = new GameObject("Predictive Squadron");
            var coordinator = coordinatorObject.AddComponent<SquadronCoordinator>();
            int memberCount = Mathf.Min(8, fighters.Count);
            coordinator.Members = new SquadronMember[memberCount];
            coordinator.born = Time.time;

            for (int memberIndex = 0; memberIndex < memberCount; memberIndex++)
            {
                if (fighters[memberIndex] == null)
                {
                    continue;
                }

                var member = fighters[memberIndex].GetComponent<SquadronMember>();
                if (member == null)
                {
                    member = fighters[memberIndex].AddComponent<SquadronMember>();
                }

                member.Coordinator = coordinator;
                coordinator.Members[memberIndex] = member;
            }

            return coordinator;
        }

        void Update()
        {
            if (NetSession.Active && !NetSession.IsHost)
            {
                return;
            }

            if (Time.time < nextScan)
            {
                return;
            }

            nextScan = Time.time + 0.4f;
            if (Members == null)
            {
                return;
            }

            // Rebuild assignments only when membership changes, avoiding slot swaps every scan.
            var liveMembers = new List<SquadronMember>(8);

            foreach (var member in Members)
            {
                if (member != null && member.isActiveAndEnabled && member.GetComponent<Health>().IsAlive)
                {
                    liveMembers.Add(member);
                }
            }

            bool changed = liveMembers.Count != Members.Length;
            if (changed)
            {
                Members = liveMembers.ToArray();
            }

            if (Members.Length == 0)
            {
                Destroy(gameObject);
                return;
            }

            target = PlayerRoster.Nearest(Members[0].transform.position);
            if (target == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    target = player.transform;
                }
            }

            if (target == null)
            {
                return;
            }

            int memberCount = Members.Length;
            if (slots.Length != memberCount)
            {
                slots = new Vector3[memberCount];
                changed = true;
            }

            // Start ahead of the target, then gradually surround it after the opening approach.
            float surround = Mathf.Clamp01((Time.time - born - 8) / 8);
            Vector3 forward = target.forward;
            forward.y = 0;
            forward.Normalize();
            if (forward.sqrMagnitude < 0.1f)
            {
                forward = Vector3.forward;
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward);

            for (int memberIndex = 0; memberIndex < memberCount; memberIndex++)
            {
                Vector3 formation = target.position
                    + forward * (23 + Mathf.Abs(memberIndex - (memberCount - 1) * 0.5f) * 3)
                    + right * ((memberIndex - (memberCount - 1) * 0.5f) * 6);
                float angle = memberIndex * Mathf.PI * 2 / memberCount + (Time.time - born) * 0.08f;
                Vector3 ring = target.position + new Vector3(
                    Mathf.Sin(angle) * 23,
                    Mathf.Sin(memberIndex * 2) * 2,
                    Mathf.Cos(angle) * 23);
                slots[memberIndex] = Vector3.Lerp(formation, ring, surround);
            }

            if (changed || assignment.Length != memberCount)
            {
                var positions = new Vector3[memberCount];

                for (int memberIndex = 0; memberIndex < memberCount; memberIndex++)
                {
                    positions[memberIndex] = Members[memberIndex].transform.position;
                }

                assignment = FormationSolver.Assign(positions, slots);
            }
        }

        public Vector3 Steering(SquadronMember member, float deltaTime)
        {
            int index = System.Array.IndexOf(Members, member);
            if (index < 0 || index >= assignment.Length)
            {
                return Vector3.zero;
            }

            Vector3 desired = Vector3.ClampMagnitude((slots[assignment[index]] - member.transform.position) * 0.8f, 8 * Mathf.Clamp(member.MobilityMultiplier, 0.2f, 1));

            // Ships and projectiles have separate avoidance handling; retain only world geometry here.
            int overlapCount = Physics.OverlapSphereNonAlloc(member.transform.position, 20, overlaps, ~0, QueryTriggerInteraction.Ignore);
            int obstacleCount = 0;

            for (int overlapIndex = 0; overlapIndex < overlapCount; overlapIndex++)
            {
                var obstacle = overlaps[overlapIndex];
                if (obstacle == null || obstacle.transform.IsChildOf(member.transform)
                    || obstacle.GetComponentInParent<Health>() != null
                    || obstacle.GetComponentInParent<Projectile>() != null)
                {
                    continue;
                }

                obstacles[obstacleCount++] = obstacle;
            }

            return AvoidanceSolver.Choose(member.transform.position, member.Velocity, desired, deltaTime, member, Members, obstacles, obstacleCount);
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
            for (int memberIndex = 0; memberIndex < slots.Length; memberIndex++)
            {
                slots[memberIndex] += delta;
            }
        }
    }

    public sealed class SquadronMember : MonoBehaviour
    {
        public SquadronCoordinator Coordinator;
        public float MobilityMultiplier = 1;

        public Vector3 Velocity
        {
            get; private set;
        }

        public bool Tick(Transform target, float deltaTime, out Vector3 aimDirection)
        {
            aimDirection = target.position;
            if (Coordinator == null)
            {
                return false;
            }

            Velocity = Coordinator.Steering(this, Mathf.Min(deltaTime, 0.1f));
            Game.World.SweptMovement.Move(transform, Velocity * deltaTime);
            Vector3 facing = target.position - transform.position;
            facing.y = 0;
            if (facing.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), 95 * deltaTime);
            }

            return true;
        }
    }
}
