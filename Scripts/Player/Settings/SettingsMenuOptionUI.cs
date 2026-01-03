using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class SettingsMenuOptionUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text currentValueText;
    [SerializeField] private Slider slider;

    private SettingsOption option;

    private bool initialized = false;


    public void Initialize(SettingsOption option)
    {
        if (initialized)
        {
            Debug.LogError("SettingsMenuOptionUI already initialized");
            return;
        }
        this.option = option;

        nameText.text = option.label;
        // TODO: make tooltip for description

        slider.minValue = option.minValue;
        slider.maxValue = option.maxValue;
        slider.value = option.Value;
        currentValueText.text = Mathf.Round(slider.value).ToString();

        slider.onValueChanged.AddListener(delegate { UpdateValue(); });

        initialized = true;
    }

    public void UpdateValue()
    {
        option.Value = slider.value;
        currentValueText.text = Mathf.Round(slider.value).ToString();
    }
}