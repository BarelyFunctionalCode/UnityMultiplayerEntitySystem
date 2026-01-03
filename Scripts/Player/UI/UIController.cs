using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;
using Steamworks;

public class UIController : MonoBehaviour
{
    [SerializeField] private GameObject playerCardPrefab;
    [SerializeField] private GameObject victoryScreenPrefabObj;

    private Player player;

    private UIDialogueBox dialogueBox;

    private GameObject UIRoot;
    private GameObject levelUIObj;
    private Image playerCardContainer;
    private Image healthPie;
    private Image torporPie;
    private GameObject victoryScreenObj;

    private UIHeldEntityInfo heldEntityInfo;

    private TextMeshProUGUI timer;

    private Dictionary<ulong, UIPlayerCard> activePlayerCards = new();

    public UIDialogueBox DialogueBox { get { return dialogueBox; } }

    private bool isInitialized = false;

    private void Awake()
    {
        player = GetComponent<Player>();
    }

    private void Update()
    {
        if (player.IsOwner && UIRoot == null)
        {
            GameObject playerUIObj = GetComponent<Player>().playerUIObj;
            if (playerUIObj) Initialize(playerUIObj.transform);
        }

        if (!isInitialized) return;

        if (LevelManager.Instance != null)
        {
            float rawTime = LevelManager.Instance.GetTimeRemaining();
            int minutes = (int)Mathf.Floor(rawTime / 60f);
            int seconds = (int)(rawTime % 60);

            timer.text = $"{minutes}:{seconds:00}";
        }
    }

    private void OnDestroy()
    {
        PlayerManager.Instance.ClientRemoveListener(OnPlayerDataChange);
    }

    private void Initialize(Transform playerUIObj)
    {
        Debug.Log("UIController: Initializing UIController...");
        UIRoot = playerUIObj.gameObject;
        levelUIObj = playerUIObj.Find("LevelUI").gameObject;
        playerCardContainer = playerUIObj.Find("Player Card Container").GetComponent<Image>();
        healthPie = levelUIObj.transform.Find("Health Pie Mask").Find("Foreground").GetComponent<Image>();
        torporPie = healthPie.transform.Find("Torpor").GetComponent<Image>();
        heldEntityInfo = levelUIObj.transform.GetComponentInChildren<UIHeldEntityInfo>();
        timer = levelUIObj.transform.Find("Timer").GetComponent<TextMeshProUGUI>();
        dialogueBox = playerUIObj.Find("DialogueBox").GetComponent<UIDialogueBox>();

        PlayerManager.Instance.ClientAddListener(OnPlayerDataChange);

        isInitialized = true;
    }

    public void Reset()
    {
        // Turn off level UI
        if (levelUIObj) ToggleLevelUI(false);

        // Clear victory screen if it exists
        if (victoryScreenObj)
        {
            Destroy(victoryScreenObj);
            victoryScreenObj = null;
        }

        // Clear player cards
        foreach (var playerCard in activePlayerCards.Values)
        {
            if (playerCard != null) Destroy(playerCard.gameObject);
        }
        activePlayerCards.Clear();
    }

    public async void AddPlayerCard(ulong clientId, ulong steamId, uint money)
    {
        if (activePlayerCards.ContainsKey(clientId)) return;

        // Get the player's steam information and avatar
        SteamId playerSteamId = new SteamId();
        playerSteamId.Value = steamId;

        string username = GameManager.Instance?.usingSteam == true ? new Friend(playerSteamId).Name : $"poopyhead_{clientId}";

        if (clientId == NetworkManager.Singleton.LocalClientId) username += "_ME";

        var image = GameManager.Instance?.usingSteam == true ? await SteamFriends.GetMediumAvatarAsync(playerSteamId) : null;
        Texture2D playerImage = null;

        // Load image data into texture2d
        if (image.HasValue)
        {
            playerImage = GetTextureFromImage(image.Value);
        }

        GameObject newPlayerCard = Instantiate(playerCardPrefab, playerCardContainer.transform);

        if (newPlayerCard.TryGetComponent(out UIPlayerCard playerCardScript))
        {
            playerCardScript.SetPlayerInformation(playerImage, username, money, clientId);
            playerCardScript.OnHealthChange(1, 1); // Set to max health
            activePlayerCards.Add(clientId, playerCardScript);
        }
    }

    public void RemovePlayerCard(ulong playerID)
    {
        if (activePlayerCards.TryGetValue(playerID, out UIPlayerCard playerCard))
        {
            activePlayerCards.Remove(playerID);
            Destroy(playerCard.gameObject);
        }
    }

    private Texture2D GetTextureFromImage(Steamworks.Data.Image image)
    {
        Texture2D texture = new Texture2D(64, 64);

        for (int x = 0; x < image.Width; x++)
        {
            for (int y = 0; y < image.Height; y++)
            {
                Steamworks.Data.Color pixel = image.GetPixel(x, y);
                texture.SetPixel(x, (int)image.Height - y, new Color(pixel.r / 255f, pixel.g / 255f, pixel.b / 255f, pixel.a / 255f));
            }
        }

        texture.Apply();

        return texture;
    }

    public void OnLocalPlayerDataChange(PlayerDataUpdateType updateType, Player player)
    {
        switch (updateType)
        {
            case PlayerDataUpdateType.Health:
                if (healthPie) healthPie.fillAmount = player.playerStats.CurrentHealth / (float)player.playerStats.MaxHealth;
                if (torporPie) torporPie.fillAmount = player.playerStats.CurrentTorpor / (float)player.playerStats.MaxTorpor;
                break;
            case PlayerDataUpdateType.Pickup:
                if (heldEntityInfo != null)
                {
                    heldEntityInfo.OnNewEntity(player.pickedUpEntity);
                }
                break;
            default:
                break;
        }

    }

    private void OnPlayerDataChange(PlayerDataUpdateType updateType, ulong ownerClientId, Player player)
    {
        if (ownerClientId == NetworkManager.Singleton.LocalClientId)
        {
            OnLocalPlayerDataChange(updateType, player);
        }
        if (activePlayerCards.TryGetValue(ownerClientId, out UIPlayerCard playerCard))
        {
            switch (updateType)
            {
                case PlayerDataUpdateType.Remove:
                    RemovePlayerCard(ownerClientId);
                    break;
                case PlayerDataUpdateType.Money:
                    playerCard.OnMoneyChange(player.playerStats.Money);
                    break;
                case PlayerDataUpdateType.Health:
                    playerCard.OnHealthChange(player.playerStats.MaxHealth, player.playerStats.CurrentHealth);
                    break;
                default:
                    break;
            }
        }
        else if (updateType == PlayerDataUpdateType.Add)
        {
            AddPlayerCard(ownerClientId, player.PlayerSteamId, player.playerStats.Money);
        }
    }

    public void ToggleLevelUI(bool active) { if (levelUIObj) levelUIObj.SetActive(active); }

    public void ShowVictoryScreen()
    {
        victoryScreenObj = Instantiate(victoryScreenPrefabObj, UIRoot.transform);
        victoryScreenObj.GetComponent<VictoryScreen>();
    }
}
