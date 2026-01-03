using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public enum DamageType { Health, Torpor, HealthAndTorpor }

public enum StatType { Health, Torpor }

public class HealthStats : MonoBehaviour, INetworkSerializable
{
    public virtual void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref _currentHealth);
        serializer.SerializeValue(ref _maxHealth);
        serializer.SerializeValue(ref _currentTorpor);
        serializer.SerializeValue(ref _torporThreshhold);
        serializer.SerializeValue(ref isKnockedOut);
    }

    [HideInInspector] public UnityEvent<StatType> onStatChangeEvent = new();
    protected Rigidbody rb = null;
    protected ulong playerClientId = ulong.MaxValue;
    public Rigidbody Rb { get { return rb; } }


    [Header("Health/Torpor")]
    [SerializeField] protected bool usesHealth = true;
    [SerializeField] protected bool usesTorpor = false;
    [SerializeField] protected float torporDecay = 1;
    [SerializeField] protected float torporDecayTimer = 1f;
    [HideInInspector] public bool isKnockedOut = false;

    public Vector3 lastDamageForceInstance = Vector3.zero;

    [SerializeField] private uint _currentHealth;
    public uint CurrentHealth
    {
        get { return _currentHealth; }
        protected set
        {
            PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.Health, ref _currentHealth, value);
        }
    }

    [SerializeField] private uint _maxHealth;
    public uint MaxHealth {
        get { return _maxHealth; }
        protected set
        {
            PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.Health, ref _maxHealth, value);
        }
    }

    [SerializeField] private float _currentTorpor = 0;
    public float CurrentTorpor {
        get { return _currentTorpor; }
        protected set
        {
            PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.Health, ref _currentTorpor, value);
        }
    }

    [SerializeField] private float _torporThreshhold = 10;
    public float MaxTorpor {
        get { return _torporThreshhold; }
        protected set
        {
            PlayerManager.Instance.BroadcastUpdate(playerClientId, PlayerDataUpdateType.Health, ref _torporThreshhold, value);
        }
    }

    [SerializeField] private float _knockoutTimer = 30;
    public float KnockoutTimer { get { return _knockoutTimer; } private set {} }


    [Header("Health/Debug")]
    [SerializeField] private bool damageOnce = false;
    [SerializeField] private bool knockout = false;


    protected virtual void Awake()
    {
        rb = GetComponentInChildren<Rigidbody>();
    }
    
    public void Initialize(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost) return;
        
        playerClientId = clientId;
        CurrentHealth = MaxHealth;
    }

    protected virtual void Update()
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsHost) return;

        if (damageOnce)
        {
            damageOnce = false;
            ApplyHealthDelta(-1);
        }

        if (knockout)
        {
            knockout = false;
            ApplyTorporDelta(MaxTorpor);
        }

        if (usesTorpor)
        {
            if (torporDecayTimer <= 0)
            {
                torporDecayTimer = 1f;
                ApplyTorporDelta(-torporDecay);
            }
            else torporDecayTimer -= Time.deltaTime;

        }
    }

    public void Damage(float value, Vector3 direction, DamageType type = DamageType.HealthAndTorpor, bool applyForce = false)
    {
        if (!NetworkManager.Singleton.IsHost) return;
        if (rb != null)
        {
            lastDamageForceInstance = applyForce ? direction * value : Vector3.zero;
            rb.AddForce(direction * value, ForceMode.Impulse);
        }

        if (type == DamageType.Health || type == DamageType.HealthAndTorpor) ApplyHealthDelta(-value);
        if (type == DamageType.Torpor || type == DamageType.HealthAndTorpor) ApplyTorporDelta(value);
    }

    private void ApplyTorporDelta(float value)
    {
        if (!usesTorpor) return;

        CurrentTorpor = Mathf.Clamp(CurrentTorpor + value, 0, MaxTorpor);
        if (CurrentTorpor >= MaxTorpor) isKnockedOut = true;
        if (CurrentTorpor <= 0) isKnockedOut = false;

        onStatChangeEvent.Invoke(StatType.Torpor);
    }

    private void ApplyHealthDelta(float value)
    {
        if (!usesHealth || CurrentHealth <= 0) return;

        CurrentHealth = (uint)Mathf.Clamp(CurrentHealth + value, 0, MaxHealth);
        onStatChangeEvent.Invoke(StatType.Health);
    }
}
