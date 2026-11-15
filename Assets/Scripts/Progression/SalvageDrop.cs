using Game.Combat;
using Game.Core;
using Game.Player;
using UnityEngine;
namespace Game.Progression
{
    public sealed class SalvageDrop : MonoBehaviour
    {
        Health health;
        bool awarded;
        static readonly DynamicLoot loot = new DynamicLoot(System.Environment.TickCount);

        void Awake()
        {
            health = GetComponent<Health>();
            if (health != null)
            {
                health.Died += Drop;
            }
        }

        void OnDestroy()
        {
            if (health != null)
            {
                health.Died -= Drop;
            }
        }

        void Drop(Health source, DamageInfo hit)
        {
            if (awarded || (Game.Net.NetSession.Active && !Game.Net.NetSession.IsHost))
            {
                return;
            }

            awarded = true;
            bool boss = GetComponent<Game.Enemies.MotherShipBoss>() != null;
            var inventory = PilotInventory.State;
            int material = loot.RollMaterial(GameSession.SelectedLevel, Game.Net.NetSession.Active ? 0 : inventory.ModuleTier(3), inventory.dropPity);
            int count = boss ? 12 : Random.Range(1, 4);
            PilotInventory.Save();
            int weapon = boss ? 5 : (Random.value < 0.18f ? Random.Range(1, Mathf.Min(5, 2 + GameSession.SelectedLevel / 2)) : -1);
            int credits = boss ? 400 : 25;

            // Guaranteed rewards are secured at death, before the sector-return timer.
            if (boss || Game.Net.NetSession.Active)
            {
                if (Game.Net.NetSession.Active)
                {
                    Game.Net.NetRunner.Instance?.AwardSalvage(hit.Instigator, material, count, weapon, credits, boss);
                }
                else
                {
                    PilotInventory.Grant(material, count, weapon, credits);
                }

                SalvagePickup.Create(transform.position, material, 0, -1, 0, true);
            }
            else
            {
                SalvagePickup.Create(transform.position, material, count, weapon, credits, false);
            }
        }
    }

    public sealed class SalvagePickup : MonoBehaviour
    {
        int material;
        int count;
        int weapon;
        int money;
        bool displayOnly;
        float expires;
        Material glow;
        Transform target;

        public static void Create(Vector3 position, int material, int count, int weapon, int money, bool displayOnly)
        {
            var pickupObject = new GameObject("Salvage / " + SalvageCatalog.Materials[material]);
            pickupObject.transform.position = position;
            var drop = pickupObject.AddComponent<SalvagePickup>();
            drop.material = material;
            drop.count = count;
            drop.weapon = weapon;
            drop.money = money;
            drop.displayOnly = displayOnly;
            if (!displayOnly)
            {
                RadarContact.Track(pickupObject, RadarKind.Salvage);
            }

            drop.expires = Time.time + (displayOnly ? 2.5f : 90);
            var icon = new GameObject("Material icon");
            icon.transform.SetParent(pickupObject.transform, false);
            var renderer = icon.AddComponent<SpriteRenderer>();
            Texture2D texture = Resources.Load<Texture2D>("Salvage/" + ((Salvage)material));
            if (texture != null)
            {
                renderer.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width / 2f);
            }

            drop.glow = Game.Graphics.GraphicsMaterials.Unlit("Salvage hologram", Color.white);
            if (texture != null)
            {
                drop.glow.SetTexture("_MainTex", texture);
            }

            renderer.sharedMaterial = drop.glow;
        }

        void Update()
        {
            if (Time.time > expires)
            {
                Destroy(gameObject);
                return;
            }

            if (Camera.main != null)
            {
                transform.rotation = Camera.main.transform.rotation;
            }

            // Reward-only visuals never attract to the pilot or grant the same loot twice.
            if (displayOnly)
            {
                return;
            }

            if (target == null)
            {
                var pilot = FindObjectOfType<PlayerController>();
                if (pilot != null)
                {
                    target = pilot.transform;
                }
            }

            if (target == null)
            {
                return;
            }

            float distance = Vector3.Distance(transform.position, target.position);
            float range = 16 + 4 * PilotInventory.State.ModuleTier(3);
            var skills = target.GetComponent<PlayerSkills>();
            if (skills != null)
            {
                range = Mathf.Max(range, skills.TractorRange);
            }

            if (distance < range)
            {
                transform.position = Vector3.MoveTowards(transform.position, target.position, 22 * Time.deltaTime);
            }

            if (distance < 3)
            {
                Collect();
            }
        }

        public void Collect()
        {
            if (displayOnly)
            {
                return;
            }

            displayOnly = true;
            PilotInventory.Grant(material, count, weapon, money);
            Destroy(gameObject);
        }

        public static void CollectRemaining()
        {
            foreach (var drop in FindObjectsOfType<SalvagePickup>())
            {
                drop.Collect();
            }
        }

        void OnDestroy()
        {
            var spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                Destroy(spriteRenderer.sprite);
            }

            if (glow != null)
            {
                Destroy(glow);
            }
        }
    }
}
