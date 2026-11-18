using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Run after AssetDatabase imports the supplied resources. No pilot preferences are written.
public static class ReferenceUiChecks
{
    [MenuItem("Tools/Group Game/Check reference UI and skill interactions")]
    public static void Run()
    {
        bool suppressed = GameSession.SuppressSave;
        bool menuOpen = PlayerSkills.MenuOpen;
        float timeScale = Time.timeScale;
        CursorLockMode cursor = Cursor.lockState;
        bool cursorVisible = Cursor.visible;
        EventSystem previousEventSystem = EventSystem.current != null ? EventSystem.current : UnityEngine.Object.FindObjectOfType<EventSystem>();
        bool navigation = previousEventSystem != null && previousEventSystem.sendNavigationEvents;
        GameObject pilot = null;
        SkillMenu menu = null;
        try
        {
            GameSession.SuppressSave = true;
            PlayerSkills.MenuOpen = false;
            Font font = Resources.Load<Font>("UI/Fonts/Roboto-Bold");
            Require(font != null && VisualUtil.UiFont() == font, "Supplied Roboto is not the runtime font");

            foreach (string name in new[] { "core_ship", "target_critical", "shield", "mobility_rocket", "repair_wrench", "utility_scan", "lock", "check_badge" })
            {
                Sprite icon = Resources.Load<Sprite>("UI/Icons/White/" + name);
                Require(icon != null, "Supplied UI icon missing or not imported as Sprite: " + name);
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(icon));
                Require(!importer.mipmapEnabled && importer.alphaIsTransparency && importer.wrapMode == TextureWrapMode.Clamp,
                    "UI sprite has unsuitable texture settings: " + name);
            }

            // Build the real skill screen after validating its imported font and sprite settings.
            pilot = new GameObject("Reference UI check pilot");
            PlayerProgression progression = pilot.AddComponent<PlayerProgression>();
            Call(progression, "Awake");
            SkillTree tree = pilot.AddComponent<SkillTree>();
            Call(tree, "Awake");
            var owned = (HashSet<SkillId>)Get(tree, "unlocked");
            owned.Clear();
            owned.Add(SkillId.Root);
            owned.Add(SkillId.Critical);
            Set(progression, "skillPoints", 0);
            PlayerSkills skills = pilot.AddComponent<PlayerSkills>();
            Call(skills, "Awake");
            menu = pilot.AddComponent<SkillMenu>();
            if (Get(menu, "canvas") == null)
            {
                Call(menu, "Awake");
            }

            Time.timeScale = 0.5f;
            menu.Open();
            Require(PlayerSkills.MenuOpen && Mathf.Approximately(Time.timeScale, 0), "Opening skill screen did not pause and block flight input");
            var views = (IList)Get(menu, "nodes");
            int nodeCount = 0;

            foreach (SkillNode node in tree.AllNodes)
            {
                nodeCount++;
            }

            Require(views.Count == nodeCount, "Graph omits a gameplay skill");
            Canvas canvas = (Canvas)Get(menu, "canvas");

            foreach (Text text in canvas.GetComponentsInChildren<Text>(true))
            {
                Require(text.font == font && !text.raycastTarget, "Text font mismatch or text blocks a control: " + text.name);
            }

            foreach (Image image in canvas.GetComponentsInChildren<Image>(true))
            {
                if (image.raycastTarget)
                {
                    Require(image.GetComponent<Button>() != null || image.GetComponent<ScrollRect>() != null || image.name == "Panel" || image.name == "Details", "Decoration intercepts pointer: " + image.name);
                }
            }

            // A real prerequisite chain must be atomic when the player has insufficient points.
            Set(menu, "pickedSkill", SkillId.Dash);
            Call(menu, "Refresh");
            int before = owned.Count;
            Call(menu, "ClickUnlock");
            Require(owned.Count == before && progression.SkillPoints == 0, "Unaffordable UI unlock spent points or unlocked a partial path");
            int cost = tree.CostToUnlockPath(SkillId.Dash);
            progression.AddSkillPoints(cost);
            Call(menu, "ClickUnlock");
            Require(tree.IsUnlocked(SkillId.Dash) && progression.SkillPoints == 0, "Unlock action did not purchase the selected prerequisite path exactly once");
            Call(menu, "ClickUnlock");
            Require((bool)Get(menu, "assigning"), "Owned active skill's action did not open slot assignment");
            Call(menu, "ClickSlot", 1);
            Require(skills.GetSlot(1) == SkillId.Dash, "Loadout action did not equip the selected skill");
            Require(((Text)Get(menu, "assignedText")).text.Contains(skills.GetBind(1).ToString()), "Detail card does not show the real assigned binding");
            Call(menu, "ClickSlot", 1);
            Require(skills.GetSlot(1) == SkillId.None, "Selecting an already-equipped slot did not clear it");

            // Narrow screens collapse details instead of shrinking graph labels and body text.
            RectTransform content = (RectTransform)Get(menu, "content");
            RectTransform details = (RectTransform)Get(menu, "detailPanel");

            foreach (Vector2 resolution in new[] { new Vector2(1920, 1080), new Vector2(1440, 1080) })
            {
                Call(menu, "ApplyLayout", resolution.x);
                Require(content.sizeDelta.x <= resolution.x && content.sizeDelta.y <= resolution.y, "Skill content exceeds " + resolution);
                Require(details.gameObject.activeSelf == (resolution.x > 1740), "Details did not collapse at narrow aspect");
            }

            Call(menu, "ToggleDetails");
            Require(details.gameObject.activeSelf, "Narrow details toggle did not expose the selected node");
            Require(((Text)Get(menu, "detailBody")).fontSize >= 18, "Narrow layout shrinks detail text below reference size");
            var graph = (RectTransform)Get(menu, "graph");
            Require(graph.GetComponent<ScrollRect>() != null, "Skill graph is not scrollable");
            Call(menu, "SetZoom", 1.4f);
            Require(Mathf.Approximately(((RectTransform)Get(menu, "treeRoot")).localScale.x, 1.4f), "Graph zoom did not enlarge nodes");
            Call(menu, "SetZoom", 1f);

            // Sweep marker directions to keep navigation cues outside the reserved instrument areas.
            MethodInfo edgePoint = typeof(Game.Campaign.CampaignObjectiveGraphic).GetMethod("EdgePoint", BindingFlags.NonPublic | BindingFlags.Static);

            for (int angle = 0; angle < 360; angle += 5)
            {
                Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                Vector2 point = (Vector2)edgePoint.Invoke(null, new object[] { direction });
                Require(point.x >= 0.079f && point.x <= 0.921f && point.y >= 0.319f && point.y <= 0.801f,
                    "Objective edge marker overlaps reserved HUD bands at " + angle + " degrees");
            }

            // Closing and reopening modal screens must restore the earlier pause state and exclude other menus.
            Call(menu, "SetOpen", false);
            Require(!PlayerSkills.MenuOpen && Mathf.Approximately(Time.timeScale, 0.5f), "Closing skill menu did not restore previous pause state");
            PlayerSkills.MenuOpen = true;
            menu.Open();
            Require(!(bool)Get(menu, "isOpen"), "Skills opened on top of another system menu");
            PlayerSkills.MenuOpen = false;

            // A detached overlay can be destroyed first during scene unload. Shutdown must still restore state.
            menu.Open();
            UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            Call(menu, "OnDisable");
            Require(!PlayerSkills.MenuOpen && Mathf.Approximately(Time.timeScale, 0.5f), "Destroyed-canvas shutdown did not restore input/pause state");
            Call(menu, "OnDisable");
            Call(menu, "OnDestroy");
            Require(canvas == null, "Skill screen leaked its detached canvas");
            Debug.Log("REFERENCE UI CHECKS: PASS supplied sprites/font, importer, all real nodes, pointer isolation, unaffordable and exact path purchase, equip/clear/binding, 16:9 and 4:3 reflow, detail toggle, graph scroll/zoom, modal exclusion, previous-time restore and canvas cleanup");
        }
        finally
        {
            if (menu != null)
            {
                Call(menu, "OnDisable");
                Call(menu, "OnDestroy");
            }

            if (pilot != null)
            {
                UnityEngine.Object.DestroyImmediate(pilot);
            }

            EventSystem currentEventSystem = EventSystem.current != null ? EventSystem.current : UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (previousEventSystem == null && currentEventSystem != null)
            {
                UnityEngine.Object.DestroyImmediate(currentEventSystem.gameObject);
            }
            else if (previousEventSystem != null)
            {
                previousEventSystem.sendNavigationEvents = navigation;
            }

            GameSession.SuppressSave = suppressed;
            PlayerSkills.MenuOpen = menuOpen;
            Time.timeScale = timeScale;
            Cursor.lockState = cursor;
            Cursor.visible = cursorVisible;
        }
    }

    private static object Get(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }

    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }

    private static void Call(object target, string name, params object[] args)
    {
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    }

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }
}
