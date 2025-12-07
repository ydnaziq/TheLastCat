using UnityEngine;
using System.Collections;
using UnityEngine.InputSystem;
using TMPro;
using DG.Tweening;
using UnityEngine.UI;

public class CutsceneManager : MonoBehaviour
{
    public static CutsceneManager Instance;

    private InputAction jumpAction;
    private CameraManager cameraManager;

    private RectTransform textPanel;
    private RectTransform imagePanel;
    private RectTransform speakerTextPanel;

    private TextMeshProUGUI dialogueText;
    private TextMeshProUGUI speakerText;
    private Image speakerPortrait;

    private Vector3 textPanelPos = new Vector3(-300, 75, 0);
    private Vector3 imagePanelPos = new Vector3(100, 75, 0);
    private Vector3 speakerTextPanelPos = new Vector3(100, 165, 0);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        cameraManager = CameraManager.instance;

        jumpAction = InputSystem.actions?.FindAction("Jump");

        textPanel = transform.Find("Text Panel") as RectTransform;
        imagePanel = transform.Find("Image Panel") as RectTransform;
        speakerTextPanel = transform.Find("Speaker Text Panel") as RectTransform;

        dialogueText = transform.Find("Text").GetComponent<TextMeshProUGUI>();
        speakerText = transform.Find("Speaker Text").GetComponent<TextMeshProUGUI>();

        speakerPortrait = transform.Find("Speaker Portrait").GetComponent<Image>();

        if (textPanel != null)
            textPanel.anchoredPosition = new Vector2(textPanelPos.x, -110f);

        if (imagePanel != null)
            imagePanel.anchoredPosition = new Vector2(imagePanelPos.x, -110f);

        if (speakerTextPanel != null)
            speakerTextPanel.anchoredPosition = new Vector2(speakerTextPanelPos.x, -50f);

        if (dialogueText != null)
            dialogueText.text = "";

        if (speakerText != null)
            speakerText.text = "";
    }

    void Update()
    {
        if (jumpAction.triggered)
        {
            StartCoroutine(PlayCutscene("Cutscenes/Example"));
        }
    }

    public IEnumerator LerpPanelsIn(float duration)
    {
        if (textPanel != null)
        {
            textPanel.DOAnchorPos(textPanelPos, duration)
                     .SetEase(Ease.OutElastic);
        }

        if (imagePanel != null)
        {
            imagePanel.DOAnchorPos(imagePanelPos, duration)
                      .SetEase(Ease.OutElastic)
                      .SetDelay(0.05f);
        }

        if (speakerTextPanel != null)
        {
            speakerTextPanel.DOAnchorPos(speakerTextPanelPos, duration)
                            .SetEase(Ease.OutElastic)
                            .SetDelay(0.1f);
        }

        yield return new WaitForSeconds(duration + 0.2f);
    }

    public IEnumerator LerpPanelsOut(float duration)
    {
        if (textPanel != null)
        {
            textPanel.DOAnchorPos(new Vector2(textPanelPos.x, -110f), duration)
                     .SetEase(Ease.InBack);
        }

        if (imagePanel != null)
        {
            imagePanel.DOAnchorPos(new Vector2(imagePanelPos.x, -110f), duration)
                      .SetEase(Ease.InBack)
                      .SetDelay(0.05f);
        }

        if (speakerTextPanel != null)
        {
            speakerTextPanel.DOAnchorPos(new Vector2(speakerTextPanelPos.x, -50f), duration)
                            .SetEase(Ease.InBack)
                            .SetDelay(0.1f);
        }

        yield return new WaitForSeconds(duration + 0.2f);
    }

    Cutscene LoadCutscene(string path)
    {
        TextAsset asset = Resources.Load<TextAsset>(path);
        if (asset == null)
        {
            Debug.LogError("Could not load JSON at: " + path);
            return null;
        }

        Cutscene cutscene = JsonUtility.FromJson<Cutscene>(asset.text);

        return cutscene;
    }

    public IEnumerator PlayCutscene(string path)
    {
        Debug.Log("Starting cutscene: " + path);
        yield return StartCoroutine(LerpPanelsIn(1f));
        Cutscene cutscene = LoadCutscene(path);
        foreach (var step in cutscene.steps)
        {
            Debug.Log("Step type: " + step.type);

            if (step.type == "dialogue")
            {
                Debug.Log("Speaker: " + step.speaker);
                Debug.Log("Text: " + step.text);
                Debug.Log("Portrait: " + step.portrait);

                dialogueText.text = step.text;
                speakerText.text = step.speaker;
                LoadPortrait(step.portrait);

                yield return new WaitForSeconds(step.seconds);

                dialogueText.text = "";
                speakerText.text = "";
            }
            else if (step.type == "action")
            {
                Debug.Log("Animation: " + step.animation);
                Debug.Log("Duration: " + step.duration);

                yield return new WaitForSeconds(step.seconds);
            }
            else if (step.type == "wait")
            {
                yield return new WaitForSeconds(step.seconds);
            }
            else if (step.type == "camera")
            {
                cameraManager.ZoomCamera(step.zoom, step.duration, true);
                PanDirection panXDirection = step.panY > 0 ? PanDirection.Right : PanDirection.Left;
                PanDirection panYDirection = step.panY > 0 ? PanDirection.Up : PanDirection.Down;
                cameraManager.PanCameraOnContact(Mathf.Abs(step.panX), step.duration, panXDirection, false);
                cameraManager.PanCameraOnContact(Mathf.Abs(step.panY), step.duration, panYDirection, false);

                yield return new WaitForSeconds(step.seconds);
            }
        }

        yield return StartCoroutine(LerpPanelsOut(1f));
    }

    public void LoadPortrait(string portraitName)
    {
        Sprite portraitSprite = Resources.Load<Sprite>("Portraits/" + portraitName);
        if (portraitSprite != null)
        {
            speakerPortrait.sprite = portraitSprite;
        }
        else
        {
            Debug.LogWarning("Portrait not found: " + portraitName);
        }
    }
}
