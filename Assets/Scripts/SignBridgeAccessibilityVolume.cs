using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Global URP volume for AR / camera color adjustments (contrast, saturation, tint) from accessibility settings.
/// Requires URP renderer with post-processing enabled for the active camera.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class SignBridgeAccessibilityVolume : MonoBehaviour
{
    public static SignBridgeAccessibilityVolume Instance { get; private set; }

    private Volume _volume;
    private VolumeProfile _profile;
    private ColorAdjustments _colorAdjustments;

    private void Awake()
    {
        Instance = this;
        SignBridgeAppearance.LoadFromPrefs();
        SignBridgeAppearance.Changed += Apply;
        EnsureVolume();
        Apply();
    }

    private void OnDestroy()
    {
        SignBridgeAppearance.Changed -= Apply;
        if (Instance == this)
        {
            Instance = null;
        }

        if (_volume != null)
        {
            Destroy(_volume.gameObject);
            _volume = null;
        }

        if (_profile != null)
        {
            Destroy(_profile);
            _profile = null;
        }

        _colorAdjustments = null;
    }

    private void EnsureVolume()
    {
        if (_volume != null)
        {
            return;
        }

        GameObject go = new GameObject("SignBridgeAccessibilityVolume");
        go.transform.SetParent(transform, false);
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 60f;
        _volume.weight = 1f;
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _colorAdjustments = _profile.Add<ColorAdjustments>(true);
        _colorAdjustments.active = true;
        _volume.profile = _profile;
    }

    public void Apply()
    {
        EnsureVolume();
        if (_volume != null)
        {
            _volume.weight = 0f;
        }

        if (_colorAdjustments != null)
        {
            _colorAdjustments.active = false;
        }
    }
}
