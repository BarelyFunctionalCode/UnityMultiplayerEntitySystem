using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIPlayerCard : MonoBehaviour
{
    [SerializeField] Image playerImage;
    [SerializeField] Image healthBar;
    [SerializeField] TextMeshProUGUI usernameText;
    [SerializeField] TextMeshProUGUI moneyText;

    private ulong playerID;

    public void SetPlayerInformation(Texture2D newImage, string newUsername, uint money, ulong newPlayerID)
    {
        if (playerImage == null || usernameText == null || healthBar == null) return;

        if (newImage)
        {
            playerImage.sprite = Sprite.Create(newImage, new Rect(playerImage.sprite.rect.position, new Vector2(newImage.width, newImage.height)), playerImage.sprite.pivot);
        }
        usernameText.text = newUsername;

        moneyText.text = money.ToString();

        playerID = newPlayerID;
    }

    public void OnHealthChange(float max, float current)
    {
        if (healthBar == null) return;

        healthBar.fillAmount = current / max;
    }

    public void OnMoneyChange(uint newMoney)
    {
        if (moneyText == null) return;

        moneyText.text = newMoney.ToString();
    }
}
