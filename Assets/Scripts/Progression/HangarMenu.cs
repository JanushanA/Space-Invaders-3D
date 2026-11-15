using System;
using Game.Campaign;
using Game.Core;
using Game.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace Game.Progression
{
    public sealed class HangarMenu : MonoBehaviour
    {
        GameObject canvasObject;
        RectTransform sheet;
        string page = "CARGO";
        string message = "";
        int campaign = 1;
        int selectedMaterial;
        int amount = 1;
        int minimumPrice;
        int maximumPrice = 1500;
        int tradePage;
        bool opened;
        bool awaitingSkills;
        bool oldNavigation;
        float oldTime;
        readonly CraftingBench bench = new CraftingBench();
        AssemblyRecipe selectedRecipe = AssemblyRecipes.All[0];
        TradingStock stock;
        InventoryState stockOwner;

        public bool IsOpen => opened;

        public CraftingBench Bench => bench;

        public AssemblyRecipe SelectedRecipe => selectedRecipe;

        void Start()
        {
            if (CampaignSession.InHideout)
            {
                campaign = CampaignSession.CurrentSector;
                Open("HIDEOUT");
            }
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (awaitingSkills && !PlayerSkills.MenuOpen)
            {
                awaitingSkills = false;
                Open("HIDEOUT");
                return;
            }

            if (opened)
            {
                if (CampaignSession.InHideout && keyboard.tabKey.wasPressedThisFrame)
                {
                    OpenSkills();
                }
                else if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (CampaignSession.InHideout)
                    {
                        Open("HIDEOUT");
                    }
                    else
                    {
                        Close();
                    }
                }

                return;
            }

            if (PlayerSkills.MenuOpen)
            {
                return;
            }

            var health = GetComponent<Game.Combat.Health>();
            if (health != null && !health.IsAlive)
            {
                return;
            }

            if (keyboard.iKey.wasPressedThisFrame)
            {
                Open("CARGO");
            }
            else if (keyboard.kKey.wasPressedThisFrame)
            {
                Open("FABRICATION");
            }
        }

        public void Open(string selectedPage)
        {
            // Switching pages clears reservations; it does not spend or refund cargo.
            if (page != selectedPage)
            {
                bench.Clear();
                if (selectedPage == "SHIPYARD")
                {
                    selectedRecipe = AssemblyRecipes.Find("ship-1");
                }
            }

            page = selectedPage;

            // Capture the gameplay state once so changing tabs does not overwrite it.
            if (!opened)
            {
                oldTime = Time.timeScale;
                opened = true;
                PlayerSkills.MenuOpen = true;
                if (EventSystem.current != null)
                {
                    oldNavigation = EventSystem.current.sendNavigationEvents;
                    EventSystem.current.sendNavigationEvents = true;
                }

                if (!Game.Net.NetSession.Active)
                {
                    Time.timeScale = 0;
                }

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            Rebuild();
        }

        public void Close()
        {
            if (!opened)
            {
                return;
            }

            opened = false;
            bench.Clear();
            PlayerSkills.MenuOpen = false;
            Time.timeScale = oldTime;
            if (EventSystem.current != null)
            {
                EventSystem.current.sendNavigationEvents = oldNavigation;
            }

            if (canvasObject != null)
            {
                canvasObject.SetActive(false);
                Destroy(canvasObject);
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OpenSkills()
        {
            var skills = GetComponent<SkillMenu>();
            if (skills == null)
            {
                return;
            }

            Close();
            awaitingSkills = CampaignSession.InHideout;
            skills.Open();
        }

        void OnDestroy()
        {
            if (opened)
            {
                PlayerSkills.MenuOpen = false;
                Time.timeScale = 1;
            }

            if (canvasObject != null)
            {
                Destroy(canvasObject);
            }
        }

        void Rebuild()
        {
            if (canvasObject != null)
            {
                canvasObject.SetActive(false);
                Destroy(canvasObject);
            }

            canvasObject = new GameObject("Hangar / " + page, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 130;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 1000);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var shade = UiChrome.MakeBox(canvas.transform, "Backdrop", new Color(0.012f, 0.035f, 0.05f, 0.96f));
            UiChrome.Stretch(shade.rectTransform);
            shade.raycastTarget = true;
            var frame = UiChrome.Panel(canvas.transform, "Systems", UiChrome.Utility);
            sheet = frame.rectTransform;
            UiChrome.Place(sheet, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1480, 900));
            Label("SHIP SYSTEMS / " + (CampaignSession.InHideout ? "WAYFARER" : "FIELD TERMINAL"), 30, 25, 1000, 35, 20, UiChrome.Gold);
            Label(page == "HIDEOUT" ? "JUMP NETWORK" : page == "FABRICATION" ? "FABRICATION / ASSEMBLY" : page, 30, 65, 1090, 55, 34, UiChrome.TextColor);
            Label(PilotInventory.State.credits.ToString("N0") + " CR", 1180, 65, 270, 50, 28, UiChrome.NeonBlue);
            string[] tabs = CampaignSession.InHideout ? new[] { "HIDEOUT", "CARGO", "FABRICATION", "SHIPYARD", "TRADE" } : new[] { "CARGO", "FABRICATION", "SHIPYARD" };

            for (int index = 0; index < tabs.Length; index++)
            {
                string selectedPage = tabs[index];
                Button(selectedPage, 30 + index * 217, 139, 205, 45, () =>
                {
                    message = "";
                    Open(selectedPage);
                }, true, page == selectedPage ? UiChrome.NeonBlue : UiChrome.MetalLight);
            }

            Button(CampaignSession.InHideout ? "MAIN MENU" : "ESC CLOSE", 1215, 139, 230, 45, () =>
            {
                bool home = CampaignSession.InHideout;
                Close();
                if (home)
                {
                    CampaignSession.Arcade();
                    UnityEngine.SceneManagement.SceneManager.LoadScene(GameSession.MenuScene);
                }
            });
            if (page == "CARGO")
            {
                Cargo();
            }
            else if (page == "FABRICATION" || page == "SHIPYARD")
            {
                Assembly();
            }
            else if (page == "TRADE")
            {
                Trade();
            }
            else
            {
                Hideout();
            }

            Label(message, 34, 850, 1410, 34, 18, UiChrome.Gold);
            if (EventSystem.current != null)
            {
                var first = canvasObject.GetComponentInChildren<Button>();
                if (first != null)
                {
                    EventSystem.current.SetSelectedGameObject(first.gameObject);
                }
            }
        }

        void MaterialArt(Transform parent, int material, float left, float topOffset, float size)
        {
            var image = new GameObject("Material " + material, typeof(RawImage)).GetComponent<RawImage>();
            image.transform.SetParent(parent, false);
            image.texture = Resources.Load<Texture2D>("Salvage/" + ((Salvage)material));
            image.raycastTarget = false;
            Place(image.rectTransform, left, topOffset, size, size);
        }

        void MaterialStrip(float topOffset)
        {
            for (int index = 0; index < 6; index++)
            {
                float left = 32 + index * 237;
                var materialPanel = UiChrome.Panel(sheet, "Material " + index, UiChrome.MetalLight);
                Place(materialPanel.rectTransform, left, topOffset, 225, 128);
                MaterialArt(materialPanel.transform, index, 75, 1, 80);
                Label(SalvageCatalog.Materials[index] + " " + PilotInventory.State.materials[index], left + 9, topOffset + 91, 210, 28, 17, UiChrome.TextColor);
            }
        }

        void Cargo()
        {
            MaterialStrip(210);
            Label("WEAPON LOCKER", 35, 358, 900, 36, 23, UiChrome.Gold);
            int row = 0;

            foreach (int item in PilotInventory.State.weapons)
            {
                int weaponId = item;
                var weaponSpec = SalvageCatalog.Weapons[weaponId];
                float topOffset = 405 + row * 51;
                Label(weaponSpec.Name.ToUpperInvariant(), 50, topOffset, 440, 37, 22, RarityColor(weaponSpec.Rarity));
                Label(weaponSpec.Rarity + " / " + weaponSpec.Damage + " damage ×" + weaponSpec.Projectiles + " / " + weaponSpec.Rate + " shots/s", 500, topOffset, 630, 37, 18, UiChrome.DimText);
                Button(PilotInventory.State.equipped == weaponId ? "EQUIPPED" : "EQUIP", 1180, topOffset, 230, 40, () =>
                {
                    PilotInventory.Equip(weaponId);
                    message = weaponSpec.Name + " equipped";
                    Rebuild();
                }).name = "Equip weapon " + weaponId;
                row++;
            }

            for (int index = 0; index < 4; index++)
            {
                Label(AssemblyRecipes.ModuleNames[index] + "\nTIER " + PilotInventory.State.ModuleTier(index) + " / 5", 40 + index * 352, 747, 340, 65, 18, UiChrome.NeonBlue);
            }

            if (Game.Net.NetSession.Active)
            {
                Label("Stored equipment applies in solo sorties; squad combat uses its server loadout.", 40, 813, 1320, 30, 17, UiChrome.DimText);
            }
        }

        public void SelectRecipe(string identifier)
        {
            var recipe = AssemblyRecipes.Find(identifier);
            if (recipe == null)
            {
                return;
            }

            selectedRecipe = recipe;
            bench.Clear();
            message = "";
            Rebuild();
        }

        void Assembly()
        {
            var state = PilotInventory.State;
            Label("MATERIAL TRAY", 35, 205, 280, 35, 22, UiChrome.Gold);

            for (int index = 0; index < 6; index++)
            {
                int material = index;
                float topOffset = 253 + index * 82;
                var choice = Button("", 35, topOffset, 270, 72, () =>
                {
                    selectedMaterial = material;
                    Rebuild();
                }, true, selectedMaterial == index ? UiChrome.NeonBlue : UiChrome.MetalLight);
                choice.name = "Material choice " + index;
                MaterialArt(choice.transform, index, 4, 3, 65);
                var name = UiChrome.MakeText(choice.transform, "Material name", SalvageCatalog.Materials[index] + "\n" + (state.materials[index] - bench.Assigned(index)) + " AVAILABLE", 17, TextAnchor.MiddleLeft, UiChrome.TextColor);
                Place(name.rectTransform, 79, 7, 183, 58);
            }

            Label("PLACE MATERIALS", 345, 205, 450, 35, 22, UiChrome.Gold);
            int[] amounts = { 1, 5, 10 };

            for (int index = 0; index < 3; index++)
            {
                int value = amounts[index];
                Button("×" + value, 345 + index * 92, 250, 82, 38, () =>
                {
                    amount = value;
                    Rebuild();
                }, true, amount == value ? UiChrome.NeonBlue : UiChrome.MetalLight);
            }

            Button("CLEAR", 625, 250, 135, 38, () =>
            {
                bench.Clear();
                message = "Grid cleared; materials remain in cargo.";
                Rebuild();
            });

            // Slot callbacks reserve cargo locally; crafting commits the complete recipe.
            for (int index = 0; index < 9; index++)
            {
                int cell = index;
                float left = 345 + (index % 3) * 138;
                float topOffset2 = 315 + (index / 3) * 134;
                var stack = bench.Cells[index];
                var slot = Button("", left, topOffset2, 126, 122, () =>
                {
                    message = bench.Add(cell, selectedMaterial, amount, state) ? "" : "Not enough unassigned material, or this slot holds another part.";
                    Rebuild();
                });
                slot.name = "Assembly slot " + index;
                var input = slot.gameObject.AddComponent<AssemblySlotInput>();
                input.Remove = () =>
                {
                    bench.Remove(cell, Keyboard.current != null && Keyboard.current.shiftKey.isPressed ? int.MaxValue : 1);
                    Rebuild();
                };
                if (stack.Count > 0)
                {
                    MaterialArt(slot.transform, stack.Material, 22, 7, 80);
                    var stackLabel = UiChrome.MakeText(slot.transform, "Stack count", stack.Count.ToString(), 22, TextAnchor.MiddleRight, UiChrome.TextColor);
                    Place(stackLabel.rectTransform, 6, 85, 106, 30);
                }
                else
                {
                    var stackLabel = UiChrome.MakeText(slot.transform, "Slot number", (index + 1).ToString(), 17, TextAnchor.MiddleCenter, UiChrome.DimText);
                    UiChrome.Stretch(stackLabel.rectTransform);
                }
            }

            Label("Select a material, then click a slot.\nRight click: remove 1. Shift + right click: clear slot.", 345, 723, 435, 57, 17, UiChrome.DimText);
            bool match = bench.Matches(state, selectedRecipe);
            bool owned = !selectedRecipe.CanReceive(state);
            Button(owned ? "OWNED / MAX TIER" : match ? "ASSEMBLE" : "MATCH RECIPE", 345, 792, 402, 46, () =>
            {
                if (bench.Craft(state, selectedRecipe, out message))
                {
                    PilotInventory.Save();
                }

                Rebuild();
            }, match && !owned, UiChrome.Gold);
            Label("RECIPE BOOK / ALL BLUEPRINTS", 835, 205, 600, 35, 22, UiChrome.Gold);

            for (int index = 0; index < AssemblyRecipes.All.Length; index++)
            {
                var recipe = AssemblyRecipes.All[index];
                int column = index % 2;
                int row = index / 2;
                Button(recipe.Name, 835 + column * 298, 250 + row * 39, 287, 34, () => SelectRecipe(recipe.Id), true, selectedRecipe == recipe ? UiChrome.NeonBlue : UiChrome.MetalLight).name = "Recipe " + recipe.Id;
            }

            Label(selectedRecipe.Name.ToUpperInvariant(), 835, 460, 600, 40, 24, UiChrome.TextColor);

            // Display the required layout separately from the player's reserved materials.
            var pattern = selectedRecipe.Pattern(state);

            for (int index = 0; index < 9; index++)
            {
                float left = 840 + (index % 3) * 72;
                float topOffset3 = 515 + (index / 3) * 72;
                var recipePanel = UiChrome.Panel(sheet, "Recipe cell " + index, UiChrome.MetalLight);
                Place(recipePanel.rectTransform, left, topOffset3, 66, 66);
                if (pattern[index].Count > 0)
                {
                    MaterialArt(recipePanel.transform, pattern[index].Material, 8, 0, 49);
                    Label(pattern[index].Count.ToString(), left + 34, topOffset3 + 40, 28, 23, 18, UiChrome.TextColor);
                }
            }

            Label(selectedRecipe.Description, 1080, 515, 355, 115, 20, UiChrome.TextColor);
            Label("ASSEMBLY FEE\n" + selectedRecipe.Price(state) + " CR" + (selectedRecipe.Kind == AssemblyKind.Module ? "\nNEXT TIER " + Mathf.Min(5, state.ModuleTier(selectedRecipe.Output) + 1) + " / 5" : ""), 1080, 650, 355, 95, 21, UiChrome.Gold);
            Label("Exact slots and quantities are required.\nEmpty cells must stay empty.", 835, 750, 600, 53, 17, UiChrome.DimText);
            if (page == "SHIPYARD")
            {
                for (int index = 0; index < 3; index++)
                {
                    int shipId = index;
                    Button((state.ship == shipId ? "ACTIVE / " : "") + SalvageCatalog.Ships[index], 35, 745 + index * 31, 270, 28, () =>
                    {
                        PilotInventory.SelectShip(shipId);
                        Rebuild();
                    }, state.ships.Contains(shipId));
                }
            }
        }

        void Trade()
        {
            var state = PilotInventory.State;
            if (stockOwner != state)
            {
                stockOwner = state;
                stock = new TradingStock(state);
            }

            if (stock.Refresh(TradingStock.CurrentEpoch))
            {
                PilotInventory.Save();
            }

            Label("SALVAGE EXCHANGE", 35, 210, 670, 35, 24, UiChrome.Gold);
            Label("PRICE RANGE", 35, 260, 170, 37, 18, UiChrome.DimText);
            var minimumInput = NumberField("Minimum price", minimumPrice, 210, 260);
            var maximumInput = NumberField("Maximum price", maximumPrice, 385, 260);
            Button("SEARCH", 565, 260, 180, 40, () =>
            {
                int.TryParse(minimumInput.text, out minimumPrice);
                int.TryParse(maximumInput.text, out maximumPrice);
                minimumPrice = Mathf.Max(0, minimumPrice);
                maximumPrice = Mathf.Max(minimumPrice, maximumPrice);
                tradePage = 0;
                Rebuild();
            });
            int refreshSeconds = (int)(300 - DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 300);
            Label("STOCK REFRESH ~" + (refreshSeconds / 60) + ":" + (refreshSeconds % 60).ToString("00") + " / SELL VALUE 50%", 810, 260, 620, 40, 18, UiChrome.DimText);

            // Search results are already sorted by price; paginate only after filtering.
            var offers = stock.Query(minimumPrice, maximumPrice);
            tradePage = Mathf.Clamp(tradePage, 0, Mathf.Max(0, (offers.Count - 1) / 7));
            Label("EQUIPMENT / MATERIALS", 40, 325, 640, 32, 18, UiChrome.DimText);
            Label("PRICE / STOCK", 750, 325, 300, 32, 18, UiChrome.DimText);

            for (int row = 0; row < 7; row++)
            {
                int index = tradePage * 7 + row;
                if (index >= offers.Count)
                {
                    break;
                }

                var offer = offers[index];
                int offerId = offer.Id;
                float topOffset = 371 + row * 60;
                Label(TradingStock.Name(offerId), 45, topOffset, 655, 45, 23, offerId < 6 ? UiChrome.TextColor : UiChrome.NeonBlue);
                Label(offer.Price + " CR / " + state.tradeStock[offerId] + " LEFT", 750, topOffset, 290, 45, 20, UiChrome.DimText);
                Button("BUY", 1050, topOffset, 175, 43, () =>
                {
                    if (stock.Buy(offerId, out message))
                    {
                        PilotInventory.Save();
                    }

                    Rebuild();
                }, state.tradeStock[offerId] > 0 && state.credits >= offer.Price).name = "Buy offer " + offerId;
                Button("SELL", 1245, topOffset, 175, 43, () =>
                {
                    if (stock.Sell(offerId, out message))
                    {
                        PilotInventory.Save();
                    }

                    bench.Clear();
                    Rebuild();
                }, offerId < 6 ? state.materials[offerId] >= 5 : offerId < 10 && state.weapons.Contains(offerId - 5)).name = "Sell offer " + offerId;
            }

            if (offers.Count == 0)
            {
                Label("No equipment in this price range.", 45, 390, 1000, 50, 24, UiChrome.DimText);
            }

            Button("PREVIOUS", 35, 805, 200, 36, () =>
            {
                tradePage--;
                Rebuild();
            }, tradePage > 0);
            Label((tradePage + 1) + " / " + Mathf.Max(1, (offers.Count + 6) / 7), 260, 805, 130, 36, 18, UiChrome.TextColor);
            Button("NEXT", 410, 805, 200, 36, () =>
            {
                tradePage++;
                Rebuild();
            }, (tradePage + 1) * 7 < offers.Count);
        }

        InputField NumberField(string name, int value, float left, float topOffset)
        {
            var inputPanel = UiChrome.Panel(sheet, name, UiChrome.MetalLight);
            Place(inputPanel.rectTransform, left, topOffset, 150, 40);
            inputPanel.raycastTarget = true;
            var input = inputPanel.gameObject.AddComponent<InputField>();
            input.contentType = InputField.ContentType.IntegerNumber;
            input.characterLimit = 7;
            var label = UiChrome.MakeText(inputPanel.transform, "Value", "", 20, TextAnchor.MiddleCenter, UiChrome.TextColor);
            UiChrome.Stretch(label.rectTransform);
            input.textComponent = label;
            input.text = value.ToString();
            return input;
        }

        void Hideout()
        {
            var graph = CampaignSession.Network;
            Label("CONNECTED JUMP LANES", 40, 210, 780, 35, 23, UiChrome.Gold);
            Func<int, Vector2> point = JumpCardPosition;

            foreach (var edge in graph.Edges)
            {
                var startPoint = point(edge.From);
                var endPoint = point(edge.To);
                Vector2 laneOffset = endPoint - startPoint;
                var line = UiChrome.MakeBox(sheet, "Jump lane", edge.From == CampaignSession.CurrentSector || edge.To == CampaignSession.CurrentSector ? UiChrome.NeonBlue : UiChrome.MetalLight);
                Place(line.rectTransform, (startPoint.x + endPoint.x) * 0.5f, (startPoint.y + endPoint.y) * 0.5f, laneOffset.magnitude, 2);
                line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                line.rectTransform.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(laneOffset.y, laneOffset.x) * Mathf.Rad2Deg);
                if (edge.From == campaign || edge.To == campaign)
                {
                    Label(edge.Cost.ToString("0.0"), (startPoint.x + endPoint.x) * 0.5f, (startPoint.y + endPoint.y) * 0.5f, 46, 22, 14, UiChrome.DimText);
                }
            }

            for (int index = 1; index <= 8; index++)
            {
                int level = index;
                var cardPosition = point(index);
                bool cleared = (PilotInventory.State.completedSectorsMask & (1 << (index - 1))) != 0;
                var color = campaign == index ? UiChrome.Gold : CampaignSession.CanLaunch(index) ? UiChrome.NeonBlue : UiChrome.MetalLight;
                Button(index + " / " + CampaignSession.Locations[index - 1].Split('/')[0].Trim() + "\n" + (cleared ? "CLEARED" : CampaignSession.CanLaunch(index) ? "READY" : "LOCKED"), cardPosition.x - 76, cardPosition.y - 28, 152, 56, () =>
                {
                    campaign = level;
                    Rebuild();
                }, true, color);
            }

            Label("CURRENT: " + CampaignSession.Locations[CampaignSession.CurrentSector - 1], 40, 780, 800, 38, 21, UiChrome.NeonBlue);
            Label("Route seed " + graph.Seed + " / Follow connected lanes; revisit cleared sectors to travel.", 40, 820, 800, 28, 16, UiChrome.DimText);
            Label(CampaignSession.Locations[campaign - 1], 880, 215, 540, 78, 28, UiChrome.TextColor);
            Label(CampaignSession.Titles[campaign - 1], 880, 315, 540, 60, 25, UiChrome.Gold);
            string briefing = CampaignSession.Briefings[campaign - 1];
            int colon = briefing.IndexOf(':');
            if (colon >= 0)
            {
                briefing = briefing.Substring(colon + 1).Trim();
            }

            Label(briefing, 880, 390, 540, 165, 21, UiChrome.TextColor);
            Label(CampaignSession.CanLaunch(campaign) ? "DIRECT ROUTE AVAILABLE" : "Travel through connected sectors to reach this location.", 880, 570, 540, 60, 20, UiChrome.DimText);
            Button("SHIP SKILLS / TAB", 880, 665, 540, 48, OpenSkills);
            Button("NEW ROUTE", 880, 725, 240, 46, () =>
            {
                CampaignSession.NewRun();
                campaign = 1;
                message = "New jump network. Gear and skills retained.";
                Rebuild();
            });
            Button("LAUNCH", 1140, 725, 280, 46, () =>
            {
                Close();
                CampaignSession.Launch(campaign);
            }, CampaignSession.CanLaunch(campaign), UiChrome.Gold);
            Label("Mission advice arrives over the flight comms channel.", 880, 790, 540, 45, 17, UiChrome.DimText);
        }

        public static Vector2 JumpCardPosition(int sector)
        {
            // Equal UI slots keep the 152 x 56 cards apart for every seed. Generated
            // network edges and weights still determine the actual available routes.
            float angle = (Mathf.Clamp(sector, 1, 8) - 1) * 45 * Mathf.Deg2Rad;
            return new Vector2(430 + Mathf.Cos(angle) * 288, 497 - Mathf.Sin(angle) * 211.5f);
        }

        static Color RarityColor(Rarity rarity) => rarity == Rarity.Legendary ? UiChrome.Gold : rarity == Rarity.Epic ? UiChrome.Mobility : rarity == Rarity.Rare ? UiChrome.NeonBlue : UiChrome.TextColor;

        Text Label(string text, float left, float topOffset, float width, float height, int size, Color color)
        {
            var label = UiChrome.MakeText(sheet, text, text, size, TextAnchor.MiddleLeft, color);
            Place(label.rectTransform, left, topOffset, width, height);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }

        Button Button(string text, float left, float topOffset, float width, float height, Action click, bool enabled = true, Color? accent = null)
        {
            var panelImage = UiChrome.Panel(sheet, text, accent ?? UiChrome.NeonBlue);
            Place(panelImage.rectTransform, left, topOffset, width, height);
            panelImage.raycastTarget = true;
            var button = panelImage.gameObject.AddComponent<Button>();
            button.targetGraphic = panelImage;
            button.interactable = enabled;
            UiChrome.StyleButton(button, accent ?? UiChrome.NeonBlue);
            button.onClick.AddListener(() => click());
            var label = UiChrome.MakeText(panelImage.transform, "Label", text, 19, TextAnchor.MiddleCenter, UiChrome.TextColor);
            UiChrome.Stretch(label.rectTransform);
            return button;
        }

        static void Place(RectTransform rectTransform, float left, float topOffset, float width, float height) => UiChrome.Place(rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(left, -topOffset), new Vector2(width, height));
    }

    public sealed class AssemblySlotInput : MonoBehaviour, IPointerClickHandler
    {
        public Action Remove;

        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Right)
            {
                Remove?.Invoke();
            }
        }
    }
}
