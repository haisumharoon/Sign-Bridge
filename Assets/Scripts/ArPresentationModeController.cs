using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Keeps VoiceSign in AR-first presentation mode when AR camera background is active.
/// This restores the missing scene component and avoids accidental overlay behavior.
/// </summary>
public sealed class ArPresentationModeController : MonoBehaviour
{
    [SerializeField] private bool forceHideCameraPreviewInAr = true;
    [SerializeField] private string cameraPreviewObjectName = "CameraPreview";

    private void Start()
    {
        if (!forceHideCameraPreviewInAr)
        {
            return;
        }

        ARCameraBackground arCameraBackground = FindObjectOfType<ARCameraBackground>();
        if (arCameraBackground == null || !arCameraBackground.isActiveAndEnabled)
        {
            return;
        }

        GameObject previewObject = GameObject.Find(cameraPreviewObjectName);
        if (previewObject == null)
        {
            return;
        }

        RawImage previewImage = previewObject.GetComponent<RawImage>();
        if (previewImage == null)
        {
            return;
        }

        previewImage.texture = null;
        previewImage.color = new Color(0f, 0f, 0f, 0f);
    }
}
