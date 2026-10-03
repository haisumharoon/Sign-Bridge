using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class SimpleVoiceScreenController : MonoBehaviour
{
    private enum VoiceUiState
    {
        Loading,
        Ready,
        Listening,
        Processing
    }

    [SerializeField] private string[] demoPhrases =
    {
        "hello how are you",
        "please help me",
        "where are you going",
        "thank you very much"
    };

    private SentenceScoringService scoringService;
    private VoskSpeechManager      voskManager;
    private SignLanguageController  signController;

    private Button   recordButton;
    private Text     recordLabel;
    private Text     transcriptText;
    private Text     scoreText;
    private Image    scoreFill;
    private RawImage cameraPreviewImage;
    private RawImage handPreviewImage;
    private Text     statusText;
    private Image    _uiOverlayDim;
    private Image    _uiTopStrip;
    private Image    _uiBubble;

    private WebCamTexture webCamTexture;
    private int           demoIndex;
    private bool          isListening;
    private bool          pendingRecordRequest;
    private VoiceUiState  uiState = VoiceUiState.Loading;
    private bool          isArCameraBackgroundActive;
    private bool          allowWebcamPreview;
    private RectTransform voicePanelRoot;
    private SentenceScoreResult _lastScoreResult = SentenceScoreResult.Create(0, "Ready");

    // ─── AR Placement Mode ────────────────────────────────────────────────
    private ARHandPlacementController _arPlacementController;
    private bool   _isArPlacementMode;
    private Text   _arGuidanceText;
    private Button _repositionButton;

    public RawImage HandPreviewImage => handPreviewImage;
    public RectTransform VoicePanelRoot => voicePanelRoot;

    // ─────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        scoringService = GetComponent<SentenceScoringService>()
                      ?? gameObject.AddComponent<SentenceScoringService>();

        signController = GetComponent<SignLanguageController>()
                      ?? gameObject.AddComponent<SignLanguageController>();

        EnsureUi();
        isArCameraBackgroundActive = FindObjectOfType<ARCameraBackground>() != null;
        allowWebcamPreview = Application.platform != RuntimePlatform.Android;
    }

    private void Start()
    {
        EnableImmersiveFullscreen();

        SignBridgeAppearance.Changed += OnSignBridgeAppearanceChanged;
        SignBridgeAppearance.LoadFromPrefs();

        if (recordButton != null)
        {
            recordButton.onClick.AddListener(OnRecordPressed);
            recordButton.interactable = true;
        }

        if (signController != null && handPreviewImage != null)
            signController.Initialize(handPreviewImage);

        SetListeningState(false);
        SetUiState(VoiceUiState.Loading);
        ApplyTranscript("Loading speech engine…");
        ApplyScore(SentenceScoreResult.Create(0, "Ready"));

        // Re-evaluate AR camera status at Start to avoid Awake-order race with runtime bootstrap.
        isArCameraBackgroundActive = FindObjectOfType<ARCameraBackground>() != null;
        bool shouldUseArCamera = Application.platform == RuntimePlatform.Android || isArCameraBackgroundActive;
        allowWebcamPreview = !shouldUseArCamera;

        if (allowWebcamPreview)
        {
            StartCoroutine(StartCameraPreview());
        }
        else if (cameraPreviewImage != null)
        {
            cameraPreviewImage.color = new Color(0f, 0f, 0f, 0f);
            cameraPreviewImage.texture = null;
        }

        ApplyVoiceAppearance();

        SetupVosk();
    }

    private void Update()
    {
        // Keep camera transform synced; some Android devices report late rotation/size.
        if (allowWebcamPreview
            && webCamTexture != null
            && webCamTexture.isPlaying
            && webCamTexture.width > 100
            && webCamTexture.didUpdateThisFrame)
        {
            ApplyCameraTransform();
        }
    }

    private void OnDestroy()
    {
        SignBridgeAppearance.Changed -= OnSignBridgeAppearanceChanged;

        if (recordButton != null)
            recordButton.onClick.RemoveListener(OnRecordPressed);

        if (webCamTexture != null)
        {
            if (webCamTexture.isPlaying) webCamTexture.Stop();
            Destroy(webCamTexture);
            webCamTexture = null;
        }
    }

    // ─── VOSK SETUP ───────────────────────────────────────────────────────
    private void SetupVosk()
    {
        voskManager = GetComponent<VoskSpeechManager>()
                   ?? gameObject.AddComponent<VoskSpeechManager>();

        voskManager.OnReady += () =>
        {
            SetUiState(VoiceUiState.Ready);
            SetStatus("Ready – tap ● to speak");
            ApplyTranscript("Tap ● to start");
            if (recordButton != null) recordButton.interactable = true;

            if (pendingRecordRequest)
            {
                pendingRecordRequest = false;
                TryStartRecordingFromTap();
            }
        };

        voskManager.OnResult += OnFinalResult;

        voskManager.OnPartial += partial =>
            ApplyTranscript(partial + "…");

        voskManager.OnError += err =>
        {
            isListening = false;
            SetListeningState(false);
            SetUiState(VoiceUiState.Ready);
            if (recordButton != null) recordButton.interactable = true;
            string friendly = string.IsNullOrWhiteSpace(err) ? "Try again" : err;
            ApplyTranscript(friendly);
            SetStatus(friendly);
        };

        StartCoroutine(voskManager.Initialize());
    }

    // ─── RECORD BUTTON ────────────────────────────────────────────────────
    private void OnRecordPressed()
    {
        if (Application.platform == RuntimePlatform.Android)
        {
            TryStartRecordingFromTap();
        }
        else
        {
            // Editor: cycle demo phrases for testing UI without a device
            string phrase = demoPhrases[demoIndex % demoPhrases.Length];
            demoIndex++;
            OnFinalResult(phrase);
        }
    }

    private void TryStartRecordingFromTap()
    {
        if (voskManager == null || !voskManager.IsReady)
        {
            pendingRecordRequest = true;
            SetStatus("Preparing speech engine…");
            ApplyTranscript("Please wait…");
            return;
        }

        ToggleVosk();
    }

    private void ToggleVosk()
    {
        if (voskManager == null || !voskManager.IsReady)
        {
            SetStatus("Speech engine loading…");
            return;
        }

        if (uiState == VoiceUiState.Processing)
        {
            SetStatus("Processing previous speech…");
            return;
        }

        if (isListening)
        {
            // Stop and transcribe
            isListening = false;
            SetListeningState(false);
            SetUiState(VoiceUiState.Processing);
            if (recordButton != null) recordButton.interactable = false;
            SetStatus("Processing…");
            voskManager.StopAndRecognize();
            return;
        }

#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
            SetStatus("Microphone permission needed…");
            StartCoroutine(WaitForMicPermissionThenRecord());
            return;
        }
#endif
        BeginRecording();
    }

#if UNITY_ANDROID
    private IEnumerator WaitForMicPermissionThenRecord()
    {
        float elapsed = 0f;
        while (!Permission.HasUserAuthorizedPermission(Permission.Microphone) && elapsed < 12f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            SetStatus("Mic permission denied");
            ApplyTranscript("Please allow microphone access");
            yield break;
        }

        yield return new WaitForSeconds(0.2f);
        BeginRecording();
    }
#endif

    private void BeginRecording()
    {
        pendingRecordRequest = false;

        if (voskManager == null || !voskManager.IsReady)
        {
            SetStatus("Speech engine not ready");
            return;
        }

        voskManager.StartListening();
        if (!voskManager.IsListening)
        {
            isListening = false;
            SetListeningState(false);
            SetUiState(VoiceUiState.Ready);
            if (recordButton != null) recordButton.interactable = true;
            ApplyTranscript("Try again");
            SetStatus("Could not start microphone");
            return;
        }

        isListening = true;
        SetListeningState(true);
        SetUiState(VoiceUiState.Listening);
        ApplyTranscript("Listening…");
        SetStatus("Listening – tap ■ to stop");
    }

    // ─── TRANSCRIPT / SCORE CALLBACKS ─────────────────────────────────────
    private void OnFinalResult(string result)
    {
        isListening = false;
        SetListeningState(false);
        SetUiState(VoiceUiState.Processing);
        if (recordButton != null) recordButton.interactable = false;
        SetStatus("Improving transcript…");

        scoringService.RefineTranscript(result, refinedTranscript =>
        {
            string transcriptToUse = string.IsNullOrWhiteSpace(refinedTranscript) ? result : refinedTranscript;
            ApplyTranscript(transcriptToUse);
            SetStatus("Scoring…");

            if (signController != null)
                signController.PlaySignForText(transcriptToUse);

            scoringService.ScoreSentence(transcriptToUse, score =>
            {
                ApplyScore(score);
                SetUiState(VoiceUiState.Ready);
                if (recordButton != null) recordButton.interactable = true;
                SetStatus("Ready – tap ● to speak again");
            });
        });
    }

    // ─── STATE HELPERS ────────────────────────────────────────────────────
    private void SetListeningState(bool listening)
    {
        if (recordLabel != null)
        {
            recordLabel.text = listening ? "■" : "●";
        }

        if (listening)
        {
            if (recordButton != null)
            {
                Color live = new Color(0.95f, 0.22f, 0.22f, 1f);
                recordButton.image.color = live;
                ColorBlock cb = recordButton.colors;
                cb.normalColor = live;
                cb.highlightedColor = new Color(1f, 0.35f, 0.35f, 1f);
                cb.pressedColor = new Color(0.75f, 0.12f, 0.12f, 1f);
                cb.selectedColor = live;
                recordButton.colors = cb;
            }

            if (recordLabel != null)
            {
                recordLabel.color = Color.white;
            }
        }
        else
        {
            SignBridgeAppearance.VoiceUiPalette palette = SignBridgeAppearance.GetVoiceUiPalette();
            if (recordButton != null)
            {
                recordButton.image.color = palette.RecordButton;
                ColorBlock cb = recordButton.colors;
                cb.normalColor = palette.RecordButton;
                cb.highlightedColor = palette.RecordButtonHighlight;
                cb.pressedColor = palette.RecordButtonPressed;
                cb.selectedColor = palette.RecordButton;
                recordButton.colors = cb;
            }

            if (recordLabel != null)
            {
                recordLabel.color = palette.RecordLabel;
            }
        }
    }

    private void ApplyTranscript(string text)
    {
        if (transcriptText != null) transcriptText.text = text;
    }

    private void ApplyScore(SentenceScoreResult score)
    {
        _lastScoreResult = score;

        if (scoreText != null)
        {
            scoreText.text = $"{score.label}  {score.score}/100";
        }

        if (scoreFill != null)
        {
            scoreFill.fillAmount = score.score / 100f;
            SignBridgeAppearance.GetScoreTierColors(out Color high, out Color mid, out Color low);
            scoreFill.color = score.score >= 75 ? high : score.score >= 45 ? mid : low;
        }
    }

    private void SetStatus(string value)
    {
        if (statusText != null) statusText.text = value;
    }

    private void EnableImmersiveFullscreen()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity != null)
                {
                    AndroidJavaObject window = activity.Call<AndroidJavaObject>("getWindow");
                    AndroidJavaObject decorView = window.Call<AndroidJavaObject>("getDecorView");

                    const int SYSTEM_UI_FLAG_FULLSCREEN = 0x00000004;
                    const int SYSTEM_UI_FLAG_HIDE_NAVIGATION = 0x00000002;
                    const int SYSTEM_UI_FLAG_IMMERSIVE_STICKY = 0x00001000;
                    const int SYSTEM_UI_FLAG_LAYOUT_STABLE = 0x00000100;
                    const int SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION = 0x00000200;
                    const int SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN = 0x00000400;

                    int flags =
                        SYSTEM_UI_FLAG_LAYOUT_STABLE |
                        SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION |
                        SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN |
                        SYSTEM_UI_FLAG_HIDE_NAVIGATION |
                        SYSTEM_UI_FLAG_FULLSCREEN |
                        SYSTEM_UI_FLAG_IMMERSIVE_STICKY;

                    decorView.Call("setSystemUiVisibility", flags);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SignBridge] Failed to enable immersive mode: {e.Message}");
        }
#endif
    }

    private void SetUiState(VoiceUiState state)
    {
        uiState = state;
    }

    /// <summary>Used when navigating away from the voice screen so Vosk does not keep recording.</summary>
    public void CancelListeningForNavigation()
    {
        if (voskManager != null && voskManager.IsListening)
        {
            isListening = false;
            SetListeningState(false);
            voskManager.StopAndRecognize();
        }
    }

    /// <summary>
    /// Called when opening the voice panel from Learn/Vocabulary: same layout as Start, mic off, show transcript and hand playback only.
    /// </summary>
    public void ShowPlaybackChrome(string transcriptLine)
    {
        CancelListeningForNavigation();
        isListening = false;
        SetListeningState(false);
        SetUiState(VoiceUiState.Ready);

        if (handPreviewImage != null)
        {
            handPreviewImage.gameObject.SetActive(true);
            handPreviewImage.transform.SetAsLastSibling();
        }

        string line = string.IsNullOrWhiteSpace(transcriptLine) ? "…" : transcriptLine.Trim();
        ApplyTranscript(line);
        SetStatus("Playing sign…");

        if (recordButton != null)
        {
            recordButton.interactable = voskManager != null && voskManager.IsReady;
        }
    }

    // ─── AR PLACEMENT MODE ────────────────────────────────────────────────

    /// <summary>
    /// Called by SignBridgeWebUiHost when the user picks "AR Surface Mode".
    /// Enables world-space playback and waits for the user to place the anchor
    /// on a detected AR plane before allowing voice input.
    /// </summary>
    public void EnterArPlacementMode()
    {
        if (_isArPlacementMode) return;
        _isArPlacementMode = true;

        // Resolve AR placement controller.
        _arPlacementController = FindObjectOfType<ARHandPlacementController>();
        if (_arPlacementController == null)
        {
            Debug.LogWarning("[SignBridge] ARHandPlacementController not found – falling back to overlay mode.");
            _isArPlacementMode = false;
            return;
        }

        // Switch sign controller to world-space mode.
        if (signController != null)
            signController.SetWorldSpacePlayback(true);

        // Hide record button until placement is confirmed.
        if (recordButton != null)
            recordButton.gameObject.SetActive(false);

        // Show AR guidance overlay.
        ShowArGuidance("Scanning for surfaces\u2026");

        StartCoroutine(WaitForArPlacement());
    }

    private IEnumerator WaitForArPlacement()
    {
        // Phase 1 – wait for at least one trackable plane.
        float elapsed = 0f;
        ARPlaneManager planeMgr = FindObjectOfType<ARPlaneManager>();
        if (planeMgr != null) planeMgr.enabled = true;

        while (elapsed < 30f)
        {
            bool hasPlanes = _arPlacementController != null && _arPlacementController.HasTrackedPlanes();
            if (hasPlanes)
            {
                ShowArGuidance("Tap a flat surface to place signs here");
                break;
            }
            ShowArGuidance($"Scanning for surfaces\u2026 ({Mathf.CeilToInt(30f - elapsed)}s)");
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (_arPlacementController == null || !_arPlacementController.HasTrackedPlanes())
        {
            ShowArGuidance("No surface found. Try Voice Mode instead.");
            _isArPlacementMode = false;
            if (signController != null) signController.SetWorldSpacePlayback(false);
            if (recordButton != null) recordButton.gameObject.SetActive(true);
            yield break;
        }

        // Phase 2 – wait for user to tap a plane (ARHandPlacementController handles Input.GetTouch).
        while (_arPlacementController != null && !_arPlacementController.AnchorPlaced)
        {
            yield return null;
        }

        // Anchor confirmed.
        ShowArGuidance(string.Empty);
        if (recordButton != null)
            recordButton.gameObject.SetActive(true);

        if (_repositionButton != null)
            _repositionButton.gameObject.SetActive(true);

        SetStatus("Anchor placed! Tap \u25cf to speak");
        ApplyTranscript("Tap \u25cf to start");
        Debug.Log("[SignBridge] AR anchor placed. Voice input ready.");

        // Show hands immediately at the anchor so user can see the placement.
        signController?.PlaySignForText("hello");
    }

    private void ShowArGuidance(string text)
    {
        if (_arGuidanceText != null)
        {
            _arGuidanceText.gameObject.SetActive(!string.IsNullOrEmpty(text));
            _arGuidanceText.text = text;
        }

        if (statusText != null)
            statusText.text = string.IsNullOrEmpty(text) ? string.Empty : text;
    }

    private void OnRepositionPressed()
    {
        if (_arPlacementController == null) return;

        _arPlacementController.ResetAnchor();

        if (_repositionButton != null)
            _repositionButton.gameObject.SetActive(false);

        if (recordButton != null)
            recordButton.gameObject.SetActive(false);

        ShowArGuidance("Tap a flat surface to reposition");
        StartCoroutine(WaitForArPlacement());
    }

    // ─── CAMERA ───────────────────────────────────────────────────────────
    private IEnumerator StartCameraPreview()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            float waited = 0f;
            while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && waited < 8f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            SetStatus("Camera permission denied");
            Debug.LogWarning("[SignBridge] Camera permission denied.");
            yield break;
        }
#else
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            SetStatus("Camera permission denied");
            yield break;
        }
#endif
        yield return new WaitForSeconds(0.5f);

        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0)
        {
            SetStatus("No camera found");
            yield break;
        }

        // Prefer rear camera
        string camName = devices[0].name;
        foreach (WebCamDevice d in devices)
        {
            if (!d.isFrontFacing) { camName = d.name; break; }
        }

        webCamTexture = new WebCamTexture(camName, Screen.width, Screen.height, 30);
        if (cameraPreviewImage != null)
        {
            cameraPreviewImage.texture = webCamTexture;
        }

        webCamTexture.Play();
        ApplyVoiceAppearance();
    }

    private void ApplyCameraTransform()
    {
        int   rotation = webCamTexture.videoRotationAngle;

        cameraPreviewImage.rectTransform.localEulerAngles = new Vector3(0f, 0f, -rotation);
        cameraPreviewImage.rectTransform.localScale = Vector3.one;

        cameraPreviewImage.uvRect = webCamTexture.videoVerticallyMirrored
            ? new Rect(0f, 1f, 1f, -1f)
            : new Rect(0f, 0f, 1f, 1f);
    }

    // ─── UI CONSTRUCTION ──────────────────────────────────────────────────
    private void OnSignBridgeAppearanceChanged()
    {
        ApplyVoiceAppearance();
    }

    /// <summary>Applies theme and color-vision palette to the Unity voice overlay (Web shell is updated separately).</summary>
    public void ApplyVoiceAppearance()
    {
        SignBridgeAppearance.VoiceUiPalette palette = SignBridgeAppearance.GetVoiceUiPalette();

        if (_uiOverlayDim != null)
        {
            _uiOverlayDim.color = palette.OverlayDim;
        }

        if (_uiTopStrip != null)
        {
            _uiTopStrip.color = palette.TopStrip;
        }

        if (scoreText != null)
        {
            scoreText.color = palette.ScoreText;
        }

        if (statusText != null)
        {
            statusText.color = palette.StatusText;
        }

        if (handPreviewImage != null)
        {
            handPreviewImage.color = palette.HandPreviewTint;
        }

        if (_uiBubble != null)
        {
            _uiBubble.color = palette.Bubble;
        }

        if (transcriptText != null)
        {
            transcriptText.color = palette.TranscriptText;
        }

        if (cameraPreviewImage != null)
        {
            if (allowWebcamPreview && webCamTexture != null && webCamTexture.isPlaying)
            {
                cameraPreviewImage.color = SignBridgeAppearance.GetWebcamTint();
            }
            else if (allowWebcamPreview)
            {
                cameraPreviewImage.color = palette.CameraPreviewFill;
            }
            else
            {
                cameraPreviewImage.color = new Color(0f, 0f, 0f, 0f);
            }
        }

        if (recordButton != null && !isListening)
        {
            recordButton.image.color = palette.RecordButton;
            ColorBlock cb = recordButton.colors;
            cb.normalColor = palette.RecordButton;
            cb.highlightedColor = palette.RecordButtonHighlight;
            cb.pressedColor = palette.RecordButtonPressed;
            cb.selectedColor = palette.RecordButton;
            recordButton.colors = cb;
        }

        if (recordLabel != null && !isListening)
        {
            recordLabel.color = palette.RecordLabel;
        }

        ApplyScore(_lastScoreResult);
    }

    private void EnsureUi()
    {
        EnsureEventSystem();

        Transform voiceParent = transform.Find(SignBridgeVoiceUi.AppCanvasName + "/" + SignBridgeVoiceUi.VoiceSignPanelName);
        if (voiceParent == null)
        {
            DestroyLegacyCanvases();
            var canvasGo = new GameObject("SignBridgeCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.anchorMin = Vector2.zero;
            canvasRt.anchorMax = Vector2.one;
            canvasRt.offsetMin = Vector2.zero;
            canvasRt.offsetMax = Vector2.zero;
            voicePanelRoot = canvasRt;
            BuildVoiceUi(voicePanelRoot);
            return;
        }

        DestroyLegacyCanvases();
        voicePanelRoot = voiceParent as RectTransform;
        for (int i = voicePanelRoot.childCount - 1; i >= 0; i--)
        {
            Transform ch = voicePanelRoot.GetChild(i);
            if (ch.name == "BackFromVoice")
            {
                continue;
            }

            UnityEngine.Object.DestroyImmediate(ch.gameObject);
        }

        BuildVoiceUi(voicePanelRoot);
        Transform backFromVoice = voicePanelRoot.Find("BackFromVoice");
        if (backFromVoice != null)
        {
            backFromVoice.SetAsLastSibling();
        }
    }

    private static void EnsureEventSystem()
    {
        EventSystem es = FindObjectOfType<EventSystem>();
        if (es == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }
        else if (es.GetComponent<StandaloneInputModule>() == null)
        {
            BaseInputModule old = es.GetComponent<BaseInputModule>();
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            es.gameObject.AddComponent<StandaloneInputModule>();
        }
    }

    private static void DestroyLegacyCanvases()
    {
        foreach (Canvas c in FindObjectsOfType<Canvas>())
        {
            if (c.name == SignBridgeVoiceUi.AppCanvasName)
            {
                continue;
            }

            UnityEngine.Object.DestroyImmediate(c.gameObject);
        }
    }

    private void BuildVoiceUi(Transform root)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Full-screen camera preview
        var preview = CreateUi("CameraPreview", root, typeof(RawImage));
        SetAnchors(preview, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        cameraPreviewImage = preview.GetComponent<RawImage>();
        cameraPreviewImage.color = new Color(0.07f, 0.09f, 0.12f, 1f);
        cameraPreviewImage.raycastTarget = false;

        var overlay = CreateUi("Overlay", root, typeof(Image));
        SetAnchors(overlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _uiOverlayDim = overlay.GetComponent<Image>();
        _uiOverlayDim.color = new Color(0f, 0f, 0f, 0.28f);
        _uiOverlayDim.raycastTarget = false;

        _uiTopStrip = null;
        scoreFill = null;
        scoreText = null;

        statusText = SpawnText("StatusLabel", root, font, 20, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.875f), new Vector2(600f, 42f));
        statusText.color = new Color(0.65f, 0.70f, 0.78f, 1f);
        Canvas statusCanvas = statusText.gameObject.AddComponent<Canvas>();
        statusCanvas.overrideSorting = true;
        statusCanvas.sortingOrder = 33;

        // Hand animation overlay (hidden until a sign plays)
        var handPreviewGo = CreateUi("HandPreview", root, typeof(RawImage));
        SetAnchors(handPreviewGo, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        handPreviewImage = handPreviewGo.GetComponent<RawImage>();
        handPreviewImage.color = new Color(0.94f, 0.95f, 0.97f, 1f);
        handPreviewImage.raycastTarget = false;
        handPreviewGo.SetActive(false);
        Canvas handSort = handPreviewGo.AddComponent<Canvas>();
        handSort.overrideSorting = true;
        handSort.sortingOrder = 18;

        _uiBubble = null;

        // Transcript is now a flat, borderless line so it does not sit inside a card.
        transcriptText = SpawnText("TranscriptText", root, font, 30,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 0.255f), new Vector2(860f, 120f));
        var trt = transcriptText.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.08f, 0.18f);
        trt.anchorMax = new Vector2(0.92f, 0.30f);
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        transcriptText.resizeTextForBestFit = true;
        transcriptText.resizeTextMinSize = 18;
        transcriptText.resizeTextMaxSize = 36;
        transcriptText.raycastTarget = false;
        transcriptText.color = new Color(0.90f, 0.91f, 0.94f, 1f);

        // Record button is separated from the transcript so it no longer lives inside a framed box.
        var btnGo = CreateUi("RecordButton", root, typeof(Image), typeof(Button));
        SetAnchors(btnGo, new Vector2(0.36f, 0.065f), new Vector2(0.64f, 0.185f), Vector2.zero, Vector2.zero);

        recordButton = btnGo.GetComponent<Button>();
        Image btnImg = btnGo.GetComponent<Image>();
        btnImg.sprite = SignBridgeUiShapeFactory.GetRoundedRectSprite(256, 256, 64);
        btnImg.type = Image.Type.Simple;
        btnImg.color = new Color(0.20f, 0.24f, 0.30f, 1f);
        recordButton.targetGraphic = btnImg;
        recordButton.interactable = true;
        Canvas recSort = btnGo.AddComponent<Canvas>();
        recSort.overrideSorting = true;
        recSort.sortingOrder = 28;
        btnGo.AddComponent<GraphicRaycaster>();

        ColorBlock cb = recordButton.colors;
        cb.normalColor = new Color(0.20f, 0.24f, 0.30f, 1f);
        cb.highlightedColor = new Color(0.26f, 0.30f, 0.38f, 1f);
        cb.pressedColor = new Color(0.14f, 0.17f, 0.22f, 1f);
        cb.selectedColor = cb.normalColor;
        cb.fadeDuration = 0.05f;
        recordButton.colors = cb;

        recordLabel = SpawnText("RecordLabel", btnGo.transform, font, 40,
            TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
        var recLabelRt = recordLabel.GetComponent<RectTransform>();
        recLabelRt.anchorMin = Vector2.zero;
        recLabelRt.anchorMax = Vector2.one;
        recLabelRt.offsetMin = Vector2.zero;
        recLabelRt.offsetMax = Vector2.zero;
        recordLabel.text = "●";
        recordLabel.fontStyle = FontStyle.Bold;
        recordLabel.raycastTarget = false;
        recordLabel.color = new Color(0.88f, 0.90f, 0.95f, 1f);

        // ─── AR guidance text (hidden until AR mode is active) ────────────
        _arGuidanceText = SpawnText("ArGuidanceText", root, font, 26,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(900f, 100f));
        var arGuidRt = _arGuidanceText.GetComponent<RectTransform>();
        arGuidRt.anchorMin = new Vector2(0.06f, 0.38f);
        arGuidRt.anchorMax = new Vector2(0.94f, 0.52f);
        arGuidRt.offsetMin = Vector2.zero;
        arGuidRt.offsetMax = Vector2.zero;
        _arGuidanceText.resizeTextForBestFit = true;
        _arGuidanceText.resizeTextMinSize = 16;
        _arGuidanceText.resizeTextMaxSize = 30;
        _arGuidanceText.raycastTarget = false;
        _arGuidanceText.color = new Color(0.98f, 0.90f, 0.55f, 1f);   // warm amber
        Canvas arGuideSort = _arGuidanceText.gameObject.AddComponent<Canvas>();
        arGuideSort.overrideSorting = true;
        arGuideSort.sortingOrder = 35;
        _arGuidanceText.gameObject.SetActive(false);

        // ─── Reposition button (visible only after AR anchor placed) ──────
        var repoGo = CreateUi("RepositionButton", root, typeof(Image), typeof(Button));
        SetAnchors(repoGo, new Vector2(0.28f, 0.02f), new Vector2(0.72f, 0.08f), Vector2.zero, Vector2.zero);
        _repositionButton = repoGo.GetComponent<Button>();
        Image repoImg = repoGo.GetComponent<Image>();
        repoImg.sprite = SignBridgeUiShapeFactory.GetRoundedRectSprite(256, 64, 24);
        repoImg.type = Image.Type.Simple;
        repoImg.color = new Color(0.14f, 0.18f, 0.24f, 0.90f);
        _repositionButton.targetGraphic = repoImg;
        Canvas repoSort = repoGo.AddComponent<Canvas>();
        repoSort.overrideSorting = true;
        repoSort.sortingOrder = 30;
        repoGo.AddComponent<GraphicRaycaster>();
        _repositionButton.onClick.AddListener(OnRepositionPressed);
        Text repoLabel = SpawnText("RepoLabel", repoGo.transform, font, 22,
            TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
        var repoLabelRt = repoLabel.GetComponent<RectTransform>();
        repoLabelRt.anchorMin = Vector2.zero;
        repoLabelRt.anchorMax = Vector2.one;
        repoLabelRt.offsetMin = Vector2.zero;
        repoLabelRt.offsetMax = Vector2.zero;
        repoLabel.text = "\u2316 Reposition";
        repoLabel.raycastTarget = false;
        repoLabel.color = new Color(0.70f, 0.78f, 0.90f, 1f);
        repoGo.SetActive(false);
    }

    // ─── UI HELPERS ──────────────────────────────────────────────────────
    private static void SetAnchors(GameObject go, Vector2 min, Vector2 max,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        RectTransform rt = go.GetComponent<RectTransform>()
                        ?? go.AddComponent<RectTransform>();
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static GameObject CreateUi(string name, Transform parent,
        params System.Type[] components)
    {
        var go = new GameObject(name, components);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Text SpawnText(string name, Transform parent, Font font,
        int size, TextAnchor anchor, Vector2 anchorPos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = anchorPos;
        rt.anchorMax        = anchorPos;
        rt.sizeDelta        = sizeDelta;
        rt.anchoredPosition = Vector2.zero;
        var t = go.GetComponent<Text>();
        t.font      = font;
        t.fontSize  = size;
        t.alignment = anchor;
        t.color     = Color.white;
        return t;
    }
}
