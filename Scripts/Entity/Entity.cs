using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;
using UnityEngine.Events;

public enum InteractState
{
    Started = 0, // Used when Interact() was successful and the interaction has started
    Frozen = 1, // Used for when an interaction is in progress and the player should not be able to move
    Success = 2,
    Done = 3
}

// Custom Unity Event that can take an InteractState Enum as a parameter
[System.Serializable]
public class InteractStateEvent : UnityEvent<InteractState>
{
}

public enum PickupState
{
    PickedUp = 0, // Used when the entity has been picked up
    PutDown = 1, // Used when the entity has been put down
}

// Custom Unity Event that can take a PickupState Enum as a parameter
[System.Serializable]
public class PickupStateEvent : UnityEvent<PickupState>
{
}


public class Entity : NetworkBehaviour, INetworkSerializable
{
    #region Serialization
    public virtual void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        if (serializer.IsWriter)
        {
            FastBufferWriter writer = serializer.GetFastBufferWriter();
            writer.WriteNetworkSerializable<NetworkBehaviourReference>(pickedUpEntity);
        }
        else
        {
            FastBufferReader reader = serializer.GetFastBufferReader();
            reader.ReadNetworkSerializable(out NetworkBehaviourReference entityRef);
            entityRef.TryGet(out pickedUpEntity);
        }
    }
    #endregion

    #region ClassVariables
    [Header("Entity Base Class")]
    [SerializeField] protected Transform activePickupContainer;
    private int selectedPickupContainerIndex = 0;
    [SerializeField] protected List<Transform> pickupContainers = new();
    [SerializeField] protected List<Transform> pickupContainerOrigins = new();
    [SerializeField] protected List<ConfigurableJoint> draggedObjectJoints = new();
    [SerializeField] protected List<TwoBoneIKConstraint> pickupIKPoints = new();
    [SerializeField] protected List<PositionConstraint> IKAttachmentPositionConstraints = new();
    [SerializeField] protected List<RotationConstraint> IKAttachmentRotationConstraints = new();

    [SerializeField] public Entity autoPickUpEntity = null;
    [SerializeField] public Entity pickedUpEntity = null;
    [SerializeField] public Entity autoInteractEntity = null;
    [SerializeField] protected Entity interactedEntity;
    [SerializeField] protected InteractStateEvent interactedEntityStateEvent;
    [SerializeField] protected GameObject entityModelObject;

    [Header("UI Information")]
    [SerializeField] protected Texture2D uiImage;
    [SerializeField] public new string name;


    private bool requestDestroy = false;
    protected float timeInAir = 0;

    protected EntityStats stats;
    protected Rigidbody rb = null;
    public Collider col = null;
    protected SoundEmitter soundEmitter;
    protected Vector3 soundSourceOffset = Vector3.zero;
    protected Vector3 lastUnrestrictedPosition = Vector3.zero;
    protected Zone.Type currentAreaClassification = Zone.Type.SAFE;
    private Material outlineMaterial = null;
    private CollisionDetectionMode previousMode;

    public float TimeInAir { get { return timeInAir; } }
    public EntityStats Stats { get { return stats; } }
    public Zone.Type AreaClassification
    {
        get { return currentAreaClassification; }
        set
        {
            // This is for tracking the last "unrestricted/safe" position the player was in
            // This is used by the AI as an alternative to placing the player in timeout
            if (currentAreaClassification == Zone.Type.SAFE) lastUnrestrictedPosition = transform.position;

            currentAreaClassification = value;
        }
    }
    public Texture2D UIImage { get { return uiImage; } }
    public Rigidbody Rb { get { return rb; } }
    public Vector3 LastUnrestrictedPosition { get { return lastUnrestrictedPosition; } }
    #endregion

    // I'm not really sure which region to put this in, will figure it out later
    // Used by the AI and player current to hide their models while the ragdoll is active
    [Rpc(SendTo.Everyone)]
    protected void SetActiveEntityModelRpc(bool active)
    {
        if (entityModelObject) entityModelObject.SetActive(active);
    }

    #region Lifecycle
    protected virtual void Awake()
    {
        rb = GetComponentInChildren<Rigidbody>();
        col = GetComponentInChildren<Collider>();

        if (GetComponentInChildren<MeshRenderer>() != null)
            outlineMaterial = GetComponentInChildren<MeshRenderer>().material;

        lastUnrestrictedPosition = transform.position;

        stats = GetComponent<EntityStats>();
        if (stats != null)
        {
            stats.onStatChangeEvent.AddListener(StatChangeState);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {

            foreach (Transform container in pickupContainers)
            {
                if (container == null) continue;

                pickupContainerOrigins.Add(container.parent);

                // Set the container to be the first one in the list
                if (activePickupContainer == null)
                {
                    activePickupContainer = container;
                    selectedPickupContainerIndex = 0;
                }
                if (container.TryGetComponent(out ConfigurableJoint joint))
                {
                    if (!draggedObjectJoints.Contains(joint)) draggedObjectJoints.Add(joint);
                }
            }

            foreach (TwoBoneIKConstraint ikPoint in pickupIKPoints)
            {
                PositionConstraint pc = ikPoint.GetComponentInChildren<PositionConstraint>();
                if (pc != null && !IKAttachmentPositionConstraints.Contains(pc)) IKAttachmentPositionConstraints.Add(pc);
                RotationConstraint rc = ikPoint.GetComponentInChildren<RotationConstraint>();
                if (rc != null && !IKAttachmentRotationConstraints.Contains(rc)) IKAttachmentRotationConstraints.Add(rc);
            }

            activePickupContainer = pickupContainers.Count > 0 ? pickupContainers[0] : null;
            if (stats != null)
            {
                CanBeInteractedWith.Value = stats.CanBeInteractedWith;
                CanBePickedUp.Value = stats.CanBePickedUp;
            }

            GameObject soundEmitterPrefab = Resources.Load<GameObject>("Prefabs/Suspicion/SoundEmitter");
            if (soundEmitterPrefab != null)
            {
                GameObject soundEmitterObj = Instantiate(soundEmitterPrefab, transform.position, Quaternion.identity);
                soundEmitterObj.transform.parent = transform;
                soundEmitter = soundEmitterObj.GetComponent<SoundEmitter>();
                soundEmitter.transform.localPosition = soundSourceOffset;
            }
            else
            {
                Debug.LogWarning("SoundEmitter prefab not found");
            }
        }
    }

    protected virtual void Update()
    {
        if (IsServer)
        {
            // If the entity has been requested to be destoryed, kill any interactions with it before destroying
            if (requestDestroy)
            {
                interactStateEvent.Invoke(InteractState.Done);

                if (!isInteractedWith && !isPickedUp)
                {
                    Destroy(gameObject);
                    return;
                }
            }

            if (!IsSpawned) return;

            if (autoPickUpEntity != null)
            {
                Debug.LogWarning($"Entity {NetworkObject.gameObject.name}: AutoPickUpEntity is set, this will automatically pick up the entity if it's within range.");
                // Automatically pick up the entity if it's within range
                float pickupRange = 5.0f;
                if (Vector3.Distance(transform.position, autoPickUpEntity.transform.position) <= pickupRange)
                {
                    bool didPickUp = autoPickUpEntity.PickUp(transform);
                    if (didPickUp) autoPickUpEntity = null;
                }
            }

            if (autoInteractEntity != null)
            {
                Debug.LogWarning($"Entity {NetworkObject.gameObject.name}: AutoInteractEntity is set, this will automatically interact with the entity if it's within range.");
                // Automatically interact with the entity if it's within range
                float interactRange = 5.0f;
                if (Vector3.Distance(transform.position, autoInteractEntity.transform.position) <= interactRange)
                {
                    bool didInteract = autoInteractEntity.Interact(transform);
                    if (didInteract) autoInteractEntity = null;
                }
            }
        }
    }

    protected virtual void FixedUpdate()
    {
        if (IsServer)
        {
            for (int i = 0; i < pickupContainers.Count; i++)
            {
                if (pickupContainers[i] == null) continue;

                if (Vector3.Distance(pickupContainers[i].position, pickupContainerOrigins[i].position) > 0.01f)
                {
                    pickupContainers[i].position = Vector3.Lerp(
                        pickupContainers[i].position,
                        pickupContainerOrigins[i].position,
                        Time.deltaTime * 10f
                    );
                    pickupContainers[i].rotation = Quaternion.Lerp(
                        pickupContainers[i].rotation,
                        pickupContainerOrigins[i].rotation,
                        Time.deltaTime * 10f
                    );
                }
                else if (Vector3.Distance(pickupContainers[i].position, pickupContainerOrigins[i].position) > 0.001f)
                {
                    // Snap to origin if close enough
                    pickupContainers[i].position = pickupContainerOrigins[i].position;
                    pickupContainers[i].rotation = pickupContainerOrigins[i].rotation;
                }
            }
            if (!isPickedUp)
            {
                if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, transform.lossyScale.y)
                    && hit.transform != transform)
                {
                    if (hit.transform.TryGetComponent(out ZoneFloor floor))
                    {
                        currentAreaClassification = floor.GetClassification(transform.position);
                    }
                }
                else
                {
                    timeInAir += Time.deltaTime;
                }
            }
        }
    }

    override public void OnDestroy()
    {
        // TODO: Set up deferred destruction for the entity
        // if (isPickedUp) PutDown(pickerUpper);
        // if (pickedUpEntity != null) pickedUpEntity.PutDown(transform);
        // if (interactorTransform != null) ResetInteract();
        // if (interactedEntity != null) interactedEntity.ResetInteract();

        base.OnDestroy();
    }

    protected virtual void StatChangeState(StatType type) { }
    #endregion

    #region Inventory
    [Rpc(SendTo.Server)]
    protected void SetNextPickupContainerRpc()
    {
        if (pickupContainers.Count == 0) return;

        bool hasPickedUpEntity = false;
        foreach (Transform container in pickupContainers)
        {
            Entity entity = container.GetComponentInChildren<Entity>();
            if (entity != null && entity.isPickedUp)
            {
                hasPickedUpEntity = true;
                break;
            }
        }
        if (!hasPickedUpEntity && pickedUpEntity != null) return; // TODO: Hack to detect dragging an entity


        // Set the next pickup container to be the next one in the list
        selectedPickupContainerIndex = (selectedPickupContainerIndex + 1) % pickupContainers.Count;
        activePickupContainer = pickupContainers[selectedPickupContainerIndex];
        pickedUpEntity = activePickupContainer.GetComponentInChildren<Entity>();
        PlayerManager.Instance.BroadcastUpdate(OwnerClientId, PlayerDataUpdateType.Pickup);
    }
    #endregion

    #region Interact
    [HideInInspector]
    public InteractStateEvent interactStateEvent = new();

    [HideInInspector]
    public NetworkVariable<bool> CanBeInteractedWith = new(false);

    [HideInInspector]
    public Transform interactorTransform;

    [HideInInspector]
    public bool isInteractedWith = false;

    // Used for when an entity needs to comunicate back to the interacting entity about the state of the interaction
    [Rpc(SendTo.Everyone)]
    protected void InformInteractorRpc(InteractState state)
    {
        interactStateEvent.Invoke(state);
    }

    [Rpc(SendTo.Everyone)]
    private void InteractStateEventSubscribeRpc(NetworkBehaviourReference interactorEntityRef)
    {
        // Find the specific server/client that owns this interactor entity, then subscribe to the interactStateEvent
        if (!interactorEntityRef.TryGet<Entity>(out var interactorEntity))
        {
            Debug.LogWarning("Interactor entity not found for " + name + " on " + gameObject.name + " in InteractStateEventSubscribeRpc()");
            return;
        }

        if (interactorEntity.NetworkObject.IsOwner)
        {
            interactorEntity.interactedEntityStateEvent = interactStateEvent;
            interactorEntity.interactedEntityStateEvent.AddListener(interactorEntity.InteractionReaction);
        }
    }

    [Rpc(SendTo.Everyone)]
    private void InteractStateEventUnsubscribeRpc(NetworkBehaviourReference interactorEntityRef)
    {
        // Find the specific server/client that owns this interactor entity, then unsubscribe to the interactStateEvent
        if (!interactorEntityRef.TryGet<Entity>(out var interactorEntity))
        {
            Debug.LogWarning("Interactor entity not found for " + name + " on " + gameObject.name + " in InteractStateEventUnsubscribeRpc()");
            return;
        }

        if (interactorEntity.NetworkObject.IsOwner)
        {
            interactorEntity.interactedEntityStateEvent.RemoveListener(interactorEntity.InteractionReaction);
            interactorEntity.interactedEntityStateEvent = null;
        }
    }

    // Initiate interaction
    public bool Interact(Transform interactor)
    {
        if (!IsServer)
        {
            if (!IsSpawned)
            {
                Debug.LogWarning("Interact() called for " + name + " on " + gameObject.name + " but the entity is not spawned.");
                return false;
            }
            Debug.LogWarning("Interact() called on client side for " + name + " on " + gameObject.name);
            return false;
        }

        if (requestDestroy || !CanBeInteractedWith.Value || isInteractedWith) return false;

        // Wire up the connection between the 2 interacting entities, then run the derived class specific code
        interactorTransform = interactor;
        isInteractedWith = true;
        interactor.GetComponent<Entity>().interactedEntity = this;
        InteractStateEventSubscribeRpc(interactorTransform.GetComponent<Entity>());
        bool didStart = StartInteractState();

        // If the derived class specific code fails, reset the interaction state
        if (!didStart) ResetInteract();
        else InformInteractorRpc(InteractState.Started);

        return didStart;
    }
    protected virtual bool StartInteractState() { return false; }

    // Reset interation state when interaction is done or interrupted
    public void ResetInteract()
    {
        if (!IsServer)
        {
            Debug.LogWarning("ResetInteract() called on client side for " + name + " on " + gameObject.name + " in ResetInteract()");
            return;
        }

        if (!isInteractedWith) return;

        // Run the derived class specific code, then remove the connection between the 2 interacting entities 
        ResetInteractState();
        InformInteractorRpc(InteractState.Done);
        isInteractedWith = false;
        InteractStateEventUnsubscribeRpc(interactorTransform.GetComponent<Entity>());
        interactorTransform.GetComponent<Entity>().interactedEntity = null;
        interactorTransform = null;
    }
    protected virtual void ResetInteractState() { }

    protected virtual void InteractionReaction(InteractState interactState) { }
    #endregion

    #region IKControl

    private Transform GenerateIKAttachmentPoints(Entity attacheeEntity, int IKIndex)
    {
        // Thing we are wanting to attach to
        Collider attacheeCollider = attacheeEntity.col;

        // Closest point on the collider to the pickup container
        Vector3 closestColliderPoint = attacheeCollider.ClosestPoint(pickupContainers[IKIndex].transform.position);

        // Get closest mesh vertex from the closest point on the collider
        // Z axis (palm-facer-direction) is facing the negative normal (against) the mesh surface
        float closestDistance = float.MaxValue;
        Vector3 attachmentPoint = Vector3.zero;
        Vector3 attachmentRotationForward = Vector3.zero;
        MeshFilter meshFilter = attacheeEntity.GetComponentInChildren<MeshFilter>();
        for (int i = 0; i < meshFilter.mesh.vertexCount; i++)
        {
            Vector3 vertex = meshFilter.mesh.vertices[i];
            Vector3 worldVertex = attacheeCollider.transform.TransformPoint(vertex);
            float distance = Vector3.Distance(worldVertex, closestColliderPoint);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                attachmentPoint = worldVertex;
                // Local-to-world normal of the mesh vertex (don't ask)
                attachmentRotationForward = -(
                    attacheeCollider.transform.TransformPoint(
                        vertex + meshFilter.mesh.normals[i]
                    ) - attachmentPoint
                ).normalized;
            }
        }

        // Y axis (hand-pointy-direction) is facing-ish in the same direction as the pickup container from the picking-up entity
        Vector3 pickupContainerOffset = pickupContainers[IKIndex].transform.position - transform.position;
        Vector3 pickupContainerLateralDirection = Vector3.ProjectOnPlane(pickupContainerOffset, Vector3.up).normalized;
        Vector3 attachmentRotationUp = Vector3.ProjectOnPlane(pickupContainerLateralDirection, attachmentRotationForward).normalized;

        // Create a empty game object at the closest point parented to the entity
        Transform attachPoint = new GameObject("Generated_IKAttachPoint_" + IKIndex).transform;
        attachPoint.SetPositionAndRotation(
            attachmentPoint,
            Quaternion.LookRotation(attachmentRotationForward, attachmentRotationUp)
        );
        attachPoint.parent = attacheeEntity.transform;

        return attachPoint;
    }


    private Transform[] GetEntityIKAttachmentPoints(Entity attacheeEntity)
    {
        Transform[] closestPoints = new Transform[IKAttachmentPositionConstraints.Count];
        for (int i = 0; i < closestPoints.Length; i++) closestPoints[i] = null;

        // Get the closest attachment point to each arm and target that point
        Transform[] premadeAttachmentPoints = ((IDraggable)attacheeEntity).GetIKAttachPoints();
        if (premadeAttachmentPoints.Length > 0)
        {
            for (int i = 0; i < closestPoints.Length; i++)
            {
                if (closestPoints[i] == null && i < premadeAttachmentPoints.Length && premadeAttachmentPoints[i] != null)
                {
                    closestPoints[i] = premadeAttachmentPoints[i];
                }
            }
        }
        else
        {
            // If no attachment points are set, generate them based on the entity collider
            for (int i = 0; i < closestPoints.Length; i++)
            {
                closestPoints[i] = GenerateIKAttachmentPoints(attacheeEntity, i);
                if (closestPoints[i] == null)
                {
                    Debug.LogWarning("Failed to generate IK attachment point for " + attacheeEntity.name + " at index " + i);
                }
            }
        }

        return closestPoints;
    }

    public void AttachIKToPoint(int IKIndex, Transform attachPoint)
    {
        if (IKIndex < 0 || IKIndex >= IKAttachmentPositionConstraints.Count) return;

        if (IKAttachmentPositionConstraints[IKIndex] != null)
        {
            IKAttachmentPositionConstraints[IKIndex].AddSource(new ConstraintSource
            {
                sourceTransform = attachPoint,
                weight = 1f
            });
            IKAttachmentPositionConstraints[IKIndex].constraintActive = true;
        }

        if (IKAttachmentRotationConstraints[IKIndex] != null)
        {
            IKAttachmentRotationConstraints[IKIndex].AddSource(new ConstraintSource
            {
                sourceTransform = attachPoint,
                weight = 1f
            });
            IKAttachmentRotationConstraints[IKIndex].constraintActive = true;
        }

        SetIKConstraintWeightRpc(IKIndex, 1f);
    }

    public Transform[] AttachIK(Entity attacheeEntity, int IKIndex = -1)
    {
        if (attacheeEntity == null || attacheeEntity is not IDraggable) return new Transform[] { };

        DetachAllIK();

        Transform[] closestPoints = GetEntityIKAttachmentPoints(attacheeEntity);

        for (int i = 0; i < closestPoints.Length; i++)
        {
            if (IKIndex >= 0 && IKIndex != i)
            {
                closestPoints[i] = null; // If a specific IK index is set, only attach to that one
                continue; // If a specific IK index is set, only attach to that one
            }
            if (closestPoints[i] != null)
            {
                AttachIKToPoint(i, closestPoints[i]);
            }
        }

        bool didAttach = false;
        foreach (Transform point in closestPoints) { if (point != null) didAttach = true; }
        if (!didAttach) return new Transform[] { };

        return closestPoints;
    }

    private void DetachIK(int IKIndex)
    {
        if (IKIndex < 0 || IKIndex >= IKAttachmentPositionConstraints.Count) return;

        // Get gameobject of attached rigidbody
        GameObject attachedObject = null;
        if (IKAttachmentPositionConstraints[IKIndex] != null && IKAttachmentPositionConstraints[IKIndex].sourceCount > 0)
        {
            // Get the source transform of the first source
            ConstraintSource source = IKAttachmentPositionConstraints[IKIndex].GetSource(0);
            attachedObject = source.sourceTransform.gameObject;
        }

        if (IKAttachmentPositionConstraints[IKIndex] != null)
        {
            if (IKAttachmentPositionConstraints[IKIndex].sourceCount <= 0) return;

            IKAttachmentPositionConstraints[IKIndex].constraintActive = false;
            IKAttachmentPositionConstraints[IKIndex].RemoveSource(0);
        }

        if (IKAttachmentRotationConstraints[IKIndex] != null)
        {
            if (IKAttachmentRotationConstraints[IKIndex].sourceCount <= 0) return;

            IKAttachmentRotationConstraints[IKIndex].constraintActive = false;
            IKAttachmentRotationConstraints[IKIndex].RemoveSource(0);
        }

        SetIKConstraintWeightRpc(IKIndex, 0f);
        
        // If the attached object is a generated IK attachment point, destroy it
        if (attachedObject != null && attachedObject.name.StartsWith("Generated_IKAttachPoint_"))
        {
            Destroy(attachedObject);
        }
    }

    public void DetachAllIK()
    {
        for (int i = 0; i < IKAttachmentPositionConstraints.Count; i++)
        {
            DetachIK(i);
        }
    }

    [Rpc(SendTo.Everyone)]
    private void SetIKConstraintWeightRpc(int IKIndex, float weight)
    {
        if (IKIndex < 0 || IKIndex >= pickupIKPoints.Count) return;

        if (pickupIKPoints[IKIndex] != null)
        {
            pickupIKPoints[IKIndex].weight = weight;
        }
    }

    #endregion

    #region Pickup
    [HideInInspector] public PickupStateEvent pickupStateEvent = new();

    [HideInInspector] public NetworkVariable<bool> CanBePickedUp = new(false);

    protected bool isOneHanded = true;
    protected Vector3 targetHoldRotation = Vector3.zero;
    public bool isPickedUp = false;
    protected Transform pickerUpper;

    public void TogglePickUp(Transform pickerUpper)
    {
        if (!IsServer)
        {
            if (!IsSpawned)
            {
                Debug.LogWarning("TogglePickUp() called for " + name + " on " + gameObject.name + " but the entity is not spawned.");
                return;
            }
            Debug.LogWarning("TogglePickUp() called on client side for " + name + " on " + gameObject.name);
            return;
        }

        if (!isPickedUp) PickUp(pickerUpper);
        else PutDown(pickerUpper);
    }

    // Parent the object to the object doing the picking up, then run derived class specific code
    public bool PickUp(Transform pickerUpper)
    {
        if (!IsServer)
        {
            if (!IsSpawned)
            {
                Debug.LogWarning("PickUp() called for " + name + " on " + gameObject.name + " but the entity is not spawned.");
                return false;
            }
            Debug.LogWarning("PickUp() called on client side for " + name + " on " + gameObject.name);
            return false;
        }

        if (requestDestroy || !CanBePickedUp.Value || isPickedUp) return false;

        this.pickerUpper = pickerUpper;

        // Run derived class specific code
        PickUpState();

        if (this is IDraggable)
        {
            if (!pickerUpper.TryGetComponent(out Entity pickerUpperEntity)) return false;

            ToggleIgnorecolliderRpc(pickerUpperEntity, true);

            Transform[] attachedPoints = pickerUpperEntity.AttachIK(this, isOneHanded ? 0 : -1);
            if (attachedPoints.Length == 0)
            {
                ToggleIgnorecolliderRpc(pickerUpperEntity, false);
                return false;
            }

            for (int i = 0; i < pickerUpperEntity.draggedObjectJoints.Count; i++)
            {
                if (attachedPoints[i] == null) continue;
                pickerUpperEntity.pickupContainers[i].position = attachedPoints[i].position;
                if (targetHoldRotation != Vector3.zero)
                {
                    pickerUpperEntity.pickupContainers[i].rotation = transform.rotation;
                }
                pickerUpperEntity.draggedObjectJoints[i].connectedBody = rb;
                pickerUpperEntity.draggedObjectJoints[i].connectedAnchor = attachedPoints[i].localPosition;
                if (targetHoldRotation != Vector3.zero)
                {
                    pickerUpperEntity.draggedObjectJoints[i].angularXDrive = new JointDrive
                    {
                        positionSpring = 10000f,
                        positionDamper = 100f,
                        maximumForce = Mathf.Infinity
                    };
                    pickerUpperEntity.draggedObjectJoints[i].angularYZDrive = new JointDrive
                    {
                        positionSpring = 10000f,
                        positionDamper = 100f,
                        maximumForce = Mathf.Infinity
                    };
                }
                pickerUpperEntity.pickupContainerOrigins[i].GetComponent<ParentConstraint>().SetRotationOffset(0, targetHoldRotation);
            }

            // TODO: Figure out what to do with this
            if (pickerUpper.TryGetComponent(out Player player))
            {
                player.playerStats.SetCurrentSpeedMult(((IDraggable)this).GetDraggingSpeedMult());
                player.playerStats.SetCurrentTurnSpeedMult(((IDraggable)this).GetDraggingSpeedMult());
            }

            pickerUpperEntity.pickedUpEntity = this;
        }
        else
        {
            // TODO: Deprecated, remove when all entities are converted to IDraggable
            NetworkObject.TrySetParent(pickerUpper.GetComponentInParent<NetworkObject>());

            if (rb != null)
            {
                previousMode = rb.collisionDetectionMode;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            if (pickerUpper.TryGetComponent(out Entity pickerUpperEntity))
            {
                pickerUpperEntity.pickedUpEntity = this;
                PickUpParentingRpc(pickerUpperEntity);
            }
        }

        isPickedUp = true;
        PlayerManager.Instance.BroadcastUpdate(OwnerClientId, PlayerDataUpdateType.Pickup);
        pickupStateEvent.Invoke(PickupState.PickedUp);
        return true;
    }

    [Rpc(SendTo.Everyone)]
    private void PickUpParentingRpc(NetworkBehaviourReference pickerUpperRef)
    {
        // Disable and collision
        if (col != null) col.enabled = false;

        if (!pickerUpperRef.TryGet<Entity>(out var pickerUpperEntity))
        {
            Debug.LogWarning("Picker Upper entity not found for " + name + " on " + gameObject.name + " in PickUpParentingRpc()");
            return;
        }

        // Parent the object to the object doing the picking up
        Transform pickupContainerTransform = pickerUpperEntity.activePickupContainer != null ? pickerUpperEntity.activePickupContainer : pickerUpperEntity.transform;

        NetworkTransform[] networkTransforms = GetComponentsInChildren<NetworkTransform>();
        foreach (NetworkTransform nt in networkTransforms)
        {
            nt.enabled = false;
        }

        transform.SetParent(pickupContainerTransform);
        transform.localPosition = Vector3.zero;
        transform.forward = pickupContainerTransform.forward;
    }

    protected virtual void PickUpState() { }

    // Parent the object to what it was previously, then run derived class specific code
    public void PutDown(Transform pickerUpper, Vector3 throwVector = default, Vector3 putDownLocation = default)
    {
        if (!IsServer)
        {
            if (!IsSpawned)
            {
                Debug.LogWarning("PutDown() called for " + name + " on " + gameObject.name + " but the entity is not spawned.");
                return;
            }
            Debug.LogWarning("PutDown() called on client side for " + name + " on " + gameObject.name);
            return;
        }

        if (this.pickerUpper != pickerUpper || !isPickedUp) return;

        this.pickerUpper.GetComponent<Entity>().pickedUpEntity = null;
        this.pickerUpper = null;

        if (this is IDraggable)
        {
            if (!pickerUpper.TryGetComponent(out Entity pickerUpperEntity)) return;

            // TODO: Figure out what to do with this
            if (pickerUpper.TryGetComponent(out Player player))
            {
                player.playerStats.SetCurrentSpeedMult(1);
                player.playerStats.SetCurrentTurnSpeedMult(1);
            }

            // Remove the position constraints
            pickerUpperEntity.DetachAllIK();

            // Remove the joints
            foreach (ConfigurableJoint joint in pickerUpperEntity.draggedObjectJoints)
            {
                joint.connectedBody = null;
                joint.connectedAnchor = Vector3.zero;
                joint.angularXDrive = new JointDrive
                {
                    positionSpring = 0f,
                    positionDamper = 0f,
                    maximumForce = 0f
                };
                joint.angularYZDrive = new JointDrive
                {
                    positionSpring = 0f,
                    positionDamper = 0f,
                    maximumForce = 0f
                };
            }

            foreach (Transform origin in pickerUpperEntity.pickupContainerOrigins)
            {
                origin.GetComponent<ParentConstraint>().SetRotationOffset(0, Vector3.zero);
            }

            ToggleIgnorecolliderRpc(pickerUpperEntity, false);
        }
        else
        {
            // TODO: Deprecated, remove when all entities are converted to IDraggable
            PutDownParentingRpc();

            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.collisionDetectionMode = previousMode;
            }
            NetworkObject.TryRemoveParent();
        }

        // If there is a specific location to put the object down, move it there
        if (putDownLocation != default) transform.position = putDownLocation;

        // Run derived class specific code
        PutDownState();

        // If a throw vector is given, apply it to the object
        if (throwVector != default) rb.AddForce(throwVector);

        isPickedUp = false;
        PlayerManager.Instance.BroadcastUpdate(OwnerClientId, PlayerDataUpdateType.Pickup);
        pickupStateEvent.Invoke(PickupState.PutDown);
    }

    [Rpc(SendTo.Everyone)]
    private void PutDownParentingRpc()
    {
        // Reset parenting
        transform.SetParent(NetworkObject.transform);
        NetworkTransform[] networkTransforms = GetComponentsInChildren<NetworkTransform>();
        foreach (NetworkTransform nt in networkTransforms)
        {
            nt.enabled = true;
        }

        // Enable and collision
        if (col != null) col.enabled = true;
    }

    protected virtual void PutDownState() { }

    public bool ToggleDragging(Transform dragger)
    {
        if (this is not IDraggable) return false;

        if (isPickedUp && pickerUpper != dragger) return false;

        if (isPickedUp && pickerUpper == dragger)
        {
            isPickedUp = false;
            pickerUpper = null;
        }
        else if (!isPickedUp)
        {
            isPickedUp = true;
            pickerUpper = dragger;
        }

        return true;
    }

    [Rpc(SendTo.Everyone)]
    private void ToggleIgnorecolliderRpc(NetworkBehaviourReference otherEntityRef, bool ignore)
    {
        if (!otherEntityRef.TryGet<Entity>(out var otherEntity))
        {
            Debug.LogWarning("Other entity not found for " + name + " on " + gameObject.name + " in ToggleIgnorecolliderRpc()");
            return;
        }

        if (col != null && otherEntity.col != null)
        {
            Physics.IgnoreCollision(col, otherEntity.col, ignore);
        }
    }
    #endregion

    #region Outline
    [Rpc(SendTo.Everyone)]
    public void OutlineRpc(NetworkBehaviourReference hoveringEntityRef)
    {
        if (!CanBePickedUp.Value && !CanBeInteractedWith.Value) return;
        if (!outlineMaterial) return;

        // Find the specific server/client that owns this hovering entity, then outline it
        hoveringEntityRef.TryGet<Entity>(out var hoveringEntity);
        if (hoveringEntity.NetworkObject.IsOwner) outlineMaterial.SetFloat("_isOutlined", 1.0f);
    }

    [Rpc(SendTo.Everyone)]
    public void ResetOutlineRpc(NetworkBehaviourReference hoveringEntityRef)
    {
        if (!CanBePickedUp.Value && !CanBeInteractedWith.Value) return;
        if (!outlineMaterial) return;

        // Find the specific server/client that owns this hovering entity, then reset the outline
        hoveringEntityRef.TryGet<Entity>(out var hoveringEntity);
        if (hoveringEntity.NetworkObject.IsOwner) outlineMaterial.SetFloat("_isOutlined", 0.0f);
    }
    #endregion

    #region Collision
    //################################
    //#### Collision related code  ###
    //################################
    protected virtual void OnCollisionEnter(Collision collision)
    {
        if (IsServer)
        {
            if (!TryGetComponent<Rigidbody>(out var _)) return;

            if (timeInAir > 0.25f)
            {
                // Damage the other entity and ourselves
                if (collision.transform.TryGetComponent(out EntityStats otherStats))
                {
                    otherStats.Damage(collision.relativeVelocity.magnitude * rb.mass / 10f, stats.transform.position - transform.position);
                }

                // Don't add velocity to ourselves, or else >:^[
                if (stats != null) stats.Damage(collision.relativeVelocity.magnitude * rb.mass / 10f, Vector3.zero);
            }

            timeInAir = 0;

            // Emit sound on collision for objects that have a rigidbody
            float impactForce = collision.relativeVelocity.magnitude;
            // TODO: This is a very arbitrary number, need to find a better way to calculate this
            if (impactForce > 5.0f) EmitSound(impactForce * 0.05f);
        }
    }
    #endregion

    #region Detection
    protected void EmitSound(float radius = 5.0f)
    {
        if (IsServer && soundEmitter != null) soundEmitter.EmitSound(radius);
    }
    #endregion
}