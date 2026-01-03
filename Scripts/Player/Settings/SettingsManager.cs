using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class SettingsOption
{
    public string label;
    public string category;
    public string subCategory;
    public string description;
    public float minValue;
    public float maxValue;
    private float _value;
    public float Value
    {
        get
        {
            return _value;
        }
        set
        {
            _value = value;
            onValueChanged?.Invoke(value);
            try
            {
                updater?.Invoke(label, value);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to update settings option {label}: {e.Message}");
            }
        }
    }
    public static Action<string, float> updater;
    public UnityEvent<float> onValueChanged = new();
    public SettingsOption(string label, string category, string subCategory = "", string description = "", float minValue = -1.0f, float maxValue = -1.0f, float initialValue = 0.0f)
    {
        this.label = label;
        this.category = category;
        this.subCategory = subCategory;
        this.description = description;
        this.minValue = minValue;
        this.maxValue = maxValue;
        Value = initialValue; // Set the initial value
    }
}

public static class SettingsManager
{
    private static bool initialized = false;

    #region Settings Initialization
    // Updates settings to match previously saved values
    private static bool GetSavedSettings()
    {
        // Load saved settings from PlayerPrefs or other storage
        foreach (var option in optionsDict.Values)
        {
            if (PlayerPrefs.HasKey(option.label)) option.Value = PlayerPrefs.GetFloat(option.label, option.Value);
        }
        SettingsOption.updater = (label, value) =>
        {
            // Save the updated value to PlayerPrefs or other storage
            PlayerPrefs.SetFloat(label, value);
            PlayerPrefs.Save();
        };
        return true;
    }
    #endregion

    #region Options Management
    // Returns a dictionary of all settings options
    private static Dictionary<string, SettingsOption> optionsDict= new(){
        { "Object Permanence", new("Object Permanence", "Gameplay", "Visuals", "How long objects remain visible after being thrown or dropped.", 0.0f, 100.0f, 69.0f) }
    };

    public static Dictionary<string, SettingsOption> GetOptionsDict()
    {
        if (!initialized) initialized = GetSavedSettings();
        return optionsDict;
    }

    // Returns a single option and optionally allows adding a listener for value changes
    public static float GetOptionValue(string label, UnityAction<float> listener = null)
    {
        if (!GetOptionsDict().TryGetValue(label, out var option)) return 0f;
        if (listener != null) option.onValueChanged.AddListener(listener);
        return option.Value;
    }
    #endregion
}

