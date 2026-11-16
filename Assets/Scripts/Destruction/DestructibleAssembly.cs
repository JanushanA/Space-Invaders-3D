using Game.Combat;
using Game.Core;
using Game.Enemies;
using Game.Net;
using Game.Progression;
using UnityEngine;
namespace Game.Destruction
{
    // Five convex structural modules augment an existing ship/station; the original hull asset is preserved.
    public sealed class DestructibleAssembly : MonoBehaviour
    {
        sealed class Module
        {
            public GameObject body;
            public float remaining;
            public bool intact = true;
            public bool engine;
        }

        // The center spine connects to each wing support, which carries its engine.
        static readonly int[,] Links = { { 0, 1 }, { 1, 2 }, { 0, 3 }, { 3, 4 } };
        readonly Module[] modules = new Module[5];
        Health health;
        EnemyController controller;
        Weapon weapon;
        float originalAdvance;
        float originalFire;
        Material hullMaterial;
        Material cutMaterial;
        bool initialized;

        public int DetachedCount
        {
            get; private set;
        }

        public int LiveEngineCount { get; private set; } = 2;

        public static DestructibleAssembly Attach(GameObject ship)
        {
            if (ship == null || NetSession.Active && !NetSession.IsHost)
            {
                return null;
            }

            var assembly = ship.GetComponent<DestructibleAssembly>();
            if (assembly == null)
            {
                assembly = ship.AddComponent<DestructibleAssembly>();
            }

            assembly.Initialize();
            return assembly;
        }

        void Initialize()
        {
            if (initialized)
            {
                return;
            }

            health = GetComponent<Health>();
            if (health == null)
            {
                return;
            }

            initialized = true;
            controller = GetComponent<EnemyController>();
            weapon = GetComponentInChildren<Weapon>();
            originalAdvance = controller != null ? controller.advanceSpeed : 0;
            originalFire = weapon != null ? weapon.fireRateMul : 1;
            hullMaterial = Game.Graphics.GraphicsMaterials.Lit("Fracture modules", new Color(0.15f, 0.24f, 0.29f), 0.75f, 0.55f);
            cutMaterial = Game.Graphics.GraphicsMaterials.Lit("Exposed cut alloy", new Color(0.95f, 0.36f, 0.08f), 0.5f, 0.35f);
            Vector3[] positions =
            {
                new Vector3(0, -0.38f, -0.25f),
                new Vector3(-1.15f, -0.2f, -0.2f),
                new Vector3(-1.9f, -0.2f, -0.65f),
                new Vector3(1.15f, -0.2f, -0.2f),
                new Vector3(1.9f, -0.2f, -0.65f)
            };
            Vector3[] scales =
            {
                new Vector3(1.3f, 0.4f, 1.8f),
                new Vector3(1.1f, 0.32f, 1.3f),
                new Vector3(0.65f, 0.65f, 1.6f),
                new Vector3(1.1f, 0.32f, 1.3f),
                new Vector3(0.65f, 0.65f, 1.6f)
            };

            for (int moduleIndex = 0; moduleIndex < 5; moduleIndex++)
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = moduleIndex == 0 ? "Structural spine" : moduleIndex == 2 || moduleIndex == 4 ? "Detachable engine" : "Wing support";
                body.layer = gameObject.layer;
                body.transform.SetParent(transform, false);
                body.transform.localPosition = positions[moduleIndex];
                body.transform.localScale = scales[moduleIndex];
                body.GetComponent<Renderer>().sharedMaterial = hullMaterial;
                modules[moduleIndex] = new Module
                {
                    body = body,
                    remaining = Mathf.Max(0.5f, health.maxHealth * 0.18f) * (moduleIndex == 0 ? 1.5f : 1),
                    engine = moduleIndex == 2 || moduleIndex == 4
                };
            }

            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        void OnDamaged(Health source, DamageInfo hit)
        {
            if (!initialized || NetSession.Active && !NetSession.IsHost)
            {
                return;
            }

            // Attribute the impact to the closest surviving module collider.
            int nearestModule = -1;
            float distance = float.PositiveInfinity;

            for (int moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
            {
                Module module = modules[moduleIndex];
                if (!module.intact || module.body == null)
                {
                    continue;
                }

                var collider = module.body.GetComponent<Collider>();
                float distanceSquared = (collider.ClosestPoint(hit.HitPoint) - hit.HitPoint).sqrMagnitude;
                if (distanceSquared < distance)
                {
                    distance = distanceSquared;
                    nearestModule = moduleIndex;
                }
            }

            if (nearestModule < 0)
            {
                return;
            }

            // Hits far from the added structure still damage the hull, but do not break an unrelated section.
            if (distance > 4)
            {
                return;
            }

            modules[nearestModule].remaining -= hit.Amount * Mathf.Max(0, health.incomingDamageMul);
            if (modules[nearestModule].remaining > 0)
            {
                return;
            }

            Detach(nearestModule, hit, true);
            ReleaseDisconnected(hit);
            ApplySystems();
        }

        void OnDied(Health source, DamageInfo hit)
        {
            if (NetSession.Active && !NetSession.IsHost)
            {
                return;
            }

            for (int moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
            {
                if (modules[moduleIndex] != null && modules[moduleIndex].intact)
                {
                    Detach(moduleIndex, hit, moduleIndex == 0);
                }
            }

            ApplySystems();
        }

        void ReleaseDisconnected(DamageInfo hit)
        {
            var intact = new bool[modules.Length];

            for (int moduleIndex = 0; moduleIndex < intact.Length; moduleIndex++)
            {
                intact[moduleIndex] = modules[moduleIndex].intact;
            }

            // Any intact section disconnected from the spine falls away with the destroyed section.
            bool[] connected = StructuralGraph.Connected(intact, Links, 0);

            for (int moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
            {
                if (modules[moduleIndex].intact && !connected[moduleIndex])
                {
                    Detach(moduleIndex, hit, false);
                }
            }
        }

        void Detach(int index, DamageInfo hit, bool shouldFracture)
        {
            Module module = modules[index];
            if (!module.intact || module.body == null)
            {
                return;
            }

            module.intact = false;
            DetachedCount++;
            Transform pose = module.body.transform;
            Mesh original = module.body.GetComponent<MeshFilter>().sharedMesh;
            Vector3 normal = hit.HitNormal.sqrMagnitude > 0.01f ? hit.HitNormal.normalized : transform.right;

            // Transform the cut plane into module space, accounting for nonuniform scale.
            Vector3 localNormal = pose.localToWorldMatrix.transpose.MultiplyVector(normal).normalized;
            Vector3 point = pose.InverseTransformPoint(hit.HitPoint);
            point = new Vector3(Mathf.Clamp(point.x, -0.3f, 0.3f), Mathf.Clamp(point.y, -0.3f, 0.3f), Mathf.Clamp(point.z, -0.3f, 0.3f));
            var inherited = GetComponent<Rigidbody>();
            Vector3 velocity = inherited != null ? inherited.velocity : transform.forward * (controller != null ? controller.advanceSpeed : 0);
            if (shouldFracture && ConvexMeshCutter.Split(original, new Plane(localNormal, point), out Mesh positiveFragment, out Mesh negativeFragment))
            {
                FractureDebris.Spawn(pose, positiveFragment, hullMaterial, cutMaterial, velocity + normal * 2, normal);
                FractureDebris.Spawn(pose, negativeFragment, hullMaterial, cutMaterial, velocity - normal * 2, -normal);
            }
            else
            {
                FractureDebris.Spawn(pose, Instantiate(original), hullMaterial, cutMaterial, velocity, normal);
            }

            Vector3 salvagePosition = pose.position;
            module.body.SetActive(false);
            Release(module.body);

            // The intact flag is set before grants; repeated damage/death cannot award a section twice.
            if (NetSession.Active)
            {
                NetRunner.Instance?.AwardSalvage(hit.Instigator, module.engine ? 3 : 0, 1, -1, 0, false);
                SalvagePickup.Create(salvagePosition, module.engine ? 3 : 0, 0, -1, 0, true);
            }
            else
            {
                SalvagePickup.Create(salvagePosition, module.engine ? 3 : 0, 1, -1, 0, false);
            }
        }

        void ApplySystems()
        {
            LiveEngineCount = 0;

            foreach (Module module in modules)
            {
                if (module != null && module.intact && module.engine)
                {
                    LiveEngineCount++;
                }
            }

            // Each lost engine reduces both movement and firing performance.
            float mobility = 0.4f + 0.3f * LiveEngineCount;
            if (controller != null)
            {
                controller.advanceSpeed = originalAdvance * mobility;
            }

            if (weapon != null)
            {
                weapon.fireRateMul = originalFire * (0.65f + 0.175f * LiveEngineCount);
            }

            var acePilot = GetComponent<Game.AI.AcePilot>();
            if (acePilot != null)
            {
                acePilot.MobilityMultiplier = mobility;
            }

            var squadronMember = GetComponent<Game.AI.SquadronMember>();
            if (squadronMember != null)
            {
                squadronMember.MobilityMultiplier = mobility;
            }
        }

        internal static void Release(Object item)
        {
            if (Application.isPlaying)
            {
                Destroy(item);
            }
            else
            {
                DestroyImmediate(item);
            }
        }

        void OnDestroy()
        {
            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
            }

            if (hullMaterial != null)
            {
                Release(hullMaterial);
            }

            if (cutMaterial != null)
            {
                Release(cutMaterial);
            }
        }
    }

    public sealed class FractureDebris : MonoBehaviour
    {
        public static int ActiveCount
        {
            get; private set;
        }
        Mesh ownedMesh;
        Material[] ownedMaterials;
        float expiry;
        bool counted;

        public static void Spawn(Transform pose, Mesh mesh, Material hull, Material cutSurfaceMaterial, Vector3 velocity, Vector3 impulse)
        {
            if (ActiveCount >= 32)
            {
                DestructibleAssembly.Release(mesh);
                return;
            }

            var debrisObject = new GameObject("Runtime fractured alloy");
            debrisObject.layer = Mathf.Max(0, LayerMask.NameToLayer(GameLayers.LevelGeometry));
            debrisObject.transform.SetPositionAndRotation(pose.position, pose.rotation);
            debrisObject.transform.localScale = pose.lossyScale;
            var filter = debrisObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            // Debris owns its material copies so destroying the parent ship cannot invalidate them.
            var renderer = debrisObject.AddComponent<MeshRenderer>();
            var debrisMaterials = mesh.subMeshCount > 1
                ? new[] { new Material(hull), new Material(cutSurfaceMaterial) }
                : new[] { new Material(hull) };
            renderer.sharedMaterials = debrisMaterials;
            var collider = debrisObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;
            var body = debrisObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.mass = Mathf.Clamp(ConvexMeshCutter.Volume(mesh) * Mathf.Abs(pose.lossyScale.x * pose.lossyScale.y * pose.lossyScale.z), 0.1f, 12);
            body.velocity = velocity;
            body.angularVelocity = new Vector3(impulse.z + 0.3f, impulse.x + 0.4f, 0.6f);
            body.drag = 0.12f;
            body.angularDrag = 0.15f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var debris = debrisObject.AddComponent<FractureDebris>();
            debris.ownedMesh = mesh;
            debris.ownedMaterials = debrisMaterials;
            debris.expiry = Time.time + 12;
            debris.counted = true;
            ActiveCount++;
        }

        void Update()
        {
            if (Time.time >= expiry)
            {
                Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if (counted)
            {
                ActiveCount = Mathf.Max(0, ActiveCount - 1);
            }

            if (ownedMesh != null)
            {
                DestructibleAssembly.Release(ownedMesh);
            }

            if (ownedMaterials != null)
            {
                foreach (Material material in ownedMaterials)
                {
                    if (material != null)
                    {
                        DestructibleAssembly.Release(material);
                    }
                }
            }
        }
    }
}
