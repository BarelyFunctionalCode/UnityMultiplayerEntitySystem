using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public enum PlayerDataUpdateType
{
    Add,
    Remove,
    Health,
    XP,
    Level,
    Money,
    Storage,
    Pickup,
    All,
    None
}

public struct PlayerReference : INetworkSerializable
{
    public ulong clientId;
    public Player player;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        if (serializer.IsWriter)
        {
            FastBufferWriter writer = serializer.GetFastBufferWriter();
            writer.WriteValueSafe(clientId);
            writer.WriteNetworkSerializable(player);
        }

        if (serializer.IsReader)
        {
            FastBufferReader reader = serializer.GetFastBufferReader();
            reader.ReadValueSafe(out clientId);

            if (player == null)
            {
                if (NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
                {
                    player = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject.GetComponentInChildren<Player>();
                }
                else
                {
                    Debug.LogWarning($"PlayerReference: Player with clientId {clientId} not found.");
                    Debug.Log(NetworkManager.Singleton.ConnectedClients.Count);
                    foreach (var client in NetworkManager.Singleton.ConnectedClients)
                    {
                        Debug.Log($"ClientId: {client.Key}, PlayerObject: {client.Value.PlayerObject}");
                    }
                    return;
                }
            }
            reader.ReadNetworkSerializableInPlace(ref player);
        }
    }
    public PlayerReference(Player player)
    {
        this.clientId = player.NetworkObject.OwnerClientId;
        this.player = player;
    }
    public static implicit operator PlayerReference(Player player)
    {
        return new PlayerReference(player);
    }
    public static implicit operator Player(PlayerReference playerRef)
    {
        return playerRef.player;
    }
}

public class PlayerManager : NetworkBehaviour
{
    [SerializeField] private bool debugMode = true;
    public static PlayerManager Instance { get; private set; } = null;
    
    public void Awake()
    {
        if (Instance != null) Destroy(gameObject);
        else Instance = this;

        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    #region Shared

    #nullable enable
    public Player? GetPlayerData(ulong clientId)
    {
        if (!IsHost)
        {
            if (client_playerDataDict.ContainsKey(clientId)) return client_playerDataDict[clientId];
            return null;
        }

        if (server_playerDataDict.ContainsKey(clientId)) return server_playerDataDict[clientId];
        return null;
    }
    #nullable disable
    
    public List<Player> GetPlayerList()
    {
        if (!IsHost)
        {
            return client_playerDataDict.Values.ToList();
        }
        return server_playerDataDict.Values.ToList();
    }
    #endregion

    #region ServerOnly
    // List of all players in the game, only accessible on the host
    private Dictionary<ulong, Player> server_playerDataDict = new();

    #nullable enable
    public Player? AddPlayer(Player player)
    {
        if (!NetworkManager.Singleton.IsHost)
        {
            Debug.LogError("PlayerManager: AddPlayer() called on non-host");
            return null;
        }

        if (server_playerDataDict.ContainsKey(player.OwnerClientId))
        {
            Debug.LogWarning($"PlayerManager: Player with id {player.OwnerClientId} already exists");
            return null;
        }

        server_playerDataDict.Add(player.OwnerClientId, player);
        if (!IsSpawned)
        {
            Debug.LogWarning("PlayerManager: PlayerManager is not spawned, cannot broadcast player data update");
            return player;
        }
        BroadcastPlayerDataUpdateRpc(PlayerDataUpdateType.Add, player);
        foreach (var playerData in server_playerDataDict.Values)
        {
            if (playerData != player)
            {
                BroadcastPlayerDataUpdateRpc(PlayerDataUpdateType.Add, playerData, player.OwnerClientId);
            }
        }

        return player;
    }
    #nullable disable

    public void RemovePlayer(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost)
        {
            Debug.LogError("PlayerManager: RemovePlayer() called on non-host");
            return;
        }

        if (!server_playerDataDict.ContainsKey(clientId))
        {
            Debug.LogWarning($"PlayerManager: Player with id {clientId} does not exist");
            return;
        }

        BroadcastPlayerDataUpdateRpc(PlayerDataUpdateType.Remove, server_playerDataDict[clientId]);
        server_playerDataDict.Remove(clientId);
    }

    public void Clear()
    {
        if (!IsHost)
        {
            client_playerDataDict.Clear();
            return;
        }
        foreach (var player in server_playerDataDict.Values)
        {
            BroadcastPlayerDataUpdateRpc(PlayerDataUpdateType.Remove, player);
        }

        server_playerDataDict.Clear();
    }
    #endregion

    #region ClientBroadcasting
    private List<UnityAction<PlayerDataUpdateType, ulong, Player>> pendingClientListeners = new();
    private UnityEvent<PlayerDataUpdateType, ulong, Player> Client_OnPlayerDataUpdateEvent = new();
    
    private Dictionary<ulong, Player> client_playerDataDict = new();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // If the client is spawned, add all pending listeners
        if (IsOwner)
        {
            foreach (var listener in pendingClientListeners)
            {
                Client_OnPlayerDataUpdateEvent.AddListener(listener);
            }
            pendingClientListeners.Clear();
        }

        if (IsHost)
        {
            foreach (var playerData in server_playerDataDict.Values)
            {
                BroadcastPlayerDataUpdateRpc(PlayerDataUpdateType.Add, playerData);
            }
        }
    }

    // When a new listener is added, it will send the newly added listener data for all players
    public void ClientAddListener(UnityAction<PlayerDataUpdateType, ulong, Player> listener)
    {
        if (!IsSpawned)
        {
            Debug.LogWarning("PlayerManager: ClientAddListener called on non-spawned client, adding to pending listeners");
            pendingClientListeners.Add(listener);
            return;
        }
        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        Client_OnPlayerDataUpdateEvent.AddListener(listener);
        TriggerPlayerDataEventRpc(localClientId);
    }

    public void ClientRemoveListener(UnityAction<PlayerDataUpdateType, ulong, Player> listener)
    {
        Client_OnPlayerDataUpdateEvent.RemoveListener(listener);
    }

    // This is used to manually trigger the player data event for a specific player
    [Rpc(SendTo.Server)]
    private void TriggerPlayerDataEventRpc(ulong requestingClientId, ulong[] clientIds = null)
    {
        if (clientIds == null) clientIds = server_playerDataDict.Keys.ToArray();

        foreach (ulong id in clientIds)
        {
            if (server_playerDataDict.ContainsKey(id))
            {
                BroadcastPlayerDataUpdateRpc(PlayerDataUpdateType.Add, server_playerDataDict[id], requestingClientId);
                BroadcastPlayerDataUpdateRpc(PlayerDataUpdateType.All, server_playerDataDict[id], requestingClientId);
            }
            else Debug.LogWarning($"PlayerManager: Player with id {id} does not exist");
        }
    }

    public void BroadcastUpdate<T>(ulong playerClientId, PlayerDataUpdateType updateType, ref T currentValue, T newValue)
    {
        bool changed = !EqualityComparer<T>.Default.Equals(currentValue, newValue);
        currentValue = newValue;
        if (!IsSpawned)
        {
            Debug.LogWarning("PlayerManager: BroadcastUpdate() called on non-spawned client, skipping broadcast");
            return;
        }
        if (!IsHost)
        {
            Debug.LogError("PlayerManager: BroadcastUpdate() called on non-host");
            return;
        }
        if (playerClientId == ulong.MaxValue) return;
        if (server_playerDataDict.ContainsKey(playerClientId))
        {
            if (changed) BroadcastPlayerDataUpdateRpc(updateType, server_playerDataDict[playerClientId]);
        }
        else Debug.LogWarning($"PlayerManager: Player with id {playerClientId} does not exist");
    }

    public void BroadcastUpdate(ulong playerClientId, PlayerDataUpdateType updateType)
    {
        if (!IsSpawned)
        {
            Debug.LogWarning("PlayerManager: BroadcastUpdate() called on non-spawned client, skipping broadcast");
            return;
        }
        if (!IsHost)
        {
            Debug.LogError("PlayerManager: BroadcastUpdate() called on non-host");
            return;
        }
        if (playerClientId == ulong.MaxValue) return;
        if (server_playerDataDict.ContainsKey(playerClientId))
        {
            BroadcastPlayerDataUpdateRpc(updateType, server_playerDataDict[playerClientId]);
        }
        else Debug.LogWarning($"PlayerManager: Player with id {playerClientId} does not exist");
    }

    // When a player's data is updated, broadcast out the updated player data to all clients
    [Rpc(SendTo.Everyone)]
    private void BroadcastPlayerDataUpdateRpc(PlayerDataUpdateType updateType, PlayerReference playerRef, ulong requestingClientId = ulong.MaxValue)
    {
        Player player = playerRef;

        if (updateType == PlayerDataUpdateType.Add) client_playerDataDict[playerRef.clientId] = player;
        else if (updateType == PlayerDataUpdateType.Remove) client_playerDataDict.Remove(playerRef.clientId);

        if (requestingClientId != ulong.MaxValue && requestingClientId != NetworkManager.Singleton.LocalClientId) return;
        if (debugMode == true)
            Debug.Log($"PlayerManager: Broadcasting player data update for player {player?.NetworkObject.OwnerClientId} {updateType:G}{(requestingClientId != ulong.MaxValue ? " to client " + requestingClientId : "")}");

        Client_OnPlayerDataUpdateEvent.Invoke(updateType, playerRef.clientId, player);
    }
    #endregion
}
