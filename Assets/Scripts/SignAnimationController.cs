using System;
using System.Collections.Generic;
using UnityEngine;

public class SignAnimationController : MonoBehaviour
{
    [Serializable]
    private struct PhraseAnimationMap
    {
        public string phrase;
        public string stateName;
    }

    [SerializeField] private Animator targetAnimator;
    [SerializeField] private string defaultStateName = "Idle";
    [SerializeField] private Transform signAnchor;
    [SerializeField] private bool useFbxSignFallback = true;
    [SerializeField] private PhraseAnimationMap[] phraseMappings =
    {
        new PhraseAnimationMap { phrase = "hello", stateName = "X" },
        new PhraseAnimationMap { phrase = "thank you", stateName = "why" },
        new PhraseAnimationMap { phrase = "letter v", stateName = "V" }
    };

    private Dictionary<string, string> phraseToState;
    private Dictionary<string, string> phraseToFbx;
    private GameObject activeSignVisual;

    private void Awake()
    {
        phraseToState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        phraseToFbx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "hello", "hello (right)" },
            { "thank you", "thank you (right)" },
            { "please", "please (right)" },
            { "help", "help (right)" },
            { "what", "what (right)" },
            { "where", "where (right)" },
            { "why", "why (right)" }
        };
        foreach (PhraseAnimationMap mapping in phraseMappings)
        {
            if (!string.IsNullOrWhiteSpace(mapping.phrase) && !string.IsNullOrWhiteSpace(mapping.stateName))
            {
                phraseToState[mapping.phrase.Trim()] = mapping.stateName.Trim();
            }
        }
    }

    public void PlayForTranscript(string transcript)
    {
        if (targetAnimator != null)
        {
            string state = ResolveState(transcript);
            targetAnimator.CrossFade(state, 0.1f);
        }
        else if (useFbxSignFallback)
        {
            ShowFbxSign(transcript);
        }
    }

    private string ResolveState(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return defaultStateName;
        }

        string lower = transcript.ToLowerInvariant();
        foreach (KeyValuePair<string, string> entry in phraseToState)
        {
            if (lower.Contains(entry.Key.ToLowerInvariant()))
            {
                return entry.Value;
            }
        }

        return defaultStateName;
    }

    private void ShowFbxSign(string transcript)
    {
        string fbxName = ResolveFbxName(transcript);
        if (string.IsNullOrWhiteSpace(fbxName))
        {
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(fbxName);
        if (prefab == null)
        {
            Debug.LogWarning($"FBX sign visual not found in Resources: {fbxName}");
            return;
        }

        if (signAnchor == null)
        {
            GameObject anchor = GameObject.Find("SignAnchor");
            if (anchor == null)
            {
                anchor = new GameObject("SignAnchor");
                anchor.transform.position = new Vector3(0f, -0.8f, 2.6f);
            }
            signAnchor = anchor.transform;
        }

        if (activeSignVisual != null)
        {
            Destroy(activeSignVisual);
        }

        activeSignVisual = Instantiate(prefab, signAnchor);
        activeSignVisual.name = $"Sign_{fbxName}";
        activeSignVisual.transform.localPosition = Vector3.zero;
        activeSignVisual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        activeSignVisual.transform.localScale = Vector3.one * 0.011f;
    }

    private string ResolveFbxName(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return null;
        }

        string lower = transcript.ToLowerInvariant();
        foreach (KeyValuePair<string, string> entry in phraseToFbx)
        {
            if (lower.Contains(entry.Key))
            {
                return entry.Value;
            }
        }

        return "hello (right)";
    }
}
