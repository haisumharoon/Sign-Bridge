using System;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class SentenceScoringService : MonoBehaviour
{
    [Header("Gemini")]
    [SerializeField] private string apiKey = "";
    [SerializeField] private string modelName = "gemini-1.5-flash";
    [SerializeField] private float requestTimeoutSeconds = 8f;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(apiKey);

    public void SetApiKey(string key)
    {
        apiKey = key?.Trim() ?? "";
    }

    public void RefineTranscript(string transcript, Action<string> onResult)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            onResult?.Invoke(transcript);
            return;
        }

        if (!HasApiKey)
        {
            onResult?.Invoke(transcript);
            return;
        }

        StartCoroutine(RefineWithGeminiCoroutine(transcript, onResult));
    }

    public void ScoreSentence(string transcript, Action<SentenceScoreResult> onResult)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            onResult?.Invoke(SentenceScoreResult.Create(0, "No speech text received."));
            return;
        }

        if (!HasApiKey)
        {
            onResult?.Invoke(LocalFallbackScore(transcript, "Gemini key missing. Using local score."));
            return;
        }

        StartCoroutine(ScoreWithGeminiCoroutine(transcript, onResult));
    }

    private IEnumerator ScoreWithGeminiCoroutine(string transcript, Action<SentenceScoreResult> onResult)
    {
        string uri = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
        string prompt = "Score the following sentence quality for clarity and grammar from 0-100. " +
                        "Return strict JSON with fields: score (number), label (Good/Okay/Bad), reason (short string). " +
                        $"Sentence: \"{transcript}\"";

        GeminiRequest requestBody = new GeminiRequest
        {
            contents = new GeminiContent[]
            {
                new GeminiContent
                {
                    parts = new GeminiPart[]
                    {
                        new GeminiPart { text = prompt }
                    }
                }
            }
        };

        string payload = JsonUtility.ToJson(requestBody);
        using UnityWebRequest request = new UnityWebRequest(uri, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = Mathf.CeilToInt(requestTimeoutSeconds);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            onResult?.Invoke(LocalFallbackScore(transcript, $"Gemini unavailable ({request.error}). Local score used."));
            yield break;
        }

        string body = request.downloadHandler.text;
        SentenceScoreResult parsed = TryParseGeminiResult(body);
        if (parsed == null)
        {
            onResult?.Invoke(LocalFallbackScore(transcript, "Unable to parse Gemini response. Local score used."));
            yield break;
        }

        onResult?.Invoke(parsed);
    }

    private IEnumerator RefineWithGeminiCoroutine(string transcript, Action<string> onResult)
    {
        string uri = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
        string prompt =
            "Rewrite the sentence to improve grammar while preserving meaning. " +
            "Return strict JSON only: {\"refined\":\"<sentence>\"}. " +
            $"Sentence: \"{transcript}\"";

        GeminiRequest requestBody = new GeminiRequest
        {
            contents = new GeminiContent[]
            {
                new GeminiContent
                {
                    parts = new GeminiPart[]
                    {
                        new GeminiPart { text = prompt }
                    }
                }
            }
        };

        string payload = JsonUtility.ToJson(requestBody);
        using UnityWebRequest request = new UnityWebRequest(uri, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = Mathf.CeilToInt(requestTimeoutSeconds);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            onResult?.Invoke(transcript);
            yield break;
        }

        string body = request.downloadHandler.text;
        string refined = TryParseRefinedSentence(body);
        onResult?.Invoke(string.IsNullOrWhiteSpace(refined) ? transcript : refined);
    }

    private SentenceScoreResult TryParseGeminiResult(string responseBody)
    {
        // Gemini may wrap model output text in candidates/content/parts.
        Match textMatch = Regex.Match(responseBody, "\"text\"\\s*:\\s*\"(?<t>(?:\\\\.|[^\"])*)\"");
        if (!textMatch.Success)
        {
            return null;
        }

        string escapedText = textMatch.Groups["t"].Value;
        string modelText = escapedText.Replace("\\n", "\n").Replace("\\\"", "\"");
        Match scoreMatch = Regex.Match(modelText, "\"score\"\\s*:\\s*(?<s>\\d+)");
        Match labelMatch = Regex.Match(modelText, "\"label\"\\s*:\\s*\"(?<l>[^\"]+)\"");
        Match reasonMatch = Regex.Match(modelText, "\"reason\"\\s*:\\s*\"(?<r>[^\"]+)\"");

        if (!scoreMatch.Success)
        {
            return null;
        }

        int score = int.Parse(scoreMatch.Groups["s"].Value);
        string reason = reasonMatch.Success ? reasonMatch.Groups["r"].Value : "Gemini score computed.";
        SentenceScoreResult result = SentenceScoreResult.Create(score, reason);

        if (labelMatch.Success)
        {
            result.label = labelMatch.Groups["l"].Value;
        }

        return result;
    }

    private string TryParseRefinedSentence(string responseBody)
    {
        Match textMatch = Regex.Match(responseBody, "\"text\"\\s*:\\s*\"(?<t>(?:\\\\.|[^\"])*)\"");
        if (!textMatch.Success)
        {
            return null;
        }

        string escapedText = textMatch.Groups["t"].Value;
        string modelText = escapedText.Replace("\\n", "\n").Replace("\\\"", "\"");
        Match refinedMatch = Regex.Match(modelText, "\"refined\"\\s*:\\s*\"(?<r>[^\"]+)\"");
        if (!refinedMatch.Success)
        {
            return null;
        }

        return refinedMatch.Groups["r"].Value.Trim();
    }

    private SentenceScoreResult LocalFallbackScore(string transcript, string reasonPrefix)
    {
        string trimmed = transcript.Trim();
        int words = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        int baseScore = Mathf.Clamp(20 + words * 12, 0, 100);

        if (trimmed.EndsWith(".") || trimmed.EndsWith("?") || trimmed.EndsWith("!"))
        {
            baseScore = Mathf.Min(100, baseScore + 10);
        }

        if (words <= 2)
        {
            baseScore = Mathf.Max(25, baseScore - 20);
        }

        return SentenceScoreResult.Create(baseScore, $"{reasonPrefix} ({words} words)");
    }

    [Serializable]
    private class GeminiRequest
    {
        public GeminiContent[] contents;
    }

    [Serializable]
    private class GeminiContent
    {
        public GeminiPart[] parts;
    }

    [Serializable]
    private class GeminiPart
    {
        public string text;
    }
}
