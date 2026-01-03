using System.Collections.Generic;
using System.Linq;
using Unity.Multiplayer.Playmode;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DevNetworkManager : MonoBehaviour
{
    [SerializeField] private GameObject networkManagerPrefab;
    [SerializeField] private Transform playerSpawnTransform;

    private float playerWaitTimer = 10f;

    private string desiredSceneName;

    private void Awake()
    {
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        desiredSceneName = SceneManager.GetActiveScene().name;

        if (FindFirstObjectByType<NetworkManager>() == null)
        {
            Instantiate(networkManagerPrefab);
            if (CurrentPlayer.ReadOnlyTags().ToList().Contains("Host"))
            {
                NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            }
        }
        else Destroy(gameObject);
    }

    private void Update()
    {
        if (NetworkManager.Singleton && NetworkManager.Singleton.IsListening &&
            GameManager.Instance && GameManager.Instance.isInitialized)
        {
            if (CurrentPlayer.ReadOnlyTags().ToList().Contains("Host"))
            {
                // Wait for all players are joined.
                if (GameManager.Instance.currentHeist != null && playerWaitTimer <= 0f)
                {
                    // NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
                    // GameManager.Instance.LoadLevel();

                    Transform cribTransform = FindFirstObjectByType<Crib>().transform;
                    foreach (Player player in PlayerManager.Instance.GetPlayerList())
                    {
                        player.ToggleMovement(false);
                        player.transform.position = cribTransform.position + Vector3.up * 2f;
                        player.ToggleMovement(true);
                    }
                    playerWaitTimer = float.MaxValue; // Prevents multiple calls
                }
                else
                {
                    playerWaitTimer -= Time.deltaTime;
                }
            }
            else
            {
                if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().GetComponentInChildren<Player>().isInitialized)
                {
                    Debug.Log("Player is initialized, preparing to join crib...");
                    GameManager.Instance.PrepJoiningOtherCrib();
                    Destroy(gameObject);
                } 
            }
        }
    }

    private void OnServerStarted()
    {
        SetHeistLevel();
        NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
    }

    private void SetHeistLevel()
    {
        // Get level info from scene name
        LevelSO levelInfo = Resources.Load($"Levels/{desiredSceneName}") as LevelSO;

        // Set current heist to the one that contains the level
        GameManager.Instance.SetCurrentHeist(levelInfo.heistName);

        // Progress the heist to the level before the desired one
        int levelIndex = GameManager.Instance.currentHeist.levelSequence.FindIndex(x => x.levelName == levelInfo.levelName);
        for (int i = 0; i < levelIndex - 1; i++) GameManager.Instance.currentHeist.SetNextLevel(true);
    }

    private void OnLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;

        LevelManager.Instance.OnPlayersLoaded();
        
        Destroy(gameObject);
    }
}
