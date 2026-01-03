using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class PauseMenu : Entity, IDraggable
{
    [SerializeField] private Transform[] ikAttachmentPoints = new Transform[0];
    [SerializeField] private GameObject visualContainerObj;
    [SerializeField] private GameObject levelObjectiveContainerObj;
    [SerializeField] private GameObject levelObjectiveUIPrefab;

    [SerializeField] private GameObject mainMenuObj;
    [SerializeField] private GameObject settingsMenuObj;

    [SerializeField] private Transform optionsListObj;
    [SerializeField] private GameObject optionPrefabObj;

    [SerializeField] private ParticleSystem dissolveEffect;

    private bool isDissolving = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        mainMenuObj.SetActive(true);
        settingsMenuObj.SetActive(false);

        InitializeSettingsMenuElements();

        if (!IsServer) return;

        CanBePickedUp.Value = true;
        targetHoldRotation = new Vector3(30f, 0, 0);

        if (GameManager.Instance.isInitialized && GameManager.Instance.currentHeist != null)
        {
            LevelData levelInfo = GameManager.Instance.currentHeist.GetCurrentLevelInfo();
            if (levelInfo != null && levelInfo.objectives != null && levelInfo.objectives.Count > 0)
            {
                PopulateLevelObjectivesRPC(levelInfo.objectives.ToArray());
                levelInfo.objectivesUpdatedEvent.AddListener(OnObjectivesUpdated);
            }
        }
    }

    protected sealed override void Update()
    {
        base.Update();

        if (isDissolving && dissolveEffect.isStopped)
        {
            isDissolving = false;
            NetworkObject.Despawn(true);
        }
    }

  [Rpc(SendTo.Everyone)]
    private void PopulateLevelObjectivesRPC(LevelObjective[] objectives)
    {
        if (levelObjectiveContainerObj == null || levelObjectiveUIPrefab == null) return;

        foreach (Transform child in levelObjectiveContainerObj.transform)
        {
            Destroy(child.gameObject);
        }

        foreach (var objective in objectives)
        {
            GameObject objectiveUI = Instantiate(levelObjectiveUIPrefab, levelObjectiveContainerObj.transform);
            TMP_Text objectiveText = objectiveUI.transform.Find("Objective").GetComponent<TMP_Text>();
            objectiveText.text = objective.isOptional ? $"<i>{objective.name}</i>" : objective.name;
            objectiveText.color = objective.isOptional ? new Color32(144, 138, 153, 255) : new Color32(79, 67, 174, 255);
            if (objective.status == LevelObjectiveStatus.Completed)
            {
                objectiveText.text = $"<s>{objective.name}</s>";
            }
            else if (!objective.isOptional && objective.status == LevelObjectiveStatus.Failed)
            {
                objectiveText.color = Color.red;
            }
        }
    }

    private void OnObjectivesUpdated(List<LevelObjective> objectives)
    {
        if (IsServer)
        {
            PopulateLevelObjectivesRPC(objectives.ToArray());
        }
    }

    public void OnSettingsPressed()
    {
        Debug.Log("Settings button pressed");
        mainMenuObj.SetActive(false);
        settingsMenuObj.SetActive(true);
    }

    public void OnDisconnectPressed()
    {
        if (IsHost) return;

        if (pickerUpper != null && pickerUpper.GetComponentInParent<NetworkObject>().IsLocalPlayer)
        {
            TogglePickUp(pickerUpper);
            GameManager.Instance.PrepGoToHomeCrib();
        }
    }

    public void OnQuitToDesktopPressed()
    {
        if (pickerUpper != null && pickerUpper.GetComponentInParent<NetworkObject>().IsLocalPlayer)
        {
            TogglePickUp(pickerUpper);
            GameManager.Instance.PrepGoToHomeCrib();
            Application.Quit();
        }
    }

    private void InitializeSettingsMenuElements()
    {
        foreach (Transform child in optionsListObj) Destroy(child.gameObject);

        // Initialize settings menu options
        foreach (var option in SettingsManager.GetOptionsDict().Values)
        {
            string category = option.category;
            string subCategory = option.subCategory;

            // TODO: set parent based on category and subCategory
            GameObject optionObj = Instantiate(optionPrefabObj, optionsListObj);
            SettingsMenuOptionUI optionUI = optionObj.GetComponent<SettingsMenuOptionUI>();
            optionUI.Initialize(option);
        }
    }

    private void Dissolve()
    {
        DissolveRPC();
        isDissolving = true;
    }

    [Rpc(SendTo.Everyone)]
    private void DissolveRPC()
    {
        visualContainerObj.SetActive(false);
        dissolveEffect.Play();
    }

    protected override void PutDownState()
    {
        CanBePickedUp.Value = false;
        rb.isKinematic = true;
        col.enabled = false;
        Dissolve();
    }

    public bool IsDraggable()
    {
        return !isPickedUp;
    }

    public float GetDraggingSpeedMult()
    {
        return Mathf.Min(25.0f / rb.mass, 1.0f);
    }

    public Transform[] GetIKAttachPoints()
    {
        return ikAttachmentPoints;
    }
}
