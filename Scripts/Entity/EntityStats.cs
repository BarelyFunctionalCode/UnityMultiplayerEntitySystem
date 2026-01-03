using NUnit.Framework.Constraints;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class EntityStats : HealthStats
{   
    [Header("Physics")]
    [SerializeField] protected bool canBePickedUp = false;
    [SerializeField] protected bool canBeInteractedWith = false;
    [SerializeField] protected bool isBreakable = false;
    [SerializeField] protected GameObject debrisPrefab;

    [Header("Misc")]
    [SerializeField] protected Entity entity;
    private Vector3 initialPosition;

    public bool CanBeInteractedWith { get { return canBeInteractedWith; } }
    public bool CanBePickedUp { get { return canBePickedUp; } }
    public bool IsBreakable { get { return isBreakable; } }
    public GameObject DebrisPrefab { get { return debrisPrefab; } }
    public Vector3 InitialPosition { get { return initialPosition; } set { initialPosition = value; } }
    public Entity Entity { get { return entity; } }

    protected override void Awake()
    {
        base.Awake();

        entity = GetComponent<Entity>();
        initialPosition = transform.position;
    }
}
