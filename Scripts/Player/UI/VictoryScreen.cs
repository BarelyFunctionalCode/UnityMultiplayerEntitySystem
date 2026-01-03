using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Steamworks;
using TMPro;
using UnityEngine.UI;

public class VictoryScreen : MonoBehaviour
{
    [Header ("Initial Page Elements")]
    [SerializeField] private Image initialPage;
    [SerializeField] private TextMeshProUGUI timeElapsed;
    [SerializeField] private TextMeshProUGUI vaultLooted;
    [SerializeField] private TextMeshProUGUI lootCollected;
    [SerializeField] private TextMeshProUGUI xpEarned;

    [Header ("Team Summary Page Elements")]
    [SerializeField] private Image teamSummaryPage;
    [SerializeField] private Image teamSummaryContainer;
    [SerializeField] private GameObject teamSummaryCardPrefab;

    [Header ("MVP Page Elements")]
    [SerializeField] private Image mvpPage;

    [Header("Animation Stuff")]
    [SerializeField] float pageTearTime = 1f;

    private Image[] pages;
    private bool isAnimating;
    private Vector3 newPagePosition;
    private int currentPage = 0;
    private float curAnimationTime = 0f;
    private PlayerControls controls;

    // Start is called before the first frame update
    void Start()
    {
        controls = new PlayerControls();
        pages = new Image[] { initialPage, teamSummaryPage, mvpPage };
        currentPage = 0;

        PopulateInitialPage();
        PopulateTeamSummary();
        PopulateMvp();

        controls.UI.Enable();
        controls.UI.Select.performed += ctx => RemovePage();
    }

    private void OnDestroy()
    {
        curAnimationTime = 0;
        isAnimating = false;
        controls.UI.Select.performed -= ctx => RemovePage();
        controls.UI.Disable();
    }

    // Testing functionality
    IEnumerator TearPages()
    {
        foreach (Image page in pages)
        {
            yield return new WaitForSeconds(pageTearTime + 1f);

            RemovePage();
        }
    }

    // Update is called once per frame
    void Update()
    {
        // Lerp page away from clipboard
        if (curAnimationTime > 0 && isAnimating)
        {
            curAnimationTime -= Time.deltaTime;

            pages[currentPage].transform.position = Vector3.Lerp(newPagePosition, pages[currentPage].transform.position, curAnimationTime / pageTearTime);
        }
        // Disabled current page and move to next page
        else if (curAnimationTime <= 0 && isAnimating)
        {
            isAnimating = false;
            curAnimationTime = 0;
            pages[currentPage].gameObject.SetActive(false);
            currentPage++;
        }
    }

    public void RemovePage()
    {
        if (isAnimating) return;
        if (currentPage >= pages.Length) {
            Destroy(gameObject);
            return;
        }

        // Enable the next page
        if (currentPage < pages.Length - 1) pages[currentPage + 1].gameObject.SetActive(true);

        // Calculate new page position and start animation
        newPagePosition = pages[currentPage].transform.position + new Vector3(100f, -1000f, 0);
        curAnimationTime = pageTearTime;
        isAnimating = true;
    }

    private void PopulateInitialPage()
    {
        // Time elapsed
        timeElapsed.text = LevelManager.Instance?.GetTimeElapsed().ToString("F0");

        // Vault looted
        vaultLooted.color = Color.red;
        vaultLooted.text = "No";

        // Loot Collected
        lootCollected.color = Color.green;

        // TODO: Make this value accessible to clients
        lootCollected.text = $"{LevelManager.Instance?.GetTotalLootCollectedValue()}";

        // XP Earned
        xpEarned.text = "9999";
    }

    private void PopulateTeamSummary()
    {
        foreach (Player player in PlayerManager.Instance.GetPlayerList())
        {

            string username = GameManager.Instance.usingSteam ? new Friend(player.PlayerSteamId).Name : $"poopyhead_{player.OwnerClientId}";

            GameObject newCard = Instantiate(teamSummaryCardPrefab, teamSummaryContainer.rectTransform);
            
            if (newCard.TryGetComponent(out TextMeshProUGUI textMeshPro))
            {
                textMeshPro.text = $"{username} | {LevelManager.Instance.GetLootValue(player.OwnerClientId)} | 0 | 0";
            }
        }
    }

    private void PopulateMvp()
    {
        // No
    }
}
