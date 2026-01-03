using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundBubble : MonoBehaviour
{
    private readonly float radiusGrowRate = 20f;
    private float maxRadius = 10.0f;

    private bool isReady = false;

    // private Guid id = new();

    private void Update()
    {
        if (!isReady) return;

        // After a bubble has reached the maximum, start the chain reaction of destroying the bubbles
        if (transform.localScale.x >= maxRadius) Destroy(gameObject);
        else
        {
            // Grow bubble until collision or max radius is reached
            float radiusGrow = radiusGrowRate * Time.deltaTime;
            transform.localScale += new Vector3(radiusGrow, radiusGrow, radiusGrow);
        }
    }

    public void Initialize(float maxRadius, bool visible = false)
    {
        this.maxRadius = maxRadius;
        GetComponent<MeshRenderer>().enabled = visible;
        isReady = true;
    }

    public float GetBubbleStrength()
    {
        return maxRadius;
    }
}
