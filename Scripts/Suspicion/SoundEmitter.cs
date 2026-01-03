using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public static class SoundLevel
{
    public const float Low = 5.0f;
    public const float Medium = 10.0f;
    public const float High = 20.0f;
    public const float Extreme = 50.0f;
}

public class SoundEmitter : MonoBehaviour
{
    [SerializeField] private GameObject soundBubblePrefab;


    private float timeElapsedFromLastEmission = 0.0f;
    private readonly bool devMode = true;

    private void Update()
    {
        timeElapsedFromLastEmission += Time.deltaTime;
    }

    public float GetTimeElapsedFromLastEmission()
    {
        return timeElapsedFromLastEmission;
    }

    public void EmitSound(float radius = SoundLevel.Medium)
    {
        GameObject soundBubble = Instantiate(soundBubblePrefab, transform.position, Quaternion.identity);
        soundBubble.GetComponent<SoundBubble>().Initialize(radius, devMode);
        timeElapsedFromLastEmission = 0.0f;
    }
}
