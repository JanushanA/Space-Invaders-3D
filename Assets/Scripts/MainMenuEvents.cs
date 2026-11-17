using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Aubrey's UI Toolkit entry flow, extended to the integrated campaign and lobby.
[RequireComponent(typeof(UIDocument))]
public class MainMenuEvents : MonoBehaviour
{
    private VisualElement root;
    private string panel = "HomePanel";
    private static readonly string[] SectorNames = { "ASHFALL", "VEIL", "DRIFT", "CINDER", "TEMPEST", "RINGS", "UMBRA", "ABYSS" };

    private void OnEnable()
    {
        Time.timeScale = 1f;
        GameSession.SuppressSave = false;
        Game.Player.PlayerSkills.MenuOpen = false;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        root = GetComponent<UIDocument>().rootVisualElement;
        var startButton = root.Q<Button>("StartGameButton");
        if (root.Q<Button>("StoryModeButton") == null && startButton != null)
        {
            var story = new Button { name = "StoryModeButton", text = "STORY MODE" };
            story.AddToClassList("button");
            startButton.parent.Insert(startButton.parent.IndexOf(startButton), story);
        }

        root.RegisterCallback<ClickEvent>(OnClick);
        root.RegisterCallback<ChangeEvent<bool>>(OnToggle);
        BuildSectors("SectorList", false);
        BuildSectors("LobbySectors", true);
        root.Q<Toggle>("InvertLook").SetValueWithoutNotify(GameSession.InvertLook);
        if (GameSession.ShowMpLobby && NetSession.Active)
        {
            Show("LobbyPanel");
        }
        else if (GameSession.ShowLevelSelect)
        {
            Show("SectorPanel");
        }
        else
        {
            Show("HomePanel");
        }

        GameSession.ShowMpLobby = false;
        GameSession.ShowLevelSelect = false;
    }

    private void OnDisable()
    {
        if (root == null)
        {
            return;
        }

        root.UnregisterCallback<ClickEvent>(OnClick);
        root.UnregisterCallback<ChangeEvent<bool>>(OnToggle);
        root = null;
    }

    private void BuildSectors(string container, bool lobby)
    {
        var list = root.Q(container);
        list.Clear();

        for (int sector = 1; sector <= GameSession.LevelCount; sector++)
        {
            var button = new Button { name = (lobby ? "LobbySector" : "Sector") + sector };
            button.userData = sector;
            button.AddToClassList("button");
            button.AddToClassList("sector-button");
            list.Add(button);
        }
    }

    private void Show(string name)
    {
        panel = name;

        foreach (string identifier in new[] { "HomePanel", "SectorPanel", "SettingsPanel", "MultiPanel", "LobbyPanel" })
        {
            root.Q(identifier).style.display = identifier == name ? DisplayStyle.Flex : DisplayStyle.None;
        }

        Refresh();
    }

    private void OnToggle(ChangeEvent<bool> inputEvent)
    {
        if ((inputEvent.target as VisualElement)?.name == "InvertLook")
        {
            GameSession.SetInvertLook(inputEvent.newValue);
        }
    }

    private void OnClick(ClickEvent inputEvent)
    {
        var element = inputEvent.target as VisualElement;

        while (element != null && !(element is Button))
        {
            element = element.parent;
        }

        if (element == null || !element.enabledInHierarchy)
        {
            return;
        }

        string identifier = element.name;
        if (element.userData is int sector)
        {
            if (identifier.StartsWith("LobbySector"))
            {
                NetRunner.Instance?.HostSetLevel(sector);
            }
            else if (GameSession.IsUnlocked(sector))
            {
                NetRunner.Instance?.Leave();
                Game.Campaign.CampaignSession.Arcade();
                GameSession.SelectedLevel = sector;
                GameSession.EnteredFromMenu = true;
                SceneManager.LoadScene(GameSession.CombatScene);
            }

            return;
        }

        switch (identifier)
        {
            case "StoryModeButton":
                Game.Campaign.CampaignSession.OpenHideout();
                break;
            case "StartGameButton":
                Show("SectorPanel");
                break;
            case "MultiplayerButton":
                Game.Campaign.CampaignSession.Arcade();
                Show("MultiPanel");
                break;
            case "SettingsButton":
                Show("SettingsPanel");
                break;
            case "CreateLobbyButton":
                NetRunner.Ensure().CreateLobby();
                Show("LobbyPanel");
                break;
            case "JoinLobbyButton":
                string code = root.Q<TextField>("JoinCode").value.Trim().ToUpperInvariant();
                if (code.Length != 6 || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9]{6}$"))
                {
                    root.Q<Label>("JoinStatus").text = "Enter a six-character lobby code.";
                    return;
                }
                NetRunner.Ensure().JoinLobby(code);
                Show("LobbyPanel");
                break;
            case "HostStartButton":
                NetRunner.Instance?.HostStart();
                break;
            case "LeaveLobbyButton":
                NetRunner.Instance?.Leave();
                Show("MultiPanel");
                break;
            case "BackButton":
            case "SettingsBackButton":
            case "MultiBackButton":
                Show("HomePanel");
                break;
            case "QuitButton":
                Application.Quit();
                break;
        }
    }

    private void Update()
    {
        if (root == null)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (panel == "LobbyPanel")
            {
                NetRunner.Instance?.Leave();
                Show("MultiPanel");
            }
            else
            {
                Show("HomePanel");
            }
        }

        Refresh();
    }

    private void Refresh()
    {
        for (int sector = 1; sector <= GameSession.LevelCount; sector++)
        {
            string label = sector + "  " + Game.Campaign.CampaignSession.Locations[sector - 1] + "   " + GameSession.GetStars(sector) + "/3 stars";
            if (sector == 2)
            {
                label += "  / MOTHERSHIP";
            }

            bool unlocked = GameSession.IsUnlocked(sector);
            var solo = root.Q<Button>("Sector" + sector);
            solo.text = label + (unlocked ? "" : "  / LOCKED");
            solo.SetEnabled(unlocked);
            var lobby = root.Q<Button>("LobbySector" + sector);
            lobby.text = (NetSession.SelectedLevel == sector ? "> " : "") + label;
            lobby.SetEnabled(NetSession.IsHost && unlocked && !NetSession.InMatch);
        }

        root.Q<Label>("LobbyStatus").text = NetSession.Status;
        root.Q<Label>("LobbyCode").text = (NetSession.ViaRelay ? "INTERNET RELAY" : "LOCAL NETWORK (LAN)") + "  /  " + NetSession.Code;
        string roster = "";

        for (int pilotSlot = 0; pilotSlot < NetSession.MaxPlayers; pilotSlot++)
        {
            roster += (NetSession.SlotTaken(pilotSlot) ? NetSession.PilotLabel(pilotSlot) + (pilotSlot == 0 ? " / HOST" : "") : "Empty slot") + "\n";
        }

        root.Q<Label>("Roster").text = roster;
        root.Q<Button>("HostStartButton").SetEnabled(NetSession.Active && NetSession.IsHost && !string.IsNullOrEmpty(NetSession.Code));
        root.Q<Label>("HostHint").text = NetSession.IsHost ? "Choose a sector, then launch the squad." : "The host chooses the sector and starts the match.";
    }
}
