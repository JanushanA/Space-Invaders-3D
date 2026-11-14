using System.Collections.Generic;
using Game.Combat;
using Game.Core;
using UnityEngine;
namespace Game.Enemies
{
    // Spawns enemies in waves around the player. Each wave is bigger than the last,
    // and the next one only starts once the current one is dead.
    // Spawns enemy waves or the sector boss and tracks when the level is complete.
    public class EnemyWaveSpawner : MonoBehaviour
    {
        public GameObject enemyPrefab;
        public int enemiesInFirstWave = 3;
        public int extraEnemiesPerWave = 2;
        public int mostEnemiesPerWave = 14;
        public float spawnDistance = 26f;
        public float spawnHeightSpread = 4f;
        public float delayBeforeFirstWave = 2f;
        public float delayBetweenWaves = 4f;
        private readonly List<GameObject> liveEnemies = new List<GameObject>();
        private int waveNumber;
        private bool waveInProgress;
        private float nextWaveTime;
        private Game.Campaign.BattleTimeline timeline;
        private int pendingWave;

        public float ReinforcementCountdown => Mathf.Max(0, nextWaveTime - Time.time);
        private Transform player;
        private int clearWave;
        private bool sectorCleared;
        private bool bossFight;
        private bool bossSpawned;
        private GameObject boss;

        public int WaveNumber
        {
            get
            {
                return waveNumber;
            }
        }

        public int EnemiesLeft
        {
            get
            {
                return liveEnemies.Count;
            }
        }

        private void Start()
        {
            if (!Game.Net.NetSession.Active && GetComponent<Game.Campaign.CampaignMission>() == null)
            {
                gameObject.AddComponent<Game.Campaign.CampaignMission>();
            }

            if (Game.Campaign.CampaignSession.InHideout)
            {
                enabled = false;
                return;
            }

            // Scales wave size and delays from sector level and multiplayer party size.
            // This keeps later levels and larger lobbies from feeling under-populated.
            int level = Mathf.Clamp(GameSession.SelectedLevel, 1, GameSession.LevelCount);
            int party = GameSession.PartySize;
            enemiesInFirstWave = (2 + level) * party;
            extraEnemiesPerWave = (1 + level / 3) * party;
            mostEnemiesPerWave = (8 + level * 2) * party;
            delayBeforeFirstWave = Mathf.Max(0.8f, 2.2f - level * 0.12f);
            delayBetweenWaves = Mathf.Max(2f, 4.2f - level * 0.2f);
            clearWave = GameSession.WavesToClear();
            nextWaveTime = Time.time + delayBeforeFirstWave;
            timeline = Game.Campaign.BattleTimeline.For(gameObject);
            if (GameSession.SelectedLevel == 2)
            {
                // Replaces normal waves with the boss encounter in the second sector.
                bossFight = true;
                nextWaveTime = Time.time + 1.4f;
            }

            QueueWave(nextWaveTime - Time.time);
        }

        private void Update()
        {
            if (sectorCleared)
            {
                return;
            }

            if (Game.Campaign.CampaignSession.StoryMode && Game.Campaign.CampaignMission.Instance != null && Game.Campaign.CampaignMission.Instance.Failed)
            {
                return;
            }

            // Removes defeated enemies before deciding whether the current wave or boss phase has ended.
            ClearOutDeadEnemies();
            if (bossFight)
            {
                TickBossFight();
                return;
            }

            if (waveInProgress && liveEnemies.Count == 0)
            {
                // Starts the next delay only after every enemy in the current wave is gone.
                waveInProgress = false;
                if (!sectorCleared && waveNumber >= clearWave)
                {
                    if (!Game.Campaign.CampaignMission.TasksComplete)
                    {
                        waveInProgress = true;
                        return;
                    }

                    FinishSector();
                    return;
                }

                QueueWave(delayBetweenWaves);
            }
        }

        private void QueueWave(float delay)
        {
            if (timeline == null)
            {
                timeline = Game.Campaign.BattleTimeline.For(gameObject);
            }

            if (pendingWave != 0)
            {
                timeline.Cancel(pendingWave);
            }

            nextWaveTime = Time.time + delay;
            pendingWave = timeline.After(delay, () =>
            {
                pendingWave = 0;
                if (this == null || !isActiveAndEnabled || sectorCleared || Game.Net.NetSession.Active && !Game.Net.NetSession.IsHost)
                {
                    return;
                }

                var mission = Game.Campaign.CampaignMission.Instance;
                if (mission != null && mission.Failed)
                {
                    return;
                }

                if (bossFight)
                {
                    if (!bossSpawned)
                    {
                        SpawnBoss();
                    }
                }
                else
                {
                    SpawnWave();
                }
            });
        }

        public void SpawnAmbush(Vector3 centre, int count)
        {
            if (enemyPrefab == null || sectorCleared || Game.Net.NetSession.Active && !Game.Net.NetSession.IsHost)
            {
                return;
            }

            for (int ambushShipIndex = 0; ambushShipIndex < Mathf.Clamp(count, 1, 4); ambushShipIndex++)
            {
                Vector3 offset = Quaternion.Euler(0, ambushShipIndex * 180 + 45, 0) * Vector3.forward * 30;
                Vector3 position = Game.World.SweptMovement.Resolve(centre + offset, 2.4f);
                var enemy = Instantiate(enemyPrefab, position, Quaternion.identity, transform);
                enemy.name = "Mission ambush " + ambushShipIndex;
                TrackEnemy(enemy);
            }
        }

        private void OnDisable()
        {
            if (timeline != null)
            {
                timeline.Clear();
            }

            pendingWave = 0;
        }

        public void TrackEnemy(GameObject enemy)
        {
            // Adds spawned escorts or special enemies to the same live list used for wave completion.
            if (enemy == null)
            {
                return;
            }

            liveEnemies.Add(enemy);
            if (Game.Net.NetCombat.Instance != null)
            {
                // Gives host-created enemies network IDs so clients can receive their snapshots.
                Game.Net.NetCombat.Instance.RegisterEnemy(enemy);
            }
        }

        private void TickBossFight()
        {
            // Waits for the boss spawn time, then finishes the sector when the mothership is inactive.
            if (sectorCleared)
            {
                return;
            }

            if (!bossSpawned)
            {
                // Boss arrival is dispatched by the indexed battle timeline.
                return;
            }

            if (boss == null || !boss.activeSelf)
            {
                if (!Game.Campaign.CampaignMission.TasksComplete)
                {
                    return;
                }

                FinishSector();
            }
        }

        private void SpawnBoss()
        {
            // Reuses the normal enemy prefab, then converts the larger instance into a MotherShipBoss.
            if (enemyPrefab == null)
            {
                Debug.LogError("[EnemyWaveSpawner] No enemy prefab, so the mothership cannot spawn.", this);
                enabled = false;
                return;
            }

            Vector3 centre = SpawnCentre();

            // Starts from the current player position so the boss encounter begins near the active party.
            Vector3 forward = Vector3.forward;
            if (player != null)
            {
                forward = player.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f)
                {
                    forward = Vector3.forward;
                }

                forward.Normalize();
            }

            Vector3 position = centre + forward * 40f;

            // Places the boss ahead of the player rather than directly on their current coordinates.
            position.y = centre.y;
            boss = Instantiate(enemyPrefab, position, Quaternion.LookRotation(-forward, Vector3.up), transform);
            boss.name = "Mothership";

            // Group hull is normalized to four units; preserve a distinct mothership silhouette.
            boss.transform.localScale = enemyPrefab.transform.localScale * 4.5f;
            EnemyController fighter = boss.GetComponent<EnemyController>();
            if (fighter != null)
            {
                fighter.enabled = false;
                Destroy(fighter);
            }

            Health health = boss.GetComponent<Health>();
            if (health != null)
            {
                // Scales boss durability by party size so extra players do not make the fight too short.
                health.maxHealth = 1000f * GameSession.PartySize;
                health.ResetHealth();
            }

            MotherShipBoss mothership = boss.AddComponent<MotherShipBoss>();
            mothership.Bind(enemyPrefab, this);
            if (Game.Net.NetCombat.Instance != null)
            {
                Game.Net.NetCombat.Instance.RegisterEnemy(boss);
            }

            bossSpawned = true;
            Debug.Log("[EnemyWaveSpawner] Sector 2 mothership spawned.");
        }

        // Dead enemies switch themselves off, so anything inactive is finished with.
        private void ClearOutDeadEnemies()
        {
            // Walks backward because removing an entry while moving forward would skip the next enemy.
            for (int entryIndex = liveEnemies.Count - 1; entryIndex >= 0; entryIndex--)
            {
                GameObject enemy = liveEnemies[entryIndex];
                if (enemy == null)
                {
                    liveEnemies.RemoveAt(entryIndex);
                }
                else if (!enemy.activeSelf)
                {
                    // Removes inactive pooled enemies and destroys their temporary wave instance.
                    liveEnemies.RemoveAt(entryIndex);
                    Destroy(enemy);
                }
            }
        }

        private void SpawnWave()
        {
            if (enemyPrefab == null)
            {
                Debug.LogError("[EnemyWaveSpawner] No enemy prefab, so no waves can spawn.", this);
                enabled = false;
                return;
            }

            waveNumber++;
            int count = enemiesInFirstWave + (waveNumber - 1) * extraEnemiesPerWave;

            // Grows the wave each round, then caps it to avoid uncontrolled performance cost.
            count = Mathf.Clamp(count, 1, Mathf.Max(1, mostEnemiesPerWave));
            Vector3 centre = SpawnCentre();

            // Ring around the player, rotated each wave so they never arrive from the same side.
            float firstAngle = Random.Range(0f, 360f);

            // Rotates the complete formation so repeated waves do not always arrive from one direction.
            var squad = new List<GameObject>();

            for (int enemyIndex = 0; enemyIndex < count; enemyIndex++)
            {
                float angle = firstAngle + 360f / count * enemyIndex;

                // Divides the full circle into equal spacing so enemies form a ring around the player.
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * spawnDistance;

                // Rotates a forward vector by the ring angle to calculate each enemy's XZ spawn offset.
                offset.y = Random.Range(-spawnHeightSpread, spawnHeightSpread);
                Vector3 spawnPosition = centre + offset;
                var mission = Game.Campaign.CampaignMission.Instance;
                if (mission != null && mission.Plan != null && mission.Plan.encounters.Count > 0)
                {
                    int anchor = mission.Plan.encounters[(waveNumber - 1 + enemyIndex) % mission.Plan.encounters.Count];
                    spawnPosition = mission.transform.TransformPoint(mission.Plan.Position(anchor)) + Vector3.up * (3 + enemyIndex / mission.Plan.encounters.Count * 4);
                }

                spawnPosition = Game.World.SweptMovement.Resolve(spawnPosition, 2.4f);
                GameObject enemy = Instantiate(enemyPrefab, spawnPosition, Quaternion.identity, transform);
                enemy.name = "Enemy_W" + waveNumber + "_" + (enemyIndex + 1);
                if (enemyIndex == 1)
                {
                    Game.Destruction.DestructibleAssembly.Attach(enemy);
                }

                if (enemyIndex == 0 && GameSession.SelectedLevel >= 3)
                {
                    Game.AI.AcePilot.Attach(enemy);
                }
                else if (squad.Count < 8)
                {
                    squad.Add(enemy);
                }

                liveEnemies.Add(enemy);
                if (Game.Net.NetCombat.Instance != null)
                {
                    Game.Net.NetCombat.Instance.RegisterEnemy(enemy);
                }
            }

            if (squad.Count > 1)
            {
                Game.AI.SquadronCoordinator.Create(squad);
            }

            waveInProgress = true;
            Debug.Log("[EnemyWaveSpawner] Wave " + waveNumber + ": " + count + " enemies.");
        }

        // Around the player if there is one, otherwise around this spawner.
        private Vector3 SpawnCentre()
        {
            // Prefers the nearest multiplayer pilot so spawns remain relevant when there is more than one player.
            Transform focus = Game.Net.PlayerRoster.Nearest(transform.position);
            if (focus != null)
            {
                player = focus;
                return focus.position;
            }

            if (player == null)
            {
                GameObject found = GameObject.FindWithTag("Player");
                if (found != null)
                {
                    player = found.transform;
                }
            }

            if (player != null)
            {
                return player.position;
            }

            return transform.position;
        }

        private void FinishSector()
        {
            Game.Progression.SalvagePickup.CollectRemaining();
            if (Game.Campaign.CampaignSession.StoryMode)
            {
                Game.Campaign.CampaignSession.Complete();
            }

            // Converts remaining player health into a one-to-three-star completion result.
            sectorCleared = true;
            if (timeline != null)
            {
                timeline.Clear();
            }

            Health playerHealth = null;
            GameObject found = GameObject.FindWithTag("Player");
            if (found != null)
            {
                playerHealth = found.GetComponent<Health>();
            }

            int stars = 1;
            if (playerHealth != null && playerHealth.maxHealth > 0f)
            {
                float ratio = playerHealth.CurrentHealth / playerHealth.maxHealth;

                // Uses health as a fraction so scoring works with different maximum-health upgrades.
                if (ratio >= 0.34f)
                {
                    stars = 2;
                }

                if (ratio >= 0.99f)
                {
                    stars = 3;
                }
            }

            if (Game.Net.NetSession.Active && Game.Net.NetRunner.Instance != null)
            {
                Game.Net.NetRunner.Instance.CompletePartyLevel(GameSession.SelectedLevel, stars);
            }
            else
            {
                GameSession.CompleteLevel(GameSession.SelectedLevel, stars);
            }

            if (Game.Net.NetSession.Active)
            {
                // Returns the whole multiplayer party through the host-controlled lobby flow.
                Invoke("ReturnToLobby", 2.2f);
                return;
            }

            GameSession.ShowLevelSelect = true;
            Invoke("ReturnToMenu", 2.2f);
        }

        private void ReturnToLobby()
        {
            Time.timeScale = 1f;
            if (Game.Net.NetRunner.Instance != null)
            {
                Game.Net.NetRunner.Instance.ReturnEveryoneToLobby();
            }
        }

        private void ReturnToMenu()
        {
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(Game.Campaign.CampaignSession.StoryMode ? GameSession.CombatScene : GameSession.MenuScene);
        }
    }
}
