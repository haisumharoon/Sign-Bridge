using UnityEngine;
using UnityEngine.UI;

public class VoiceSignUIController : MonoBehaviour
{
    [Header("Core Controllers")]
    [SerializeField] private VoiceInputController voiceInputController;
    [SerializeField] private SentenceScoringService scoringService;
    [SerializeField] private SignAnimationController signAnimationController;

    [Header("UI References")]
    [SerializeField] private Text titleText;
    [SerializeField] private Text transcriptionText;
    [SerializeField] private Text scoreLabelText;
    [SerializeField] private Text statusText;
    [SerializeField] private Text reasonText;
    [SerializeField] private Button voiceButton;
    [SerializeField] private Button nextDemoButton;
    [SerializeField] private Slider scoreSlider;

    private void Awake()
    {
        AutoWireIfNeeded();
    }

    private void OnEnable()
    {
        AutoWireIfNeeded();
    }

    private void Start()
    {
        ResolveUiReferencesIfMissing();
        SetStatus("Ready");
        SetTranscript("Tap the mic button and speak.");
        ApplyScore(SentenceScoreResult.Create(0, "Awaiting transcript."));

        if (!Application.isPlaying)
        {
            return;
        }

        voiceButton.onClick.AddListener(OnVoicePressed);
        if (nextDemoButton != null)
        {
            nextDemoButton.onClick.AddListener(voiceInputController.UseNextDemoPhrase);
        }

        voiceInputController.ListeningStateChanged += OnListeningStateChanged;
        voiceInputController.PartialTranscriptReceived += OnPartialTranscript;
        voiceInputController.FinalTranscriptReceived += OnFinalTranscript;
        voiceInputController.SpeechErrorReceived += OnSpeechError;

        UpdateVoiceButtonText();
    }

    private void OnDestroy()
    {
        if (voiceInputController != null)
        {
            voiceInputController.ListeningStateChanged -= OnListeningStateChanged;
            voiceInputController.PartialTranscriptReceived -= OnPartialTranscript;
            voiceInputController.FinalTranscriptReceived -= OnFinalTranscript;
            voiceInputController.SpeechErrorReceived -= OnSpeechError;
        }
    }

    private void OnVoicePressed()
    {
        if (voiceInputController == null) return;
        voiceInputController.ToggleListening();
    }

    private void OnListeningStateChanged(bool isListening)
    {
        SetStatus(isListening ? "Listening..." : "Ready");
        UpdateVoiceButtonText();
    }

    private void OnPartialTranscript(string text)
    {
        SetTranscript(text);
    }

    private void OnFinalTranscript(string text)
    {
        SetTranscript(text);
        SetStatus("Scoring sentence...");
        signAnimationController.PlayForTranscript(text);
        scoringService.ScoreSentence(text, result =>
        {
            ApplyScore(result);
            SetStatus("Done");
        });
    }

    private void OnSpeechError(string error)
    {
        SetStatus("Voice error");
        reasonText.text = error;
    }

    private void ApplyScore(SentenceScoreResult result)
    {
        scoreSlider.value = result.score / 100f;
        scoreLabelText.text = $"{result.label} ({result.score})";
        reasonText.text = result.reason;
    }

    private void SetTranscript(string text)
    {
        transcriptionText.text = string.IsNullOrWhiteSpace(text) ? "-" : text;
    }

    private void SetStatus(string text)
    {
        statusText.text = text;
    }

    private void UpdateVoiceButtonText()
    {
        Text buttonText = voiceButton.GetComponentInChildren<Text>();
        if (buttonText != null)
        {
            buttonText.text = voiceInputController.IsListening ? "Stop Listening" : "Start Voice";
        }
    }

    private void AutoWireIfNeeded()
    {
        if (voiceInputController == null) voiceInputController = GetComponent<VoiceInputController>();
        if (scoringService == null) scoringService = GetComponent<SentenceScoringService>();
        if (signAnimationController == null) signAnimationController = GetComponent<SignAnimationController>();

        bool missingUi = titleText == null || transcriptionText == null || scoreSlider == null || voiceButton == null;
        if (missingUi)
        {
            BuildSimpleUI();
        }
    }

    public void EnsureUiScaffold()
    {
        Canvas chosen = FindObjectOfType<Canvas>();
        if (chosen == null || chosen.transform.Find("VoiceSignPanel") == null)
        {
            BuildSimpleUI();
        }
    }

    public void BuildSimpleUI()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            canvas = BuildCanvas();
        }
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Transform existingPanel = canvas.transform.Find("VoiceSignPanel");
        Image panel = existingPanel == null
            ? CreatePanel(canvas.transform, "VoiceSignPanel", new Vector2(0.5f, 0.53f), new Vector2(0.86f, 0.58f), new Color(0.07f, 0.09f, 0.16f, 0.92f))
            : existingPanel.GetComponent<Image>();
        titleText = CreateText(panel.transform, "Voice to Sign Bridge", font, 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.9f));
        transcriptionText = CreateText(panel.transform, "Transcript appears here", font, 19, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.67f));
        scoreLabelText = CreateText(panel.transform, "Score", font, 17, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.51f));
        statusText = CreateText(panel.transform, "Ready", font, 15, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.18f));
        reasonText = CreateText(panel.transform, "Reason", font, 14, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.1f));

        scoreSlider = CreateSlider(panel.transform, new Vector2(0.15f, 0.39f), new Vector2(0.85f, 0.45f));
        voiceButton = CreateButton(panel.transform, "Start Voice", font, new Vector2(0.5f, 0.28f), new Vector2(220, 52), new Color(0.15f, 0.56f, 0.95f));
        nextDemoButton = CreateButton(panel.transform, "Next Demo Phrase", font, new Vector2(0.5f, 0.02f), new Vector2(220, 42), new Color(0.27f, 0.28f, 0.39f));
        ResolveUiReferencesIfMissing();
    }

    private static Canvas BuildCanvas()
    {
        GameObject canvasObject = new GameObject("VoiceSignCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        return canvas;
    }

    private static Image CreatePanel(Transform parent, string panelName, Vector2 anchor, Vector2 sizeAnchor, Color color)
    {
        GameObject go = new GameObject(panelName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor - sizeAnchor * 0.5f;
        rt.anchorMax = anchor + sizeAnchor * 0.5f;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Text CreateText(Transform parent, string value, Font font, int size, TextAnchor align, Vector2 anchor)
    {
        string objectName = value.Replace(" ", "") + "Text";
        Transform existing = parent.Find(objectName);
        GameObject go = existing == null ? new GameObject(objectName, typeof(RectTransform), typeof(Text)) : existing.gameObject;
        if (existing == null) go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(780, 120);
        Text text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = align;
        text.color = Color.white;
        text.text = value;
        return text;
    }

    private static Button CreateButton(Transform parent, string title, Font font, Vector2 anchor, Vector2 size, Color color)
    {
        string objectName = title.Replace(" ", "") + "Button";
        Transform existing = parent.Find(objectName);
        GameObject go = existing == null ? new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button)) : existing.gameObject;
        if (existing == null) go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.sizeDelta = size;
        Image image = go.GetComponent<Image>();
        image.color = color;
        Button button = go.GetComponent<Button>();

        Transform existingLabel = go.transform.Find("Label");
        GameObject textObject = existingLabel == null ? new GameObject("Label", typeof(RectTransform), typeof(Text)) : existingLabel.gameObject;
        if (existingLabel == null) textObject.transform.SetParent(go.transform, false);
        RectTransform textRt = textObject.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        Text label = textObject.GetComponent<Text>();
        label.font = font;
        label.fontSize = 18;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = title;
        return button;
    }

    private static Slider CreateSlider(Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        Transform existing = parent.Find("ScoreSlider");
        GameObject sliderGo = existing == null ? new GameObject("ScoreSlider", typeof(RectTransform), typeof(Slider)) : existing.gameObject;
        if (existing == null) sliderGo.transform.SetParent(parent, false);
        RectTransform rt = sliderGo.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        GameObject background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(sliderGo.transform, false);
        RectTransform bgRt = background.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        Image bgImage = background.GetComponent<Image>();
        bgImage.color = new Color(1f, 1f, 1f, 0.2f);

        GameObject fillArea = new GameObject("FillArea", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        RectTransform fillAreaRt = fillArea.GetComponent<RectTransform>();
        fillAreaRt.anchorMin = Vector2.zero;
        fillAreaRt.anchorMax = Vector2.one;
        fillAreaRt.offsetMin = new Vector2(5, 5);
        fillAreaRt.offsetMax = new Vector2(-5, -5);

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        Image fillImage = fill.GetComponent<Image>();
        fillImage.color = new Color(0.23f, 0.78f, 0.39f, 1f);

        Slider slider = sliderGo.GetComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0f;
        slider.fillRect = fillRt;
        slider.targetGraphic = fillImage;
        return slider;
    }

    private void ResolveUiReferencesIfMissing()
    {
        if (voiceButton == null)
        {
            GameObject voice = GameObject.Find("StartVoiceButton");
            if (voice != null) voiceButton = voice.GetComponent<Button>();
        }

        if (nextDemoButton == null)
        {
            GameObject next = GameObject.Find("NextDemoPhraseButton");
            if (next != null) nextDemoButton = next.GetComponent<Button>();
        }

        if (scoreSlider == null)
        {
            GameObject slider = GameObject.Find("ScoreSlider");
            if (slider != null) scoreSlider = slider.GetComponent<Slider>();
        }
    }
}
