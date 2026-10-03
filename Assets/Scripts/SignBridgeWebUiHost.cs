using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Hosts the app shell as HTML/CSS/JS in a native WebView (gree unity-webview package).
/// Uses reflection so the project compiles before the UPM package is resolved.
/// </summary>
[DefaultExecutionOrder(-110)]
public sealed class SignBridgeWebUiHost : MonoBehaviour
{
    private const string WebViewTypeName = "Gree.UnityWebView.WebViewObject";
    private const string WebViewAssemblyName = "unity-webview";

    private static readonly Regex JsonActionRx = new Regex(
        "\"action\"\\s*:\\s*\"([^\"]*)\"",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex JsonTokenRx = new Regex(
        "\"token\"\\s*:\\s*\"([^\"]*)\"",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex JsonPathRx = new Regex(
        "\"path\"\\s*:\\s*\"([^\"]*)\"",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex JsonStayInShellTrueRx = new Regex(
        "\"stayInShell\"\\s*:\\s*true",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex JsonThemeRx = new Regex(
        "\"theme\"\\s*:\\s*\"([^\"]*)\"",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex JsonColorBlindRx = new Regex(
        "\"colorBlind\"\\s*:\\s*\"([^\"]*)\"",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private Type _webViewType;
    private Component _webView;
    private RectTransform _voiceSignPanel;
    private SimpleVoiceScreenController _voiceScreen;
    private SignLanguageController _signLanguage;

    private void Awake()
    {
        _voiceScreen = GetComponent<SimpleVoiceScreenController>();
        _signLanguage = GetComponent<SignLanguageController>();

        EnsureVoiceShell();

        StartCoroutine(InitializeWebUiShell());
    }

    private void Start()
    {
        EnsureSignLanguageRefs();
    }

    /// <summary>
    /// SignLanguageController is added in <see cref="SimpleVoiceScreenController.Awake"/>; this host runs earlier
    /// (<see cref="DefaultExecutionOrder"/> -110), so the reference must be refreshed after all Awakes.
    /// </summary>
    private void EnsureSignLanguageRefs()
    {
        if (_voiceScreen == null)
        {
            _voiceScreen = GetComponent<SimpleVoiceScreenController>();
        }

        if (_signLanguage == null)
        {
            _signLanguage = GetComponent<SignLanguageController>();
        }
    }

    private static Type ResolveWebViewObjectType()
    {
        Type t = Type.GetType(WebViewTypeName + ", " + WebViewAssemblyName);
        if (t != null)
        {
            return t;
        }

        try
        {
            Assembly asm = Assembly.Load(WebViewAssemblyName);
            t = asm.GetType(WebViewTypeName);
            if (t != null)
            {
                return t;
            }
        }
        catch
        {
            // ignored
        }

        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                t = asm.GetType(WebViewTypeName);
                if (t != null)
                {
                    return t;
                }
            }
            catch
            {
                // ignored
            }
        }

        return null;
    }

    private IEnumerator InitializeWebUiShell()
    {
        const int maxFramesToWait = 120;

        for (int frame = 0; frame < maxFramesToWait; frame++)
        {
            _webViewType = ResolveWebViewObjectType();
            if (_webViewType != null)
            {
                yield return BootWebView();
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning(
            "[SignBridgeWebUiHost] unity-webview WebViewObject not resolved after waiting for package resolution. Showing the native fallback menu.");
        ShowNativeFallbackMenu();
    }

    private void ShowNativeFallbackMenu()
    {
        if (_voiceSignPanel == null)
        {
            return;
        }

        _voiceSignPanel.gameObject.SetActive(true);

        Transform backVoice = _voiceSignPanel.Find("BackFromVoice");
        if (backVoice != null)
        {
            backVoice.gameObject.SetActive(false);
        }

        if (_voiceSignPanel.Find("FallbackTitle") != null)
        {
            return;
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject title = new GameObject("FallbackTitle", typeof(RectTransform), typeof(Text));
        title.transform.SetParent(_voiceSignPanel, false);
        RectTransform titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.08f, 0.66f);
        titleRt.anchorMax = new Vector2(0.92f, 0.80f);
        titleRt.offsetMin = Vector2.zero;
        titleRt.offsetMax = Vector2.zero;
        Text titleText = title.GetComponent<Text>();
        titleText.font = font;
        titleText.fontSize = 42;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = new Color(0.94f, 0.95f, 0.98f, 1f);
        titleText.text = "SignBridge";
        titleText.raycastTarget = false;

        GameObject message = new GameObject("FallbackMessage", typeof(RectTransform), typeof(Text));
        message.transform.SetParent(_voiceSignPanel, false);
        RectTransform messageRt = message.GetComponent<RectTransform>();
        messageRt.anchorMin = new Vector2(0.10f, 0.53f);
        messageRt.anchorMax = new Vector2(0.90f, 0.66f);
        messageRt.offsetMin = Vector2.zero;
        messageRt.offsetMax = Vector2.zero;
        Text messageText = message.GetComponent<Text>();
        messageText.font = font;
        messageText.fontSize = 24;
        messageText.fontStyle = FontStyle.Normal;
        messageText.alignment = TextAnchor.MiddleCenter;
        messageText.color = new Color(0.78f, 0.81f, 0.86f, 1f);
        messageText.text = "The HTML shell is unavailable. Use Start to open voice mode.";
        messageText.raycastTarget = false;

        GameObject startButton = new GameObject("FallbackStartButton", typeof(RectTransform), typeof(Image), typeof(Button));
        startButton.transform.SetParent(_voiceSignPanel, false);
        RectTransform buttonRt = startButton.GetComponent<RectTransform>();
        buttonRt.anchorMin = new Vector2(0.20f, 0.33f);
        buttonRt.anchorMax = new Vector2(0.80f, 0.46f);
        buttonRt.offsetMin = Vector2.zero;
        buttonRt.offsetMax = Vector2.zero;

        Image buttonImage = startButton.GetComponent<Image>();
        buttonImage.color = new Color(0.20f, 0.55f, 0.95f, 0.98f);

        Button button = startButton.GetComponent<Button>();
        button.targetGraphic = buttonImage;
        button.onClick.AddListener(OpenVoicePanel);

        GameObject buttonLabel = new GameObject("Text", typeof(RectTransform), typeof(Text));
        buttonLabel.transform.SetParent(startButton.transform, false);
        RectTransform labelRt = buttonLabel.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        Text labelText = buttonLabel.GetComponent<Text>();
        labelText.font = font;
        labelText.fontSize = 34;
        labelText.fontStyle = FontStyle.Bold;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.color = Color.white;
        labelText.text = "Start";
        labelText.raycastTarget = false;
    }

    private void OnDestroy()
    {
        if (_webView != null)
        {
            Destroy(_webView.gameObject);
            _webView = null;
        }
    }

    private void EnsureVoiceShell()
    {
        Transform existing = transform.Find(SignBridgeVoiceUi.AppCanvasName);
        if (existing != null)
        {
            _voiceSignPanel = existing.Find(SignBridgeVoiceUi.VoiceSignPanelName) as RectTransform;
            return;
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasGo = new GameObject(SignBridgeVoiceUi.AppCanvasName, typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        RectTransform canvasRt = canvasGo.GetComponent<RectTransform>();
        canvasRt.anchorMin = Vector2.zero;
        canvasRt.anchorMax = Vector2.one;
        canvasRt.offsetMin = Vector2.zero;
        canvasRt.offsetMax = Vector2.zero;

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        _voiceSignPanel = CreateFullPanel(SignBridgeVoiceUi.VoiceSignPanelName, canvasGo.transform);
        Image voiceClear = _voiceSignPanel.gameObject.AddComponent<Image>();
        voiceClear.color = new Color(0f, 0f, 0f, 0f);
        voiceClear.raycastTarget = false;

        GameObject backVoice = new GameObject("BackFromVoice", typeof(RectTransform), typeof(Image), typeof(Button));
        backVoice.transform.SetParent(_voiceSignPanel, false);
        RectTransform backRt = backVoice.GetComponent<RectTransform>();
        backRt.anchorMin = new Vector2(0.04f, 0.90f);
        backRt.anchorMax = new Vector2(0.18f, 0.965f);
        backRt.offsetMin = Vector2.zero;
        backRt.offsetMax = Vector2.zero;
        Image backImg = backVoice.GetComponent<Image>();
        backImg.sprite = SignBridgeUiShapeFactory.GetRoundedRectSprite(256, 96, 28);
        backImg.type = Image.Type.Simple;
        backImg.color = new Color(0.16f, 0.19f, 0.24f, 0.98f);

        Button backBtn = backVoice.GetComponent<Button>();
        backBtn.targetGraphic = backImg;
        backBtn.onClick.AddListener(ShowWebShell);
        AddCenteredLabel(backVoice.transform, font, "Back", 28);
        Transform backTextTf = backVoice.transform.Find("Text");
        if (backTextTf != null)
        {
            Text backTxt = backTextTf.GetComponent<Text>();
            if (backTxt != null) backTxt.color = new Color(0.92f, 0.93f, 0.95f, 1f);
        }

        _voiceSignPanel.gameObject.SetActive(false);
    }

    private static RectTransform CreateFullPanel(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static void AddCenteredLabel(Transform parent, Font font, string text, int size)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Text t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.text = text;
        t.raycastTarget = false;
    }

    private IEnumerator BootWebView()
    {
        if (_webViewType == null)
        {
            yield break;
        }

        GameObject wvGo = new GameObject("SignBridgeWebView");
        wvGo.transform.SetParent(transform, false);
        _webView = wvGo.AddComponent(_webViewType) as Component;

        MethodInfo init = FindInitMethod(_webViewType);
        if (init == null)
        {
            Debug.LogError("[SignBridgeWebUiHost] WebViewObject.Init not found.");
            yield break;
        }

        object[] initArgs = BuildInitArguments(init, this);
        init.Invoke(_webView, initArgs);

        MethodInfo isInit = _webViewType.GetMethod("IsInitialized", BindingFlags.Public | BindingFlags.Instance);
        while (isInit != null && !(bool)isInit.Invoke(_webView, null))
        {
            yield return null;
        }

        ApplyWebViewMargins();
        InvokeWebView("SetVisibility", true);

        string tempWebRoot = Path.Combine(Application.temporaryCachePath, "WebUI");
        Directory.CreateDirectory(tempWebRoot);

        yield return CopyWebUiAssetsToTemp(tempWebRoot);

        string indexDst = Path.Combine(tempWebRoot, "index.html");
        if (!File.Exists(indexDst))
        {
            Debug.LogError("[WebUI] index.html was not copied to: " + indexDst);
            yield break;
        }

        string url = "file://" + indexDst.Replace("\\", "/").Replace(" ", "%20");
        InvokeWebView("LoadURL", url);
    }

    private static IEnumerator CopyWebUiAssetsToTemp(string tempWebRoot)
    {
        string[] fileNames = { "index.html", "logo.png", "buttons-background.png" };
        foreach (string name in fileNames)
        {
            string relative = Path.Combine("WebUI", name);
            string src = Path.Combine(Application.streamingAssetsPath, relative);
            string dst = Path.Combine(tempWebRoot, name);
            bool required = name == "index.html";
            Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? Application.temporaryCachePath);

            if (src.Contains("://", StringComparison.Ordinal))
            {
                using UnityWebRequest uwr = UnityWebRequest.Get(src);
                yield return uwr.SendWebRequest();
                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    if (required)
                    {
                        Debug.LogError("[WebUI] Failed to read: " + src + " — " + uwr.error);
                    }

                    continue;
                }

                File.WriteAllBytes(dst, uwr.downloadHandler.data);
                continue;
            }

            if (!File.Exists(src))
            {
                if (required)
                {
                    Debug.LogError("[WebUI] Missing StreamingAssets file: " + src);
                }

                continue;
            }

            File.WriteAllBytes(dst, File.ReadAllBytes(src));
        }
    }

    private static MethodInfo FindInitMethod(Type webViewType)
    {
        MethodInfo best = null;
        int bestLen = -1;
        foreach (MethodInfo mi in webViewType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (mi.Name != "Init" || mi.ReturnType != typeof(void))
            {
                continue;
            }

            int len = mi.GetParameters().Length;
            if (len > bestLen)
            {
                bestLen = len;
                best = mi;
            }
        }

        return best;
    }

    private static object[] BuildInitArguments(MethodInfo init, SignBridgeWebUiHost host)
    {
        ParameterInfo[] ps = init.GetParameters();
        object[] args = new object[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
            if (args[i] != null && args[i].GetType() == typeof(DBNull))
            {
                args[i] = null;
            }
        }

        if (ps.Length > 0)
        {
            args[0] = CreateCallbackDelegate(ps[0].ParameterType, host, nameof(HandleJsFromWeb));
        }

        if (ps.Length > 1)
        {
            args[1] = CreateCallbackDelegate(ps[1].ParameterType, host, nameof(WebViewOnError));
        }

        if (ps.Length > 2)
        {
            args[2] = CreateCallbackDelegate(ps[2].ParameterType, host, nameof(WebViewOnHttpError));
        }

        if (ps.Length > 3)
        {
            args[3] = CreateCallbackDelegate(ps[3].ParameterType, host, nameof(WebViewOnLoaded));
        }

        return args;
    }

    private static Delegate CreateCallbackDelegate(Type delegateType, object target, string methodName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo handler = target.GetType().GetMethod(methodName, flags);
        if (handler == null)
        {
            throw new MissingMethodException(target.GetType().FullName, methodName);
        }

        return Delegate.CreateDelegate(delegateType, target, handler);
    }

    private void HandleJsFromWeb(string msg)
    {
        if (string.IsNullOrEmpty(msg))
        {
            return;
        }

        try
        {
            if (!TryUnpackWebUiMessage(msg, out WebUiMessage m))
            {
                return;
            }

            switch (m.action)
            {
                case "openVoice":
                    OpenVoicePanel();
                    break;
                case "openArMode":
                    OpenArPlacementMode();
                    break;
                case "playSign":
                    OpenVoiceAndPlaySign(m.token, m.path, m.stayInShell);
                    break;
                case "showWeb":
                    ShowWebShell();
                    break;
                case "setAppearance":
                    SignBridgeAppearance.SetFromStrings(m.theme, m.colorBlind);
                    PushWebAppearanceJs();
                    EnsureSignLanguageRefs();
                    _voiceScreen?.ApplyVoiceAppearance();
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[WebUI] Bad message: " + msg + " — " + e.Message);
        }
    }

    private static bool TryUnpackWebUiMessage(string raw, out WebUiMessage m)
    {
        m = new WebUiMessage();
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        string s = raw.Trim();
        if (s.StartsWith("unity:", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring("unity:".Length).TrimStart();
        }

        try
        {
            s = UnityWebRequest.UnEscapeURL(s);
        }
        catch
        {
            // keep original
        }

        WebUiMessage parsed = null;
        try
        {
            parsed = JsonUtility.FromJson<WebUiMessage>(s);
        }
        catch
        {
            // handled below
        }

        if (parsed != null && !string.IsNullOrEmpty(parsed.action))
        {
            m = parsed;
        }

        if (string.IsNullOrEmpty(m.action))
        {
            Match ma = JsonActionRx.Match(s);
            if (ma.Success)
            {
                m.action = ma.Groups[1].Value;
            }
        }

        if (string.IsNullOrEmpty(m.token))
        {
            Match mt = JsonTokenRx.Match(s);
            if (mt.Success)
            {
                m.token = mt.Groups[1].Value;
            }
        }

        if (string.IsNullOrEmpty(m.path))
        {
            Match mp = JsonPathRx.Match(s);
            if (mp.Success)
            {
                m.path = mp.Groups[1].Value;
            }
        }

        if (!m.stayInShell && JsonStayInShellTrueRx.IsMatch(s))
        {
            m.stayInShell = true;
        }

        if (string.IsNullOrEmpty(m.theme))
        {
            Match mth = JsonThemeRx.Match(s);
            if (mth.Success)
            {
                m.theme = mth.Groups[1].Value;
            }
        }

        if (string.IsNullOrEmpty(m.colorBlind))
        {
            Match mcb = JsonColorBlindRx.Match(s);
            if (mcb.Success)
            {
                m.colorBlind = mcb.Groups[1].Value;
            }
        }

        return !string.IsNullOrEmpty(m.action);
    }

    private void WebViewOnError(string m)
    {
        Debug.LogWarning("[WebUI] " + m);
    }

    private void WebViewOnHttpError(string m)
    {
        Debug.LogWarning("[WebUI HTTP] " + m);
    }

    private void WebViewOnLoaded(string _)
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS
        const string unityCallPolyfill = @"
if (!(window.webkit && window.webkit.messageHandlers)) {
  window.Unity = {
    call: function(msg) { window.location = 'unity:' + msg; }
  };
}";
        InvokeWebView("EvaluateJS", unityCallPolyfill);
#endif
        List<SignCatalogRow> wordRows = SignLanguageController.GetWordRowsForWeb();
        List<SignCatalogRow> vocabRows = SignLanguageController.GetVocabularyRowsForWeb();
        List<SignCatalogRow> alphaRows = SignLanguageController.GetAlphabetRowsForWeb();
        SplitCatalogRows(wordRows, out List<string> wordLabels, out List<string> wordPaths);
        SplitCatalogRows(vocabRows, out List<string> vocabLabels, out List<string> vocabPaths);
        SplitCatalogRows(alphaRows, out List<string> alphaLabels, out List<string> alphaPaths);
#if UNITY_EDITOR
        Debug.Log(
            "[SignBridge] Learn catalog counts — words: " + wordRows.Count
            + ", vocabulary: " + vocabRows.Count
            + ", alphabet: " + alphaRows.Count
            + " (from Resources/SignAssets).");
#endif
        string payload =
            "window.__sb = { "
            + "wordLabels: " + JsonStringArray(wordLabels)
            + ", wordPaths: " + JsonStringArray(wordPaths)
            + ", vocabLabels: " + JsonStringArray(vocabLabels)
            + ", vocabPaths: " + JsonStringArray(vocabPaths)
            + ", alphaLabels: " + JsonStringArray(alphaLabels)
            + ", alphaPaths: " + JsonStringArray(alphaPaths)
            + " }; if (window.__sbOnData) window.__sbOnData();";
        InvokeWebView("EvaluateJS", payload);
        PushWebAppearanceJs();
    }

    private void PushWebAppearanceJs()
    {
        if (_webView == null || _webViewType == null)
        {
            return;
        }

        SignBridgeAppearance.LoadFromPrefs();
        string theme = SignBridgeAppearance.ThemeToWebString();
        string cb = SignBridgeAppearance.ColorBlindToWebString();
        string js = "if(window.__sbSyncAppearance)window.__sbSyncAppearance('" + theme + "','" + cb + "');";
        InvokeWebView("EvaluateJS", js);
    }

    private void ApplyWebViewMargins()
    {
        if (_webView == null || _webViewType == null)
        {
            return;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        MethodInfo sm = _webViewType.GetMethod(
            "SetMargins",
            flags,
            null,
            new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool) },
            null);
        if (sm != null)
        {
            sm.Invoke(_webView, new object[] { 0, 0, 0, 0, false });
            return;
        }

        sm = _webViewType.GetMethod(
            "SetMargins",
            flags,
            null,
            new[] { typeof(int), typeof(int), typeof(int), typeof(int) },
            null);
        sm?.Invoke(_webView, new object[] { 0, 0, 0, 0 });
    }

    private void InvokeWebView(string method, params object[] args)
    {
        if (_webView == null || _webViewType == null)
        {
            return;
        }

        MethodInfo mi = _webViewType.GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
        if (mi == null)
        {
            Debug.LogWarning("[WebUI] Missing method: " + method);
            return;
        }

        mi.Invoke(_webView, args);
    }

    private static void SplitCatalogRows(
        IReadOnlyList<SignCatalogRow> rows,
        out List<string> labels,
        out List<string> paths)
    {
        labels = new List<string>(rows.Count);
        paths = new List<string>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            labels.Add(rows[i].Label);
            paths.Add(rows[i].ResourcePath);
        }
    }

    private static string JsonStringArray(IList<string> items)
    {
        StringBuilder b = new StringBuilder();
        b.Append('[');
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                b.Append(',');
            }

            b.Append('"');
            string s = items[i] ?? string.Empty;
            for (int j = 0; j < s.Length; j++)
            {
                char c = s[j];
                if (c == '\\' || c == '"')
                {
                    b.Append('\\');
                }

                if (c == '\r' || c == '\n')
                {
                    continue;
                }

                b.Append(c);
            }

            b.Append('"');
        }

        b.Append(']');
        return b.ToString();
    }

    private void OpenVoicePanel()
    {
        _voiceScreen?.CancelListeningForNavigation();
        InvokeWebView("SetVisibility", false);

        if (_voiceSignPanel != null)
        {
            _voiceSignPanel.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Hides the Web shell and enters AR Surface Mode, where the user places
    /// a real-world anchor before speaking.
    /// </summary>
    private void OpenArPlacementMode()
    {
        _voiceScreen?.CancelListeningForNavigation();
        InvokeWebView("SetVisibility", false);

        EnsureSignLanguageRefs();

        if (_voiceSignPanel != null)
            _voiceSignPanel.gameObject.SetActive(true);

        if (_signLanguage != null
            && !_signLanguage.IsSignPlaybackReady
            && _voiceScreen != null
            && _voiceScreen.HandPreviewImage != null)
        {
            _signLanguage.Initialize(_voiceScreen.HandPreviewImage);
        }

        _voiceScreen?.EnterArPlacementMode();
    }

    private void OpenVoiceAndPlaySign(string token, string resourcePath, bool stayInShell = false)
    {
        EnsureSignLanguageRefs();

        token = token?.Trim();
        resourcePath = resourcePath?.Trim();
        if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(resourcePath))
        {
            return;
        }

        // Vocabulary (stayInShell): never hide the Web shell or show the full voice page. AR world signs need no overlay; otherwise force overlay once.
        bool vocabularyStayOnWeb = stayInShell;
        bool worldPlaybackWhileOnWeb = stayInShell
            && _signLanguage != null
            && _signLanguage.WillPlaySignsInWorldSpace();

        if (!vocabularyStayOnWeb)
        {
            OpenVoicePanel();
            string chrome = !string.IsNullOrWhiteSpace(token) ? token : resourcePath;
            _voiceScreen?.ShowPlaybackChrome(chrome);
        }

        if (_signLanguage == null)
        {
            return;
        }

        if (!_signLanguage.IsSignPlaybackReady
            && _voiceScreen != null
            && _voiceScreen.HandPreviewImage != null)
        {
            _signLanguage.Initialize(_voiceScreen.HandPreviewImage);
        }

        if (vocabularyStayOnWeb)
        {
            if (!worldPlaybackWhileOnWeb)
            {
                _signLanguage.BeginWebShellSignPlayback();
            }
        }
        else
        {
            _signLanguage.BeginWebShellSignPlayback();
        }

        if (!string.IsNullOrWhiteSpace(resourcePath))
        {
            _signLanguage.PlaySignResourcePath(resourcePath);
        }
        else
        {
            _signLanguage.PlaySignForText(token);
        }
    }

    public void ShowWebShell()
    {
        _voiceScreen?.CancelListeningForNavigation();
        if (_voiceSignPanel != null)
        {
            _voiceSignPanel.gameObject.SetActive(false);
        }

        InvokeWebView("SetVisibility", true);
    }

    [Serializable]
    private class WebUiMessage
    {
        public string action;
        public string token;
        public string path;
        public bool stayInShell;
        public string theme;
        public string colorBlind;
    }
}
