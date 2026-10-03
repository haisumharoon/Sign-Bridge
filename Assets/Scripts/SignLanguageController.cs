using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.Playables;
using UnityEngine.Animations;

/// <summary>One row for Learn / Vocabulary Web lists (label shown, Resources path for playback).</summary>
public readonly struct SignCatalogRow
{
    public string Label { get; }
    public string ResourcePath { get; }

    public SignCatalogRow(string label, string resourcePath)
    {
        Label = label;
        ResourcePath = resourcePath;
    }
}

/// <summary>
/// Resolves sign FBX assets from Resources/SignAssets and plays them either in AR world space
/// (anchored via ARHandPlacementController) or in legacy overlay mode.
/// </summary>
public class SignLanguageController : MonoBehaviour
{
    private static readonly Vector3 HandRenderOrigin = new Vector3(1000f, 0f, 0f);

    // Maps keywords -> Resources path (without extension)
    private static readonly Dictionary<string, string> WordToFbx =
        new Dictionary<string, string>
        {
            { "hello",  "SignAssets/hello (right)"     },
            { "help",   "SignAssets/help (right)"       },
            { "please", "SignAssets/please (right)"     },
            { "thank",  "SignAssets/thank you (right)"  },
            { "you",    "SignAssets/thank you (right)"  },
            { "what",   "SignAssets/what (right)"       },
            { "where",  "SignAssets/where (right)"      },
            { "why",    "SignAssets/why (right)"        },
            { "how",    "SignAssets/how (right)"        },
            { "who",    "SignAssets/who (right)"        },
            { "when",   "SignAssets/when (right)"       },
            { "name",   "SignAssets/name (right)"       },
            { "my",     "SignAssets/my (right)"         },
            { "work",   "SignAssets/work (right)"       },
            { "love",   "SignAssets/love (right)"       },
            { "go",     "SignAssets/go (right)"         },
            { "eat",    "SignAssets/eat (right)"        },
            { "drink",  "SignAssets/drink (right)"      },
            { "water",  "SignAssets/water (right)"      },
            { "school", "SignAssets/school (right)"     },
            { "today",  "SignAssets/today (right)"      },
            { "day",    "SignAssets/day (right)"        },
            { "night",  "SignAssets/night (right)"      },
            { "mom",    "SignAssets/mom (right)"        },
            { "dad",    "SignAssets/dad (right)"        },
            { "girl",   "SignAssets/girl (right)"       },
            { "boy",    "SignAssets/boy (right)"        },
            { "yes",    "SignAssets/ok (right)"         },
            { "no",     "SignAssets/bad (right)"        },
        };

    private static readonly Dictionary<string, string> PhraseToFbx =
        new Dictionary<string, string>
        {
            { "thank you", "SignAssets/thank you (right)" },
            { "please help", "SignAssets/help (right)" },
            { "how are you", "SignAssets/how (right)" },
            { "letter v", "SignAssets/alphabet_v" },
        };

    private static readonly Dictionary<string, string> TokenAliases =
        new Dictionary<string, string>
        {
            { "thanks", "thank" },
            { "thx", "thank" },
            { "pls", "please" },
            { "okey", "ok" },
            { "okay", "ok" },
            { "v", "alphabet_v" },
            { "vee", "alphabet_v" },
        };

    // Layer used exclusively by the hand render camera (layer 8 = "Water", free in this project)
    private const int HandLayer = 8;

    private Camera       handCamera;
    private RenderTexture handRenderTexture;
    private readonly List<GameObject> currentHandModels = new List<GameObject>();
    private readonly List<string> currentHandPaths = new List<string>();
    private readonly List<Vector3> currentOverlayAnchors = new List<Vector3>();
    private RawImage     handDisplay;
    private bool         isInitialized;
    private static Dictionary<string, string> dynamicWordToPath;
    private ARHandPlacementController arPlacementController;

    /// <summary>When true, the next playback uses the hand RT overlay so Learn/Vocab taps show animation (AR world mode otherwise skips the RT camera).</summary>
    private bool forceOverlayPlaybackOnce;

    private static List<GameObject> cachedSignAssetRoots;

    /// <summary>All GameObject roots under Resources/SignAssets (any importer type). Cached per domain.</summary>
    private static IReadOnlyList<GameObject> GetAllSignAssetPrefabRoots()
    {
        if (cachedSignAssetRoots != null)
        {
            return cachedSignAssetRoots;
        }

        UnityEngine.Object[] raw = Resources.LoadAll("SignAssets");
        List<GameObject> list = new List<GameObject>(raw.Length);
        HashSet<int> ids = new HashSet<int>();
        foreach (UnityEngine.Object o in raw)
        {
            if (o is not GameObject go) continue;
            int id = go.GetInstanceID();
            if (!ids.Add(id)) continue;
            list.Add(go);
        }

        cachedSignAssetRoots = list;
        return cachedSignAssetRoots;
    }

    public bool IsSignPlaybackReady => isInitialized;

    /// <summary>True when signs render in AR world space (no RT overlay). Web vocabulary can stay on the shell when this is true.</summary>
    public bool WillPlaySignsInWorldSpace()
    {
        return ShouldUseWorldSpacePlayback();
    }

    [Header("World Hand Placement")]
    [SerializeField] private bool preferWorldSpacePlayback = false;
    [SerializeField] private float worldHandOffset = 0.18f;
    [SerializeField] private float worldHandUniformScale = 0.22f;
    [SerializeField] private float worldHandYawOffset = 180f;

    [Header("Overlay Centering")]
    [SerializeField] private float overlayCenterY = 0.08f;
    [SerializeField] private float overlayPairSpacing = 0.36f;

    // ─── PUBLIC API ───────────────────────────────────────────────────────

    /// <summary>Call once from SimpleVoiceScreenController.Start(), passing the overlay RawImage.</summary>
    public void Initialize(RawImage displayImage)
    {
        if (isInitialized) return;
        handDisplay = displayImage;
        arPlacementController = GetComponent<ARHandPlacementController>();

        if (handDisplay != null)
        {
            SetupHandCamera();
        }

        if (ShouldUseWorldSpacePlayback())
        {
            if (handDisplay != null)
            {
                handDisplay.gameObject.SetActive(false);
            }

            if (arPlacementController != null)
            {
                arPlacementController.EnsureAnchorExists();
            }
        }
        else
        {
            EnsureHandOverlayVisible();
        }

        ValidateMappings();
        isInitialized = true;
        // Keep both hands visible from the start.
        PlaySignForText("hello");
        Debug.Log("[SignBridge] SignLanguageController initialized.");
    }

    /// <summary>
    /// Enables or disables AR world-space sign playback at runtime.
    /// When enabled, sign models are placed at the ARHandPlacementController anchor
    /// instead of the render-texture overlay camera.
    /// </summary>
    public void SetWorldSpacePlayback(bool enabled)
    {
        preferWorldSpacePlayback = enabled;

        if (enabled)
        {
            if (arPlacementController == null)
                arPlacementController = GetComponent<ARHandPlacementController>();

            if (arPlacementController != null)
                arPlacementController.EnsureAnchorExists();

            // Hide the hand overlay display — world space uses direct scene rendering.
            if (handDisplay != null)
                handDisplay.gameObject.SetActive(false);
        }
        else
        {
            // Returning to overlay mode: re-show the hand overlay display.
            EnsureHandOverlayVisible();
        }

        Debug.Log($"[SignBridge] World-space playback: {enabled}");
    }

    /// <summary>Parse <paramref name="text"/> for known words and play the matching sign.</summary>
    public void PlaySignForText(string text)
    {
        if (string.IsNullOrEmpty(text) || !isInitialized) return;

        StopCurrentAnimation();
        StartCoroutine(PlaySequenceForText(text));
    }

    /// <summary>Play exactly one sign prefab by Resources path (e.g. <c>SignAssets/hello (right)</c>). Used by Learn lists so every FBX maps reliably.</summary>
    public void PlaySignResourcePath(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath) || !isInitialized) return;

        StopCurrentAnimation();
        StartCoroutine(PlayResourcePathRoutine(resourcePath.Trim()));
    }

    /// <summary>Call before <see cref="PlaySignResourcePath"/> / <see cref="PlaySignForText"/> when opening from the Web shell so the hand preview renders in AR world scenes.</summary>
    public void BeginWebShellSignPlayback()
    {
        forceOverlayPlaybackOnce = true;
        if (handDisplay != null && handCamera == null)
        {
            SetupHandCamera();
        }
    }

    private IEnumerator PlayResourcePathRoutine(string resourcePath)
    {
        bool clearWebOverlay = forceOverlayPlaybackOnce;
        try
        {
            EnsureHandOverlayVisible();
            yield return null;
            yield return PlaySingleSign(resourcePath);
        }
        finally
        {
            if (clearWebOverlay)
            {
                forceOverlayPlaybackOnce = false;
                EnsureHandOverlayVisible();
            }
        }
    }

    private IEnumerator PlaySequenceForText(string text)
    {
        bool clearWebOverlay = forceOverlayPlaybackOnce;
        try
        {
            List<string> paths = ResolveSignSequence(text);
            if (paths.Count == 0)
            {
                Debug.Log($"[SignBridge] No sign mapped for: \"{text}\"");
                yield break;
            }

            EnsureHandOverlayVisible();

            foreach (string path in paths)
            {
                yield return PlaySingleSign(path);
            }
        }
        finally
        {
            if (clearWebOverlay)
            {
                forceOverlayPlaybackOnce = false;
                EnsureHandOverlayVisible();
            }
        }
    }

    private IEnumerator PlaySingleSign(string fbxPath)
    {
        DestroyCurrentModels();

        GameObject prefab = TryLoadSignPrefab(fbxPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[SignBridge] Resources.Load failed for: {fbxPath}  " +
                             "(make sure the FBX is inside Assets/Resources/ or Assets/Resources/SignAssets/)");
            yield break;
        }

        bool worldPlayback = ShouldUseWorldSpacePlayback() && !forceOverlayPlaybackOnce;
        Transform worldRoot = null;
        if (worldPlayback)
        {
            if (arPlacementController == null)
            {
                arPlacementController = GetComponent<ARHandPlacementController>();
            }

            if (arPlacementController != null)
            {
                arPlacementController.EnsureAnchorExists();
                worldRoot = arPlacementController.AnchorTransform;
            }
        }

        bool singleHandLetter = IsSingleHandLetterPath(fbxPath);

        // In world-space AR mode use Default layer (0) so the AR camera can see the models.
        // In overlay mode use HandLayer so only the dedicated hand render camera sees them.
        int modelLayer = worldPlayback ? 0 : HandLayer;

        // Instantiate right-hand and left-hand variants when available.
        GameObject rightModel = InstantiateSignModel(
            prefab,
            true,
            worldPlayback,
            worldRoot);
        SetLayerRecursive(rightModel, modelLayer);
        currentHandModels.Add(rightModel);
        currentHandPaths.Add(fbxPath);
        currentOverlayAnchors.Add(GetOverlayAnchorPosition(isRightHand: true, singleHand: singleHandLetter));

        if (!singleHandLetter)
        {
            string leftPath = ToLeftVariantPath(fbxPath);
            GameObject leftPrefab = TryLoadSignPrefab(leftPath);
            GameObject leftModel = null;
            bool leftUsesMirroredFallback = false;
            if (leftPrefab != null)
            {
                leftModel = InstantiateSignModel(
                    leftPrefab,
                    false,
                    worldPlayback,
                    worldRoot);
            }
            else
            {
                // Fallback: mirror right-hand model so both sides are visible.
                Debug.LogWarning($"[SignBridge] Left-hand variant missing for {fbxPath}, using mirrored fallback.");
                leftUsesMirroredFallback = true;
                leftModel = InstantiateSignModel(
                    prefab,
                    false,
                    worldPlayback,
                    worldRoot);
                MirrorLeftModel(leftModel);
            }
            SetLayerRecursive(leftModel, modelLayer);
            currentHandModels.Add(leftModel);
            currentHandPaths.Add(leftUsesMirroredFallback ? fbxPath : leftPath);
            currentOverlayAnchors.Add(GetOverlayAnchorPosition(isRightHand: false, singleHand: false));
        }

        if (!worldPlayback)
        {
            FrameCurrentModels();
        }

        WarmupAnimatorsOnRoots(currentHandModels);
        yield return null;

        float clipLength = 2.0f;
        for (int i = 0; i < currentHandModels.Count; i++)
        {
            string clipPath = i < currentHandPaths.Count ? currentHandPaths[i] : fbxPath;
            float modelLength = PlayAnimationOnModel(currentHandModels[i], clipPath);
            clipLength = Mathf.Max(clipLength, modelLength);
        }

        EnsureHandOverlayVisible();

        Debug.Log($"[SignBridge] Playing sign \"{fbxPath}\" for {clipLength:F1}s");
        yield return new WaitForSeconds(Mathf.Max(0.5f, clipLength + 0.25f));
    }

    // ─── PRIVATE ─────────────────────────────────────────────────────────

    private void SetupHandCamera()
    {
        if (handCamera != null)
        {
            return;
        }

        // Exclude HandLayer from the main camera so models only appear in the RT
        Camera main = Camera.main;
        if (main != null)
            main.cullingMask &= ~(1 << HandLayer);

        // Dedicated camera for hand rendering
        var camGo = new GameObject("HandRenderCamera");
        handCamera = camGo.AddComponent<Camera>();
        handCamera.clearFlags    = CameraClearFlags.SolidColor;
        handCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent
        handCamera.cullingMask   = 1 << HandLayer;
        handCamera.depth         = -2;     // renders before everything else
        handCamera.orthographic  = true;
        handCamera.fieldOfView   = 50f;
        handCamera.nearClipPlane = 0.01f;
        handCamera.farClipPlane  = 60f;
        handCamera.orthographicSize = 1.25f;
        handCamera.allowMSAA     = true;
        handCamera.allowHDR      = false;

        // Place the camera in an isolated world area (x=1000) so it never
        // accidentally sees any other scene geometry
        camGo.transform.position = HandRenderOrigin + new Vector3(0f, 0.05f, -2.6f);
        camGo.transform.rotation = Quaternion.identity;

        // Keep AA sample count aligned with URP camera target requirements.
        var urpData = camGo.GetComponent<UniversalAdditionalCameraData>();
        if (urpData != null)
        {
            urpData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            urpData.renderPostProcessing = false;
            urpData.renderShadows = false;
        }

        int texW = Mathf.Max(720, Screen.width);
        int texH = Mathf.Max(1280, Screen.height);
        handRenderTexture = new RenderTexture(texW, texH, 16, RenderTextureFormat.ARGB32);
        int aa = Mathf.Max(1, QualitySettings.antiAliasing);
        handRenderTexture.antiAliasing = aa;
        handRenderTexture.useMipMap = false;
        handRenderTexture.autoGenerateMips = false;
        handRenderTexture.Create();
        handCamera.targetTexture = handRenderTexture;

        if (handDisplay != null)
            handDisplay.texture = handRenderTexture;
    }

    private void StopCurrentAnimation()
    {
        StopAllCoroutines();
        DestroyCurrentModels();
        EnsureHandOverlayVisible();
    }

    private static List<string> ResolveSignSequence(string transcript)
    {
        List<string> result = new List<string>();
        string lower = transcript.ToLowerInvariant();

        foreach (var phrase in PhraseToFbx.OrderByDescending(k => k.Key.Length))
        {
            if (lower.Contains(phrase.Key))
            {
                result.Add(phrase.Value);
                lower = lower.Replace(phrase.Key, " ");
            }
        }

        char[] split = { ' ', '\t', '\r', '\n', ',', '.', '?', '!', ';', ':', '\'', '"', '-', '(', ')' };
        string[] tokens = lower.Split(split, System.StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in tokens)
        {
            string normalizedToken = NormalizeToken(token);
            if (TokenAliases.TryGetValue(normalizedToken, out string alias))
                normalizedToken = alias;

            if (WordToFbx.TryGetValue(normalizedToken, out string path))
            {
                result.Add(path);
                continue;
            }

            if (TryResolveDynamicSignPath(normalizedToken, out string dynamicPath))
            {
                result.Add(dynamicPath);
                continue;
            }

            // Spell unknown words using available alphabet signs.
            int beforeFallback = result.Count;
            foreach (char c in normalizedToken)
            {
                if (TryResolveLetterPath(c, out string letterPath))
                {
                    result.Add(letterPath);
                }
            }

            if (result.Count == beforeFallback)
            {
                Debug.LogWarning($"[SignBridge] No full-word sign or letters resolved for token=\"{normalizedToken}\"");
            }
            else
            {
                Debug.Log($"[SignBridge] Word fallback -> letters token=\"{normalizedToken}\" count={result.Count - beforeFallback}");
            }
        }

        if (result.Count == 0 && WordToFbx.TryGetValue("hello", out string fallback))
            result.Add(fallback);

        return result;
    }

    private static readonly string[] SignAssetFolderPrefixes =
    {
        "SignAssets/",
        "SignAssets/FUZE_Technologies_Hands/",
    };

    private static GameObject TryLoadSignPrefab(string mappedPath)
    {
        if (string.IsNullOrWhiteSpace(mappedPath)) return null;

        foreach (string candidate in EnumerateSignPrefabLoadCandidates(mappedPath))
        {
            GameObject prefab = Resources.Load<GameObject>(candidate);
            if (prefab != null) return prefab;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSignPrefabLoadCandidates(string mappedPath)
    {
        string t = mappedPath.Trim();
        yield return t;

        if (!t.StartsWith("SignAssets/", StringComparison.OrdinalIgnoreCase))
        {
            yield return "SignAssets/" + t;
            foreach (string folder in SignAssetFolderPrefixes)
            {
                if (!folder.StartsWith("SignAssets/", StringComparison.Ordinal)) continue;
                yield return folder + t;
            }
        }

        if (t.StartsWith("SignAssets/", StringComparison.OrdinalIgnoreCase))
        {
            string tail = t.Substring("SignAssets/".Length);
            yield return tail;
            foreach (string folder in SignAssetFolderPrefixes)
            {
                if (folder.Equals("SignAssets/", StringComparison.OrdinalIgnoreCase)) continue;
                yield return folder + tail;
            }
        }
    }

    /// <summary>First Resources path that loads for this asset file name (any subfolder under Resources).</summary>
    public static string ResolveSignResourcePathForName(string assetFileName)
    {
        if (string.IsNullOrWhiteSpace(assetFileName)) return null;
        string name = assetFileName.Trim();
        foreach (string candidate in EnumerateSignPrefabLoadCandidates("SignAssets/" + name))
        {
            if (Resources.Load<GameObject>(candidate) != null) return candidate;
        }

        return "SignAssets/" + name;
    }

    private static void ValidateMappings()
    {
        foreach (var kv in WordToFbx)
        {
            ValidateMappedPath(kv.Key, kv.Value);
        }

        foreach (var kv in PhraseToFbx)
        {
            ValidateMappedPath(kv.Key, kv.Value);
        }
    }

    private static bool TryResolveDynamicSignPath(string token, out string mappedPath)
    {
        mappedPath = null;
        if (string.IsNullOrWhiteSpace(token)) return false;
        EnsureDynamicIndex();
        return dynamicWordToPath.TryGetValue(token, out mappedPath);
    }

    private static bool TryResolveLetterPath(char c, out string mappedPath)
    {
        mappedPath = null;
        char lower = char.ToLowerInvariant(c);
        if (lower < 'a' || lower > 'z') return false;

        string letterKey = lower.ToString();
        EnsureDynamicIndex();
        if (dynamicWordToPath.TryGetValue(letterKey, out mappedPath))
            return true;

        string alphabetPath = ResolveSignResourcePathForName("alphabet_" + letterKey);
        GameObject prefab = TryLoadSignPrefab(alphabetPath);
        if (prefab != null)
        {
            mappedPath = alphabetPath;
            return true;
        }

        return false;
    }

    private static void EnsureDynamicIndex()
    {
        if (dynamicWordToPath != null) return;

        dynamicWordToPath = new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyList<GameObject> signAssets = GetAllSignAssetPrefabRoots();
        foreach (GameObject go in signAssets)
        {
            if (go == null) continue;
            string n = go.name.ToLowerInvariant();
            string path = ResolveSignResourcePathForName(go.name);

            if (n.StartsWith("alphabet_", StringComparison.Ordinal) && n.Length >= "alphabet_".Length + 1)
            {
                string letter = n.Substring("alphabet_".Length, 1);
                if (letter.Length == 1)
                {
                    char ch = letter[0];
                    if (ch >= 'a' && ch <= 'z')
                    {
                        RegisterLetterPath(letter, path);
                    }
                }

                continue;
            }

            bool hasHand = n.Contains("(") && (n.Contains("right") || n.Contains("left"));
            bool preferRight = n.Contains("right");
            string stripped = n.Replace("(right)", "").Replace("(left)", "").Trim();
            string bodyForNorm = hasHand ? stripped : n;
            string fullKey = NormalizeToken(bodyForNorm);
            if (!string.IsNullOrEmpty(fullKey))
            {
                RegisterDynamicPathKey(fullKey, path, preferRight);
            }

            string[] parts = (hasHand ? stripped : n).Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in parts)
            {
                string key = NormalizeToken(p);
                if (key.Length < 2 || key == "right" || key == "left") continue;
                RegisterDynamicPathKey(key, path, preferRight);
            }
        }

        RegisterFallbackAlphabetLetters();
    }

    private static void RegisterDynamicPathKey(string key, string path, bool preferThisIfRight)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 2) return;

        if (!dynamicWordToPath.TryGetValue(key, out string existing))
        {
            dynamicWordToPath[key] = path;
            return;
        }

        bool thisRight = path.IndexOf("(right)", StringComparison.OrdinalIgnoreCase) >= 0;
        bool existingRight = existing.IndexOf("(right)", StringComparison.OrdinalIgnoreCase) >= 0;
        if (preferThisIfRight && thisRight && !existingRight)
        {
            dynamicWordToPath[key] = path;
        }
    }

    private static void RegisterLetterPath(string letterKey, string resourcePath)
    {
        if (string.IsNullOrEmpty(letterKey)) return;

        if (!dynamicWordToPath.ContainsKey(letterKey))
        {
            dynamicWordToPath[letterKey] = resourcePath;
        }
    }

    private static void RegisterFallbackAlphabetLetters()
    {
        for (int i = 0; i < 26; i++)
        {
            char c = (char)('a' + i);
            string key = c.ToString();
            if (dynamicWordToPath.ContainsKey(key)) continue;
            if (TryFindAlphabetResourcePath(c, out string path))
            {
                dynamicWordToPath[key] = path;
            }
        }
    }

    private static string StripHandSuffix(string assetName)
    {
        if (string.IsNullOrEmpty(assetName)) return assetName;
        int ir = assetName.IndexOf(" (right)", StringComparison.OrdinalIgnoreCase);
        if (ir >= 0) return assetName.Substring(0, ir).TrimEnd();
        int il = assetName.IndexOf(" (left)", StringComparison.OrdinalIgnoreCase);
        if (il >= 0) return assetName.Substring(0, il).TrimEnd();
        return assetName.TrimEnd();
    }

    private static bool IsAlphabetAssetName(string assetName)
    {
        return assetName != null
            && assetName.Length >= 10
            && assetName.StartsWith("alphabet_", StringComparison.OrdinalIgnoreCase);
    }

    private static string MakeDisplayLabel(string assetName)
    {
        if (TryGetLetterFromAlphabetName(assetName, out char letter))
        {
            return "Letter " + char.ToUpperInvariant(letter);
        }

        string stem = StripHandSuffix(assetName);
        stem = stem.Replace('_', ' ');
        TextInfo ti = CultureInfo.CurrentCulture.TextInfo;
        return ti.ToTitleCase(stem.ToLowerInvariant());
    }

    private static bool TryGetLetterFromAlphabetName(string assetName, out char letter)
    {
        letter = '\0';
        if (!IsAlphabetAssetName(assetName)) return false;
        string rest = assetName.Substring("alphabet_".Length);
        if (rest.Length != 1) return false;
        letter = char.ToLowerInvariant(rest[0]);
        return letter >= 'a' && letter <= 'z';
    }

    private static List<SignCatalogRow> BuildSignCatalogGrouped(bool excludeAlphabet)
    {
        IReadOnlyList<GameObject> signAssets = GetAllSignAssetPrefabRoots();
        Dictionary<string, GameObject> pick = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        foreach (GameObject go in signAssets)
        {
            if (go == null) continue;
            if (excludeAlphabet && IsLetterLearningAssetName(go.name)) continue;

            string stem = StripHandSuffix(go.name);
            if (string.IsNullOrWhiteSpace(stem))
            {
                stem = go.name.Trim();
            }

            if (string.IsNullOrWhiteSpace(stem)) continue;

            string key = stem.Trim().ToLowerInvariant();
            bool prefer = go.name.IndexOf("(right)", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!pick.TryGetValue(key, out GameObject existing))
            {
                pick[key] = go;
                continue;
            }

            bool existingRight = existing.name.IndexOf("(right)", StringComparison.OrdinalIgnoreCase) >= 0;
            if (prefer && !existingRight)
            {
                pick[key] = go;
            }
        }

        List<SignCatalogRow> rows = new List<SignCatalogRow>(pick.Count);
        foreach (GameObject go in pick.Values)
        {
            rows.Add(new SignCatalogRow(MakeDisplayLabel(go.name), ResolveSignResourcePathForName(go.name)));
        }

        rows.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        return rows;
    }

    /// <summary>Learn → Words: every sign prefab under Resources/SignAssets except alphabet clips.</summary>
    public static List<SignCatalogRow> GetWordRowsForWeb()
    {
        return BuildSignCatalogGrouped(excludeAlphabet: true);
    }

    /// <summary>Full catalog under SignAssets plus phrase shortcuts from <see cref="PhraseToFbx"/>.</summary>
    public static List<SignCatalogRow> GetVocabularyRowsForWeb()
    {
        List<SignCatalogRow> rows = BuildSignCatalogGrouped(excludeAlphabet: false);
        HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SignCatalogRow r in rows)
        {
            seenPaths.Add(r.ResourcePath);
        }

        TextInfo ti = CultureInfo.CurrentCulture.TextInfo;
        foreach (KeyValuePair<string, string> kv in PhraseToFbx)
        {
            string path = kv.Value;
            if (string.IsNullOrWhiteSpace(path) || seenPaths.Contains(path)) continue;

            seenPaths.Add(path);
            string lbl = ti.ToTitleCase(kv.Key.Trim().ToLowerInvariant());
            rows.Add(new SignCatalogRow(lbl + " · phrase", path));
        }

        rows.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        return rows;
    }

    /// <summary>Every letter / alphabet clip under SignAssets (including nested folders).</summary>
    public static List<SignCatalogRow> GetAlphabetRowsForWeb()
    {
        IReadOnlyList<GameObject> signAssets = GetAllSignAssetPrefabRoots();
        List<SignCatalogRow> rows = new List<SignCatalogRow>();
        HashSet<string> seenPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (GameObject go in signAssets)
        {
            if (go == null) continue;
            if (!IsLetterLearningAssetName(go.name)) continue;

            string path = ResolveSignResourcePathForName(go.name);
            if (!seenPath.Add(path)) continue;

            rows.Add(new SignCatalogRow(MakeLetterRowLabel(go.name), path));
        }

        rows.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        return rows;
    }

    private static bool IsLetterLearningAssetName(string assetName)
    {
        if (string.IsNullOrEmpty(assetName)) return false;
        string n = assetName.ToLowerInvariant();
        if (n.StartsWith("alphabet_", StringComparison.Ordinal)) return true;
        if (n.StartsWith("letter_", StringComparison.Ordinal)) return true;
        if (n.StartsWith("letter-", StringComparison.Ordinal)) return true;
        return false;
    }

    private static string MakeLetterRowLabel(string assetName)
    {
        if (TryGetLetterFromAlphabetName(assetName, out char letter))
        {
            return "Letter " + char.ToUpperInvariant(letter);
        }

        string stem = StripHandSuffix(assetName).Replace('_', ' ');
        TextInfo ti = CultureInfo.CurrentCulture.TextInfo;
        return ti.ToTitleCase(stem.ToLowerInvariant());
    }

    private static bool TryFindAlphabetResourcePath(char letter, out string resourcePath)
    {
        resourcePath = null;
        char lower = char.ToLowerInvariant(letter);
        if (lower < 'a' || lower > 'z') return false;

        string name = "alphabet_" + lower;
        resourcePath = ResolveSignResourcePathForName(name);
        return Resources.Load<GameObject>(resourcePath) != null;
    }

    private static string NormalizeToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string normalized = raw.Trim().ToLowerInvariant()
            .Replace("{", "")
            .Replace("}", "")
            .Replace("(", "")
            .Replace(")", "");

        char[] chars = normalized.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray();
        return new string(chars);
    }

    private static void ValidateMappedPath(string token, string mappedPath)
    {
        GameObject prefab = TryLoadSignPrefab(mappedPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[SignBridge] Missing sign asset mapping token=\"{token}\" path=\"{mappedPath}\"");
        }
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private void FrameCurrentModels()
    {
        if (currentHandModels.Count == 0 || handCamera == null) return;
        // Keep overlay consistently centered regardless of clip/root-motion content.
        Vector3 target = HandRenderOrigin + new Vector3(0f, overlayCenterY, 0f);
        handCamera.orthographicSize = 1.25f;
        handCamera.transform.position = target + new Vector3(0f, 0f, -2.6f);
        handCamera.transform.LookAt(target);
    }

    private static bool TryGetModelBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        if (root == null) return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool initialized = false;
        foreach (Renderer r in renderers)
        {
            if (r == null || !r.enabled) continue;

            if (!initialized)
            {
                bounds = r.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return initialized;
    }

    private static bool TryGetModelsBounds(List<GameObject> roots, out Bounds bounds)
    {
        bounds = default;
        if (roots == null || roots.Count == 0) return false;

        bool initialized = false;
        foreach (GameObject go in roots)
        {
            if (go == null) continue;
            if (!TryGetModelBounds(go, out Bounds b)) continue;

            if (!initialized)
            {
                bounds = b;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(b);
            }
        }

        return initialized;
    }

    private static string ToLeftVariantPath(string mappedPath)
    {
        if (string.IsNullOrWhiteSpace(mappedPath)) return mappedPath;
        return mappedPath.Replace("(right)", "(left)");
    }

    private void DestroyCurrentModels()
    {
        for (int i = 0; i < currentHandModels.Count; i++)
        {
            if (currentHandModels[i] != null)
                Destroy(currentHandModels[i]);
        }
        currentHandModels.Clear();
        currentHandPaths.Clear();
        currentOverlayAnchors.Clear();
    }

    private static void WarmupAnimatorsOnRoots(IEnumerable<GameObject> roots)
    {
        if (roots == null)
        {
            return;
        }

        foreach (GameObject root in roots)
        {
            if (root == null)
            {
                continue;
            }

            foreach (Animator anim in root.GetComponentsInChildren<Animator>(true))
            {
                anim.enabled = true;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                anim.Rebind();
                anim.Update(0f);
            }

            foreach (Animation a in root.GetComponentsInChildren<Animation>(true))
            {
                a.enabled = true;
            }
        }
    }

    private float PlayAnimationOnModel(GameObject model, string mappedPath)
    {
        if (model == null) return 0f;

        float clipLength = 2.0f;

        Animation legacyAnim = model.GetComponentInChildren<Animation>(true);
        if (legacyAnim != null)
        {
            legacyAnim.cullingType = AnimationCullingType.AlwaysAnimate;
            if (legacyAnim.clip != null)
            {
                clipLength = legacyAnim.clip.length;
                legacyAnim.Play(legacyAnim.clip.name);
            }
            else
            {
                legacyAnim.Play();
            }
            return clipLength;
        }

        Animator animator = model.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.speed = 1f;
            animator.applyRootMotion = false;
            if (animator.runtimeAnimatorController != null
                && animator.runtimeAnimatorController.animationClips != null
                && animator.runtimeAnimatorController.animationClips.Length > 0)
            {
                clipLength = animator.runtimeAnimatorController.animationClips[0].length;
            }
            if (animator.runtimeAnimatorController != null)
            {
                animator.Play(0, 0, 0f);
                animator.Update(0.001f);
                return clipLength;
            }
        }

        if (TryPlayClipFromResources(model, mappedPath, out float loadedLen))
        {
            return loadedLen;
        }

        Debug.LogWarning($"[SignBridge] No playable clip found for: {mappedPath}");
        return clipLength;
    }

    private bool TryPlayClipFromResources(GameObject model, string mappedPath, out float clipLength)
    {
        clipLength = 0f;
        if (model == null || string.IsNullOrWhiteSpace(mappedPath)) return false;

        AnimationClip[] clips = null;
        string clipSourcePath = null;
        foreach (string candidate in EnumerateSignPrefabLoadCandidates(mappedPath.Trim()))
        {
            AnimationClip[] found = Resources.LoadAll<AnimationClip>(candidate);
            if (found != null && found.Length > 0)
            {
                clips = found;
                clipSourcePath = candidate;
                break;
            }
        }

        if ((clips == null || clips.Length == 0) && mappedPath.Contains("(left)"))
        {
            // Some signs only have right-hand clips; mirrored left model should still animate.
            string rightPath = mappedPath.Replace("(left)", "(right)");
            foreach (string candidate in EnumerateSignPrefabLoadCandidates(rightPath))
            {
                AnimationClip[] found = Resources.LoadAll<AnimationClip>(candidate);
                if (found != null && found.Length > 0)
                {
                    clips = found;
                    clipSourcePath = candidate;
                    break;
                }
            }
        }
        if (clips == null || clips.Length == 0) return false;
        if (!string.IsNullOrEmpty(clipSourcePath))
        {
            mappedPath = clipSourcePath;
        }

        AnimationClip chosen = null;
        foreach (AnimationClip c in clips)
        {
            if (c == null) continue;
            string n = c.name.ToLowerInvariant();
            if (n.Contains("__preview__") || n.Contains("t-pose")) continue;
            if (c.length <= 0.05f) continue;
            chosen = c;
            break;
        }
        if (chosen == null) return false;

        if (chosen.legacy)
        {
            Animation anim = model.GetComponent<Animation>();
            if (anim == null) anim = model.AddComponent<Animation>();
            anim.playAutomatically = false;
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            if (anim.GetClip(chosen.name) == null)
                anim.AddClip(chosen, chosen.name);
            if (anim.GetClip(chosen.name) == null) return false;
            anim.clip = anim.GetClip(chosen.name);
            bool played = anim.Play(chosen.name);
            if (!played) return false;
        }
        else
        {
            if (!PlayNonLegacyClip(model, chosen))
                return false;
        }

        clipLength = Mathf.Max(0.5f, chosen.length);
        Debug.Log($"[SignBridge] Clip fallback from Resources: {mappedPath} -> {chosen.name} ({clipLength:F2}s)");
        return true;
    }

    private static bool PlayNonLegacyClip(GameObject model, AnimationClip clip)
    {
        if (model == null || clip == null) return false;

        Animator animator = model.GetComponentInChildren<Animator>(true);
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;

        ClipPlayableRunner runner = model.GetComponent<ClipPlayableRunner>();
        if (runner == null) runner = model.AddComponent<ClipPlayableRunner>();
        return runner.Play(animator, clip);
    }

    private sealed class ClipPlayableRunner : MonoBehaviour
    {
        private PlayableGraph graph;
        private AnimationPlayableOutput output;
        private AnimationClipPlayable playable;

        public bool Play(Animator animator, AnimationClip clip)
        {
            if (animator == null || clip == null) return false;

            StopGraph();
            animator.Rebind();
            animator.Update(0f);
            graph = PlayableGraph.Create("SignClipGraph");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            playable = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(playable);
            graph.Play();
            graph.Evaluate(0.02f);
            animator.Update(0.02f);
            return true;
        }

        private void OnDisable() => StopGraph();
        private void OnDestroy() => StopGraph();

        private void StopGraph()
        {
            if (graph.IsValid())
                graph.Destroy();
        }
    }

    private void EnsureHandOverlayVisible()
    {
        if (handDisplay == null)
        {
            return;
        }

        if (ShouldUseWorldSpacePlayback() && !forceOverlayPlaybackOnce)
        {
            handDisplay.gameObject.SetActive(false);
            return;
        }

        handDisplay.gameObject.SetActive(true);
        if (handRenderTexture != null && handDisplay.texture != handRenderTexture)
        {
            handDisplay.texture = handRenderTexture;
        }

        if (handCamera != null)
        {
            handCamera.enabled = true;
        }
    }

    private void OnDestroy()
    {
        StopCurrentAnimation();

        if (handRenderTexture != null)
        {
            handRenderTexture.Release();
            Destroy(handRenderTexture);
        }

        if (handCamera != null)
            Destroy(handCamera.gameObject);
    }

    private bool ShouldUseWorldSpacePlayback()
    {
        return preferWorldSpacePlayback && arPlacementController != null;
    }

    private GameObject InstantiateSignModel(GameObject prefab, bool isRightHand, bool worldPlayback, Transform worldRoot)
    {
        if (!worldPlayback || worldRoot == null)
        {
            // In overlay mode the hand render camera looks toward +Z.
            // Positive X = screen-right from the camera.
            // Sign language is displayed mirror-style (as if you face the signer),
            // so the signer's RIGHT hand must appear on the VIEWER'S LEFT → negative X offset.
            float overlayX = isRightHand ? -worldHandOffset : worldHandOffset;
            Vector3 worldPos = HandRenderOrigin + new Vector3(overlayX, 0f, 0f);
            GameObject overlayInstance = Instantiate(prefab, worldPos, Quaternion.Euler(0f, 180f, 0f));
            NormalizeOverlayModel(overlayInstance, worldPos);
            return overlayInstance;
        }

        GameObject worldInstance = Instantiate(prefab, worldRoot);
        worldInstance.transform.localPosition = new Vector3(isRightHand ? -worldHandOffset : worldHandOffset, 0f, 0f);
        worldInstance.transform.localRotation = Quaternion.Euler(0f, worldHandYawOffset, 0f);
        worldInstance.transform.localScale = Vector3.one * worldHandUniformScale;
        return worldInstance;
    }

    private static void MirrorLeftModel(GameObject leftModel)
    {
        if (leftModel == null) return;
        Vector3 s = leftModel.transform.localScale;
        leftModel.transform.localScale = new Vector3(-Mathf.Abs(s.x), s.y, s.z);
    }

    private static void NormalizeOverlayModel(GameObject model, Vector3 desiredCenter)
    {
        if (model == null)
        {
            return;
        }

        if (!TryGetModelBounds(model, out Bounds bounds))
        {
            model.transform.position = desiredCenter;
            return;
        }

        float currentHeight = Mathf.Max(0.0001f, bounds.size.y);
        const float targetHeight = 0.62f;
        float scaleFactor = Mathf.Clamp(targetHeight / currentHeight, 0.35f, 2.5f);
        model.transform.localScale *= scaleFactor;

        if (TryGetModelBounds(model, out bounds))
        {
            Vector3 delta = desiredCenter - bounds.center;
            model.transform.position += delta;
        }
        else
        {
            model.transform.position = desiredCenter;
        }
    }

    private void LateUpdate()
    {
        // During Web Learn playback we force overlay mode; LateUpdate must still stabilize models.
        if (ShouldUseWorldSpacePlayback() && !forceOverlayPlaybackOnce)
        {
            return;
        }

        // Re-lock model roots to their overlay anchors so clips cannot drift off-screen.
        int count = Mathf.Min(currentHandModels.Count, currentOverlayAnchors.Count);
        for (int i = 0; i < count; i++)
        {
            GameObject model = currentHandModels[i];
            if (model == null)
            {
                continue;
            }
            bool singleHand = count == 1;
            bool isRightHand = i == 0;
            Vector3 desiredAnchor = GetOverlayAnchorPosition(isRightHand, singleHand);
            currentOverlayAnchors[i] = desiredAnchor;
            StabilizeOverlayModel(model, desiredAnchor);
        }
    }

    private static bool IsSingleHandLetterPath(string mappedPath)
    {
        if (string.IsNullOrWhiteSpace(mappedPath))
        {
            return false;
        }

        string path = mappedPath.ToLowerInvariant().Trim();
        if (path.StartsWith("signassets/"))
        {
            path = path.Substring("signassets/".Length);
        }

        return path.StartsWith("alphabet_");
    }

    private static void StabilizeOverlayModel(GameObject model, Vector3 desiredCenter)
    {
        if (model == null)
        {
            return;
        }

        if (!TryGetModelBounds(model, out Bounds bounds))
        {
            model.transform.position = desiredCenter;
            return;
        }

        const float maxHeight = 0.95f;
        float currentHeight = Mathf.Max(0.0001f, bounds.size.y);
        if (currentHeight > maxHeight)
        {
            float downScale = Mathf.Clamp(maxHeight / currentHeight, 0.7f, 1f);
            model.transform.localScale *= downScale;

            if (!TryGetModelBounds(model, out bounds))
            {
                model.transform.position = desiredCenter;
                return;
            }
        }

        Vector3 delta = desiredCenter - bounds.center;
        model.transform.position += delta;
    }

    private Vector3 GetOverlayAnchorPosition(bool isRightHand, bool singleHand)
    {
        float halfSpacing = overlayPairSpacing * 0.5f;
        // Mirror: signer's right hand appears on viewer's left (negative X).
        float x = singleHand ? 0f : (isRightHand ? -halfSpacing : halfSpacing);
        return HandRenderOrigin + new Vector3(x, overlayCenterY, 0f);
    }

    /// <summary>Display labels for word-style lists (all Resources/SignAssets clips except alphabet).</summary>
    public static List<string> GetLearnableWordTokenList()
    {
        return GetWordRowsForWeb().Select(r => r.Label).ToList();
    }

    /// <summary>Phrase keys for vocabulary / phrase practice (maps to <see cref="PhraseToFbx"/>).</summary>
    public static List<string> GetLearnablePhraseList()
    {
        return PhraseToFbx.Keys.OrderByDescending(k => k.Length).ToList();
    }
}
