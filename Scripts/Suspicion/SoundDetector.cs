using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;


public class SoundDetector : MonoBehaviour
{
    private SphereCollider detectorTrigger;

    private float detectionRange = 10f;

    public UnityEvent<DetectionEvent> detectionEvent = new();


    void Awake()
    {
        detectorTrigger = GetComponent<SphereCollider>();
        detectorTrigger.radius = detectionRange;
    }

    public void SetDetectionRange(float range)
    {
        detectionRange = range;
        detectorTrigger.radius = range;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Sound"))
        {
            // get the navmesh path from detector to sound bubble source
            SoundBubble bubble = other.GetComponent<SoundBubble>();
            Vector3 bubblePosition = bubble.transform.position;
            float soundStrength = bubble.GetBubbleStrength();
            float detectionStrength = soundStrength + detectionRange;

            NavMeshPath path = new();
            if (Physics.Raycast(bubblePosition, Vector3.down, out RaycastHit bubbleGroundHit))
            {
                bubblePosition = bubbleGroundHit.point;
            }
            if (NavMesh.CalculatePath(transform.position, bubblePosition, NavMesh.AllAreas, path) || path.status == NavMeshPathStatus.PathPartial)
            {
                // Get point on path that is at most detectionStrength away from the detector
                Vector3 possibleSource = transform.position;
                for (int i = 0; i < path.corners.Length; i++)
                {
                    Debug.DrawLine(possibleSource, path.corners[i], Color.red, 10f);
                    if (Vector3.Distance(possibleSource, path.corners[i]) < detectionStrength)
                    {
                        detectionStrength -= Vector3.Distance(possibleSource, path.corners[i]);
                        possibleSource = path.corners[i];
                    }
                    else
                    {
                        possibleSource += (path.corners[i] - possibleSource).normalized * detectionStrength;
                        break;
                    }
                }

                if (possibleSource != transform.position)
                {
                    Debug.DrawLine(transform.position, possibleSource, Color.green, 10f);
                    detectionEvent.Invoke(new DetectionEvent(bubble.GetInstanceID(), possibleSource, soundStrength));
                }
            }
            else Debug.Log(path.status);
        }
    }
}
