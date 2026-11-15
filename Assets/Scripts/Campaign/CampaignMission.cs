using System.Collections.Generic;
using Game.Combat;
using Game.Core;
using Game.Graphics;
using Game.Player;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Game.Campaign
{
    public sealed class CampaignMission : MonoBehaviour
    {
        public static CampaignMission Instance
        {
            get; private set;
        }

        public bool Complete
        {
            get; private set;
        }

        public bool Failed
        {
            get; private set;
        }

        public string Objective
        {
            get; private set;
        }

        public string Advice { get; private set; } = "Recover the navigation core to unlock the route.";

        public SectorPlan Plan
        {
            get; private set;
        }

        public int Stage
        {
            get; private set;
        }

        public Transform Pilot => pilot;

        public Transform NavigationCore => beacons.Count > 0 ? beacons[0] : null;

        public Transform ExtractionBeacon => beacons.Count > 2 ? beacons[2] : null;

        public Transform Escort => escort;

        public IReadOnlyList<Transform> Relays => relays;

        public int RelayProgress => relayStep;

        public float EscortHealthFraction => escortHealth == null ? 0 : escortHealth.CurrentHealth / Mathf.Max(1, escortHealth.maxHealth);

        public float ExtractionProgress => Mathf.Clamp01(transfer / 8);

        public static bool TasksComplete => Instance == null || !CampaignSession.StoryMode || Instance.Complete;
        readonly List<Object> owned = new List<Object>();
        readonly List<Transform> beacons = new List<Transform>();
        readonly List<Transform> relays = new List<Transform>();
        readonly List<Transform> hazards = new List<Transform>();
        Transform pilot;
        Transform escort;
        Health escortHealth;
        EscortNavigator navigator;
        BattleTimeline timeline;
        float transfer;
        int relayStep;
        bool extractionClock;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            if (CampaignSession.InHideout)
            {
                BuildHangar();
                return;
            }

            int seed = CampaignSession.Seed != 0 ? CampaignSession.Seed : 81511 + GameSession.SelectedLevel * 101;
            Plan = SectorGenerator.Generate(seed, GameSession.SelectedLevel);
            BuildSector();
            if (!CampaignSession.StoryMode)
            {
                enabled = false;
                return;
            }

            timeline = BattleTimeline.For(gameObject);
            MissionHud.Attach(this);
        }

        void BuildSector()
        {
            var stone = Own(GraphicsMaterials.Lit("Mission debris", new Color(0.7f, 0.68f, 0.64f), 0.08f, 0.19f));
            stone.SetTexture("_BaseColorMap", Resources.Load<Texture2D>("Orbital/RockSurface"));
            var meshes = new Mesh[6];

            for (int index = 0; index < 6; index++)
            {
                meshes[index] = Own(Game.World.OrbitalEnvironment.NavigationAsteroidMesh(index));
            }

            for (int index = 0; index < Plan.blocked.Length; index++)
            {
                if (Plan.blocked[index])
                {
                    var rock = new GameObject("Navigation asteroid " + index, typeof(MeshFilter), typeof(MeshRenderer), typeof(SphereCollider));
                    rock.layer = LayerMask.NameToLayer(GameLayers.LevelGeometry);
                    rock.transform.SetParent(transform, false);
                    rock.transform.localPosition = Plan.Position(index);
                    rock.transform.localScale = new Vector3(11, 8, 13);
                    rock.GetComponent<SphereCollider>().radius = 0.5f;
                    rock.GetComponent<MeshFilter>().sharedMesh = meshes[index % 6];
                    var renderer = rock.GetComponent<Renderer>();
                    renderer.sharedMaterial = stone;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }

            if (!CampaignSession.StoryMode)
            {
                return;
            }

            var kinds = new[] { MissionBeaconAsset.Kind.NavigationCore, MissionBeaconAsset.Kind.Relay, MissionBeaconAsset.Kind.Extraction };
            string[] labels = { "Recover navigation core", "Convoy jump uplink", "Extraction beacon" };

            for (int index = 0; index < 3; index++)
            {
                var beacon = MissionBeaconAsset.Create(transform, kinds[index], UiChrome.NeonBlue, labels[index]);
                beacon.localPosition = Plan.Position(Plan.objectives[index]);
                beacons.Add(beacon);
            }

            if (GameSession.SelectedLevel % 2 == 1)
            {
                var transportObject = new GameObject("Civilian transport / protect");
                transportObject.transform.SetParent(transform, false);
                transportObject.transform.localPosition = beacons[0].localPosition + Vector3.up * 8;
                escort = transportObject.transform;
                transportObject.layer = LayerMask.NameToLayer(GameLayers.Player);
                escortHealth = transportObject.AddComponent<Health>();
                escortHealth.maxHealth = 18;
                escortHealth.deactivateOnDeath = false;
                escortHealth.ResetHealth();
                var prefab = Resources.Load<GameObject>("Ships/Player2");
                if (prefab != null)
                {
                    var visual = Instantiate(prefab, escort, false);
                    visual.name = "Armoured civilian transport";
                    visual.transform.localScale = Vector3.one * 1.7f;
                }

                var collision = transportObject.AddComponent<BoxCollider>();
                collision.size = new Vector3(6, 2, 8);
                navigator = transportObject.AddComponent<EscortNavigator>();
                navigator.Bind(Plan, transform, beacons[1]);
            }
            else
            {
                Color[] colors = { UiChrome.Gold, UiChrome.NeonBlue, UiChrome.Mobility };
                string[] names = { "AMBER", "CYAN", "VIOLET" };

                for (int index = 0; index < 3; index++)
                {
                    var relay = MissionBeaconAsset.Create(transform, MissionBeaconAsset.Kind.Relay, colors[index], names[index] + " relay");
                    relay.localPosition = Plan.Position(Plan.relays[index]);
                    relays.Add(relay);
                }
            }

            Physics.SyncTransforms();
        }

        void Update()
        {
            if (!CampaignSession.StoryMode || CampaignSession.InHideout || Plan == null || beacons.Count < 3 || Complete || PlayerSkills.MenuOpen)
            {
                return;
            }

            if (pilot == null)
            {
                var player = FindObjectOfType<PlayerController>();
                if (player != null)
                {
                    pilot = player.transform;
                }
                else
                {
                    return;
                }
            }

            if (escortHealth != null && !escortHealth.IsAlive)
            {
                Failed = true;
                timeline.Clear();
                Objective = "TRANSPORT LOST / Press Enter to retry";
                Advice = "Keep close to the convoy and intercept attackers.";
                if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
                {
                    Retry();
                }

                return;
            }

            bool action = Keyboard.current != null ? Keyboard.current.eKey.wasPressedThisFrame : Input.GetKeyDown(KeyCode.E);
            Transform target = beacons[Mathf.Clamp(Stage, 0, 2)];
            float distance = Vector3.Distance(pilot.position, target.position);

            // The core unlocks the sector-specific task, which then unlocks extraction.
            if (Stage == 0)
            {
                Objective = "RECOVER NAVIGATION CORE / " + Mathf.CeilToInt(distance) + " m / Approach within 12 m, press E";
                var waves = GetComponent<Game.Enemies.EnemyWaveSpawner>();
                Advice = waves != null && waves.ReinforcementCountdown > 0 ? "Hostile arrival in " + Mathf.CeilToInt(waves.ReinforcementCountdown) + " s. Recover the navigation core." : "Recover the navigation core to unlock the route.";
                if (distance < 12 && action)
                {
                    RecoverCore();
                }
            }
            else if (Stage == 1 && escort != null)
            {
                float escortDistance = Vector3.Distance(pilot.position, escort.position);
                Objective = "ESCORT TRANSPORT / Hull " + Mathf.CeilToInt(EscortHealthFraction * 100) + "% / " + Mathf.CeilToInt(escortDistance) + " m";
                Advice = escortDistance < 40 ? "Follow the cyan route. The transport replans around hazards and blocked lanes." : "Return within 40 m of the transport to continue the escort.";
                navigator.Tick(escortDistance < 40 ? 7 : 0, Time.deltaTime);
                if (navigator.Arrived)
                {
                    Stage = 2;
                }
            }
            else if (Stage == 1)
            {
                Objective = "ALIGN RELAYS / AMBER > CYAN > VIOLET / " + relayStep + " / 3";
                Advice = "Align the distant relays in order. A wrong relay resets the sequence.";
                if (action)
                {
                    // An interaction selects only the closest relay within its activation radius.
                    int nearestRelay = -1;
                    float nearestDistance = 8;

                    for (int index = 0; index < relays.Count; index++)
                    {
                        float relayDistance = Vector3.Distance(pilot.position, relays[index].position);
                        if (relayDistance < nearestDistance)
                        {
                            nearestDistance = relayDistance;
                            nearestRelay = index;
                        }
                    }

                    if (nearestRelay >= 0)
                    {
                        relayStep = nearestRelay == relayStep ? relayStep + 1 : 0;
                        if (relayStep == 3)
                        {
                            Stage = 2;
                        }
                    }
                }
            }
            else
            {
                bool clear = ExtractionClear();
                Objective = "SECURE EXTRACTION / " + Mathf.FloorToInt(ExtractionProgress * 100) + "%";
                Advice = clear ? "Hold within 16 m while the beacon charges." : "Approach the beacon and clear hostiles within 25 m.";
                if (!extractionClock)
                {
                    extractionClock = true;
                    timeline.After(0.25f, ExtractionTick);
                }
            }
        }

        void RecoverCore()
        {
            Stage = 1;
            Game.Progression.PilotInventory.Grant(1, 3, -1, 40);
            Advice = "Incoming ambush. Move away from the unstable reactor.";
            timeline.After(3, () =>
            {
                if (this == null || Failed || Complete)
                {
                    return;
                }

                var spawner = GetComponent<Game.Enemies.EnemyWaveSpawner>();
                if (spawner != null)
                {
                    spawner.SpawnAmbush(NavigationCore.position, 2);
                }
            });
            ArmReactor(NavigationCore.position + Vector3.right * 18, 4);
            if (escort != null)
            {
                timeline.After(2.5f, EscortRaid);
            }
        }

        bool ExtractionClear()
        {
            if (pilot == null || ExtractionBeacon == null || Vector3.Distance(pilot.position, ExtractionBeacon.position) >= 16)
            {
                return false;
            }

            foreach (var enemy in FindObjectsOfType<Game.Enemies.EnemyController>())
            {
                if (enemy.isActiveAndEnabled && Vector3.Distance(enemy.transform.position, ExtractionBeacon.position) < 25)
                {
                    return false;
                }
            }

            return true;
        }

        void ExtractionTick()
        {
            if (this == null || Failed || Complete || !isActiveAndEnabled)
            {
                return;
            }

            if (Stage == 2 && !PlayerSkills.MenuOpen && ExtractionClear())
            {
                transfer += 0.25f;
            }

            if (transfer >= 8)
            {
                Complete = true;
                Objective = "OBJECTIVES COMPLETE / Finish remaining hostiles";
                Advice = "The jump beacon is secure. Clear remaining enemies.";
                return;
            }

            timeline.After(0.25f, ExtractionTick);
        }

        void EscortRaid()
        {
            if (this == null || Failed || Complete || Stage != 1 || escort == null)
            {
                return;
            }

            foreach (var enemy in FindObjectsOfType<Game.Enemies.EnemyController>())
            {
                if (enemy.isActiveAndEnabled && Vector3.Distance(enemy.transform.position, escort.position) < 60)
                {
                    var weapon = enemy.GetComponentInChildren<Weapon>();
                    var pool = ProjectilePool.Instance;
                    if (weapon != null && weapon.definition != null && pool != null)
                    {
                        var weaponDefinition = weapon.definition;
                        Vector3 direction = (escort.position - enemy.transform.position).normalized;
                        var shot = pool.Get(weaponDefinition.projectilePrefab);
                        if (shot != null)
                        {
                            shot.Launch(enemy.transform.position + direction * 3, direction, new ProjectileLaunchData(weaponDefinition.projectileSpeed, weaponDefinition.damagePerProjectile, weaponDefinition.projectileRadius, weaponDefinition.projectileLifetime, DamageSource.EnemyProjectile, enemy.gameObject, GameLayers.EnemyWeaponHitMask), pool);
                        }
                    }

                    break;
                }
            }

            timeline.After(2.5f, EscortRaid);
        }

        public float HazardCost(Vector3 point)
        {
            float cost = 0;

            foreach (var hazard in hazards)
            {
                if (hazard != null)
                {
                    cost += Mathf.Max(0, 30 - Vector3.Distance(point, hazard.position)) * 8;
                }
            }

            return cost;
        }

        public void ArmReactor(Vector3 position, float delay)
        {
            if (timeline == null)
            {
                timeline = BattleTimeline.For(gameObject);
            }

            var reactor = MissionBeaconAsset.Create(transform, MissionBeaconAsset.Kind.NavigationCore, UiChrome.NeonRed, "UNSTABLE REACTOR / DETONATION");
            reactor.position = position;
            reactor.localScale = Vector3.one * 0.65f;
            hazards.Add(reactor);
            timeline.After(delay, () =>
            {
                if (reactor == null)
                {
                    return;
                }

                Vector3 centre = reactor.position;

                // A ship may have several colliders; apply reactor damage once per health component.
                var damaged = new HashSet<Health>();

                foreach (var collider in Physics.OverlapSphere(centre, 14))
                {
                    var health = collider.GetComponentInParent<Health>();
                    if (health != null && damaged.Add(health))
                    {
                        health.ApplyDamage(new DamageInfo(3, DamageSource.Script, gameObject, centre, Vector3.up));
                    }
                }

                hazards.Remove(reactor);
                reactor.localScale = Vector3.one * 4;
                timeline.After(0.18f, () =>
                {
                    if (reactor != null)
                    {
                        Destroy(reactor.gameObject);
                    }
                });
            });
        }

        void Retry()
        {
            Time.timeScale = 1;
            UnityEngine.SceneManagement.SceneManager.LoadScene(GameSession.CombatScene);
        }

        void BuildHangar()
        {
            var metal = Own(GraphicsMaterials.Lit("Hangar decking", new Color(0.12f, 0.17f, 0.2f), 0.6f, 0.4f));

            for (int index = 0; index < 7; index++)
            {
                var girder = GameObject.CreatePrimitive(PrimitiveType.Cube);
                girder.name = "Wayfarer berth support";
                girder.transform.SetParent(transform, false);
                girder.transform.localPosition = new Vector3((index - 3) * 7, -3, 10);
                girder.transform.localScale = new Vector3(0.5f, 0.5f, 60);
                girder.GetComponent<Renderer>().sharedMaterial = metal;
            }

            for (int index = 0; index < 3; index++)
            {
                var crewMember = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                crewMember.name = new[] { "Commander Vale", "Chief Rook", "Dr. Imani" }[index];
                crewMember.transform.SetParent(transform, false);
                crewMember.transform.localPosition = new Vector3(-7 + index * 7, 0, 12);
                crewMember.GetComponent<Renderer>().sharedMaterial = metal;
            }
        }

        T Own<T>(T value) where T : Object
        {
            owned.Add(value);
            return value;
        }

        void OnDisable()
        {
            if (timeline != null)
            {
                timeline.Clear();
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (timeline != null)
            {
                timeline.Clear();
            }

            foreach (var value in owned)
            {
                if (value != null)
                {
                    Destroy(value);
                }
            }
        }
    }
}
