using System;

[Serializable]
public class SentenceScoreResult
{
    public int score;
    public string label;
    public string reason;

    public static SentenceScoreResult Create(int rawScore, string reasonText)
    {
        int clamped = Math.Max(0, Math.Min(100, rawScore));
        return new SentenceScoreResult
        {
            score = clamped,
            label = ToLabel(clamped),
            reason = string.IsNullOrWhiteSpace(reasonText) ? "Scored from transcript quality." : reasonText
        };
    }

    public static string ToLabel(int scoreValue)
    {
        if (scoreValue >= 75) return "Good";
        if (scoreValue >= 45) return "Okay";
        return "Bad";
    }
}
