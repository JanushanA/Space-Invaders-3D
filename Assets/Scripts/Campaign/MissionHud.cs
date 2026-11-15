using Game.Combat;
using Game.Core;
using Game.Player;
using UnityEngine;
using UnityEngine.UI;
namespace Game.Campaign
{
    // Reference cards are live UI: no baked counters, distance or hull values.
    public sealed class MissionHud : MonoBehaviour
    {
        CampaignMission mission;
        GameObject canvas;
        RectTransform panel;
        RectTransform advicePanel;
        RectTransform relayRow;
        RectTransform hullRow;
        Text location;
        Text type;
        Text title;
        Text metric;
        Text metricLabel;
        Text instruction;
        Text action;
        Text hullValue;
        Text advice;
        Image icon;
        Image hullFill;
        readonly Image[] relayCards = new Image[3];
        readonly Text[] relayLabels = new Text[3];
        static readonly string[] RelayNames = { "AMBER", "CYAN", "VIOLET" };
        static readonly Color[] RelayColors = { UiChrome.Gold, UiChrome.NeonBlue, UiChrome.Mobility };

        public static MissionHud Attach(CampaignMission source)
        {
            var view = source.GetComponent<MissionHud>();
            if (view == null)
            {
                view = source.gameObject.AddComponent<MissionHud>();
            }

            view.mission = source;
            if (view.canvas == null)
            {
                view.Build();
            }

            return view;
        }

        void Build()
        {
            canvas = new GameObject("Mission instruments", typeof(Canvas), typeof(CanvasScaler));
            var overlayCanvas = canvas.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 105;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0;
            panel = UiChrome.Panel(canvas.transform, "Mission reference card", UiChrome.NeonBlue).rectTransform;
            UiChrome.Place(panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -28), new Vector2(588, 214));
            location = Label(panel, "Location", "", 18, new Vector2(24, 174), new Vector2(388, 28));
            var badge = UiChrome.Panel(panel, "Mission type", UiChrome.NeonBlue);
            UiChrome.Place(badge.rectTransform, Vector2.zero, Vector2.zero, new Vector2(430, 171), new Vector2(134, 32));
            type = UiChrome.MakeText(badge.transform, "Type", "", 17, TextAnchor.MiddleCenter, UiChrome.TextColor);
            UiChrome.Stretch(type.rectTransform);
            Rule(panel, 158);
            icon = UiChrome.MakeImage(panel, "Mission icon", UiChrome.Icon("core_ship"), UiChrome.NeonBlue);
            UiChrome.Place(icon.rectTransform, Vector2.zero, Vector2.zero, new Vector2(27, 88), new Vector2(62, 62));
            title = Label(panel, "Task", "", 27, new Vector2(106, 83), new Vector2(314, 68));
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            metric = Label(panel, "Live metric", "", 37, new Vector2(438, 103), new Vector2(133, 49));
            metricLabel = Label(panel, "Metric label", "DISTANCE", 13, new Vector2(438, 80), new Vector2(133, 22));
            metricLabel.color = UiChrome.Utility;
            var divider = UiChrome.MakeBox(panel, "Metric divider", UiChrome.Utility);
            UiChrome.Place(divider.rectTransform, Vector2.zero, Vector2.zero, new Vector2(425, 83), new Vector2(1, 67));
            relayRow = GameHud.Root(panel, "Ordered relays", Vector2.zero, new Vector2(255, 76), new Vector2(330, 30));

            for (int index = 0; index < 3; index++)
            {
                relayCards[index] = UiChrome.Panel(relayRow, "Relay " + RelayNames[index], RelayColors[index]);
                relayCards[index].rectTransform.anchoredPosition = new Vector2((index - 1) * 110, 0);
                relayCards[index].rectTransform.sizeDelta = new Vector2(104, 29);
                relayLabels[index] = UiChrome.MakeText(relayCards[index].transform, "State", "", 13, TextAnchor.MiddleCenter, RelayColors[index]);
                UiChrome.Stretch(relayLabels[index].rectTransform);
            }

            hullRow = GameHud.Root(panel, "Escort integrity", Vector2.zero, new Vector2(294, 69), new Vector2(540, 36));
            Label(hullRow, "Hull label", "TRANSPORT HULL", 14, new Vector2(0, 18), new Vector2(380, 20));
            hullValue = Label(hullRow, "Hull percentage", "", 17, new Vector2(460, 18), new Vector2(80, 22));
            hullValue.alignment = TextAnchor.MiddleRight;
            var track = UiChrome.MakeBox(hullRow, "Hull track", UiChrome.Metal);
            UiChrome.Place(track.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(540, 9));
            hullFill = UiChrome.MakeBox(track.transform, "Live hull", UiChrome.NeonBlue);
            UiChrome.Stretch(hullFill.rectTransform);
            hullFill.type = Image.Type.Filled;
            hullFill.fillMethod = Image.FillMethod.Horizontal;
            Rule(panel, 50);
            instruction = Label(panel, "Instruction", "", 18, new Vector2(24, 13), new Vector2(360, 29));
            instruction.color = UiChrome.Gold;
            action = Label(panel, "Action", "", 16, new Vector2(380, 13), new Vector2(185, 29));
            action.alignment = TextAnchor.MiddleRight;
            advicePanel = UiChrome.Panel(canvas.transform, "Pilot advice", UiChrome.Utility).rectTransform;
            UiChrome.Place(advicePanel, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -28), new Vector2(350, 116));
            Label(advicePanel, "Advice heading", "PILOT ADVICE", 16, new Vector2(20, 77), new Vector2(308, 24)).color = UiChrome.Utility;
            advice = Label(advicePanel, "Advice", "", 17, new Vector2(20, 15), new Vector2(308, 60));
            advice.horizontalOverflow = HorizontalWrapMode.Wrap;
            var marker = new GameObject("Objective navigation", typeof(RectTransform));
            marker.transform.SetParent(canvas.transform, false);
            marker.AddComponent<CampaignObjectiveGraphic>().Bind(ReadCue, null);
        }

        void LateUpdate()
        {
            if (mission == null || canvas == null)
            {
                return;
            }

            bool visible = !PlayerSkills.MenuOpen && mission.Pilot != null;
            if (mission.Pilot != null)
            {
                var pilotHealth = mission.Pilot.GetComponent<Health>();
                visible &= pilotHealth == null || pilotHealth.IsAlive;
            }

            panel.gameObject.SetActive(visible);
            advicePanel.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            // The card and world marker read the same live mission target and progress.
            var objectiveCue = ReadCue();
            bool relay = mission.Stage == 1 && mission.Escort == null;
            bool escort = mission.Stage == 1 && mission.Escort != null;
            location.text = CampaignSession.Locations[Mathf.Clamp(GameSession.SelectedLevel - 1, 0, 7)];
            type.text = mission.Complete ? "COMPLETE" : mission.Failed ? "FAILED" : mission.Stage == 0 ? "RECOVERY" : relay ? "RELAY LINK" : escort ? "ESCORT" : "EXTRACTION";
            title.text = mission.Complete ? "SECTOR SECURED" : mission.Failed ? "TRANSPORT LOST" : mission.Stage == 0 ? "RECOVER\nNAVIGATION CORE" : relay ? "ALIGN RELAYS" : escort ? "ESCORT TRANSPORT" : "SECURE EXTRACTION";
            title.fontSize = relay ? 29 : 25;
            title.rectTransform.anchoredPosition = new Vector2(106, relay || escort ? 108 : 83);
            title.rectTransform.sizeDelta = new Vector2(314, relay || escort ? 41 : 68);
            icon.sprite = UiChrome.Icon(escort ? "core_ship" : "target_brackets");
            float distance = objectiveCue.Valid ? Vector3.Distance(objectiveCue.Origin, objectiveCue.Position) : 0;
            metric.text = mission.Complete ? "DONE" : mission.Failed ? "—" : relay ? mission.RelayProgress + " / 3" : Mathf.CeilToInt(distance) + " m";
            metricLabel.text = relay ? "ALIGNED" : "DISTANCE";
            relayRow.gameObject.SetActive(relay && !mission.Complete && !mission.Failed);
            hullRow.gameObject.SetActive(escort && !mission.Complete && !mission.Failed);

            for (int index = 0; index < 3; index++)
            {
                relayLabels[index].text = (index < mission.RelayProgress ? "✓ " : (index + 1).ToString("00") + " ") + RelayNames[index];
                relayCards[index].color = index == mission.RelayProgress ? Color.Lerp(UiChrome.MetalDark, RelayColors[index], 0.2f) : UiChrome.MetalDark;
            }

            hullFill.fillAmount = mission.EscortHealthFraction;
            hullValue.text = Mathf.RoundToInt(mission.EscortHealthFraction * 100) + "%";
            instruction.text = mission.Complete ? "All mission objectives complete" : mission.Failed ? "Retry this sector" : mission.Stage == 0 ? "Approach within 12 m" : relay ? "Approach the " + RelayNames[Mathf.Clamp(mission.RelayProgress, 0, 2)].ToLowerInvariant() + " relay" : escort ? "Stay within 40 m" : "Hold within 16 m / clear raiders";
            action.text = mission.Complete ? "" : mission.Failed ? "[ ENTER ] RETRY" : mission.Stage == 0 ? "[ E ] RECOVER" : relay ? "[ E ] ALIGN" : escort ? (distance <= 40 ? "✓ IN RANGE" : "OUT OF RANGE") : Mathf.RoundToInt(mission.ExtractionProgress * 100) + "% TRANSFER";
            action.color = objectiveCue.Valid && distance <= objectiveCue.InteractionRange ? UiChrome.NeonBlue : UiChrome.Utility;
            advice.text = mission.Advice;
        }

        CampaignObjectiveGraphic.Cue ReadCue()
        {
            var objectiveCue = new CampaignObjectiveGraphic.Cue();
            if (mission == null || mission.Pilot == null || mission.Complete || mission.Failed)
            {
                return objectiveCue;
            }

            var health = mission.Pilot.GetComponent<Health>();
            if (health != null && !health.IsAlive)
            {
                return objectiveCue;
            }

            Transform target = mission.Stage == 0 ? mission.NavigationCore : mission.Stage == 2 ? mission.ExtractionBeacon : mission.Escort;
            objectiveCue.Name = mission.Stage == 0 ? "NAVIGATION CORE" : mission.Stage == 2 ? "EXTRACTION BEACON" : "CIVILIAN TRANSPORT";
            objectiveCue.InteractionRange = mission.Stage == 0 ? 12 : mission.Stage == 2 ? 16 : 40;
            objectiveCue.Action = mission.Stage == 0 ? "[E] RECOVER" : mission.Stage == 2 ? "HOLD / CLEAR RAIDERS" : "ESCORT / STAY CLOSE";
            objectiveCue.Color = UiChrome.NeonBlue;
            if (mission.Stage == 1 && mission.Escort == null && mission.Relays.Count > 0)
            {
                int step = Mathf.Clamp(mission.RelayProgress, 0, mission.Relays.Count - 1);
                target = mission.Relays[step];
                objectiveCue.Name = RelayNames[step] + " RELAY";
                objectiveCue.Action = "[E] ALIGN";
                objectiveCue.InteractionRange = 8;
                objectiveCue.Color = RelayColors[step];
            }

            if (target == null)
            {
                return objectiveCue;
            }

            objectiveCue.Valid = true;
            objectiveCue.Position = target.position;
            objectiveCue.Origin = mission.Pilot.position;
            return objectiveCue;
        }

        static Text Label(Transform parent, string name, string value, int size, Vector2 position, Vector2 bounds)
            => GameHud.Label(parent, name, value, size, position, bounds, UiChrome.TextColor);

        static void Rule(Transform parent, float verticalPosition)
        {
            var rule = UiChrome.MakeBox(parent, "Rule", UiChrome.Utility);
            UiChrome.Place(rule.rectTransform, Vector2.zero, Vector2.zero, new Vector2(24, verticalPosition), new Vector2(540, 1));
        }

        void OnDestroy()
        {
            if (canvas != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(canvas);
                }
                else
                {
                    DestroyImmediate(canvas);
                }
            }
        }
    }
}
