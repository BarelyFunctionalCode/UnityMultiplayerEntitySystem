using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using Utilities;

public class UIDialogueBox : MonoBehaviour
{
    [SerializeField] private GameObject dialogueBox;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField, Range(1, 100)] private float dialoguePrintSpeed = 5;
    [SerializeField, Range(1, 5)] private float dialogueBoxHideTime = 3f;

    [Header("Debug")]
    [SerializeField] private bool debugDialogueBox = false;
    [SerializeField] private string debugMessage = "This is a test string to test the capabilities of the dialogue box";

    // Const for fastest and slowest print times
    private const float PRINT_TIME_MAX = 0.05f;
    private const float PRINT_TIME_MIN = 0.5f;

    private RectTransform parentRect;
    private RectTransform dialogueRect;
    private Transform speaker;
    private CountdownTimer dialogueBoxHideTimer;
    private CountdownTimer printDelayTimer;
    private float currentPrintSpeed;
    private string wholeMessage;
    private int currentSubstringLength = 0;
    private bool autoPrintMessage = false;

    private void Start()
    {
        dialogueRect = GetComponent<RectTransform>();
        parentRect = transform.parent.GetComponent<RectTransform>();

        currentPrintSpeed = dialoguePrintSpeed;

        // Timer for hiding our dialogue box
        dialogueBoxHideTimer = new CountdownTimer(dialogueBoxHideTime);
        dialogueBoxHideTimer.OnTimerStop += () => { if (dialogueBox) dialogueBox.SetActive(false); };

        if (debugDialogueBox) ConfigureDialogue(debugMessage, null, true);
    }

    private void Update()
    {
        if (dialogueRect && speaker && dialogueBox.activeSelf)
        {
            // Convert speaker pos from world position to screen point
            Vector3 screenPos = Camera.main.WorldToScreenPoint(speaker.position);
            Vector2 scaleRatio = new Vector2(parentRect.rect.width / Screen.width, parentRect.rect.height / Screen.height);

            // Define half width and half height for math later
            float halfRectWidth = (dialogueRect.rect.width / scaleRatio.x) / 2f;
            float halfRectHeight = (dialogueRect.rect.height / scaleRatio.y) / 2f;
            float xpos = screenPos.x;
            float ypos = screenPos.y;

            // Clamp position in repsect to rect size so it stays on screen
            xpos = Mathf.Clamp(xpos, 0 + halfRectWidth, Screen.width - halfRectWidth);
            ypos = Mathf.Clamp(ypos, 0 + halfRectHeight, Screen.height - halfRectHeight);

            screenPos = new Vector3(xpos, ypos, screenPos.z);

            // Set position
            dialogueRect.position = screenPos;
        }

        // Tick our timers
        dialogueBoxHideTimer.Tick(Time.deltaTime);
        printDelayTimer.Tick(Time.deltaTime);

        // Auto prints message so AI can show dialogue while in patrol
        if (autoPrintMessage)
        {
            if (ShowDialogue())
            {
                autoPrintMessage = false;
                dialogueBoxHideTimer.Start();
            }
        }
    }

    // Configures the dialogue and resets the box. Alternatively can set a new print speed for the dialogue
    public void ConfigureDialogue(string newMessage, Transform newSpeaker, bool autoPrintMessage = false, float printSpeed = 0)
    {
        if (newMessage.Length <= 0 || !dialogueBox || !dialogueText) return;

        // Configure message variables
        if (printSpeed > 0) currentPrintSpeed = Mathf.Clamp(printSpeed, 1, 100);
        else currentPrintSpeed = dialoguePrintSpeed;

        this.autoPrintMessage = autoPrintMessage;
        wholeMessage = newMessage;
        currentSubstringLength = 0;
        speaker = newSpeaker;

        // Show message box
        dialogueBox.SetActive(true);

        // Configure print delay timer
        printDelayTimer = new CountdownTimer(Mathf.Lerp(PRINT_TIME_MIN, PRINT_TIME_MAX, currentPrintSpeed / 100f));
    }

    // This dialogue is only going to be trigger in the Update function of another entity,
    // so we can have the dialogue read out further with each subsequent ShowDialogue call
    public bool ShowDialogue()
    {
        // Return true if we are done printing the message
        if (currentSubstringLength > wholeMessage.Length) return true;

        if (printDelayTimer.IsFinished)
        {
            dialogueText.text = wholeMessage.Substring(0, currentSubstringLength);

            currentSubstringLength++;

            printDelayTimer.Start();
        }

        return false;
    }
}
