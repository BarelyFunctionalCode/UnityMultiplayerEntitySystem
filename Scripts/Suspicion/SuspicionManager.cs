using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class DetectionEvent
{
    public int id;
    private Vector3 sourcePosition = Vector3.zero;
    private Transform sourceTransform = null;
    private float strength = 0.0f;
    private bool hasTransform = false;
    public bool hasUpdated = true;
    public bool hasAlerted = false;

    public DetectionEvent(int id, Transform sourceTransform, float strength)
    {
        this.id = id;
        this.sourceTransform = sourceTransform;
        this.sourcePosition = sourceTransform.position;
        this.strength = strength;
        hasTransform = true;
    }

    public DetectionEvent(int id, Vector3 sourcePosition, float strength)
    {
        this.id = id;
        this.sourcePosition = sourcePosition;
        this.strength = strength;
    }

    public Vector3? GetPosition()
    {
        hasUpdated = false;
        if (sourceTransform != null) return sourceTransform.position;
        if (hasTransform && sourceTransform == null) return null;
        return sourcePosition;
    }

    public float GetStrength() => strength;
    public void UpdateStrength(float newStrength) => strength = newStrength;
    public void DecreaseStrength(float amount) => strength = Mathf.Max(strength - amount, 0.0f);
}

public class SuspicionManager : NetworkBehaviour
{
    #region ClassVariables
    [SerializeField] private NetworkVariable<float> suspicionLevel = new(0.0f);

    [Header("UI")]
    [SerializeField] private bool showUI = true;
    [SerializeField] private GameObject uiObj;
    [SerializeField] private GameObject searchingIconObj;
    [SerializeField] private GameObject alertIconObj;
    [SerializeField] private GameObject susBarObj;
    [SerializeField] private Image susBar;
    [SerializeField] private Image susBarVisual;

    private SoundDetector soundDetector;
    private EntityDetector entityDetector;
    private NetworkVariable<float> alertThreshold = new(2.0f);
    private float suspicionDecayRate = 4.0f;
    private float soundDetectionRange;
    private float soundDetectionMaxRange;
    private float entityDetectionRange;
    private float entityDetectionMaxRange;
    private float entityDetectionFOV;
    
    #endregion

    #region Lifecycle

    private void Update()
    {
        if (!IsSpawned) return;

        float susPercent = suspicionLevel.Value / alertThreshold.Value;
        if (IsServer)
        {
            // Detection ranges are increased based on the suspicion level
            if (soundDetector)
            {
                float susSoundRadius = Mathf.Lerp(soundDetectionRange, soundDetectionMaxRange, susPercent);
                soundDetector.SetDetectionRange(susSoundRadius);
            }

            if (entityDetector)
            {
                float susEntityRadius = Mathf.Lerp(entityDetectionRange, entityDetectionMaxRange, susPercent);
                entityDetector.SetDetectionRange(susEntityRadius);
            }

            if (soundDetector || entityDetector) ProcessDetectionEvents();
            ProcessAlert();
        }

        // Update the UI if it is enabled
        if (showUI)
        {
            if (Camera.main) uiObj.transform.LookAt(Camera.main.transform.position, Vector3.up);
            // Debug.Log($"Suspicion Level: {suspicionLevel.Value}, Alert Threshold: {alertThreshold.Value}, Sus Percent: {susPercent} - Alerted: {isAlerted}, Cooldown Timer: {alertCooldownTimer}");

            if (!isAlerted && suspicionLevel.Value < alertThreshold.Value)
            {
                susBarObj.SetActive(true);
                susBar.fillAmount = Mathf.Lerp(susBar.fillAmount, susPercent, Time.deltaTime * 5f);
                susBarVisual.color = Color.HSVToRGB(Mathf.Lerp(0.33f, 0.0f, susBar.fillAmount), 1.0f, 1.0f);
            }
            else susBarObj.SetActive(false);

            searchingIconObj.SetActive(!isAlerted && susPercent > 0.01f && suspicionLevel.Value < alertThreshold.Value);
            alertIconObj.SetActive(isAlerted && alertCooldownTimer > alertCooldown * 0.75f);
        }
    }
    #endregion

    #region DetectionProcessing
    private List<DetectionEvent> detectionEvents = new();
    private void OnDetection(DetectionEvent eventData)
    {
        if (!IsServer)
        {
            Debug.LogWarning("SuspicionManager: OnDetection called on a non-server instance. Ignoring event.");
            return;
        }
        // Check if the detection event is already in the list
        for (int i = 0; i < detectionEvents.Count; i++)
        {
            if (detectionEvents[i].id == eventData.id)
            {
                detectionEvents[i].hasUpdated = true;
                detectionEvents[i].hasAlerted = false;
                if (eventData.GetStrength() > detectionEvents[i].GetStrength()) detectionEvents[i].UpdateStrength(eventData.GetStrength());
                return;
            }
        }
        detectionEvents.Add(eventData);
    }

    private void ProcessDetectionEvents()
    {
        // Go through the list of current detection events
        float suspicionDelta = 0.0f;
        DetectionEvent focusedDetectionEvent = null;
        for (int i = 0; i < detectionEvents.Count; i++)
        {
            DetectionEvent detection = detectionEvents[i];
            // If the detection event has zero strength, remove it from the list
            if (detection.GetStrength() <= 0.0f)
            {
                detectionEvents.RemoveAt(i);
                i--;
            }
            else
            {
                // Find the event with the highest strength to focus on, and update suspicion level
                if (detection.hasUpdated) suspicionDelta += detection.GetStrength();
                detection.DecreaseStrength(suspicionDecayRate * Time.deltaTime);
                if (focusedDetectionEvent == null || detection.GetStrength() > focusedDetectionEvent.GetStrength())
                {
                    focusedDetectionEvent = detection;
                }
            }
            detection.hasUpdated = false;
        }
        suspicionLevel.Value += suspicionDelta * Time.deltaTime;

        // Once there are no events, slowly decrease suspicion level
        if (!isAlerted && detectionEvents.Count == 0) suspicionLevel.Value -= Time.deltaTime;
        suspicionLevel.Value = Mathf.Clamp(suspicionLevel.Value, 0.0f, alertThreshold.Value);

        // If suspicion level maxes out, alert listeners
        if (suspicionLevel.Value >= alertThreshold.Value && focusedDetectionEvent?.hasAlerted == false)
        {
            focusedDetectionEvent.hasAlerted = true;
            Alert(focusedDetectionEvent.GetPosition());
        }
    }
    #endregion

    #region AlertProcessing
    [SerializeField] public SuspicionManager chainedManager = null;
    public UnityEvent<Vector3?> POIEvent = new();
    private bool isAlerted = false;
    private float alertCooldown = 30.0f;
    private float alertCooldownTimer = 0.0f;
    public void Alert(Vector3? position = null, float cooldownMultiplier = 1.0f)
    {
        if (!IsServer) return;
        if (position.HasValue)
        {
            POIEvent.Invoke(position.Value);
            isAlerted = true;
            alertCooldownTimer = alertCooldown * cooldownMultiplier;
            suspicionLevel.Value = alertThreshold.Value;
            // Only alert chained managers for non-null events
            if (chainedManager != null) chainedManager.Alert(position.Value, cooldownMultiplier);
        }
        else
        {
            Debug.Log($"Alert triggered with no position for object: {gameObject.name}");
            POIEvent.Invoke(null);
            isAlerted = false;
            alertCooldownTimer = 0.0f;
        }
    }

    private void ProcessAlert()
    {
        if (!IsServer || !isAlerted) return;
        if (alertCooldownTimer > 0.0f)
        {
            alertCooldownTimer -= Time.deltaTime;
            if (alertCooldownTimer <= 0.0f)
            {
                Alert(null);
            }
        }
    }
    #endregion

    #region DetectionSettings
    public void UpdateSoundDetection(bool enabled, float soundDetectionRange = 0.0f, float soundDetectionMaxRange = 0.0f)
    {
        if (!IsServer) return;
        UpdateSoundDetection(enabled);
        UpdateSoundDetection(soundDetectionRange, soundDetectionMaxRange);
    }

    public void UpdateSoundDetection(bool enabled)
    {
        if (!IsServer) return;
        if (soundDetector == enabled) return;

        if (enabled)
        {
            soundDetector = GetComponentInChildren<SoundDetector>(true);
            soundDetector.gameObject.SetActive(true);
            soundDetector.detectionEvent.AddListener(OnDetection);
        }
        else
        {
            soundDetector.detectionEvent.RemoveListener(OnDetection);
            soundDetector.gameObject.SetActive(false);
            soundDetector = null;
        }
    }

    public void UpdateSoundDetection(float soundDetectionRange, float soundDetectionMaxRange)
    {
        if (!IsServer) return;
        this.soundDetectionRange = soundDetectionRange;
        this.soundDetectionMaxRange = soundDetectionMaxRange;
    }
    
    public void UpdateEntityDetection(bool enabled, float entityDetectionRange = 0.0f, float entityDetectionMaxRange = 0.0f, float detectionFOV = 0.0f)
    {
        if (!IsServer) return;
        UpdateEntityDetection(enabled);
        UpdateEntityDetection(entityDetectionRange, entityDetectionMaxRange);
        UpdateEntityDetection(detectionFOV);
    }

    public void UpdateEntityDetection(bool enabled)
    {
        if (!IsServer) return;
        if (entityDetector == enabled) return;
        if (enabled)
        {
            entityDetector = GetComponentInChildren<EntityDetector>(true);
            entityDetector.gameObject.SetActive(true);
            entityDetector.detectionEvent.AddListener(OnDetection);
        }
        else
        {
            entityDetector.detectionEvent.RemoveListener(OnDetection);
            entityDetector.gameObject.SetActive(false);
            entityDetector = null;
        }
    }

    public void UpdateEntityDetection(float entityDetectionRange, float entityDetectionMaxRange)
    {
        if (!IsServer) return;
        this.entityDetectionRange = entityDetectionRange;
        this.entityDetectionMaxRange = entityDetectionMaxRange;
    }

    public void UpdateEntityDetection(float detectionFOV)
    {
        if (!IsServer) return;
        if (entityDetector == null) entityDetector = GetComponentInChildren<EntityDetector>();
        entityDetectionFOV = detectionFOV;
        entityDetector.SetDetectionFOV(detectionFOV);
    }
    #endregion

    #region AlertSettings
    public void UpdateAlerting(float newAlertThreshold, float newAlertCooldown, float newSuspicionDecayRate = -1.0f)
    {
        if (!IsServer) return;
        alertThreshold.Value = newAlertThreshold;
        suspicionLevel.Value = Mathf.Clamp(suspicionLevel.Value, 0.0f, alertThreshold.Value);

        alertCooldown = newAlertCooldown;
        if (newSuspicionDecayRate >= 0.0f) suspicionDecayRate = newSuspicionDecayRate;
    }
    #endregion

    #region Debug
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, soundDetectionRange);

        float FOVRadians = entityDetectionFOV / 2 * Mathf.Deg2Rad;
        Gizmos.color = Color.blue;
        Gizmos.DrawLineStrip(new Vector3[5]
        {
            transform.position,
            transform.position + transform.forward * (entityDetectionRange * Mathf.Cos(FOVRadians)) + transform.right * (entityDetectionRange * Mathf.Sin(FOVRadians)),
            transform.position + transform.forward * entityDetectionRange,
            transform.position + transform.forward * (entityDetectionRange * Mathf.Cos(FOVRadians)) - transform.right * (entityDetectionRange * Mathf.Sin(FOVRadians)),
            transform.position
        }, true);
    }
    #endregion
}
