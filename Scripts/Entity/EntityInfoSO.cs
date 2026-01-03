using UnityEngine;

[CreateAssetMenu(fileName = "EntityInfo", menuName = "Scriptable Objects/EntityInfo")]
public class EntityInfoSO : ScriptableObject
{
    // Scriptable object for entity information
    public string itemName;
    public string description;
    public Sprite sprite;
    public GameObject prefab;

    [Header("Store Info")]
    public bool isInStore = true;
    public int price;
    public int level;

    [Header("Loot Info")]
    public bool isLoot = false;
    public LootCategory lootCategory = LootCategory.Default;

    [Header("Small Loot Info")]
    public int smallLootValue = 10;

    [Header("Large Loot Info")]
    public bool isLargeLoot = false;
    public int largeLootValue = 50;
    public float mass = 10.0f;
    public float drag = 0.5f;
    public float angularDrag = 0.05f;
    public bool isOneHanded = true;
    public Vector3 targetHoldRotation = Vector3.zero;
}
