using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Collections.ObjectModel;
using Unity.VisualScripting;
using System.Collections.Specialized;

public class PlayerStats : EntityStats
{
    public override void NetworkSerialize<T>(BufferSerializer<T> serializer)
    {
        base.NetworkSerialize(serializer);



        serializer.SerializeValue(ref _money);
        serializer.SerializeValue(ref _xp);
        serializer.SerializeValue(ref _level);
        serializer.SerializeValue(ref currentMoveSpeedMult);
        serializer.SerializeValue(ref currentTurnSpeedMult);


        if (serializer.IsWriter)
        {
            FastBufferWriter writer = serializer.GetFastBufferWriter();
            writer.WriteValueSafe(_storage.Count);
            foreach (var item in _storage)
            {
                writer.WriteValueSafe(item);
            }
        }
        else
        {
            FastBufferReader reader = serializer.GetFastBufferReader();
            reader.ReadValueSafe(out int storageCount);
            _storage = new List<string>();
            for (int i = 0; i < storageCount; i++)
            {
                reader.ReadValueSafe(out string item);
                _storage.Add(item);
            }
        }
    }

    [Header("Movement And Camera")]
    [SerializeField] private float walkSpeed;
    [SerializeField] private float runSpeed;
    [SerializeField] private float horizontalSensitivity = 10.0f;
    [SerializeField] private float verticalSensitivity = 20.0f;
    [Range(0.0f, 1.0f)]
    [SerializeField] private float turnSpeed = 0.9f;
    [SerializeField] private float idleTransitionSpeed = 10f;
    [SerializeField] private float moveSpeedMult = 1f;

    [Header("Misc")]
    [SerializeField] private float entityGrabDistance = 4.0f;

    [SerializeField] protected uint _money = 0;
    public uint Money
    {
        get { return _money; }
        set
        {
            PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.Money, ref _money, value);
        }
    }

    [SerializeField] protected uint _xp = 0;
    public uint XP
    {
        get { return _xp; }
        protected set
        {
            PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.XP, ref _xp, value);
        }
    }

    [SerializeField] protected uint _level = 0;
    public uint Level
    {
        get { return _level; }
        protected set
        {
            PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.Level, ref _level, value);
        }
    }

    [SerializeField] protected List<string> _storage = new List<string>();
    private List<string> Storage
    {
        get { return _storage; }
        set
        {
            PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.Storage, ref _storage, value);
        }
    }


    private float currentMoveSpeedMult = 1f;
    private float currentTurnSpeedMult = 1f;

    public float BaseWalkSpeed { get { return walkSpeed; } }
    public float WalkSpeed { get { return walkSpeed * currentMoveSpeedMult; } }
    public float RunSpeed { get { return runSpeed; } }
    public float VerticalSensitivity { get { return verticalSensitivity; } }
    public float HorizontalSensitivity { get { return horizontalSensitivity; } }
    public float EntityGrabDistance { get { return entityGrabDistance; } }
    public float TurnSpeed { get { return turnSpeed * currentTurnSpeedMult; } }
    public float IdleTransitionSpeed { get { return idleTransitionSpeed; } }

    public void SetInventory()
    {

        Money = 1337;
        XP = 69;
        Level = 1;
        Storage = new List<string> { "Ladder", "Ladder", "Ladder" };
    }

    public void SetCurrentSpeedMult(float newMult)
    {
        PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.None, ref currentMoveSpeedMult, newMult);
    }

    public void SetCurrentTurnSpeedMult(float newMult)
    {
        PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.None, ref currentTurnSpeedMult, newMult);
    }

    public void ResetMoveSpeedMult()
    {
        PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.None, ref currentMoveSpeedMult, moveSpeedMult);
    }

    public List<string> GetStorage()
    {
        return Storage;
    }

    public void AddStorageItem(string item)
    {
        Storage.Add(item);
        PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.Storage);
    }
    
    public void RemoveStorageItem(string item)
    {
        if (!Storage.Contains(item)) return;
        Storage.Remove(item);
        PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.Storage);
    }

}
