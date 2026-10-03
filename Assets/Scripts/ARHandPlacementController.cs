using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

public class ARHandPlacementController : MonoBehaviour
{
    [SerializeField] private string anchorObjectName = "SignAnchor";
    [SerializeField] private bool allowSinglePlacement = true;
    [SerializeField] private bool requirePlaneHit = true;
    [SerializeField] private float cameraFallbackDistance = 1.2f;

    private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    private Camera mainCamera;
    private bool anchorPlaced;
    public Transform AnchorTransform { get; private set; }
    public bool AnchorPlaced => anchorPlaced;

    private void Awake()
    {
        mainCamera = Camera.main;
        FixCameraTracking();
        raycastManager = FindObjectOfType<ARRaycastManager>();
        planeManager = FindObjectOfType<ARPlaneManager>();
        if (planeManager != null)
        {
            planeManager.enabled = true;
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
        }
        if (raycastManager != null)
        {
            raycastManager.enabled = true;
        }
        EnsureAnchorExists();
        UpdateFallbackAnchorPose();
    }

    private void Update()
    {
        if (allowSinglePlacement && anchorPlaced)
        {
            return;
        }

        if (!TryGetTouchPosition(out Vector2 touchPosition))
        {
            return;
        }

        if (!TryPlaceFromScreenTouch(touchPosition))
        {
            if (!requirePlaneHit)
            {
                UpdateFallbackAnchorPose();
                anchorPlaced = true;
            }
        }
    }

    public void EnsureAnchorExists()
    {
        if (AnchorTransform != null)
        {
            return;
        }

        GameObject existing = GameObject.Find(anchorObjectName);
        if (existing != null)
        {
            AnchorTransform = existing.transform;
            return;
        }

        GameObject created = new GameObject(anchorObjectName);
        AnchorTransform = created.transform;
    }

    /// <summary>
    /// Resets the placed anchor so the user can scan and tap again to reposition.
    /// </summary>
    public void ResetAnchor()
    {
        anchorPlaced = false;

        if (planeManager == null)
            planeManager = FindObjectOfType<ARPlaneManager>();
        if (planeManager != null)
            planeManager.enabled = true;

        if (raycastManager == null)
            raycastManager = FindObjectOfType<ARRaycastManager>();
        if (raycastManager != null)
            raycastManager.enabled = true;

        UpdateFallbackAnchorPose();
        Debug.Log("[ARPlacement] Anchor reset. Waiting for new placement.");
    }

    public bool TryPlaceFromScreenTouch(Vector2 screenPosition)
    {
        if (raycastManager == null)
        {
            raycastManager = FindObjectOfType<ARRaycastManager>();
        }

        if (raycastManager == null || AnchorTransform == null)
        {
            return false;
        }

        if (!raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
        {
            return false;
        }

        Pose hitPose = hits[0].pose;
        
        var oldAnchor = AnchorTransform.GetComponent<ARAnchor>();
        if (oldAnchor != null)
        {
            Destroy(oldAnchor);
        }

        AnchorTransform.position = hitPose.position;
        
        if (mainCamera == null) mainCamera = Camera.main;
        Vector3 camForwardOnPlane = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up).normalized;
        if (camForwardOnPlane == Vector3.zero) camForwardOnPlane = Vector3.forward;
        AnchorTransform.rotation = Quaternion.LookRotation(camForwardOnPlane, Vector3.up);
        
        AnchorTransform.gameObject.AddComponent<ARAnchor>();

        anchorPlaced = true;
        return true;
    }

    public bool HasTrackedPlanes()
    {
        if (planeManager == null)
        {
            planeManager = FindObjectOfType<ARPlaneManager>();
        }

        if (planeManager != null && !planeManager.enabled)
        {
            planeManager.enabled = true;
        }

        return planeManager != null && planeManager.trackables.count > 0;
    }

    private void FixCameraTracking()
    {
        if (mainCamera == null) return;
        
        var oldDriver = mainCamera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        if (oldDriver != null)
        {
            if (oldDriver.positionAction == null || oldDriver.positionAction.bindings.Count == 0)
            {
                Destroy(oldDriver);
                if (mainCamera.GetComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>() == null)
                {
                    mainCamera.gameObject.AddComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>();
                    Debug.Log("[ARPlacement] Replaced unconfigured InputSystem TrackedPoseDriver with SpatialTracking TrackedPoseDriver.");
                }
            }
        }
    }

    public void UpdateFallbackAnchorPose()
    {
        if (AnchorTransform == null)
        {
            return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            AnchorTransform.position = new Vector3(0f, -0.2f, 1.8f);
            AnchorTransform.rotation = Quaternion.identity;
            return;
        }

        Transform camTx = mainCamera.transform;
        AnchorTransform.position = camTx.position + (camTx.forward * cameraFallbackDistance);
        AnchorTransform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(camTx.forward, Vector3.up), Vector3.up);
    }

    private static bool TryGetTouchPosition(out Vector2 touchPosition)
    {
        touchPosition = default;
        if (Input.touchCount <= 0)
        {
            return false;
        }

        Touch touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Began)
        {
            return false;
        }

        touchPosition = touch.position;
        return true;
    }
}
