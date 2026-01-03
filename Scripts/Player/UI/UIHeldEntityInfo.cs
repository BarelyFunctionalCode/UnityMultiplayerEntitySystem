using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIHeldEntityInfo : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI entityName;
    [SerializeField] private TextMeshProUGUI entityDetailText;
    [SerializeField] private Image entityImage;

    public void OnNewEntity(Entity newEntity)
    {
        if (entityImage == null || entityDetailText == null || entityName == null) return;

        if (newEntity == null)
        {
            entityName.text = "";
            entityDetailText.text = "";
            entityImage.sprite = null;
            entityImage.color = Color.clear;
        }
        else
        {
            if (newEntity.UIImage == null) return;

            entityName.text = newEntity.name;
            entityImage.sprite = Sprite.Create(newEntity.UIImage, new Rect(0, 0, newEntity.UIImage.width, newEntity.UIImage.height), new Vector2(0.5f, 0.5f));
            entityDetailText.text = "";
            entityImage.color = Color.white;

            if (newEntity as Loot)
            {
                Loot newLoot = newEntity as Loot;

                entityDetailText.text = $"${newLoot.Value}";
            }
            else if (newEntity as Gadget)
            {
                Gadget newGadget = newEntity as Gadget;
                entityDetailText.text = $"{newGadget.UsesRemaining} uses";
            }
        }
    }
}
