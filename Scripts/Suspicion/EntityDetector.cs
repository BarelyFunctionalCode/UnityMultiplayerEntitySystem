using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;


public class EntityDetector : MonoBehaviour
{
    private BoxCollider col;

    private float detectionRange = 20f;
    private float detectionFOV = 90f;

    public UnityEvent<DetectionEvent> detectionEvent = new();


    void Awake()
    {
        col = GetComponent<BoxCollider>();
        SetDetectionRange(detectionRange);
    }

    // Sets the box collider so that it can at least detect entities at `range` distance in a 90 degree FOV
    public void SetDetectionRange(float range)
    {
        detectionRange = range;
        col.size = new Vector3(range * 2f, range / 2f, range);
        col.center = new Vector3(0f, 0f, range / 2f);
    }

    public void SetDetectionFOV(float fov) => detectionFOV = fov;

    private void OnTriggerStay(Collider other)
    {
        if (other.TryGetComponent(out Entity entity) && entity != null)
        {
            // TODO: Figure out how the hell the zoning works
            // If the entity is not a player and is not far away from its initial position, then it isn't suspicious
            if (!entity.Stats || (!other.CompareTag("Player") && (Vector3.Distance(entity.Stats.InitialPosition, other.transform.position) < 5 || entity.isPickedUp)) ||
                (entity is Player && entity.AreaClassification == Zone.Type.SAFE)) return;
            

            Vector3 direction = (other.transform.position - transform.position).normalized;
            if (Vector3.Angle(transform.forward, direction) < detectionFOV / 2f)
            {
                float distance = Vector3.Distance(transform.position, other.transform.position);
                if (distance < detectionRange)
                {
                    // Cast Ray to see if the entity is visible
                    if (Physics.Raycast(transform.position, direction, out RaycastHit hit, detectionRange))
                    {
                        // Wasn't detecting player entity since the entity script isn't on the object being hit by the raycast
                        // so I added an extra condition instead of just replacing it 
                        Entity hitEntity = hit.transform.GetComponentInParent<Entity>();

                        if ((hitEntity != null && hitEntity == entity) ||
                            (hit.transform.TryGetComponent(out hitEntity) && hitEntity == entity))
                        {
                            detectionEvent.Invoke(new DetectionEvent(entity.GetInstanceID(), other.transform, Mathf.Sqrt(detectionRange - distance)));
                        }
                    }
                }
            }
        }
    }
}
