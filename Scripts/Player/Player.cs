using Steamworks;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using Unity.Netcode.Components;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Palmmedia.ReportGenerator.Core;

public struct inputState
{
    public double timestamp;
    public Vector2 input;
    public Quaternion lookDirection;
}

public class Player : Entity
{
    #region Serialization
    public override void NetworkSerialize<T>(BufferSerializer<T> serializer)
    {
        base.NetworkSerialize(serializer);

        serializer.SerializeValue(ref localId.Value);

        if (serializer.IsWriter)
        {
            // other serialized class instances
            FastBufferWriter writer = serializer.GetFastBufferWriter();
            writer.WriteNetworkSerializable(playerStats);
        }
        else
        {
            FastBufferReader reader = serializer.GetFastBufferReader();
            reader.ReadNetworkSerializableInPlace(ref playerStats);
        }
    }
    #endregion

    #region ClassVariables
    [Header("Player Class")]
    // Stats
    public PlayerStats playerStats;
    private SteamId localId;
    public SteamId PlayerSteamId { get { return localId; } }

    // Controls
    public PlayerControls controls;
    private Vector2 moveInput;
    private List<inputState> inputBuffer = new();
    private float smoothDampVelocity;
    private CharacterController characterController;
    private AnticipatedNetworkTransform anticipatedNetworkTransform;
    private bool pauseMenuEnabled = false;
    [SerializeField] private GameObject pauseMenuPrefabObj;
    private PauseMenu pauseMenu;
    private bool movementEnabled = true;
    private bool gravityEnabled = true;
    private Collider climbableTrigger;

    // Camera
    [SerializeField] private GameObject playerCameraPrefabObj;
    private GameObject playerCameraObj;
    private CinemachineCamera cineCam;
    private CinemachineOrbitalFollow cameraOrbit = null;
    [SerializeField] private Transform freeLookTargetObj;
    private Transform cameraTargetTransform;
    private AudioListener audioListener;

    // UI
    [SerializeField] private GameObject playerUIPrefabObj;
    public GameObject playerUIObj;
    [SerializeField] private UIController ui;
    public UIController UI { get { return ui; } }

    // Animation
    [SerializeField] private Animator animator;
    [SerializeField] private ParticleSystem animationFootstepParticleSystem;
    private float previousSpeed = 0;

    // Entity Interaction
    // TODO: Figure out what needs to be moved to Entity class
    [HideInInspector] public Entity hoveredEntity = null;
    public List<Transform> PickupContainers { get { return pickupContainers; } }

    // TODO: Move this to Entity class
    // [Range(0.0f, 1.0f)]
    // [SerializeField] private float draggableMovementInfluence = 1f;
    private float dropTimer = -1f;

    // Ragdoll
    private RagdollManager ragdollManager;

    public bool isInitialized = false;
    #endregion

    #region Lifecycle
    protected sealed override void Awake()
    {
        base.Awake();

        playerStats = stats as PlayerStats;

        characterController = GetComponent<CharacterController>();
        anticipatedNetworkTransform = GetComponent<AnticipatedNetworkTransform>();
        anticipatedNetworkTransform.StaleDataHandling = StaleDataHandling.Reanticipate;
        ragdollManager = GetComponent<RagdollManager>();
    }

    public sealed override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        SceneManager.activeSceneChanged += ChangedActiveScene;

        if (IsOwner)
        {
            // Capture the mouse cursor
            Cursor.lockState = CursorLockMode.Locked;

            // Set up the player controls
            controls = new PlayerControls();

            // Set up the input callbacks
            controls.Gameplay.Move.performed += ctx => Move(ctx.ReadValue<Vector2>());
            controls.Gameplay.Move.canceled += ctx => Move(ctx.ReadValue<Vector2>());
            controls.Gameplay.SwitchHands.performed += ctx => SwitchHands((int)ctx.ReadValue<float>());
            controls.Gameplay.Use.performed += ctx => UseRpc();
            controls.Gameplay.Drop.started += ctx => StartDropRpc();
            controls.Gameplay.Drop.canceled += ctx => FinishDrop();
            controls.Global.Exit.performed += ctx => ExitPressedRpc();

            if (!IsHost) Initialize();
        }

        ToggleMovement(true);
    }

    public sealed override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        SceneManager.activeSceneChanged -= ChangedActiveScene;

        if (IsOwner)
        {
            // If the player is despawned, disable the inputs
            // I was getting random errors in the editor for a null value, so this is a quick fix
            controls.Gameplay.Disable();
            controls.Global.Disable();

            // Disable audio listener
            if (audioListener) audioListener.enabled = false;
        }
    }

    protected sealed override void Update()
    {
        base.Update();

        // Update hovered entity
        if (IsOwner && isInitialized) CheckLookTarget();
        if (dropTimer >= 0f) dropTimer += Time.deltaTime;
    }

    protected sealed override void FixedUpdate()
    {
        base.FixedUpdate();
        if (!IsOwner || !isInitialized) return;


        Vector2 adjustedInput;
        Quaternion lookDirection;
        CalculateMovementInputs(out adjustedInput, out lookDirection);

        if (!IsServer)
        {
            inputBuffer.Add(new inputState { timestamp = NetworkManager.LocalTime.Time, input = adjustedInput, lookDirection = lookDirection });
            HandleMovement(adjustedInput, lookDirection); // Client side prediction
        }
        HandleMovementRpc(adjustedInput, lookDirection);

        UpdateAnimatorVelocityRpc();
    }

    [Rpc(SendTo.Server)]
    private void UpdateAnimatorVelocityRpc()
    {
        // Report speed
        float speed = Mathf.Lerp(previousSpeed, Vector3.ProjectOnPlane(characterController.velocity, Vector3.up).magnitude / playerStats.BaseWalkSpeed, Time.fixedDeltaTime * playerStats.IdleTransitionSpeed);
        previousSpeed = speed;

        if (animator != null) animator.SetFloat("Velocity", speed);
    }
    #endregion

    #region Initialization
    public void Initialize()
    {
        if (IsOwner)
        {
            // If the player is spawned, enable the inputs
            controls.Gameplay.Enable();
            controls.Global.Enable();

            if (!playerUIObj) playerUIObj = Instantiate(playerUIPrefabObj);

            if (!playerCameraObj)
            {
                playerCameraObj = Instantiate(playerCameraPrefabObj);
                audioListener = playerCameraObj.GetComponentInChildren<AudioListener>();
                cineCam = playerCameraObj.GetComponentInChildren<CinemachineCamera>();
                cineCam.Follow = freeLookTargetObj;
                cameraTargetTransform = cineCam.Follow;
                cineCam.Lens.FieldOfView = SettingsManager.GetOptionValue("Object Permanence", UpdateCameraFOV);

                // Enable audio listener
                audioListener.enabled = true;

                // Enable the camera
                cineCam.Priority.Value = 1;
            }

            // if (FindFirstObjectByType<DevNetworkManager>() != null) UI.ToggleLevelUI(true);

            if (GameManager.Instance?.usingSteam == true)
            {
                localId = SteamClient.SteamId.Value;
            }
            InitializeRpc();
        }
        isInitialized = true;
    }

    [Rpc(SendTo.Server)]
    private void InitializeRpc()
    {
        PlayerManager.Instance.AddPlayer(this);
        playerStats.Initialize(OwnerClientId);
        playerStats.SetInventory();
    }

    private void UpdateCameraFOV(float fov)
    {
        cineCam.Lens.FieldOfView = fov;
    }
    #endregion

    #region Cleanup
    [Rpc(SendTo.Server)]
    public void DisconnectCleanupRpc()
    {
        if (pickedUpEntity)
        {
            dropTimer = 0f;
            PutDownEntityRpc(transform.position + transform.forward);
        }
        if (interactedEntity)
        {
            interactedEntity.ResetInteract();
        }

        DisconnectCleanupOwnerRpc();
    }

    [Rpc(SendTo.Owner)]
    private void DisconnectCleanupOwnerRpc()
    {
        // Disable the UI
        ui.Reset();
    }
    #endregion

    #region UI
    [Rpc(SendTo.Owner)]
    public void ToggleLevelUIRpc(bool isEnabled)
    {
        ui.ToggleLevelUI(isEnabled);
    }
    #endregion

    [Rpc(SendTo.Everyone)]
    private void DisablePlayerRpc(bool hidePlayer)
    {
        if (controls != null) controls.Gameplay.Disable();

        if (hidePlayer && entityModelObject)
        {
            SetActiveEntityModelRpc(false);
        }

        characterController.enabled = false;
    }

    [Rpc(SendTo.Everyone)]
    private void EnablePlayerRpc()
    {
        if (controls != null) controls.Gameplay.Enable();

        if (entityModelObject) SetActiveEntityModelRpc(true);

        characterController.enabled = true;
    }


    #region Inputs
    public void ToggleMouseInputs(bool isEnabled)
    {
        if (isEnabled)
        {
            controls.Gameplay.Look.Enable();
            controls.Gameplay.Use.Enable();
            controls.Gameplay.Drop.Enable();
        }
        else
        {
            controls.Gameplay.Look.Disable();
            controls.Gameplay.Use.Disable();
            controls.Gameplay.Drop.Disable();
        }
    }

    private void Move(Vector2 direction) { moveInput = direction; }
    private void SwitchHands(int _) => SetNextPickupContainerRpc();

    // Pressing the "Use" button
    [Rpc(SendTo.Server)]
    private void UseRpc() => UseEntity();

    // Pressing the "Drop" button
    [Rpc(SendTo.Server)]
    private void StartDropRpc()
    {
        if (pickedUpEntity) dropTimer = 0f;
    }

    // Releasing the "Drop" button
    private void FinishDrop()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        // Get the point at which the entity will be thrown at
        Vector3 aimPoint = ray.GetPoint(1000f);
        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);

        foreach (RaycastHit hit in hits)
        {
            if (!hit.transform.CompareTag("Player"))
            {
                aimPoint = hit.point;
                break;
            }
        }

        PutDownEntityRpc(aimPoint);
    }

    // Pressing the "Exit" button
    [Rpc(SendTo.Server)]
    private void ExitPressedRpc()
    {
        if (interactedEntity != null) interactedEntity.ResetInteract();
        else TogglePauseMenu();
    }
    #endregion

    #region PauseMenu


    public void TogglePauseMenu(PauseMenu newPauseMenu = null)
    {
        // Don't let the player pause if they are knocked out, but let them unpause if they are 
        if (playerStats.isKnockedOut && !pauseMenuEnabled) return;

        pauseMenuEnabled = !pauseMenuEnabled;
        if (IsServer)
        {
            if (pauseMenuEnabled)
            {
                // Spawn pause menu
                Vector3 spawnPosition = transform.position + transform.right * 0.3f + transform.up * -0.1f;
                Quaternion spawnRotation = Quaternion.Euler(transform.rotation.eulerAngles + new Vector3(0f, 90f, 90f));
                GameObject newPauseMenuObj = Instantiate(pauseMenuPrefabObj, spawnPosition, spawnRotation);
                NetworkObject networkObj = newPauseMenuObj.GetComponent<NetworkObject>();
                networkObj.Spawn(true);
                newPauseMenu = newPauseMenuObj.GetComponentInChildren<PauseMenu>();

                // Set position constraint to player
                newPauseMenu.PickUp(transform);
            }
            else
            {
                // Destroy pause menu
                if (pauseMenu != null)
                {
                    pauseMenu.PutDown(transform);
                }
            }
        }
        if (IsOwner)
        {
            if (pauseMenuEnabled)
            {
                // Check for pause menu object
                if (newPauseMenu != null)
                {
                    // Disable non-ui controls
                    Cursor.lockState = CursorLockMode.Confined;
                    ToggleMouseInputs(false);

                    // Set vcam priority
                    CinemachineCamera pauseCam = newPauseMenu.GetComponentInChildren<CinemachineCamera>();
                    pauseCam.Priority.Value = 100;

                    // Register pause menu controls
                }
            }
            else
            {
                // Enable non-ui controls
                Cursor.lockState = CursorLockMode.Locked;
                ToggleMouseInputs(true);

                CinemachineCamera pauseCam = pauseMenu.GetComponentInChildren<CinemachineCamera>();
                pauseCam.Priority.Value = 0;
            }
        }

        pauseMenu = newPauseMenu;
        if (IsServer && !IsOwner) TogglePauseMenuRpc(newPauseMenu);
    }

    [Rpc(SendTo.Owner)]
    public void TogglePauseMenuRpc(NetworkBehaviourReference incomingPauseMenu)
    {
        TogglePauseMenu(incomingPauseMenu.TryGet(out PauseMenu newPauseMenu) ? newPauseMenu : null);
    }

    #endregion

        #region Movement
    private void CalculateMovementInputs(out Vector2 adjustedInput, out Quaternion lookDirection)
    {
        // Rotate moveInput to be relative to the camera
        Vector3 adjustedInput3D = cineCam.transform.TransformDirection(new(moveInput.x, 0, moveInput.y));

        // Calculate how much influence the dragged entity has on the player's movement
        // TODO: Redo this later
        // if (moveInput.magnitude > 0f && draggedEntity)
        // {
        //     Vector3 entityDirection = (draggedEntity.transform.position - transform.position).normalized;
        //     Debug.DrawLine(transform.position, transform.position + (entityDirection * 10f), Color.blue);
        //     adjustedInput3D = (adjustedInput3D + (Vector3.ProjectOnPlane(entityDirection, Vector3.up).normalized * 0.5f)).normalized;
        //     Debug.DrawLine(transform.position, transform.position + (adjustedInput3D * 10f), Color.yellow);
        // }

        Debug.DrawLine(transform.position, transform.position + (adjustedInput3D * 10f), Color.green);
        adjustedInput = new(adjustedInput3D.x, adjustedInput3D.z);

        // Get target rotation angle for player and dampen the change
        // float angleChangeFactor = 1f - (Vector3.Dot(adjustedInput3D.normalized, Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized) + 1f) / 2f;
        float targetAngle = Mathf.Atan2(adjustedInput.normalized.x, adjustedInput.normalized.y) * Mathf.Rad2Deg;
        targetAngle = Mathf.SmoothDampAngle(
            transform.eulerAngles.y,
            targetAngle,
            ref smoothDampVelocity,
            Mathf.Max(1f - playerStats.TurnSpeed, 0.0001f)
        );

        lookDirection = Quaternion.Euler(0f, targetAngle, 0f);
    }

    [Rpc(SendTo.Server)]
    private void HandleMovementRpc(Vector2 movementInput, Quaternion lookDirection)
    {
        HandleMovement(movementInput, lookDirection);
    }

    private void HandleMovement(Vector2 movementInput, Quaternion lookDirection)
    {
        if (characterController.enabled == false) return;

        // Screw gravity (*floats into the air endlessly*)
        Vector3 gravityVector = Vector3.zero;
        if (gravityEnabled) gravityVector = Physics.gravity * Time.fixedDeltaTime;

        Vector3 movementVector = Vector3.zero;
        Quaternion newRotation = transform.rotation;
        if (!stats.isKnockedOut && movementEnabled && movementInput.magnitude >= 0.01f)
        {
            // Set the player rotation to face the way they are walking
            newRotation = lookDirection;

            // Move the player
            Vector3 moveDirection = new Vector3(movementInput.x, 0, movementInput.y).normalized;
            // Vector3 moveDirection = lookDirection * Vector3.forward;

            movementVector = moveDirection * playerStats.WalkSpeed * Time.fixedDeltaTime;
            Debug.DrawLine(transform.position, transform.position + (movementVector * 10f), Color.red);

            // If in a "climbable" tagged trigger, movementInput.y will control the player's vertical movement
            if (climbableTrigger != null)
            {
                // TODO: This is not a reliable way to check if the player is still in the trigger
                if (climbableTrigger.bounds.Contains(transform.position))
                {
                    Debug.Log("Climbing");
                    Vector3 climbDirection = climbableTrigger.transform.up;
                    movementVector += 2f * Time.fixedDeltaTime * movementInput.y * climbDirection;
                    gravityVector = Vector3.zero; // Disable gravity while climbing
                }
                else
                {
                    climbableTrigger = null;
                }
            }

        }

        characterController.Move(gravityVector + movementVector);
        anticipatedNetworkTransform.AnticipateState(new AnticipatedNetworkTransform.TransformState
        {
            Position = transform.position, // TODO: I'm not sure if this has the new position or the old position
            Rotation = newRotation,
            Scale = transform.localScale
        });
    }

    // https://github.com/Unity-Technologies/com.unity.multiplayer.samples.bitesize/blob/022594f453adf5bd26f7cc40dd3ee27b06002738/Experimental/Anticipation%20Sample/Assets/Scripts/PlayerMovableObject.cs#L109
    public override void OnReanticipate(double lastRoundTripTime)
    {
        // Debug.Log("Reanticipating");
        // Get previous client-side state and the time that the server sent this authoritative state
        var previousState = anticipatedNetworkTransform.PreviousAnticipatedState;
        var authorityTime = NetworkManager.LocalTime.Time - lastRoundTripTime;

        // Sync physics after server overwrites the transform
        Physics.SyncTransforms();

        // Replay inputs between last round trip time and now
        var now = NetworkManager.LocalTime.Time;
        var lastInputTime = authorityTime;
        int count = 0;
        foreach (var input in inputBuffer)
        {
            if (inputBuffer.Count > count + 1 && input.timestamp == inputBuffer[count + 1].timestamp) continue;
            if (input.timestamp > authorityTime)
            {
                if ((float)(input.timestamp - lastInputTime) > 0.0f)
                {
                    Physics.simulationMode = SimulationMode.Script;
                    Physics.Simulate((float)(input.timestamp - lastInputTime));
                    Physics.simulationMode = SimulationMode.FixedUpdate;
                }

                HandleMovement(input.input, input.lookDirection);

                lastInputTime = input.timestamp;
                count++;
            }
        }
        if ((float)(now - lastInputTime) > 0.0f)
        {
            Physics.simulationMode = SimulationMode.Script;
            Physics.Simulate((float)(now - lastInputTime));
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }

        inputBuffer.RemoveAll(item => item.timestamp < authorityTime);

        // This prevents small amounts of wobble from slight differences.
        var sqDist = Vector3.SqrMagnitude(previousState.Position - anticipatedNetworkTransform.AnticipatedState.Position);
        if (sqDist <= 0.1f)
        {
            anticipatedNetworkTransform.AnticipateState(previousState);
            Physics.SyncTransforms();
        }
        // else if (sqDist < 3f * 3f)
        // {
        //     // Server updates are not necessarily smooth, so applying reanticipation can also result in
        //     // hitchy, unsmooth animations. To compensate for that, we call this to smooth from the previous
        //     // anticipated state (stored in "anticipatedValue") to the new state (which, because we have used
        //     // the "Move" method that updates the anticipated state of the transform, is now the current
        //     // transform anticipated state)
        //     anticipatedNetworkTransform.Smooth(previousState, anticipatedNetworkTransform.AnticipatedState, 0.1f);
        //     Physics.SyncTransforms();
        // }
    }

    public void ToggleMovement(bool isEnabled)
    {
        characterController.enabled = isEnabled;
        movementEnabled = isEnabled;
        if (IsServer && !IsOwner) ToggleMovementRpc(isEnabled);
    }

    [Rpc(SendTo.Owner)]
    public void ToggleMovementRpc(bool isEnabled) => ToggleMovement(isEnabled);


    public void ToggleGravity(bool isEnabled)
    {
        gravityEnabled = isEnabled;
        if (IsServer && !IsOwner) ToggleGravityRpc(isEnabled);
    }

    [Rpc(SendTo.Owner)]
    public void ToggleGravityRpc(bool isEnabled) => ToggleGravity(isEnabled);

    public void AnimationFootstepEvent(int footIndex)
    {
        var emitParams = new ParticleSystem.EmitParams();
        Vector3 footPosition = animationFootstepParticleSystem.transform.position;
        footPosition += transform.right * (footIndex == 0 ? -0.1f : 0.1f);
        emitParams.rotation = transform.rotation.eulerAngles.y;
        emitParams.position = footPosition;
        emitParams.applyShapeToPosition = true;
        animationFootstepParticleSystem.Emit(emitParams, 1);
    }
    #endregion

    #region EntityInteraction
    private void CheckLookTarget()
    {
        if (!cameraOrbit)
        {
            cameraOrbit = playerCameraObj.GetComponentInChildren<CinemachineOrbitalFollow>();
        }

        // Cast a ray from the camera to the mouse cursor
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        ray.origin += ray.direction * cameraOrbit.Radius;

        // If the ray hits something, check if it's an entity
        bool foundEntity = false;
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            // if (hit.transform.TryGetComponent(out Renderer renderer))
            // {
            //     if (renderer.material.shader.name == "Shader Graphs/AdvancedDarkness")
            //     {
            //         Shader.SetGlobalVector("_VisibleOrigin", hit.point);
            //         Shader.SetGlobalVector("_SourceOrigin", Camera.main.transform.position);
            //         Shader.SetGlobalFloat("_IsVisible", 1);
            //         Shader.SetGlobalFloat("_VisibleRadius", 1);
            //     }
            // }
            // else
            // {
            //     Shader.SetGlobalFloat("_IsVisible", 0);
            // }

            if (hit.distance < playerStats.EntityGrabDistance && hit.transform.TryGetComponent<Entity>(out _))
                foundEntity = true;
        }

        // Have the server verify and set the Hovered Entity if the client finds one
        if (foundEntity) SetHoveredEntityRpc(ray);
        else RemoveHoveredEntityRpc();
    }

    [Rpc(SendTo.Server)]
    private void SetHoveredEntityRpc(Ray ray) // TODO: This a possible cheat vector, since the client can send any ray it wants
    {
        // If the ray hits something, check if it's an entity
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            if (hit.distance < playerStats.EntityGrabDistance)
            {
                Entity entity = hit.transform.GetComponentInParent<Entity>();
                if (entity == null) return;
                
                // If it's an entity, outline it and set it as the hovered entity
                if (hoveredEntity != null && hoveredEntity != entity)
                {
                    hoveredEntity.ResetOutlineRpc(this);
                }
                hoveredEntity = entity;
                entity.OutlineRpc(this);
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void RemoveHoveredEntityRpc()
    {
        // If the ray didn't hit an entity, reset the outline of the previously hovered entity
        if (hoveredEntity != null)
        {
            hoveredEntity.ResetOutlineRpc(this);
            hoveredEntity = null;
        }
    }

    private void UseEntity()
    {
        // If you're holding an entity, interact with it
        if (pickedUpEntity)
        {
            pickedUpEntity.Interact(transform);
            return;
        }

        // If you're not holding an entity, check if you're looking at one
        // You can't do anything except drop an entity if you're currently dragging one
        if (hoveredEntity != null)
        {
            Debug.Log("Using entity: " + hoveredEntity.name);
            // If it's an entity that can be interacted with, interact with it
            if (hoveredEntity.CanBeInteractedWith.Value)
            {
                hoveredEntity.Interact(transform);
            }
            // If it's an entity that can be picked up, pick it up
            else if (hoveredEntity.CanBePickedUp.Value && hoveredEntity != this && !hoveredEntity.CompareTag("Player"))
            {
                hoveredEntity.TogglePickUp(transform);
            }
        }

    }

    [Rpc(SendTo.Server)]
    private void PutDownEntityRpc(Vector3 aimPoint)
    {
        if (!pickedUpEntity) return;

        if (dropTimer <= 0.2f) pickedUpEntity.TogglePickUp(transform);
        else
        {
            // Otherwise, throw the entity
            float throwForce = Mathf.Clamp(dropTimer, 0.2f, 2f) * 1000f;
            Vector3 throwVector = (aimPoint - activePickupContainer.transform.position).normalized;

            pickedUpEntity.PutDown(transform, throwVector * throwForce);
        }

        dropTimer = -1f;
    }
    #endregion

    #region EntityStates
    override protected void PickUpState()
    {
        characterController.enabled = false;

        if (pickerUpper.TryGetComponent(out AI ai) && ai.FollowCameraTarget != null) cineCam.Follow = ai.FollowCameraTarget;
        else cineCam.Follow = pickerUpper;

        if (pickedUpEntity != null)
        {
            pickedUpEntity.PutDown(transform);
            pickedUpEntity = null;
        }
    }

    protected override void PutDownState()
    {
        cineCam.Follow = cameraTargetTransform;
        characterController.enabled = true;
        transform.localScale = Vector3.one;

        if (ragdollManager.isRagdolled)
        {
            if (rb != null) rb.isKinematic = !stats.isKnockedOut;

            //if (stats.isKnockedOut) cineCam.Follow = ragdollManager.mainBone.transform;
            if (stats.isKnockedOut && ragdollManager.CameraFollowTarget) cineCam.Follow = ragdollManager.CameraFollowTarget.transform;
            else //ragdollManager.SetRagdollActive(false);
            {
                ragdollManager.DespawnRagdollRpc();
            }
        }
    }

    protected override void StatChangeState(StatType statType)
    {
        if (statType == StatType.Torpor)
        {
            if (playerStats.isKnockedOut && !ragdollManager.isRagdolled)
            {
                //cineCam.Follow = ragdollManager.mainBone.transform;
                ragdollManager.SpawnRagdollRpc(transform.position, transform.rotation, playerStats.lastDamageForceInstance);
                if (ragdollManager.CameraFollowTarget) cineCam.Follow = ragdollManager.CameraFollowTarget.transform;

                DisablePlayerRpc(true);

                //ragdollManager.SetRagdollActive(true, true, playerStats);
            }

            if (!playerStats.isKnockedOut && ragdollManager.isRagdolled)
            {
                if (isPickedUp || ragdollManager.CurrentRagdoll.isPickedUp) return;

                transform.position = ragdollManager.CameraFollowTarget.transform.position + new Vector3(0, characterController.height, 0);

                EnablePlayerRpc();

                cineCam.Follow = cameraTargetTransform;
                ragdollManager.DespawnRagdollRpc();
                //ragdollManager.SetRagdollActive(false);
            }
        }
    }

    // Reacting to the various states of the entity that the player is currently interacting with
    protected sealed override void InteractionReaction(InteractState interactState)
    {
        if (!IsOwner)
        {
            Debug.LogWarning("InteractionReaction called on non-owner player");
            return;
        }

        if (interactState == InteractState.Frozen) controls.Gameplay.Disable();
        if (interactState == InteractState.Done)
        {
            controls.Gameplay.Enable();
        }
    }
    #endregion

    #region Collision
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Climbable"))
        {
            climbableTrigger = other;
        }
    }


    // this script pushes all rigidbodies that the character touches
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!IsServer) return;

        Rigidbody body = hit.collider.attachedRigidbody;

        // no rigidbody
        if (body == null || body.isKinematic) return;

        // We dont want to push objects below us
        if (hit.moveDirection.y < -0.3) return;

        // Calculate push direction from move direction,
        // we only push objects to the sides never up and down
        Vector3 pushDir = new(hit.moveDirection.x, 0, hit.moveDirection.z);

        // If you know how fast your character is trying to move,
        // then you can also multiply the push velocity by that.

        // Apply the push
        body.linearVelocity = pushDir * 2.0f;
    }

    public void ToggleCollider(bool isEnabled)
    {
        GetComponent<Collider>().enabled = isEnabled;
        if (IsServer && !IsOwner) ToggleColliderRpc(isEnabled);
    }

    [Rpc(SendTo.Owner)]
    public void ToggleColliderRpc(bool isEnabled) => ToggleCollider(isEnabled);
    #endregion

    #region SceneManagement
    private void ChangedActiveScene(Scene _, Scene next)
    {
        ChangedActiveSceneRpc(next.name);
    }

    [Rpc(SendTo.Owner)]
    private void ChangedActiveSceneRpc(string sceneName)
    {
        if (GameManager.Instance?.debugMode == true) Debug.Log("Changed active scene for " + name + " " + NetworkManager.Singleton.LocalClientId);
        if (playerUIObj) SceneManager.MoveGameObjectToScene(playerUIObj, SceneManager.GetSceneByName(sceneName));
        if (playerCameraObj) SceneManager.MoveGameObjectToScene(playerCameraObj, SceneManager.GetSceneByName(sceneName));

        if (sceneName == "ThaCrib") Initialize();
    }
    #endregion
}
