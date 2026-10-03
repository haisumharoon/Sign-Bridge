using System;
using UnityEngine;

/// <summary>
/// Persisted appearance: Web shell theme and color-vision / contrast modes for Unity UI + AR camera (via post volume).
/// </summary>
public static class SignBridgeAppearance
{
    public const string PrefTheme = "SignBridge.Appearance.Theme";
    public const string PrefColorBlind = "SignBridge.Appearance.ColorBlind";

    public enum ThemeId
    {
        Dark = 0,
        Light = 1,
    }

    /// <summary>Accessibility modes for camera + UI tints (approximate assist, not medical calibration).</summary>
    public enum ColorBlindId
    {
        None = 0,
        HighContrast = 1,
        Protanopia = 2,
        Deuteranopia = 3,
        Tritanopia = 4,
    }

    public static event Action Changed;

    public static ThemeId Theme { get; private set; } = ThemeId.Dark;
    public static ColorBlindId ColorBlind { get; private set; } = ColorBlindId.None;

    public static void LoadFromPrefs()
    {
        Theme = (ThemeId)Mathf.Clamp(PlayerPrefs.GetInt(PrefTheme, 0), 0, 1);
        ColorBlind = (ColorBlindId)Mathf.Clamp(PlayerPrefs.GetInt(PrefColorBlind, 0), 0, 4);
    }

    public static void Save(ThemeId theme, ColorBlindId colorBlind)
    {
        Theme = theme;
        ColorBlind = colorBlind;
        PlayerPrefs.SetInt(PrefTheme, (int)theme);
        PlayerPrefs.SetInt(PrefColorBlind, (int)colorBlind);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static bool TryParseTheme(string s, out ThemeId theme)
    {
        theme = ThemeId.Dark;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        s = s.Trim().ToLowerInvariant();
        if (s == "light" || s == "1")
        {
            theme = ThemeId.Light;
            return true;
        }

        if (s == "dark" || s == "0")
        {
            theme = ThemeId.Dark;
            return true;
        }

        return false;
    }

    public static bool TryParseColorBlind(string s, out ColorBlindId mode)
    {
        mode = ColorBlindId.None;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        s = s.Trim().ToLowerInvariant();
        switch (s)
        {
            case "none":
            case "0":
                mode = ColorBlindId.None;
                return true;
            case "highcontrast":
            case "high_contrast":
            case "1":
                mode = ColorBlindId.HighContrast;
                return true;
            case "protanopia":
            case "proto":
            case "2":
                mode = ColorBlindId.Protanopia;
                return true;
            case "deuteranopia":
            case "deuto":
            case "3":
                mode = ColorBlindId.Deuteranopia;
                return true;
            case "tritanopia":
            case "tri":
            case "4":
                mode = ColorBlindId.Tritanopia;
                return true;
            default:
                return false;
        }
    }

    public static void SetFromStrings(string themeStr, string colorBlindStr)
    {
        LoadFromPrefs();
        ThemeId t = Theme;
        ColorBlindId c = ColorBlind;
        if (TryParseTheme(themeStr, out ThemeId parsedT))
        {
            t = parsedT;
        }

        if (TryParseColorBlind(colorBlindStr, out ColorBlindId parsedC))
        {
            c = parsedC;
        }

        Save(t, c);
    }

    public readonly struct VoiceUiPalette
    {
        public readonly Color CameraPreviewFill;
        public readonly Color OverlayDim;
        public readonly Color TopStrip;
        public readonly Color ScoreFill;
        public readonly Color ScoreText;
        public readonly Color StatusText;
        public readonly Color HandPreviewTint;
        public readonly Color Bubble;
        public readonly Color TranscriptText;
        public readonly Color RecordButton;
        public readonly Color RecordButtonHighlight;
        public readonly Color RecordButtonPressed;
        public readonly Color RecordLabel;

        public VoiceUiPalette(
            Color cameraPreviewFill,
            Color overlayDim,
            Color topStrip,
            Color scoreFill,
            Color scoreText,
            Color statusText,
            Color handPreviewTint,
            Color bubble,
            Color transcriptText,
            Color recordButton,
            Color recordButtonHighlight,
            Color recordButtonPressed,
            Color recordLabel)
        {
            CameraPreviewFill = cameraPreviewFill;
            OverlayDim = overlayDim;
            TopStrip = topStrip;
            ScoreFill = scoreFill;
            ScoreText = scoreText;
            StatusText = statusText;
            HandPreviewTint = handPreviewTint;
            Bubble = bubble;
            TranscriptText = transcriptText;
            RecordButton = recordButton;
            RecordButtonHighlight = recordButtonHighlight;
            RecordButtonPressed = recordButtonPressed;
            RecordLabel = recordLabel;
        }
    }

    public static VoiceUiPalette GetVoiceUiPalette()
    {
        bool light = Theme == ThemeId.Light;
        bool hc = ColorBlind == ColorBlindId.HighContrast;

        if (light)
        {
            if (hc)
            {
                return new VoiceUiPalette(
                    new Color(0.96f, 0.97f, 0.99f, 1f),
                    new Color(0f, 0f, 0f, 0.18f),
                    new Color(0.88f, 0.90f, 0.94f, 0.98f),
                    new Color(0f, 0.35f, 0.75f, 1f),
                    new Color(0.05f, 0.08f, 0.14f, 1f),
                    new Color(0.12f, 0.16f, 0.24f, 1f),
                    Color.white,
                    new Color(0.98f, 0.98f, 1f, 0.98f),
                    new Color(0.04f, 0.07f, 0.12f, 1f),
                    new Color(0.75f, 0.78f, 0.86f, 1f),
                    new Color(0.82f, 0.85f, 0.92f, 1f),
                    new Color(0.68f, 0.71f, 0.80f, 1f),
                    new Color(0.06f, 0.09f, 0.16f, 1f));
            }

            return new VoiceUiPalette(
                new Color(0.93f, 0.94f, 0.96f, 1f),
                new Color(0f, 0f, 0f, 0.12f),
                new Color(0.90f, 0.92f, 0.95f, 0.96f),
                new Color(0.15f, 0.45f, 0.85f, 1f),
                new Color(0.12f, 0.14f, 0.20f, 1f),
                new Color(0.28f, 0.32f, 0.42f, 1f),
                Color.white,
                new Color(0.96f, 0.97f, 0.99f, 0.96f),
                new Color(0.15f, 0.17f, 0.22f, 1f),
                new Color(0.82f, 0.85f, 0.90f, 1f),
                new Color(0.88f, 0.90f, 0.94f, 1f),
                new Color(0.78f, 0.80f, 0.86f, 1f),
                new Color(0.14f, 0.16f, 0.22f, 1f));
        }

        // Dark theme
        if (hc)
        {
            return new VoiceUiPalette(
                new Color(0.02f, 0.03f, 0.05f, 1f),
                new Color(0f, 0f, 0f, 0.45f),
                new Color(0.06f, 0.08f, 0.12f, 0.98f),
                new Color(0.95f, 0.85f, 0.2f, 1f),
                Color.white,
                new Color(0.92f, 0.94f, 0.98f, 1f),
                new Color(0.98f, 0.98f, 1f, 1f),
                new Color(0.08f, 0.10f, 0.14f, 0.98f),
                Color.white,
                new Color(0.12f, 0.14f, 0.18f, 1f),
                new Color(0.18f, 0.20f, 0.26f, 1f),
                new Color(0.08f, 0.10f, 0.14f, 1f),
                Color.white);
        }

        // Dark + protan / deut / tri — shift accents away from ambiguous reds/greens
        Color score = new Color(0.35f, 0.65f, 0.95f, 1f);
        if (ColorBlind == ColorBlindId.Protanopia)
        {
            score = new Color(0.25f, 0.72f, 0.95f, 1f);
        }
        else if (ColorBlind == ColorBlindId.Deuteranopia)
        {
            score = new Color(0.35f, 0.55f, 0.98f, 1f);
        }
        else if (ColorBlind == ColorBlindId.Tritanopia)
        {
            score = new Color(0.55f, 0.72f, 0.35f, 1f);
        }

        return new VoiceUiPalette(
            new Color(0.07f, 0.09f, 0.12f, 1f),
            new Color(0f, 0f, 0f, 0.28f),
            new Color(0.12f, 0.15f, 0.20f, 0.96f),
            score,
            new Color(0.92f, 0.93f, 0.96f, 1f),
            new Color(0.65f, 0.70f, 0.78f, 1f),
            new Color(0.94f, 0.95f, 0.97f, 1f),
            new Color(0.14f, 0.17f, 0.22f, 0.96f),
            new Color(0.90f, 0.91f, 0.94f, 1f),
            new Color(0.20f, 0.24f, 0.30f, 1f),
            new Color(0.26f, 0.30f, 0.38f, 1f),
            new Color(0.14f, 0.17f, 0.22f, 1f),
            new Color(0.88f, 0.90f, 0.95f, 1f));
    }

    public static string ThemeToWebString()
    {
        return Theme == ThemeId.Light ? "light" : "dark";
    }

    public static string ColorBlindToWebString()
    {
        switch (ColorBlind)
        {
            case ColorBlindId.HighContrast: return "highContrast";
            case ColorBlindId.Protanopia: return "protanopia";
            case ColorBlindId.Deuteranopia: return "deuteranopia";
            case ColorBlindId.Tritanopia: return "tritanopia";
            default: return "none";
        }
    }

    /// <summary>Traffic-light colors for the score meter; avoids ambiguous red–green pairs when color-blind modes are on.</summary>
    public static void GetScoreTierColors(out Color high, out Color mid, out Color low)
    {
        switch (ColorBlind)
        {
            case ColorBlindId.HighContrast:
                if (Theme == ThemeId.Light)
                {
                    high = new Color(0f, 0.35f, 0.72f, 1f);
                    mid = new Color(0.75f, 0.45f, 0f, 1f);
                    low = new Color(0.55f, 0f, 0.35f, 1f);
                }
                else
                {
                    high = new Color(1f, 0.92f, 0.2f, 1f);
                    mid = new Color(0.35f, 0.85f, 1f, 1f);
                    low = new Color(1f, 0.55f, 0.85f, 1f);
                }

                return;
            case ColorBlindId.Protanopia:
                high = new Color(0.2f, 0.75f, 0.95f, 1f);
                mid = new Color(0.95f, 0.78f, 0.2f, 1f);
                low = new Color(0.55f, 0.35f, 0.95f, 1f);
                return;
            case ColorBlindId.Deuteranopia:
                high = new Color(0.25f, 0.55f, 0.98f, 1f);
                mid = new Color(0.98f, 0.75f, 0.2f, 1f);
                low = new Color(0.72f, 0.35f, 0.95f, 1f);
                return;
            case ColorBlindId.Tritanopia:
                high = new Color(0.55f, 0.78f, 0.35f, 1f);
                mid = new Color(0.95f, 0.55f, 0.35f, 1f);
                low = new Color(0.35f, 0.45f, 0.85f, 1f);
                return;
            default:
                high = new Color(0.18f, 0.82f, 0.35f, 1f);
                mid = new Color(0.95f, 0.73f, 0.15f, 1f);
                low = new Color(0.92f, 0.30f, 0.20f, 1f);
                return;
        }
    }

    /// <summary>Multiply tint for live webcam preview (AR uses passthrough, not this).</summary>
    public static Color GetWebcamTint()
    {
        switch (ColorBlind)
        {
            case ColorBlindId.HighContrast:
                return new Color(1f, 0.96f, 0.90f, 1f);
            case ColorBlindId.Protanopia:
                return new Color(0.93f, 0.97f, 1f, 1f);
            case ColorBlindId.Deuteranopia:
                return new Color(0.95f, 0.96f, 1f, 1f);
            case ColorBlindId.Tritanopia:
                return new Color(1f, 0.98f, 0.92f, 1f);
            default:
                return Color.white;
        }
    }
}
