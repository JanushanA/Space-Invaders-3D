using System.Collections.Generic;
using Game.Combat;
using Game.Core;
using Game.Enemies;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

// Keeps the flight display in sync with hull damage, experience, weapons, abilities and nearby targets.
namespace Game.Player
{
    // Read-only flight instrumentation, shared by cockpit and chase camera modes.
    [DefaultExecutionOrder(125)]
    public class GameHud : MonoBehaviour
    {
        public static readonly Color Amber = UiChrome.Gold;
        public static readonly Color Cyan = UiChrome.NeonBlue;
        private Health health;
        private Health bossHealth;
        private PlayerProgression progression;
        private PlayerSkills skills;
        private SkillTree tree;
        private Weapon weapon;
        private Canvas canvas;
        private RectTransform instruments;
        private RectTransform bossRoot;
        private RectTransform targetRoot;
        private FlightHudGraphic flightGraphic;
        private Text hullText;
        private Text xpText;
        private Text weaponText;
        private Text contactText;
        private Text modeText;
        private Text bossText;
        private Text targetText;
        private Text pickupText;
        private Image hullFill;
        private Image xpFill;
        private Image bossFill;
        private Image targetFill;
        private readonly Text[] slotKeys = new Text[PlayerSkills.SlotCount];
        private readonly Image[] slotIcons = new Image[PlayerSkills.SlotCount];
        private readonly Text[] slotNames = new Text[PlayerSkills.SlotCount];
        private readonly Text[] slotStates = new Text[PlayerSkills.SlotCount];
        private readonly Image[] slotFills = new Image[PlayerSkills.SlotCount];
        private readonly List<RadarContact> contacts = new List<RadarContact>();
        private readonly List<Color> radarColors = new List<Color>();
        private readonly List<Vector2> radarPoints = new List<Vector2>();
        private MotherShipBoss boss;
        private NetBossPresentation remoteBoss;
        private float nextScan;
        private float nextText;
        private float hitAmount;

        private void Awake()
        {
            health = GetComponent<Health>();
            progression = GetComponent<PlayerProgression>();
            skills = GetComponent<PlayerSkills>();
            tree = GetComponent<SkillTree>();
            weapon = GetComponentInChildren<Weapon>();
            BuildCanvas();
            if (GetComponent<CockpitRig>() == null)
            {
                gameObject.AddComponent<CockpitRig>();
            }
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.HealthChanged += HealthChanged;
                health.Damaged += Damaged;
            }

            if (progression != null)
            {
                progression.Changed += RefreshBars;
            }

            if (skills != null)
            {
                skills.HotbarChanged += RefreshSlots;
            }

            if (canvas != null)
            {
                canvas.gameObject.SetActive(true);
            }

            RefreshBars();
            RefreshSlots();
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.HealthChanged -= HealthChanged;
                health.Damaged -= Damaged;
            }

            if (progression != null)
            {
                progression.Changed -= RefreshBars;
            }

            if (skills != null)
            {
                skills.HotbarChanged -= RefreshSlots;
            }

            if (canvas != null)
            {
                canvas.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (canvas == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(canvas.gameObject);
            }
            else
            {
                DestroyImmediate(canvas.gameObject);
            }
        }

        private void HealthChanged(float current, float maximum)
        {
            RefreshBars();
        }

        private void Damaged(Health source, DamageInfo info)
        {
            hitAmount = 0.7f;
        }

        private void LateUpdate()
        {
            bool alive = health == null || health.IsAlive;
            instruments.gameObject.SetActive(alive && !PlayerSkills.MenuOpen);
            if (!alive || PlayerSkills.MenuOpen)
            {
                return;
            }

            Camera view = FlightView.Instance != null ? FlightView.Instance.ViewCamera : Camera.main;
            if (view == null)
            {
                return;
            }

            if (Time.unscaledTime >= nextScan)
            {
                ScanContacts();
                nextScan = Time.unscaledTime + 0.5f;
            }

            UpdateContacts(view);
            hitAmount = Mathf.MoveTowards(hitAmount, 0f, Time.unscaledDeltaTime * 1.8f);
            flightGraphic.Damage = hitAmount;
            flightGraphic.SetVerticesDirty();
            if (Time.unscaledTime < nextText)
            {
                return;
            }

            nextText = Time.unscaledTime + 0.1f;
            RefreshSlots();
            RefreshBoss();
            bool firstPerson = FlightView.Instance != null && FlightView.Instance.IsFirstPerson;
            modeText.text = (firstPerson ? "COCKPIT" : "CHASE") + "   [V] VIEW   [TAB] SKILLS   [I] CARGO   [K] CRAFT";
            pickupText.text = Time.unscaledTime < Game.Progression.PilotInventory.PickupUntil ? Game.Progression.PilotInventory.LastPickup.ToUpperInvariant() : "";
            weaponText.text = weapon != null && weapon.definition != null
                ? Game.Progression.PilotInventory.EquippedWeaponName.ToUpperInvariant() : "OFFLINE";
        }

        private void RefreshBars()
        {
            if (hullText == null)
            {
                return;
            }

            float fraction = health != null && health.maxHealth > 0f ? Mathf.Clamp01(health.CurrentHealth / health.maxHealth) : 0f;
            hullText.text = Mathf.RoundToInt(fraction * 100f).ToString("000") + "%";
            hullFill.fillAmount = fraction;
            hullFill.color = fraction < 0.3f ? UiChrome.NeonRed : Cyan;
            xpFill.fillAmount = progression != null && progression.XpToNext > 0 ? (float)progression.Xp / progression.XpToNext : 0f;
            xpText.text = progression != null ? "PILOT " + progression.Level.ToString("00") + "    " + progression.Xp + " / " + progression.XpToNext + " XP    " + progression.SkillPoints + " POINTS" : "PILOT 01";
        }

        private void RefreshSlots()
        {
            if (skills == null || slotNames[0] == null)
            {
                return;
            }

            for (int abilitySlot = 0; abilitySlot < PlayerSkills.SlotCount; abilitySlot++)
            {
                SkillId skillId = skills.GetSlot(abilitySlot);
                string key = skills.GetBind(abilitySlot).ToString().Replace("Alpha", "");
                string label = skillId == SkillId.None ? "UNASSIGNED" : skillId.ToString();
                if (skillId != SkillId.None && tree != null && !string.IsNullOrEmpty(tree.GetNode(skillId).title))
                {
                    label = tree.GetNode(skillId).title;
                }

                slotKeys[abilitySlot].text = key;
                slotIcons[abilitySlot].sprite = skillId == SkillId.None ? UiChrome.Icon("target_brackets") : SkillMenu.IconFor(skillId);
                slotIcons[abilitySlot].color = skillId == SkillId.None ? UiChrome.MetalLight : Amber;
                slotNames[abilitySlot].text = label.ToUpperInvariant();
                slotNames[abilitySlot].color = skillId == SkillId.None ? UiChrome.DimText : UiChrome.TextColor;
                float cooldown = skills.Cooldown01(abilitySlot);
                slotStates[abilitySlot].text = skillId == SkillId.None ? "—" : cooldown > 0.01f ? Mathf.CeilToInt(cooldown * skills.CooldownSeconds(skillId)) + "s" : "READY";
                slotStates[abilitySlot].color = cooldown > 0.01f ? Amber : Cyan;
                slotFills[abilitySlot].fillAmount = skillId == SkillId.None ? 0f : 1f - cooldown;
            }
        }

        private void ScanContacts()
        {
            contacts.Clear();
            boss = FindObjectOfType<MotherShipBoss>();
            remoteBoss = FindObjectOfType<NetBossPresentation>();
            bossHealth = boss != null ? boss.GetComponent<Health>() : remoteBoss != null ? remoteBoss.GetComponent<Health>() : null;
        }

        private void UpdateContacts(Camera view)
        {
            // Range results are refreshed each frame from the shared moving index.
            contacts.Clear();
            float range = skills != null ? skills.SensorRange : 150;
            RadarIndex.Query(transform.position, range, RadarKind.Hostile | RadarKind.Missile | RadarKind.Salvage, contacts);
            radarPoints.Clear();
            radarColors.Clear();
            Health target = null;
            float best = float.MaxValue;
            int hostiles = 0;
            int missiles = 0;
            int salvage = 0;

            foreach (RadarContact contact in contacts)
            {
                if (contact == null || !contact.Live)
                {
                    continue;
                }

                RadarKind kind = contact.Kind;
                if (kind == RadarKind.Hostile)
                {
                    hostiles++;
                }
                else if (kind == RadarKind.Missile)
                {
                    missiles++;
                }
                else
                {
                    salvage++;
                }

                Vector3 relative = view.transform.InverseTransformDirection(contact.transform.position - transform.position);
                if (radarPoints.Count < 96)
                {
                    radarPoints.Add(Vector2.ClampMagnitude(new Vector2(relative.x, relative.z) / range, 1f));
                    radarColors.Add(kind == RadarKind.Hostile ? UiChrome.NeonRed : kind == RadarKind.Missile ? Amber : UiChrome.Support);
                }

                if (kind != RadarKind.Hostile || contact.Health == null || !RadarIndex.LineOfSight(view.transform.position, contact))
                {
                    continue;
                }

                Vector3 point = view.WorldToViewportPoint(contact.transform.position);
                if (point.z <= 0f || point.x < 0.08f || point.x > 0.92f || point.y < 0.23f || point.y > 0.9f)
                {
                    continue;
                }

                float score = (new Vector2(point.x, point.y) - Vector2.one * 0.5f).sqrMagnitude;
                if (score < best)
                {
                    best = score;
                    target = contact.Health;
                }
            }

            flightGraphic.Contacts = radarPoints;
            flightGraphic.ContactColors = radarColors;
            contactText.text = hostiles + " HOSTILE / " + missiles + " MISSILE / " + salvage + " SALVAGE  ·  " + range + "m";
            targetRoot.gameObject.SetActive(target != null);
            if (target == null)
            {
                return;
            }

            Vector3 projected = view.WorldToViewportPoint(target.transform.position);
            targetRoot.anchorMin = targetRoot.anchorMax = new Vector2(projected.x, projected.y);
            targetRoot.anchoredPosition = Vector2.zero;
            targetFill.fillAmount = target.maxHealth > 0 ? Mathf.Clamp01(target.CurrentHealth / target.maxHealth) : 0;
            targetText.text = "<color=#FF6E79>◇</color>  " + (target == bossHealth ? "MOTHERSHIP" : "HOSTILE") + "   |   <color=#4CEAF2>" + Mathf.RoundToInt(Vector3.Distance(transform.position, target.transform.position)) + " m</color>";
        }

        private void RefreshBoss()
        {
            bool visible = bossHealth != null && bossHealth.IsAlive && bossHealth.gameObject.activeInHierarchy;
            bossRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            BossPresentationState state = boss != null ? boss.CapturePresentation() : remoteBoss.Presentation;
            bossFill.fillAmount = bossHealth.maxHealth > 0f ? Mathf.Clamp01(bossHealth.CurrentHealth / bossHealth.maxHealth) : 0f;
            bossText.text = "MOTHERSHIP   /   STAGE " + state.Stage + "   /   " + PhaseLabel(state);
            bossFill.color = state.Invulnerable ? Cyan : Amber;
        }

        private static string PhaseLabel(BossPresentationState state)
        {
            if (state.Invulnerable)
            {
                return "SHIELDED · DESTROY ESCORTS";
            }

            switch (state.Phase)
            {
                case 1:
                    return state.BeamLocked ? "LASER LOCK" : "CHARGING LASER";
                case 2:
                    return "LASER FIRE";
                case 3:
                case 4:
                    return "SWEEP LASER";
                case 5:
                    return "BARRAGE";
                case 6:
                    return "LAUNCHING ESCORTS";
                case 7:
                    return "NOVA";
                case 8:
                case 9:
                    return "RAMMING";
                case 10:
                    return "OVERLOAD · DOUBLE DAMAGE";
                case 11:
                    return "CORE OVERLOAD";
                default:
                    return "APPROACHING";
            }
        }

        private void BuildCanvas()
        {
            var sceneObject = new GameObject("Flight instruments");
            canvas = sceneObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = sceneObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0;
            instruments = Root(sceneObject.transform, "Instruments", Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
            UiChrome.Stretch(instruments);
            flightGraphic = instruments.gameObject.AddComponent<FlightHudGraphic>();
            flightGraphic.raycastTarget = false;
            var crosshair = UiChrome.MakeImage(instruments, "Supplied flight crosshair", Resources.Load<Sprite>("UI/FlightCrosshair"), Color.white);
            crosshair.rectTransform.sizeDelta = new Vector2(168, 168);
            crosshair.preserveAspect = true;
            var left = Root(instruments, "Ship telemetry", Vector2.zero, new Vector2(32, 32), new Vector2(560, 210));
            left.pivot = Vector2.zero;
            left.localScale = Vector3.one * 0.75f;
            InstrumentGlass(left);
            Image ship = UiChrome.MakeImage(left, "Ship icon", UiChrome.Icon("core_ship"), UiChrome.TextColor);
            UiChrome.Place(ship.rectTransform, Vector2.zero, Vector2.zero, new Vector2(18, 119), new Vector2(60, 70));
            Label(left, "Hull label", "HULL INTEGRITY", 23, new Vector2(94, 156), new Vector2(320, 34), UiChrome.TextColor);
            hullText = Label(left, "Hull", "", 37, new Vector2(418, 148), new Vector2(124, 46), Cyan);
            hullFill = Bar(left, "Hull integrity", new Vector2(94, 126), 448, 14, Cyan);
            SegmentBar(hullFill.transform.parent, 448, 14, 18);
            Divider(left, new Vector2(18, 107), 524, UiChrome.MetalLight);
            xpText = Label(left, "Progression", "", 16, new Vector2(18, 76), new Vector2(524, 28), UiChrome.TextColor);
            xpFill = Bar(left, "Experience", new Vector2(18, 62), 524, 8, Amber);
            Divider(left, new Vector2(18, 46), 524, UiChrome.MetalLight);
            modeText = Label(left, "Flight mode", "", 14, new Vector2(18, 13), new Vector2(524, 26), UiChrome.DimText);
            var right = Root(instruments, "Armament", new Vector2(1, 0), new Vector2(-32, 32), new Vector2(580, 240));
            right.pivot = new Vector2(1, 0);
            right.localScale = Vector3.one * 0.75f;
            InstrumentGlass(right);
            Image primary = UiChrome.MakeImage(right, "Primary icon", UiChrome.Icon("projectile_burst"), Cyan);
            UiChrome.Place(primary.rectTransform, Vector2.zero, Vector2.zero, new Vector2(22, 174), new Vector2(64, 50));
            Label(right, "Primary label", "PRIMARY", 15, new Vector2(105, 207), new Vector2(430, 20), Amber);
            weaponText = Label(right, "Primary", "PULSE CANNON", 26, new Vector2(105, 173), new Vector2(450, 34), UiChrome.TextColor);
            weaponText.resizeTextForBestFit = true;
            weaponText.resizeTextMinSize = 18;
            weaponText.resizeTextMaxSize = 26;
            Divider(right, new Vector2(18, 162), 544, Cyan);

            for (int abilitySlot = 0; abilitySlot < PlayerSkills.SlotCount; abilitySlot++)
            {
                float coordinateY = 116 - abilitySlot * 49;
                Image key = UiChrome.Panel(right, "Ability binding " + abilitySlot, UiChrome.Utility);
                UiChrome.Place(key.rectTransform, Vector2.zero, Vector2.zero, new Vector2(20, coordinateY), new Vector2(42, 38));
                slotKeys[abilitySlot] = UiChrome.MakeText(key.transform, "Key", "", 21, TextAnchor.MiddleCenter, UiChrome.TextColor);
                UiChrome.Stretch(slotKeys[abilitySlot].rectTransform);
                slotIcons[abilitySlot] = UiChrome.MakeImage(right, "Ability icon " + abilitySlot, UiChrome.Icon("target_brackets"), UiChrome.MetalLight);
                UiChrome.Place(slotIcons[abilitySlot].rectTransform, Vector2.zero, Vector2.zero, new Vector2(78, coordinateY + 3), new Vector2(34, 34));
                slotNames[abilitySlot] = Label(right, "Ability " + abilitySlot, "", 19, new Vector2(127, coordinateY + 3), new Vector2(300, 34), UiChrome.TextColor);
                slotStates[abilitySlot] = Label(right, "Ability state " + abilitySlot, "", 16, new Vector2(466, coordinateY + 4), new Vector2(92, 32), Cyan);
                slotStates[abilitySlot].alignment = TextAnchor.MiddleCenter;
                slotFills[abilitySlot] = Bar(right, "Ability recovery " + abilitySlot, new Vector2(127, coordinateY - 4), 429, 2, Cyan);
            }

            var radar = Root(instruments, "Contact count", new Vector2(0.5f, 0), new Vector2(0, 48), new Vector2(440, 22));
            contactText = Label(radar, "Contacts", "", 13, Vector2.zero, new Vector2(440, 22), Amber);
            contactText.alignment = TextAnchor.MiddleCenter;
            var notice = Root(instruments, "Salvage notice", new Vector2(0.5f, 0), new Vector2(0, 292), new Vector2(760, 44));
            pickupText = UiChrome.MakeText(notice, "Pickup", "", 20, TextAnchor.MiddleCenter, UiChrome.Support);
            UiChrome.Stretch(pickupText.rectTransform);
            bossRoot = Root(instruments, "Boss telemetry", new Vector2(0.5f, 1), new Vector2(0, -66), new Vector2(640, 54));
            InstrumentGlass(bossRoot);
            bossText = Label(bossRoot, "Boss phase", "", 16, new Vector2(0, 20), new Vector2(640, 24), Amber);
            bossText.alignment = TextAnchor.MiddleCenter;
            bossFill = Bar(bossRoot, "Boss hull", new Vector2(0, 4), 640, 8, Amber);
            SegmentBar(bossFill.transform.parent, 640, 8, 20);
            bossRoot.gameObject.SetActive(false);
            targetRoot = Root(instruments, "Contact bracket", Vector2.one * 0.5f, Vector2.zero, new Vector2(140, 104));
            var bracket = targetRoot.gameObject.AddComponent<FlightHudGraphic>();
            bracket.BracketOnly = true;
            bracket.raycastTarget = false;
            Image targetCard = UiChrome.Panel(targetRoot, "Target identity", UiChrome.Utility);
            targetCard.rectTransform.anchoredPosition = new Vector2(0, -76);
            targetCard.rectTransform.sizeDelta = new Vector2(238, 30);
            targetText = UiChrome.MakeText(targetCard.transform, "Range", "", 13, TextAnchor.MiddleCenter, UiChrome.TextColor);
            UiChrome.Stretch(targetText.rectTransform);
            targetFill = Bar(targetRoot, "Target hull", new Vector2(-26, 119), 192, 10, Cyan);
            SegmentBar(targetFill.transform.parent, 192, 10, 12);
            targetRoot.gameObject.SetActive(false);
        }

        private static void InstrumentGlass(RectTransform parent)
        {
            var glass = UiChrome.Panel(parent, "Instrument glass", new Color(0.58f, 0.84f, 1f, 0.68f));
            UiChrome.Stretch(glass.rectTransform);
            glass.rectTransform.offsetMin = new Vector2(-8, -8);
            glass.rectTransform.offsetMax = new Vector2(8, 8);
            glass.transform.SetAsFirstSibling();
        }

        private static void Divider(Transform parent, Vector2 position, float width, Color color)
        {
            Image rule = UiChrome.MakeBox(parent, "Divider", color);
            UiChrome.Place(rule.rectTransform, Vector2.zero, Vector2.zero, position, new Vector2(width, 1));
        }

        private static void SegmentBar(Transform parent, float width, float height, int segments)
        {
            for (int segmentIndex = 1; segmentIndex < segments; segmentIndex++)
            {
                Image tick = UiChrome.MakeBox(parent, "Segment", new Color(0.02f, 0.13f, 0.18f, 0.7f));
                UiChrome.Place(tick.rectTransform, Vector2.zero, Vector2.zero, new Vector2(segmentIndex * width / segments, 0), new Vector2(1, height));
            }
        }

        internal static RectTransform Root(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            UiChrome.Place(rect, anchor, Vector2.one * 0.5f, position, size);
            return rect;
        }

        internal static Text Label(Transform parent, string name, string value, int size, Vector2 position, Vector2 bounds, Color color)
        {
            Text text = UiChrome.MakeText(parent, name, value, size, TextAnchor.MiddleLeft, color);
            text.fontStyle = FontStyle.Normal;
            UiChrome.Place(text.rectTransform, Vector2.zero, Vector2.zero, position, bounds);
            return text;
        }

        private static Image Bar(Transform parent, string name, Vector2 position, float width, float height, Color color)
        {
            Image track = UiChrome.MakeBox(parent, name, new Color(color.r, color.g, color.b, 0.15f));
            UiChrome.Place(track.rectTransform, Vector2.zero, Vector2.zero, position, new Vector2(width, height));
            Image fill = UiChrome.MakeBox(track.transform, "Value", color);
            UiChrome.Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            return fill;
        }
    }
}
