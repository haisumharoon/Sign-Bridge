using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Supplies labels for Learn / Vocabulary lists. Web UI uses resource paths from
/// <see cref="SignLanguageController.GetWordRowsForWeb"/> / <see cref="SignLanguageController.GetVocabularyRowsForWeb"/> for playback.
/// </summary>
public static class LearnContentProvider
{
    public static List<string> GetWordTokens()
    {
        return SignLanguageController.GetLearnableWordTokenList();
    }

    public static List<string> GetPhraseTokens()
    {
        return SignLanguageController.GetLearnablePhraseList();
    }

    /// <summary>Vocabulary screen: full SignAssets catalog plus phrase rows (labels only).</summary>
    public static List<string> GetVocabularyTokens()
    {
        return SignLanguageController.GetVocabularyRowsForWeb().Select(r => r.Label).ToList();
    }

    public static List<string> GetAlphabetTokens()
    {
        return Enumerable.Range('a', 26).Select(c => ((char)c).ToString()).ToList();
    }
}
