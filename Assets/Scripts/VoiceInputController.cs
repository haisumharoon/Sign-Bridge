using System;
using UnityEngine;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using UnityEngine.Windows.Speech;
#endif

public class VoiceInputController : MonoBehaviour
{
    [Header("Fallback Transcript Input")]
    [SerializeField] private bool useEditorSimulation = true;
    [SerializeField] private string[] demoPhrases =
    {
        "hello how are you",
        "please help me",
        "thank you very much",
        "where is the hospital"
    };

    [Header("State")]
    [SerializeField] private bool isListening;
    [SerializeField] private int selectedDemoIndex;

    public event Action<bool> ListeningStateChanged;
    public event Action<string> PartialTranscriptReceived;
    public event Action<string> FinalTranscriptReceived;
    public event Action<string> SpeechErrorReceived;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private DictationRecognizer dictationRecognizer;
#endif

    public bool IsListening => isListening;

    private void Awake()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (!useEditorSimulation)
        {
            dictationRecognizer = new DictationRecognizer();
            dictationRecognizer.DictationResult += OnDictationResult;
            dictationRecognizer.DictationHypothesis += OnDictationHypothesis;
            dictationRecognizer.DictationError += OnDictationError;
            dictationRecognizer.DictationComplete += OnDictationComplete;
        }
#endif
    }

    public void ToggleListening()
    {
        if (isListening)
        {
            StopListening();
        }
        else
        {
            StartListening();
        }
    }

    public void StartListening()
    {
        if (isListening) return;

#if UNITY_ANDROID && KKSPEECH_PRESENT
        StartKkSpeechRecording();
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (useEditorSimulation)
        {
            EmitDemoTranscript();
        }
        else
        {
            StartWindowsDictation();
        }
#else
        EmitDemoTranscript();
#endif
    }

    public void StopListening()
    {
        if (!isListening) return;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (dictationRecognizer != null && dictationRecognizer.Status == SpeechSystemStatus.Running)
        {
            dictationRecognizer.Stop();
        }
#endif

        SetListeningState(false);
    }

    public void UseNextDemoPhrase()
    {
        if (demoPhrases == null || demoPhrases.Length == 0) return;
        selectedDemoIndex = (selectedDemoIndex + 1) % demoPhrases.Length;
        FinalTranscriptReceived?.Invoke(demoPhrases[selectedDemoIndex]);
    }

#if UNITY_ANDROID && KKSPEECH_PRESENT
    private void StartKkSpeechRecording()
    {
        if (!KKSpeech.SpeechRecognizer.ExistsOnDevice())
        {
            SpeechErrorReceived?.Invoke("Speech recognition is unavailable on this device.");
            return;
        }

        KKSpeech.SpeechRecognizer.StartRecording(true);
        SetListeningState(true);
    }
#endif

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private void StartWindowsDictation()
    {
        if (dictationRecognizer == null)
        {
            SpeechErrorReceived?.Invoke("Dictation recognizer was not initialized.");
            return;
        }

        try
        {
            dictationRecognizer.Start();
            SetListeningState(true);
        }
        catch (Exception ex)
        {
            SpeechErrorReceived?.Invoke($"Failed to start dictation: {ex.Message}");
            SetListeningState(false);
        }
    }

    private void OnDictationResult(string text, ConfidenceLevel confidence)
    {
        FinalTranscriptReceived?.Invoke(text);
        StopListening();
    }

    private void OnDictationHypothesis(string text)
    {
        PartialTranscriptReceived?.Invoke(text);
    }

    private void OnDictationError(string error, int hresult)
    {
        SpeechErrorReceived?.Invoke($"{error} ({hresult})");
        StopListening();
    }

    private void OnDictationComplete(DictationCompletionCause cause)
    {
        if (cause != DictationCompletionCause.Complete)
        {
            SpeechErrorReceived?.Invoke($"Dictation ended: {cause}");
        }

        StopListening();
    }
#endif

    private void EmitDemoTranscript()
    {
        if (demoPhrases == null || demoPhrases.Length == 0)
        {
            SpeechErrorReceived?.Invoke("No demo phrase configured.");
            return;
        }

        SetListeningState(true);
        string phrase = demoPhrases[Mathf.Clamp(selectedDemoIndex, 0, demoPhrases.Length - 1)];
        PartialTranscriptReceived?.Invoke(phrase);
        FinalTranscriptReceived?.Invoke(phrase);
        SetListeningState(false);
    }

    private void SetListeningState(bool value)
    {
        isListening = value;
        ListeningStateChanged?.Invoke(isListening);
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (dictationRecognizer != null)
        {
            dictationRecognizer.DictationResult -= OnDictationResult;
            dictationRecognizer.DictationHypothesis -= OnDictationHypothesis;
            dictationRecognizer.DictationError -= OnDictationError;
            dictationRecognizer.DictationComplete -= OnDictationComplete;
            dictationRecognizer.Dispose();
            dictationRecognizer = null;
        }
#endif
    }
}
