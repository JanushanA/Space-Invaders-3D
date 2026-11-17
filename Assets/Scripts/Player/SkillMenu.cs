using System;
using System.Collections.Generic;
using Game.Combat;
using Game.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Builds the interactive skill map and loadout screen, then updates it as the player selects or buys skills.
namespace Game.Player
{
    // Reference-inspired chrome; every node and requirement comes from the saved gameplay tree.
    public class SkillMenu : MonoBehaviour
    {
        private SkillTree tree;
        private PlayerSkills skills;
        private PlayerProgression progression;
        private Health health;
        private Canvas canvas;
        private GameObject panel;
        private RectTransform content;
        private RectTransform graph;
        private RectTransform treeRoot;
        private RectTransform loadout;
        private RectTransform detailPanel;
        private RectTransform graphViewport;
        private Button detailToggle;
        private bool narrow;
        private bool detailsVisible;
        private float layoutWidth;
        private Text pointsText;
        private Text detailBranch;
        private Text detailTitle;
        private Text detailBody;
        private Text detailCost;
        private Text detailStatus;
        private Text assignedText;
        private Text unlockLabel;
        private Text instruction;
        private Text zoomText;
        private Image detailIcon;
        private Button unlockButton;
        private SkillId pickedSkill = SkillId.Critical;
        private readonly List<NodeView> nodes = new List<NodeView>();
        private readonly List<LinkView> links = new List<LinkView>();
        private readonly List<SlotView> slots = new List<SlotView>();
        private readonly Dictionary<SkillId, Vector2> positions = new Dictionary<SkillId, Vector2>();
        private bool built;
        private bool isOpen;
        private bool assigning;
        private float previousTimeScale = 1f;
        private int openedFrame = -1;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private float zoom = 1f;
        private SkillId lastClick;
        private float lastClickTime;
        private static readonly string[] Branches = { "OFFENSE", "DEFENSE", "MOBILITY", "SUPPORT", "UTILITY" };
        private static readonly string[] BranchIcons = { "target_critical", "shield", "mobility_rocket", "repair_wrench", "utility_scan" };

        private sealed class NodeView
        {
            public SkillId id;
            public Image outline;
            public Image selected;
            public Image icon;
            public Image badge;
            public Text label;
            public Text status;
            public Button button;
        }

        private sealed class LinkView
        {
            public SkillId target;
            public Image image;
        }

        private sealed class SlotView
        {
            public Text key;
            public Text name;
            public Text status;
            public Image icon;
            public Image outline;
            public Button button;
        }

        private void Awake()
        {
            tree = GetComponent<SkillTree>();
            skills = GetComponent<PlayerSkills>();
            progression = GetComponent<PlayerProgression>();
            health = GetComponent<Health>();
            EnsureEventSystem();
            BuildUi();
            panel.SetActive(false);
        }

        private void OnEnable()
        {
            if (tree != null)
            {
                tree.Changed += Refresh;
            }

            if (skills != null)
            {
                skills.HotbarChanged += Refresh;
            }

            if (progression != null)
            {
                progression.Changed += Refresh;
            }
        }

        private void OnDisable()
        {
            if (tree != null)
            {
                tree.Changed -= Refresh;
            }

            if (skills != null)
            {
                skills.HotbarChanged -= Refresh;
            }

            if (progression != null)
            {
                progression.Changed -= Refresh;
            }

            if (isOpen)
            {
                SetOpen(false);
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

        public void Open()
        {
            if (!PlayerSkills.MenuOpen && (health == null || health.IsAlive))
            {
                SetOpen(true);
            }
        }

        public void HideForDeath()
        {
            if (isOpen)
            {
                SetOpen(false);
            }

            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        private void SetOpen(bool value)
        {
            if (value && panel == null)
            {
                return;
            }

            if (isOpen == value)
            {
                return;
            }

            isOpen = value;
            PlayerSkills.MenuOpen = value;
            if (panel != null)
            {
                panel.SetActive(value);
            }

            if (value)
            {
                openedFrame = Time.frameCount;
                previousTimeScale = Time.timeScale;
                previousCursorLock = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
                Time.timeScale = 0f;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                ApplyLayout(Mathf.Max(1440, Screen.height > 0 ? Screen.width * 1080f / Screen.height : 1920));
                Refresh();
                FocusPicked();
            }
            else
            {
                Time.timeScale = previousTimeScale;
                Cursor.lockState = previousCursorLock;
                Cursor.visible = previousCursorVisible;
                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }
        }

        private void Update()
        {
            if (health != null && !health.IsAlive)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            bool tabPressed = keyboard != null ? keyboard.tabKey.wasPressedThisFrame : Input.GetKeyDown(KeyCode.Tab);
            if (tabPressed && Time.frameCount != openedFrame && (isOpen || !PlayerSkills.MenuOpen))
            {
                SetOpen(!isOpen);
            }

            if (!isOpen)
            {
                return;
            }

            float width = Mathf.Max(1440, Screen.height > 0 ? Screen.width * 1080f / Screen.height : 1920);
            if (Mathf.Abs(width - layoutWidth) > 1)
            {
                ApplyLayout(width);
            }

            if (keyboard == null)
            {
                return;
            }

            if (keyboard.dKey.wasPressedThisFrame && narrow)
            {
                ToggleDetails();
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                SetOpen(false);
                return;
            }

            // Explicit keyboard navigation avoids a second EventSystem submit purchasing twice.
            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                MoveSelection(Vector2.left);
            }

            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                MoveSelection(Vector2.right);
            }

            if (keyboard.upArrowKey.wasPressedThisFrame)
            {
                MoveSelection(Vector2.up);
            }

            if (keyboard.downArrowKey.wasPressedThisFrame)
            {
                MoveSelection(Vector2.down);
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                ClickUnlock();
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                ClickSlot(0);
            }

            if (keyboard.digit2Key.wasPressedThisFrame)
            {
                ClickSlot(1);
            }

            if (keyboard.digit3Key.wasPressedThisFrame)
            {
                ClickSlot(2);
            }

            if (keyboard.minusKey.wasPressedThisFrame)
            {
                SetZoom(zoom - 0.1f);
            }

            if (keyboard.equalsKey.wasPressedThisFrame)
            {
                SetZoom(zoom + 0.1f);
            }

            if (keyboard.homeKey.wasPressedThisFrame)
            {
                SetZoom(1f);
            }

            if ((Debug.isDebugBuild || Application.isEditor) && keyboard.backslashKey.wasPressedThisFrame && progression != null)
            {
                progression.AddSkillPoints(1000);
            }
        }

        private void MoveSelection(Vector2 direction)
        {
            if (!positions.TryGetValue(pickedSkill, out Vector2 origin))
            {
                return;
            }

            float best = float.MaxValue;
            SkillId nearest = pickedSkill;

            foreach (var pair in positions)
            {
                Vector2 candidateOffset = pair.Value - origin;
                float forward = Vector2.Dot(candidateOffset, direction);
                if (forward < 5f)
                {
                    continue;
                }

                float cross = Mathf.Abs(candidateOffset.x * direction.y - candidateOffset.y * direction.x);
                float score = candidateOffset.magnitude + cross * 2f;
                if (score < best)
                {
                    best = score;
                    nearest = pair.Key;
                }
            }

            Select(nearest);
            FocusPicked();
        }

        private void FocusPicked()
        {
            if (zoom > 1.01f && positions.TryGetValue(pickedSkill, out Vector2 position))
            {
                treeRoot.anchoredPosition = -position * zoom;
            }

            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                if (nodes[nodeIndex].id == pickedSkill && EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(nodes[nodeIndex].button.gameObject);
                    break;
                }
            }
        }

        private void Refresh()
        {
            if (panel == null || tree == null)
            {
                return;
            }

            if (!built)
            {
                BuildTree();
            }

            pointsText.text = (progression != null ? progression.SkillPoints.ToString() : "0") + " SKILL POINTS";

            foreach (NodeView view in nodes)
            {
                bool owned = tree.IsUnlocked(view.id);
                bool available = tree.CanUnlock(view.id);
                Color color = BranchColor(Category(view.id));
                view.outline.color = owned || available ? color : UiChrome.MetalLight;
                view.selected.enabled = view.id == pickedSkill;
                view.icon.color = owned || available ? UiChrome.TextColor : UiChrome.MetalLight;
                view.badge.sprite = UiChrome.Icon(owned ? "check_badge" : available ? "hex_available" : "lock");
                view.badge.color = owned ? UiChrome.NeonBlue : available ? color : UiChrome.DimText;
                view.status.text = owned ? "UNLOCKED" : available ? "AVAILABLE" : "LOCKED";
                view.status.color = owned ? UiChrome.NeonBlue : UiChrome.DimText;
            }

            foreach (LinkView link in links)
            {
                Color branchColor = BranchColor(Category(link.target));
                branchColor.a = tree.IsUnlocked(link.target) ? 0.7f : 0.18f;
                link.image.color = branchColor;
            }

            for (int abilitySlot = 0; abilitySlot < slots.Count; abilitySlot++)
            {
                SkillId skillId = skills != null ? skills.GetSlot(abilitySlot) : SkillId.None;
                SkillNode node = tree.GetNode(skillId);
                bool filled = skillId != SkillId.None;
                slots[abilitySlot].key.text = skills != null ? skills.GetBind(abilitySlot).ToString().Replace("Alpha", "") : "—";
                slots[abilitySlot].name.text = filled ? node.title.ToUpperInvariant() : "UNASSIGNED";
                slots[abilitySlot].status.text = filled ? "EQUIPPED  /  [" + (abilitySlot + 1) + "] ASSIGN" : "[" + (abilitySlot + 1) + "] ASSIGN SELECTED";
                slots[abilitySlot].icon.sprite = filled ? IconFor(node.id) : UiChrome.Icon("target_brackets");
                slots[abilitySlot].icon.color = filled ? BranchColor(Category(skillId)) : UiChrome.MetalLight;
                slots[abilitySlot].outline.color = filled ? BranchColor(Category(skillId)) : UiChrome.MetalLight;
            }

            ShowDetails();
        }

        private void Select(SkillId skillId)
        {
            pickedSkill = skillId;
            assigning = false;
            Refresh();
        }

        private void ClickNode(SkillId skillId)
        {
            bool twice = skillId == lastClick && Time.unscaledTime - lastClickTime < 0.35f;
            lastClick = skillId;
            lastClickTime = Time.unscaledTime;
            Select(skillId);
            if (twice && !tree.IsUnlocked(skillId))
            {
                tree.TryUnlockPath(skillId);
                Refresh();
            }
        }

        private void ClickUnlock()
        {
            if (tree == null || pickedSkill == SkillId.None)
            {
                return;
            }

            if (tree.IsUnlocked(pickedSkill))
            {
                if (!tree.IsActiveSkill(pickedSkill))
                {
                    return;
                }

                assigning = true;
                ShowDetails();
                if (narrow)
                {
                    detailsVisible = false;
                    detailPanel.gameObject.SetActive(false);
                }

                if (slots.Count > 0 && EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(slots[0].button.gameObject);
                }

                return;
            }

            tree.TryUnlockPath(pickedSkill);
            Refresh();
        }

        private void ClickSlot(int index)
        {
            if (skills == null || index >= PlayerSkills.SlotCount)
            {
                return;
            }

            if (tree.IsActiveSkill(pickedSkill) && tree.IsUnlocked(pickedSkill))
            {
                skills.AssignToSlot(index, skills.GetSlot(index) == pickedSkill ? SkillId.None : pickedSkill);
                assigning = false;
                Refresh();
            }
            else if (skills.GetSlot(index) != SkillId.None)
            {
                Select(skills.GetSlot(index));
            }
        }

        private void ShowDetails()
        {
            SkillNode node = tree.GetNode(pickedSkill);
            bool owned = tree.IsUnlocked(pickedSkill);
            bool active = tree.IsActiveSkill(pickedSkill);
            int pathCost = tree.CostToUnlockPath(pickedSkill);
            Color accent = BranchColor(Category(pickedSkill));
            detailBranch.text = pickedSkill == SkillId.Root ? "SHIP / CORE" : Branches[Category(pickedSkill)] + " / " + (active ? "ACTIVE SKILL" : "PASSIVE UPGRADE");
            detailBranch.color = accent;
            detailTitle.text = pickedSkill == SkillId.Root ? "PILOT CORE" : node.title.ToUpperInvariant();
            detailIcon.sprite = IconFor(pickedSkill);
            detailIcon.color = accent;
            detailStatus.text = owned ? "UNLOCKED" : tree.CanUnlock(pickedSkill) ? "AVAILABLE" : "LOCKED";
            detailStatus.color = owned ? UiChrome.NeonBlue : accent;
            detailBody.text = node.detail;
            if (!owned && node.parent != SkillId.None)
            {
                detailBody.text += "\n\nRequires: " + tree.GetNode(node.parent).title + (tree.IsUnlocked(node.parent) ? " (unlocked)" : " (locked)");
            }

            detailCost.text = owned ? (active ? "ACTIVE ABILITY" : "PASSIVE / ALWAYS APPLIED") : "NODE " + node.cost + " PT   /   FULL PATH " + pathCost + " PT";
            string binding = "—";
            if (skills != null)
            {
                for (int abilitySlot = 0; abilitySlot < PlayerSkills.SlotCount; abilitySlot++)
                {
                    if (skills.GetSlot(abilitySlot) == pickedSkill)
                    {
                        binding = skills.GetBind(abilitySlot).ToString();
                    }
                }
            }

            assignedText.text = "ASSIGNED SLOT                 " + binding;
            unlockLabel.text = owned ? active ? "CHANGE SLOT   [ENTER]" : "UPGRADE APPLIED" : "UNLOCK PATH  " + pathCost + " PT";
            unlockButton.interactable = owned ? active : progression != null && progression.SkillPoints >= pathCost;
            instruction.text = assigning ? "Choose a loadout slot below, or press 1, 2 or 3.\nAssigning the same skill again clears that slot."
                : owned && active ? "Configure this ability in your loadout.\nClick a key badge to cycle its binding."
                : !owned ? "Unlock buys all missing prerequisites.\nDouble-click a node to unlock its path."
                : "Passive upgrades apply automatically.";
        }

        private static int Category(SkillId skillId) => SkillTree.Branch(skillId);

        private static Color BranchColor(int branch)
        {
            switch (branch)
            {
                case 0:
                    return UiChrome.Gold;
                case 1:
                    return UiChrome.NeonBlue;
                case 2:
                    return UiChrome.Mobility;
                case 3:
                    return UiChrome.Support;
                default:
                    return UiChrome.Utility;
            }
        }

        public static Sprite IconFor(SkillId skillId)
        {
            string icon;
            switch (skillId)
            {
                case SkillId.Root:
                    icon = "core_ship";
                    break;
                case SkillId.Critical:
                    icon = "target_critical";
                    break;
                case SkillId.Shield:
                    icon = "shield";
                    break;
                case SkillId.Barrier:
                    icon = "shield_layered";
                    break;
                case SkillId.Repair:
                    icon = "repair_plus";
                    break;
                case SkillId.Dash:
                    icon = "dash";
                    break;
                case SkillId.Afterburner:
                    icon = "afterburner";
                    break;
                case SkillId.HomingBurst:
                    icon = "satellite_dish";
                    break;
                case SkillId.Bomb:
                case SkillId.PlasmaBomb:
                    icon = "missile_volley";
                    break;
                default:
                    icon = Category(skillId) == 3 ? "repair_wrench" : Category(skillId) == 4 ? "utility_scan" : Category(skillId) == 1 ? "armor_honeycomb" : Category(skillId) == 2 ? "thrust_boost" : "projectile_burst";
                    break;
            }

            return UiChrome.Icon(icon);
        }

        private static string NodeCaption(SkillNode node) => node.id == SkillId.Root ? "CORE" : node.title.ToUpperInvariant();

        private void BuildTree()
        {
            built = true;
            var groups = new List<SkillId>[5];

            for (int branchIndex = 0; branchIndex < groups.Length; branchIndex++)
            {
                groups[branchIndex] = new List<SkillId>();
            }

            foreach (SkillNode node in tree.AllNodes)
            {
                if (node.id != SkillId.Root)
                {
                    groups[Category(node.id)].Add(node.id);
                }
            }

            for (int branch = 0; branch < 5; branch++)
            {
                groups[branch].Sort((firstSkill, secondSkill) => tree.GetNode(firstSkill).y.CompareTo(tree.GetNode(secondSkill).y));

                // Three single tiers followed by a two-option final tier: five nodes total.
                for (int nodeIndex = 0; nodeIndex < groups[branch].Count; nodeIndex++)
                {
                    int row = Mathf.Min(nodeIndex, 3);
                    float horizontalPosition = -496 + branch * 248 + (nodeIndex == 3 ? -57 : nodeIndex == 4 ? 57 : 0);
                    positions[groups[branch][nodeIndex]] = new Vector2(horizontalPosition, 212 - row * 131);
                }
            }

            positions[SkillId.Root] = new Vector2(0, -306);

            foreach (SkillNode node in tree.AllNodes)
            {
                if (node.parent != SkillId.None)
                {
                    MakeLink(node.parent, node.id);
                }

                if (node.extraLink != SkillId.None)
                {
                    MakeLink(node.extraLink, node.id);
                }
            }

            foreach (SkillNode node in tree.AllNodes)
            {
                MakeNode(node);
            }
        }

        private void MakeLink(SkillId from, SkillId targetSkill)
        {
            Vector2 startPosition = positions[from];
            Vector2 endPosition = positions[targetSkill];

            // Elbow routing gives the graph the reference's circuit-trace appearance.
            float routeY = Mathf.Min(startPosition.y, endPosition.y) - 43;
            if (from == SkillId.Root)
            {
                routeY = -306;
            }

            Vector2 bendA = new Vector2(startPosition.x, routeY);
            Vector2 bendB = new Vector2(endPosition.x, routeY);
            Segment(startPosition, bendA, targetSkill);
            Segment(bendA, bendB, targetSkill);
            Segment(bendB, endPosition, targetSkill);
        }

        private void Segment(Vector2 startPosition, Vector2 endPosition, SkillId target)
        {
            Vector2 segmentOffset = endPosition - startPosition;
            if (segmentOffset.sqrMagnitude < 1)
            {
                return;
            }

            Image image = UiChrome.MakeBox(treeRoot, "Prerequisite " + target, Color.white);
            image.rectTransform.anchoredPosition = (startPosition + endPosition) * 0.5f;
            image.rectTransform.sizeDelta = new Vector2(segmentOffset.magnitude, 2);
            image.rectTransform.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(segmentOffset.y, segmentOffset.x) * Mathf.Rad2Deg);
            links.Add(new LinkView { target = target, image = image });
        }

        private void MakeNode(SkillNode node)
        {
            RectTransform root = GameHud.Root(treeRoot, node.title, Vector2.one * 0.5f, positions[node.id], new Vector2(72, 100));
            Image fill = UiChrome.MakeImage(root, "Node button", VisualUtil.HexSprite(), UiChrome.Ink);
            fill.rectTransform.sizeDelta = Vector2.one * 64;
            fill.raycastTarget = true;
            Button button = fill.gameObject.AddComponent<Button>();
            UiChrome.StyleButton(button, UiChrome.NeonBlue);
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            SkillId skillId = node.id;
            button.onClick.AddListener(() => ClickNode(skillId));
            Image outline = UiChrome.MakeImage(root, "Category outline", UiChrome.Icon("hex_available"), BranchColor(Category(skillId)));
            outline.rectTransform.sizeDelta = Vector2.one * 82;
            Image selected = UiChrome.MakeImage(root, "Selection", UiChrome.Icon("hex_available"), UiChrome.Gold);
            selected.rectTransform.sizeDelta = Vector2.one * 94;
            Image icon = UiChrome.MakeImage(root, "Skill icon", IconFor(skillId), UiChrome.TextColor);
            icon.rectTransform.sizeDelta = Vector2.one * 34;
            Image badge = UiChrome.MakeImage(root, "Ownership", UiChrome.Icon("lock"), UiChrome.DimText);
            badge.rectTransform.anchoredPosition = new Vector2(27, 24);
            badge.rectTransform.sizeDelta = Vector2.one * 19;
            Text label = Text(root, "Name", NodeCaption(node), 13, new Vector2(0, -48), new Vector2(108, 32), UiChrome.TextColor);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            Text status = Text(root, "Status", "LOCKED", 10, new Vector2(0, -73), new Vector2(84, 16), UiChrome.DimText);
            if (skillId == SkillId.Root)
            {
                fill.rectTransform.sizeDelta = Vector2.one * 54;
                outline.rectTransform.sizeDelta = Vector2.one * 66;
                selected.rectTransform.sizeDelta = Vector2.one * 76;
                status.gameObject.SetActive(false);
            }

            nodes.Add(new NodeView { id = skillId, outline = outline, selected = selected, icon = icon, badge = badge, label = label, status = status, button = button });
        }

        private void BuildUi()
        {
            GameObject sceneObject = new GameObject("SkillMenu");
            canvas = sceneObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            CanvasScaler scaler = sceneObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;
            sceneObject.AddComponent<GraphicRaycaster>();
            Image background = UiChrome.MakeBox(sceneObject.transform, "Panel", UiChrome.Ink);
            UiChrome.Stretch(background.rectTransform);
            background.raycastTarget = true;
            panel = background.gameObject;
            content = GameHud.Root(panel.transform, "Content", Vector2.one * 0.5f, Vector2.zero, new Vector2(1872, 1028));
            Text(content, "Kicker", "SHIP SYSTEMS", 18, new Vector2(-752, 486), new Vector2(320, 24), UiChrome.Gold, TextAnchor.MiddleLeft);
            Text(content, "Heading", "SKILL TREE", 34, new Vector2(-752, 451), new Vector2(320, 44), UiChrome.TextColor, TextAnchor.MiddleLeft);
            MakeButton(content, "Tree tab", "SKILL TREE", new Vector2(-430, 464), new Vector2(190, 50), () =>
            {
                assigning = false;
                SetZoom(1);
                Select(pickedSkill);
            }, UiChrome.NeonBlue);
            MakeButton(content, "Loadout tab", "LOADOUT", new Vector2(-220, 464), new Vector2(190, 50), () =>
            {
                assigning = true;
                instruction.text = "Select an unlocked active skill, then click a loadout slot.\nKeys 1, 2 and 3 assign the selected skill.";
            }, UiChrome.Utility);
            Image points = UiChrome.Panel(content, "Skill points", UiChrome.NeonBlue);
            UiChrome.Place(points.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(590, 464), new Vector2(290, 48));
            pointsText = Text(points.transform, "Value", "0 SKILL POINTS", 21, Vector2.zero, new Vector2(270, 42), UiChrome.TextColor);
            MakeButton(content, "Close", "TAB  CLOSE", new Vector2(828, 464), new Vector2(180, 48), () => SetOpen(false), UiChrome.Utility);
            Rule(content, new Vector2(0, 424), new Vector2(1872, 2), UiChrome.NeonBlue);
            Image graphPanel = UiChrome.Panel(content, "Skill graph", UiChrome.Utility);
            UiChrome.Place(graphPanel.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(-236, -2), new Vector2(1400, 804));
            graph = graphPanel.rectTransform;
            graphPanel.raycastTarget = true;
            Image grid = UiChrome.MakeImage(graph, "Grid", UiChrome.Grid(), new Color(0.3f, 0.8f, 0.9f, 0.13f));
            UiChrome.Stretch(grid.rectTransform);
            grid.rectTransform.offsetMin = new Vector2(16, 16);
            grid.rectTransform.offsetMax = new Vector2(-16, -16);
            grid.type = Image.Type.Tiled;
            RectTransform clip = GameHud.Root(graph, "Graph viewport", Vector2.one * 0.5f, new Vector2(0, -5), new Vector2(1350, 700));
            graphViewport = clip;
            clip.gameObject.AddComponent<RectMask2D>();
            treeRoot = GameHud.Root(clip, "Nodes and prerequisites", Vector2.one * 0.5f, new Vector2(0, 20), new Vector2(1320, 740));
            ScrollRect scroll = graph.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = clip;
            scroll.content = treeRoot;
            scroll.horizontal = true;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36;
            scroll.inertia = false;

            for (int branchIndex = 0; branchIndex < 5; branchIndex++)
            {
                Color branchColor = BranchColor(branchIndex);
                Image image = UiChrome.Panel(graph, Branches[branchIndex], branchColor);
                image.rectTransform.anchoredPosition = new Vector2(-496 + branchIndex * 248, 350);
                image.rectTransform.sizeDelta = new Vector2(210, 48);
                Image icon = UiChrome.MakeImage(image.transform, "Category icon", UiChrome.Icon(BranchIcons[branchIndex]), branchColor);
                icon.rectTransform.anchoredPosition = new Vector2(-76, 0);
                icon.rectTransform.sizeDelta = Vector2.one * 30;
                Text(image.transform, "Label", Branches[branchIndex], 18, new Vector2(20, 0), new Vector2(150, 40), UiChrome.TextColor);
            }

            Text(graph, "Legend", "✓ UNLOCKED    ◇ AVAILABLE    LOCKED", 14, new Vector2(-395, -369), new Vector2(540, 24), UiChrome.DimText);
            MakeButton(graph, "Reset zoom", "HOME", new Vector2(390, -369), new Vector2(76, 36), () => SetZoom(1), UiChrome.Utility);
            MakeButton(graph, "Zoom out", "−", new Vector2(468, -369), new Vector2(46, 36), () => SetZoom(zoom - 0.1f), UiChrome.Utility);
            zoomText = Text(graph, "Zoom", "100%", 15, new Vector2(530, -369), new Vector2(68, 36), UiChrome.TextColor);
            MakeButton(graph, "Zoom in", "+", new Vector2(595, -369), new Vector2(46, 36), () => SetZoom(zoom + 0.1f), UiChrome.Utility);
            BuildDetails();
            BuildLoadout();
            detailToggle = MakeButton(content, "Detail toggle", "D  DETAILS", new Vector2(385, 464), new Vector2(130, 48), ToggleDetails, UiChrome.Utility);
            detailToggle.gameObject.SetActive(false);
        }

        private void BuildDetails()
        {
            Image card = UiChrome.Panel(content, "Details", UiChrome.Utility);
            card.raycastTarget = true;
            detailPanel = card.rectTransform;
            card.rectTransform.anchoredPosition = new Vector2(710, -2);
            card.rectTransform.sizeDelta = new Vector2(436, 804);
            detailBranch = Text(card.transform, "Branch", "OFFENSE / ACTIVE SKILL", 18, new Vector2(0, 351), new Vector2(388, 44), UiChrome.Gold);
            Rule(card.transform, new Vector2(0, 320), new Vector2(378, 1), UiChrome.MetalLight);
            Image ring = UiChrome.MakeImage(card.transform, "Icon hex", UiChrome.Icon("hex_available"), new Color(0.4f, 0.75f, 0.85f, 0.35f));
            ring.rectTransform.anchoredPosition = new Vector2(0, 206);
            ring.rectTransform.sizeDelta = Vector2.one * 220;
            detailIcon = UiChrome.MakeImage(ring.transform, "Icon", UiChrome.Icon("target_critical"), UiChrome.Gold);
            detailIcon.rectTransform.sizeDelta = Vector2.one * 100;
            detailTitle = Text(card.transform, "Name", "CRITICAL", 34, new Vector2(0, 83), new Vector2(386, 74), UiChrome.TextColor);
            detailTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            detailTitle.resizeTextForBestFit = true;
            detailTitle.resizeTextMinSize = 24;
            detailTitle.resizeTextMaxSize = 34;
            Image state = UiChrome.Panel(card.transform, "State badge", UiChrome.NeonBlue);
            state.rectTransform.anchoredPosition = new Vector2(0, 16);
            state.rectTransform.sizeDelta = new Vector2(232, 44);
            detailStatus = Text(state.transform, "Status", "UNLOCKED", 22, Vector2.zero, new Vector2(220, 40), UiChrome.NeonBlue);
            detailBody = Text(card.transform, "Description", "", 18, new Vector2(0, -71), new Vector2(370, 110), UiChrome.TextColor);
            detailBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            Rule(card.transform, new Vector2(0, -145), new Vector2(378, 1), UiChrome.MetalLight);
            detailCost = Text(card.transform, "Cost", "", 15, new Vector2(0, -169), new Vector2(374, 40), UiChrome.DimText);
            assignedText = Text(card.transform, "Assigned slot", "", 17, new Vector2(0, -210), new Vector2(374, 40), UiChrome.TextColor);
            Rule(card.transform, new Vector2(0, -236), new Vector2(378, 1), UiChrome.MetalLight);
            unlockButton = MakeButton(card.transform, "Unlock or equip", "CHANGE SLOT", new Vector2(0, -278), new Vector2(378, 56), ClickUnlock, UiChrome.Gold);
            unlockLabel = unlockButton.GetComponentInChildren<Text>();
            instruction = Text(card.transform, "Instructions", "", 15, new Vector2(0, -350), new Vector2(370, 70), UiChrome.DimText);
            instruction.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void BuildLoadout()
        {
            Image tray = UiChrome.Panel(content, "Active loadout", UiChrome.Utility);
            tray.rectTransform.anchoredPosition = new Vector2(0, -466);
            tray.rectTransform.sizeDelta = new Vector2(1872, 112);
            loadout = tray.rectTransform;
            Text(loadout, "Heading", "ACTIVE LOADOUT", 24, new Vector2(-745, 7), new Vector2(300, 40), UiChrome.TextColor);
            Text(loadout, "Navigation hint", "ARROWS: SELECT   ENTER: ACTION", 12, new Vector2(-745, -25), new Vector2(300, 22), UiChrome.DimText);

            for (int abilitySlot = 0; abilitySlot < PlayerSkills.SlotCount; abilitySlot++)
            {
                int slot = abilitySlot;
                Button button = MakeButton(loadout, "Slot " + (abilitySlot + 1), "", new Vector2(-350 + abilitySlot * 498, 0), new Vector2(474, 78), () => ClickSlot(slot), UiChrome.Utility);
                Image outline = button.transform.Find("Border").GetComponent<Image>();
                Image icon = UiChrome.MakeImage(button.transform, "Ability icon", UiChrome.Icon("target_brackets"), UiChrome.MetalLight);
                icon.rectTransform.anchoredPosition = new Vector2(-105, 0);
                icon.rectTransform.sizeDelta = Vector2.one * 42;
                Text name = Text(button.transform, "Skill name", "UNASSIGNED", 21, new Vector2(67, 13), new Vector2(280, 32), UiChrome.TextColor, TextAnchor.MiddleLeft);
                Text state = Text(button.transform, "Equipped state", "", 12, new Vector2(67, -17), new Vector2(280, 26), UiChrome.NeonBlue, TextAnchor.MiddleLeft);
                Button key = MakeButton(button.transform, "Binding", "R", new Vector2(-183, 0), new Vector2(54, 46), () =>
                {
                    if (skills != null)
                    {
                        skills.CycleBind(slot);
                        Refresh();
                    }
                }, UiChrome.Utility);
                slots.Add(new SlotView { key = key.GetComponentInChildren<Text>(), name = name, status = state, icon = icon, outline = outline, button = button });
            }
        }

        private void SetZoom(float value)
        {
            zoom = Mathf.Clamp(value, 0.8f, 1.4f);
            treeRoot.localScale = Vector3.one * zoom;
            if (Mathf.Approximately(zoom, 1f))
            {
                treeRoot.anchoredPosition = new Vector2(0, 20);
            }
            else if (zoom > 1f)
            {
                FocusPicked();
            }

            zoomText.text = Mathf.RoundToInt(zoom * 100) + "%";
        }

        private void ToggleDetails()
        {
            detailsVisible = !detailsVisible;
            detailPanel.gameObject.SetActive(!narrow || detailsVisible);
            detailToggle.GetComponentInChildren<Text>().text = detailsVisible ? "D  GRAPH" : "D  DETAILS";
            if (detailsVisible)
            {
                detailPanel.SetAsLastSibling();
            }
        }

        private void ApplyLayout(float width)
        {
            layoutWidth = width;
            narrow = width < 1740;
            float canvasWidth = narrow ? Mathf.Min(width, 1720) : 1920;
            content.sizeDelta = new Vector2(canvasWidth - 48, 1028);
            float half = content.sizeDelta.x * 0.5f;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.referenceResolution = new Vector2(1440, 1080);
            scaler.matchWidthOrHeight = Screen.height > 0 && Screen.width / (float)Screen.height < 4f / 3f ? 0 : 1;
            Position("Kicker", new Vector2(-half + 184, 486));
            Position("Heading", new Vector2(-half + 184, 451));
            Position("Tree tab", new Vector2(narrow ? -300 : -430, 464));
            Position("Loadout tab", new Vector2(narrow ? -120 : -220, 464));
            Position("Skill points", new Vector2(narrow ? 185 : 590, 464));
            ((RectTransform)content.Find("Skill points")).sizeDelta = new Vector2(narrow ? 250 : 290, 48);
            ((RectTransform)content.Find("Tree tab")).sizeDelta = new Vector2(narrow ? 160 : 190, 50);
            ((RectTransform)content.Find("Loadout tab")).sizeDelta = new Vector2(narrow ? 160 : 190, 50);
            Position("Close", new Vector2(half - 108, 464));
            graph.anchoredPosition = new Vector2(narrow ? 0 : -236, -2);
            graph.sizeDelta = new Vector2(narrow ? content.sizeDelta.x : 1400, 804);
            detailPanel.anchoredPosition = new Vector2(half - 226, -2);
            detailPanel.gameObject.SetActive(!narrow || detailsVisible);
            detailToggle.gameObject.SetActive(narrow);
            loadout.sizeDelta = new Vector2(content.sizeDelta.x, 112);
            loadout.Find("Heading").GetComponent<RectTransform>().anchoredPosition = new Vector2(-half + (narrow ? 130 : 190), 7);
            loadout.Find("Navigation hint").GetComponent<RectTransform>().anchoredPosition = new Vector2(-half + (narrow ? 130 : 190), -25);
            loadout.Find("Heading").GetComponent<Text>().fontSize = narrow ? 20 : 24;

            for (int abilitySlot = 0; abilitySlot < slots.Count; abilitySlot++)
            {
                SlotView view = slots[abilitySlot];
                RectTransform rect = view.button.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(narrow ? -250 + abilitySlot * 370 : -350 + abilitySlot * 498, 0);
                rect.sizeDelta = new Vector2(narrow ? 350 : 474, 78);
                view.icon.rectTransform.anchoredPosition = new Vector2(narrow ? -72 : -105, 0);
                view.key.transform.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(narrow ? -135 : -183, 0);
                view.name.rectTransform.anchoredPosition = new Vector2(narrow ? 57 : 67, 13);
                view.name.rectTransform.sizeDelta = new Vector2(narrow ? 200 : 280, 32);
                view.name.fontSize = narrow ? 18 : 21;
                view.status.rectTransform.anchoredPosition = new Vector2(narrow ? 57 : 67, -17);
                view.status.rectTransform.sizeDelta = new Vector2(narrow ? 200 : 280, 26);
                view.status.fontSize = narrow ? 10 : 12;
            }

            // Keep the heading underline within the content bounds after reflow.
            Transform rule = content.Find("Divider");
            if (rule != null)
            {
                rule.GetComponent<RectTransform>().sizeDelta = new Vector2(content.sizeDelta.x, 2);
            }
        }

        private void Position(string child, Vector2 position)
        {
            Transform item = content.Find(child);
            if (item != null)
            {
                item.GetComponent<RectTransform>().anchoredPosition = position;
            }
        }

        private static Text Text(Transform parent, string name, string value, int size, Vector2 position, Vector2 bounds, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            Text text = UiChrome.MakeText(parent, name, value, size, align, color);
            UiChrome.Place(text.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, position, bounds);
            return text;
        }

        private static void Rule(Transform parent, Vector2 position, Vector2 size, Color color)
        {
            Image image = UiChrome.MakeBox(parent, "Divider", color);
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
        }

        private static Button MakeButton(Transform parent, string name, string label, Vector2 position, Vector2 size, Action action, Color color)
        {
            Image image = UiChrome.Panel(parent, name, color);
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => action());
            UiChrome.StyleButton(button, color);
            Text(image.transform, "Label", label, 18, Vector2.zero, size - new Vector2(16, 4), color);
            return button;
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = EventSystem.current != null ? EventSystem.current : FindObjectOfType<EventSystem>();
            if (eventSystem == null)
            {
                GameObject sceneObject = new GameObject("EventSystem");
                eventSystem = sceneObject.AddComponent<EventSystem>();
                sceneObject.AddComponent<InputSystemUIInputModule>();
            }

            eventSystem.sendNavigationEvents = false;
        }
    }
}
