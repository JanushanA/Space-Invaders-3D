using Game.Combat;
using Game.Core;
using Game.Enemies;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace Game.Player
{
    // Mission failed overlay: retry, sector map, main menu.
    // Shows the death screen and handles retry rules for solo and multiplayer games.
    public class DeathFlow : MonoBehaviour
    {
        private Health health;
        private bool open;
        private int selected;
        private GameObject root;
        private Image[] buttonGlows;
        private Image[] buttonOuter;
        private Image[] buttonInner;
        private Text[] buttonLabels;
        private Text subText;

        private void Awake()
        {
            // Builds the failure UI once and keeps it hidden until a player or party death occurs.
            health = GetComponent<Health>();
            EnsureEventSystem();
            BuildUi();
            SetOpen(false);
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.Died += HandleDied;
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Died -= HandleDied;
            }
        }

        private void Update()
        {
            // Handles keyboard selection only while the failure screen is visible and gameplay is paused.
            if (!open)
            {
                return;
            }

            if (Down(KeyCode.UpArrow) || Down(KeyCode.W))
            {
                // Wraps upward from the first button to the last using modulo arithmetic.
                selected = (selected + 2) % 3;
                PaintButtons();
            }

            if (Down(KeyCode.DownArrow) || Down(KeyCode.S))
            {
                // Moves down through the three options and wraps back to the first button.
                selected = (selected + 1) % 3;
                PaintButtons();
            }

            if (Down(KeyCode.Space))
            {
                Retry();
                return;
            }

            if (Down(KeyCode.Return) || Down(KeyCode.KeypadEnter))
            {
                Activate(selected);
                return;
            }

            if (Down(KeyCode.Escape))
            {
                MainMenu();
            }
        }

        private void HandleDied(Health source, DamageInfo info)
        {
            // Lets multiplayer wait for a confirmed party wipe instead of showing one player's retry screen.
            if (Game.Net.NetSession.Active && Game.Net.NetSession.InMatch)
            {
                return;
            }

            OpenFailedUi(info);
        }

        public void OpenForPartyWipe()
        {
            OpenFailedUi(new DamageInfo(0f, DamageSource.Script, null, Vector3.zero, Vector3.up));
        }

        private void OpenFailedUi(DamageInfo info)
        {
            // Hides other gameplay UI, records the cause of death, then pauses the game before opening controls.
            SkillMenu skillMenu = GetComponent<SkillMenu>();
            if (skillMenu != null)
            {
                skillMenu.HideForDeath();
            }

            GameSession.NoteDeath(KillerName(info));
            if (subText != null)
            {
                if (Game.Net.NetSession.Active)
                {
                    subText.text = "ALL SHIPS WERE DESTROYED IN " + GameSession.SectorLabel();
                }
                else
                {
                    subText.text = "YOUR SHIP WAS DESTROYED IN " + GameSession.SectorLabel();
                }
            }

            selected = 0;
            PaintButtons();

            // Resets selection to Retry so keyboard input has a predictable first action.
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SetOpen(true);
        }

        private void Activate(int index)
        {
            // Converts the selected UI index into the matching retry, map, or main-menu action.
            if (index == 0)
            {
                Retry();
            }
            else if (index == 1)
            {
                SectorMap();
            }
            else
            {
                MainMenu();
            }
        }

        private void Retry()
        {
            // Lets only the multiplayer host restart so every connected player loads the same match together.
            Time.timeScale = 1f;
            if (Game.Net.NetSession.Active)
            {
                if (!Game.Net.NetSession.IsHost)
                {
                    // Shows feedback instead of allowing a client to desynchronise the party by reloading alone.
                    if (subText != null)
                    {
                        subText.text = "ONLY THE HOST CAN RETRY";
                    }

                    return;
                }

                if (Game.Net.NetRunner.Instance != null)
                {
                    Game.Net.NetRunner.Instance.HostStart();
                }

                return;
            }

            SceneManager.LoadScene(GameSession.CombatScene);
        }

        private void SectorMap()
        {
            // Sends multiplayer parties back to their lobby but opens normal level select for solo play.
            Time.timeScale = 1f;
            if (Game.Net.NetSession.Active && Game.Net.NetRunner.Instance != null)
            {
                Game.Net.NetRunner.Instance.ReturnEveryoneToLobby();
                return;
            }

            GameSession.ShowLevelSelect = true;
            SceneManager.LoadScene(GameSession.MenuScene);
        }

        private void MainMenu()
        {
            // Leaves the network session before loading the menu so old connections cannot continue running.
            Time.timeScale = 1f;
            if (Game.Net.NetRunner.Instance != null)
            {
                Game.Net.NetRunner.Instance.Leave();
            }

            GameSession.ShowLevelSelect = false;
            GameSession.ShowMpLobby = false;
            SceneManager.LoadScene(GameSession.MenuScene);
        }

        private void SetOpen(bool isEnabled)
        {
            // Changes the logical input gate and the root UI visibility together.
            open = isEnabled;
            if (root != null)
            {
                root.SetActive(isEnabled);
            }
        }

        private static string KillerName(DamageInfo info)
        {
            // Converts the damage instigator into a readable label for score and death-screen text.
            if (info.Instigator == null)
            {
                return "ENEMY FIRE";
            }

            MotherShipBoss boss = info.Instigator.GetComponentInParent<MotherShipBoss>();
            if (boss != null)
            {
                if (info.Instigator.GetComponent<MotherShipBoss>() != null)
                {
                    return "MOTHERSHIP";
                }

                return "MOTHERSHIP ESCORT";
            }

            return "ENEMY FIGHTER";
        }

        private void PaintButtons()
        {
            // Repaints every button from the selected index so keyboard and mouse selection look identical.
            string retryName = "RETRY";
            if (Game.Net.NetSession.Active && !Game.Net.NetSession.IsHost)
            {
                retryName = "WAIT FOR HOST";
            }

            string[] names = { retryName, "SECTOR MAP", "MAIN MENU" };

            for (int entryIndex = 0; entryIndex < 3; entryIndex++)
            {
                // Applies a brighter red accent and arrow markers only to the currently selected action.
                bool isEnabled = entryIndex == selected;
                Color accent = UiChrome.NeonBlue;
                if (isEnabled)
                {
                    accent = GameHud.Amber;
                }

                if (buttonGlows[entryIndex] != null)
                {
                    Color glow = accent;
                    if (isEnabled)
                    {
                        glow.a = 0.09f;
                    }
                    else
                    {
                        glow.a = 0.025f;
                    }

                    buttonGlows[entryIndex].color = glow;
                }

                if (buttonOuter[entryIndex] != null)
                {
                    buttonOuter[entryIndex].color = accent;
                }

                if (buttonInner[entryIndex] != null)
                {
                    Color inner = accent;
                    inner.a = 0.18f;
                    buttonInner[entryIndex].color = inner;
                }

                if (buttonLabels[entryIndex] != null)
                {
                    buttonLabels[entryIndex].color = Color.white;
                    if (isEnabled)
                    {
                        buttonLabels[entryIndex].text = names[entryIndex] + "   /   ENTER";
                    }
                    else
                    {
                        buttonLabels[entryIndex].text = names[entryIndex];
                    }
                }
            }
        }

        private void BuildUi()
        {
            // Builds an opaque screen-space overlay and its sci-fi button layout at runtime.
            GameObject canvasObject = new GameObject("DeathCanvas");
            canvasObject.transform.SetParent(null, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();
            root = new GameObject("Failed");
            root.transform.SetParent(canvasObject.transform, false);
            Stretch(root.AddComponent<RectTransform>());
            Image wash = UiChrome.MakeBox(root.transform, "Wash", UiChrome.Ink);

            // Instrument-dark backing keeps failure actions legible above the stopped scene.
            Stretch(wash.rectTransform);
            wash.raycastTarget = true;
            Text title = UiChrome.MakeText(root.transform, "Title", "MISSION FAILED", 46, TextAnchor.MiddleCenter, UiChrome.NeonRed);
            title.rectTransform.anchoredPosition = new Vector2(0f, 170f);
            title.rectTransform.sizeDelta = new Vector2(900f, 72f);
            Image titleGlow = UiChrome.MakeImage(root.transform, "TitleGlow", UiChrome.SoftGlow(), new Color(1f, 0.56f, 0.18f, 0.055f));
            titleGlow.rectTransform.anchoredPosition = new Vector2(0f, 170f);
            titleGlow.rectTransform.sizeDelta = new Vector2(560f, 90f);
            titleGlow.transform.SetSiblingIndex(1);
            subText = UiChrome.MakeText(root.transform, "Sub", "YOUR SHIP WAS DESTROYED IN SECTOR 01", 18, TextAnchor.MiddleCenter, Color.white);
            subText.rectTransform.anchoredPosition = new Vector2(0f, 118f);
            subText.rectTransform.sizeDelta = new Vector2(900f, 28f);
            buttonGlows = new Image[3];
            buttonOuter = new Image[3];
            buttonInner = new Image[3];
            buttonLabels = new Text[3];
            MakeChoice(0, "RETRY", new Vector2(0f, 20f));
            MakeChoice(1, "SECTOR MAP", new Vector2(0f, -58f));
            MakeChoice(2, "MAIN MENU", new Vector2(0f, -136f));
            Image failureIcon = UiChrome.MakeImage(root.transform, "Failure icon", UiChrome.Icon("warning"), UiChrome.NeonRed);
            failureIcon.rectTransform.anchoredPosition = new Vector2(0, 258);
            failureIcon.rectTransform.sizeDelta = new Vector2(58, 58);
            Text hint = UiChrome.MakeText(root.transform, "Keyboard help", "UP / DOWN  SELECT     ENTER  CONFIRM     SPACE  RETRY", 15, TextAnchor.MiddleCenter, UiChrome.DimText);
            hint.rectTransform.anchoredPosition = new Vector2(0, -215);
            hint.rectTransform.sizeDelta = new Vector2(800, 32);
            PaintButtons();
        }

        private void MakeChoice(int index, string label, Vector2 position)
        {
            // Creates one layered chrome button and stores its visuals for later highlight updates.
            GameObject sceneObject = new GameObject(label);
            sceneObject.transform.SetParent(root.transform, false);
            RectTransform rect = sceneObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(560f, 64f);
            rect.anchoredPosition = position;
            Image glow = UiChrome.MakeImage(sceneObject.transform, "Glow", UiChrome.SoftGlow(), new Color(0.35f, 0.85f, 1f, 0.16f));
            glow.rectTransform.sizeDelta = new Vector2(640f, 110f);
            buttonGlows[index] = glow;
            Image fill = UiChrome.MakeImage(sceneObject.transform, "Fill", UiChrome.ChamferFill(), UiChrome.MetalDark);
            fill.type = Image.Type.Sliced;
            fill.raycastTarget = true;
            Stretch(fill.rectTransform);
            Image grid = UiChrome.MakeImage(fill.transform, "Grid", UiChrome.Grid(), new Color(1f, 1f, 1f, 0.12f));
            grid.type = Image.Type.Tiled;
            Stretch(grid.rectTransform);
            grid.rectTransform.offsetMin = new Vector2(16f, 10f);
            grid.rectTransform.offsetMax = new Vector2(-16f, -10f);
            Image ventsL = UiChrome.MakeImage(sceneObject.transform, "VentsL", UiChrome.Vents(), new Color(1f, 1f, 1f, 0.16f));
            ventsL.rectTransform.sizeDelta = new Vector2(14f, 36f);
            ventsL.rectTransform.anchoredPosition = new Vector2(-248f, 0f);
            Image ventsR = UiChrome.MakeImage(sceneObject.transform, "VentsR", UiChrome.Vents(), new Color(1f, 1f, 1f, 0.16f));
            ventsR.rectTransform.sizeDelta = new Vector2(14f, 36f);
            ventsR.rectTransform.anchoredPosition = new Vector2(248f, 0f);
            Image inner = UiChrome.MakeImage(sceneObject.transform, "Inner", UiChrome.ChamferRing(), UiChrome.Utility);
            inner.type = Image.Type.Sliced;
            Stretch(inner.rectTransform);
            inner.rectTransform.offsetMin = new Vector2(4f, 4f);
            inner.rectTransform.offsetMax = new Vector2(-4f, -4f);
            buttonInner[index] = inner;
            Image outer = UiChrome.MakeImage(sceneObject.transform, "Outer", UiChrome.ChamferRing(), UiChrome.NeonBlue);
            outer.type = Image.Type.Sliced;
            Stretch(outer.rectTransform);
            outer.rectTransform.offsetMin = new Vector2(-2f, -2f);
            outer.rectTransform.offsetMax = new Vector2(2f, 2f);
            buttonOuter[index] = outer;
            Text text = UiChrome.MakeText(sceneObject.transform, "Label", label, 24, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);
            buttonLabels[index] = text;
            int pick = index;
            Button button = fill.gameObject.AddComponent<Button>();

            // Uses Unity's pointer button while keyboard navigation calls the same Activate method.
            ColorBlock colors = button.colors;
            colors.highlightedColor = Color.Lerp(Color.white, UiChrome.NeonBlue, 0.35f);
            colors.pressedColor = UiChrome.NeonBlue;
            button.colors = colors;
            button.targetGraphic = fill;
            button.onClick.AddListener(delegate
            {
                selected = pick;
                PaintButtons();
                Activate(pick);
            });
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static bool Down(KeyCode key)
        {
            // Checks supported new Input System keys explicitly, then keeps the legacy fallback for compatibility.
            if (Keyboard.current != null)
            {
                if (key == KeyCode.Space)
                {
                    return Keyboard.current.spaceKey.wasPressedThisFrame;
                }

                if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    return Keyboard.current.enterKey.wasPressedThisFrame ||
                           Keyboard.current.numpadEnterKey.wasPressedThisFrame;
                }

                if (key == KeyCode.Escape)
                {
                    return Keyboard.current.escapeKey.wasPressedThisFrame;
                }

                if (key == KeyCode.UpArrow)
                {
                    return Keyboard.current.upArrowKey.wasPressedThisFrame;
                }

                if (key == KeyCode.DownArrow)
                {
                    return Keyboard.current.downArrowKey.wasPressedThisFrame;
                }

                if (key == KeyCode.W)
                {
                    return Keyboard.current.wKey.wasPressedThisFrame;
                }

                if (key == KeyCode.S)
                {
                    return Keyboard.current.sKey.wasPressedThisFrame;
                }
            }

            return Input.GetKeyDown(key);
        }

        private static void EnsureEventSystem()
        {
            // Reuses an existing EventSystem or creates one so runtime buttons can receive mouse clicks.
            if (EventSystem.current != null)
            {
                EventSystem.current.sendNavigationEvents = false;
                return;
            }

            GameObject sceneObject = new GameObject("EventSystem");
            EventSystem eventSystem = sceneObject.AddComponent<EventSystem>();
            eventSystem.sendNavigationEvents = false;
            sceneObject.AddComponent<InputSystemUIInputModule>();
        }
    }
}
