using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Offline speech recognition via Vosk (vosk-android AAR + bundled model).
/// The model lives in StreamingAssets/vosk-model-en/ and is copied to
/// Application.persistentDataPath on first run so Vosk can open it as a
/// regular file path.
///
/// Usage:
///   1. Call Initialize() once (as a StartCoroutine) and wait for OnReady.
///   2. Call StartListening() to begin recording via Unity Microphone.
///   3. Call StopAndRecognize() to stop and get the final transcript.
///   4. Subscribe to OnResult, OnPartial, OnError events.
/// </summary>
public class VoskSpeechManager : MonoBehaviour
{
    public event Action<string> OnResult;
    public event Action<string> OnPartial;
    public event Action<string> OnError;
    public event Action         OnReady;

    public bool IsReady     => _isReady;
    public bool IsListening => _isListening;

    private const string ModelSubfolder = "vosk-model-en";
    private const int    SampleRate     = 16000;
    private const int    BufferSeconds  = 60;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject _model;
    private AndroidJavaObject _recognizer;
#endif

    private bool      _isReady;
    private bool      _isListening;
    private AudioClip _micClip;
    private int       _lastPos;
    private string    _lastPartialText = "";

    private static readonly string[] ModelFiles =
    {
        "am/final.mdl",
        "conf/mfcc.conf",
        "conf/model.conf",
        "graph/Gr.fst",
        "graph/HCLr.fst",
        "graph/disambig_tid.int",
        "graph/phones/word_boundary.int",
        "ivector/final.dubm",
        "ivector/final.ie",
        "ivector/final.mat",
        "ivector/global_cmvn.stats",
        "ivector/online_cmvn.conf",
        "ivector/splice.conf"
    };

    // ─── PUBLIC API ──────────────────────────────────────────────────────────

    public IEnumerator Initialize()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string modelDest = Path.Combine(Application.persistentDataPath, ModelSubfolder);
        Debug.Log($"[Vosk] streamingAssetsPath={Application.streamingAssetsPath}");
        Debug.Log($"[Vosk] persistentDataPath={Application.persistentDataPath}");

        if (!HasCompleteModel(modelDest, out string missingFile))
        {
            Debug.Log($"[Vosk] Model incomplete ({missingFile ?? "unknown"}). Re-extracting…");
            TryDeleteDirectory(modelDest);
            yield return StartCoroutine(ExtractModel(modelDest));
        }

        if (!HasCompleteModel(modelDest, out missingFile))
        {
            Debug.LogError($"[Vosk] Model extraction failed. Missing: {missingFile}");
            OnError?.Invoke("Speech model extraction failed");
            yield break;
        }

        Debug.Log($"[Vosk] Loading model from: {modelDest}");
        LogModelSummary(modelDest);
        if (!TryInitializeRecognizer(modelDest, out string initError))
        {
            Debug.LogWarning("[Vosk] Init failed on first attempt. Re-extracting model and retrying once.");
            TryDeleteDirectory(modelDest);
            yield return StartCoroutine(ExtractModel(modelDest));

            if (!HasCompleteModel(modelDest, out missingFile))
            {
                Debug.LogError($"[Vosk] Model extraction failed after retry. Missing: {missingFile}");
                OnError?.Invoke("Speech model extraction failed");
                yield break;
            }

            LogModelSummary(modelDest);
            if (!TryInitializeRecognizer(modelDest, out initError))
            {
                Debug.LogError($"[Vosk] Init error after retry: {initError}");
                OnError?.Invoke(BuildFriendlyInitError(initError));
                yield break;
            }
        }

        Debug.Log("[Vosk] Ready.");
#else
        // In-editor: no Vosk, demo mode used instead.
        yield return null;
        Debug.Log("[Vosk] Editor mode – Vosk disabled, demo phrases used.");
#endif
        _isReady = true;
        OnReady?.Invoke();
    }

    /// <summary>Start recording. Call after OnReady fires.</summary>
    public void StartListening()
    {
        if (!_isReady || _isListening) return;

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            OnError?.Invoke("No microphone found on device");
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (_recognizer != null)
            _recognizer.Call("reset");
#endif
        _micClip  = Microphone.Start(null, true, BufferSeconds, SampleRate);
        if (_micClip == null)
        {
            OnError?.Invoke("Microphone failed to start");
            return;
        }
        _lastPos  = 0;
        _lastPartialText = "";
        _isListening = true;
        StartCoroutine(AudioFeedLoop());
        Debug.Log("[Vosk] StartListening");
    }

    /// <summary>Stop recording and fire OnResult / OnError with the transcript.</summary>
    public void StopAndRecognize()
    {
        if (!_isListening) return;

        _isListening = false;
        // AudioFeedLoop exits naturally when _isListening becomes false.

        if (Microphone.IsRecording(null))
            Microphone.End(null);

#if UNITY_ANDROID && !UNITY_EDITOR
        if (_recognizer == null)
        {
            OnError?.Invoke("Speech engine unavailable");
            return;
        }

        string finalJson = _recognizer.Call<string>("getFinalResult");
        _recognizer.Call("reset");

        string text = ExtractText(finalJson);
        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(_lastPartialText))
        {
            text = _lastPartialText.Trim();
            Debug.Log($"[Vosk] Using partial fallback as final: \"{text}\"");
        }
        Debug.Log($"[Vosk] Final JSON: {finalJson}  →  \"{text}\"");

        if (!string.IsNullOrWhiteSpace(text))
            OnResult?.Invoke(text);
        else
            OnError?.Invoke("No speech detected – tap to try again");
#else
        // Editor fallback handled by caller; nothing to do here.
        OnError?.Invoke("Vosk not available in editor");
#endif
    }

    // ─── AUDIO PROCESSING LOOP ───────────────────────────────────────────────

    private IEnumerator AudioFeedLoop()
    {
        int bufLen = BufferSeconds * SampleRate;

        while (_isListening)
        {
            yield return new WaitForSeconds(0.15f);

            if (_micClip == null || !Microphone.IsRecording(null))
            {
                Debug.LogWarning("[Vosk] Mic stopped unexpectedly.");
                _isListening = false;
                OnError?.Invoke("Microphone stopped unexpectedly");
                break;
            }

            int currentPos = Microphone.GetPosition(null);
            if (currentPos == _lastPos) continue;

            int count = currentPos > _lastPos
                ? currentPos - _lastPos
                : bufLen - _lastPos + currentPos;

            if (count <= 0) continue;

            float[] samples = new float[count];
            _micClip.GetData(samples, _lastPos % bufLen);
            _lastPos = currentPos;

#if UNITY_ANDROID && !UNITY_EDITOR
            short[] pcm = ToPCM16Short(samples);

            try
            {
                bool accepted = AcceptWaveformCompat(pcm);
                if (accepted)
                {
                    string resultJson = _recognizer.Call<string>("getResult");
                    string resultText = ExtractText(resultJson);
                    if (!string.IsNullOrWhiteSpace(resultText))
                    {
                        _lastPartialText = resultText;
                        OnPartial?.Invoke(resultText);
                    }
                }
                else
                {
                    string partialJson = _recognizer.Call<string>("getPartialResult");
                    string partialText = ExtractPartial(partialJson);
                    if (!string.IsNullOrWhiteSpace(partialText))
                    {
                        _lastPartialText = partialText;
                        OnPartial?.Invoke(partialText);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Vosk] acceptWaveForm error: {e.Message}");
            }
#endif
        }
    }

    // ─── MODEL EXTRACTION ────────────────────────────────────────────────────

    private IEnumerator ExtractModel(string destPath)
    {
        Directory.CreateDirectory(destPath);
        string srcBase = Application.streamingAssetsPath + "/" + ModelSubfolder;

        foreach (string rel in ModelFiles)
        {
            string url   = srcBase + "/" + rel.Replace("\\", "/");
            string local = Path.Combine(destPath, rel.Replace("/", Path.DirectorySeparatorChar.ToString()));

            string dir = Path.GetDirectoryName(local);
            if (dir != null) Directory.CreateDirectory(dir);

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    File.WriteAllBytes(local, req.downloadHandler.data);
                    Debug.Log($"[Vosk] Extracted: {rel} ({req.downloadHandler.data?.Length ?? 0} bytes)");
                }
                else
                {
                    Debug.LogError($"[Vosk] Failed to extract {rel}: {req.error}");
                }
            }
        }
    }

    private static bool HasCompleteModel(string modelPath, out string missingFile)
    {
        missingFile = null;

        if (!Directory.Exists(modelPath))
        {
            missingFile = "<model directory>";
            return false;
        }

        foreach (string rel in ModelFiles)
        {
            string normalizedRel = rel.Replace("/", Path.DirectorySeparatorChar.ToString());
            string fullPath = Path.Combine(modelPath, normalizedRel);
            FileInfo fileInfo = new FileInfo(fullPath);
            if (!fileInfo.Exists || fileInfo.Length <= 0)
            {
                missingFile = rel;
                return false;
            }
        }

        return true;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Vosk] Failed to delete stale model dir ({path}): {e.Message}");
        }
    }

    private bool TryInitializeRecognizer(string modelPath, out string initError)
    {
        initError = "";

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            _recognizer?.Dispose();
            _model?.Dispose();
            _recognizer = null;
            _model = null;

            using (AndroidJavaClass _ = new AndroidJavaClass("org.vosk.LibVosk")) { }
            using (AndroidJavaClass _ = new AndroidJavaClass("com.sun.jna.Pointer")) { }

            using (AndroidJavaClass voskClass = new AndroidJavaClass("org.vosk.LibVosk"))
            {
                // Some Vosk Android builds expose setLogLevel(int), others do not.
                // Treat it as optional so recognizer init remains version-compatible.
                TrySetOptionalLogLevel(voskClass);
            }

            _model = new AndroidJavaObject("org.vosk.Model", modelPath);
            _recognizer = new AndroidJavaObject("org.vosk.Recognizer", _model, (float)SampleRate);
            return true;
        }
        catch (Exception e)
        {
            initError = e.ToString();
            Debug.LogError($"[Vosk] TryInitializeRecognizer failed: {initError}");
            return false;
        }
#else
        initError = "Recognizer initialization is only available on Android device runtime.";
        return false;
#endif
    }

    private static string BuildFriendlyInitError(string rawError)
    {
        if (string.IsNullOrEmpty(rawError))
        {
            return "Speech engine failed to start";
        }

        string lowered = rawError.ToLowerInvariant();
        if (lowered.Contains("classnotfoundexception") || lowered.Contains("noclassdeffounderror"))
        {
            return "Speech engine files missing from Android build";
        }

        if (lowered.Contains("unsatisfiedlinkerror") || lowered.Contains("dlopen failed"))
        {
            return "Speech engine native library failed to load (ABI/plugin issue)";
        }

        if (lowered.Contains("permission"))
        {
            return "Microphone permission blocked speech startup";
        }

        if (lowered.Contains("model") || lowered.Contains("open") || lowered.Contains("path"))
        {
            return "Not able to start speech model";
        }

        return "Speech engine failed to start";
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    private static short[] ToPCM16Short(float[] samples)
    {
        short[] shorts = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            shorts[i] = (short)Mathf.Clamp(samples[i] * 32767f, -32768f, 32767f);
        }
        return shorts;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private bool AcceptWaveformCompat(short[] pcm)
    {
        if (_recognizer == null || pcm == null || pcm.Length == 0) return false;

        // Try the short[] array signature natively supported by Vosk Android
        try
        {
            return _recognizer.Call<bool>("acceptWaveForm", pcm, pcm.Length);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Vosk] acceptWaveForm(short[], int) failed: {e.Message}");
        }

        // Older or alternative bindings might not take the length argument
        try
        {
            return _recognizer.Call<bool>("acceptWaveForm", pcm);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Vosk] acceptWaveForm(short[]) failed: {e.Message}");
        }

        return false;
    }
#endif

    /// <summary>Parses {"text": "..."} JSON returned by Vosk.</summary>
    private static string ExtractText(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";

        return ExtractQuotedJsonValue(json, "text");
    }

    private static string ExtractPartial(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";

        return ExtractQuotedJsonValue(json, "partial");
    }

    private static string ExtractQuotedJsonValue(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return "";

        int ti = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (ti < 0) return "";
        int ci = json.IndexOf(':', ti + key.Length + 2);
        if (ci < 0) return "";
        int q1 = json.IndexOf('"', ci + 1);
        if (q1 < 0) return "";
        int q2 = json.IndexOf('"', q1 + 1);
        if (q2 < 0) return "";
        return json.Substring(q1 + 1, q2 - q1 - 1).Trim();
    }

    private void OnDestroy()
    {
        _isListening = false;
        if (Microphone.IsRecording(null))
            Microphone.End(null);

#if UNITY_ANDROID && !UNITY_EDITOR
        _recognizer?.Dispose();
        _model?.Dispose();
#endif
    }

    private static void LogModelSummary(string modelPath)
    {
        try
        {
            long totalBytes = 0;
            int fileCount = 0;
            foreach (string rel in ModelFiles)
            {
                string normalizedRel = rel.Replace("/", Path.DirectorySeparatorChar.ToString());
                string fullPath = Path.Combine(modelPath, normalizedRel);
                if (File.Exists(fullPath))
                {
                    totalBytes += new FileInfo(fullPath).Length;
                    fileCount++;
                }
            }

            Debug.Log($"[Vosk] Model summary: {fileCount}/{ModelFiles.Length} files, {totalBytes} bytes");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Vosk] Failed to compute model summary: {e.Message}");
        }
    }

    private static void TrySetOptionalLogLevel(AndroidJavaClass voskClass)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (voskClass == null) return;

        try
        {
            voskClass.CallStatic("setLogLevel", 0);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Vosk] setLogLevel unavailable in this Vosk build: {e.Message}");
        }
#endif
    }
}
