using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

public class VoiceSignSceneBootstrap : MonoBehaviour
{
    private const string RootObjectName = "VoiceSignSystem";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureVoiceSignBootstrap()
    {
        EnsureSystemObjects();
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void EnsureEditorVisibleSetup()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (Application.isPlaying)
            {
                return;
            }

            if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isLoaded)
            {
                EnsureSystemObjects();
            }
        };
    }
#endif

    private static void EnsureSystemObjects()
    {
        GameObject root = GameObject.Find(RootObjectName);
        if (root == null)
        {
            root = new GameObject(RootObjectName);
        }

        // Remove legacy components from previous attempts.
        if (root.GetComponent<VoiceInputController>() != null) UnityEngine.Object.DestroyImmediate(root.GetComponent<VoiceInputController>());
        if (root.GetComponent<SignAnimationController>() != null) UnityEngine.Object.DestroyImmediate(root.GetComponent<SignAnimationController>());
        if (root.GetComponent<VoiceSignUIController>() != null) UnityEngine.Object.DestroyImmediate(root.GetComponent<VoiceSignUIController>());

        if (root.GetComponent<SentenceScoringService>() == null) root.AddComponent<SentenceScoringService>();
        AddSignBridgeWebUiHostIfAvailable(root);
        AddSimpleVoiceControllerIfAvailable(root);
        AddArPlacementControllerIfAvailable(root);
        if (root.GetComponent<SignBridgeAccessibilityVolume>() == null)
        {
            root.AddComponent<SignBridgeAccessibilityVolume>();
        }

        EnsureCamera();
        EnsureArRuntimeObjects();
        EnsureDirectionalLight();

#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

    private static void EnsureCamera()
    {
        if (Camera.main != null)
        {
            return;
        }

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.03f, 0.07f);
        cameraObject.transform.position = Vector3.zero;
    }

    private static void EnsureDirectionalLight()
    {
        if (UnityEngine.Object.FindObjectOfType<Light>() != null)
        {
            return;
        }

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static void AddSignBridgeWebUiHostIfAvailable(GameObject root)
    {
        if (root.GetComponent<SignBridgeWebUiHost>() == null)
        {
            root.AddComponent<SignBridgeWebUiHost>();
        }
    }

    private static void AddSimpleVoiceControllerIfAvailable(GameObject root)
    {
        Type controllerType = Type.GetType("SimpleVoiceScreenController, Assembly-CSharp");
        if (controllerType == null)
        {
            return;
        }

        if (root.GetComponent(controllerType) == null)
        {
            root.AddComponent(controllerType);
        }
    }

    private static void AddArPlacementControllerIfAvailable(GameObject root)
    {
        Type controllerType = Type.GetType("ARHandPlacementController, Assembly-CSharp");
        if (controllerType == null)
        {
            return;
        }

        if (root.GetComponent(controllerType) == null)
        {
            root.AddComponent(controllerType);
        }
    }

    private static void EnsureArRuntimeObjects()
    {
        Type arSessionType = ResolveType("UnityEngine.XR.ARFoundation.ARSession, Unity.XR.ARFoundation");
        Type arInputManagerType = ResolveType("UnityEngine.XR.ARFoundation.ARInputManager, Unity.XR.ARFoundation");
        Type arPlaneManagerType = ResolveType("UnityEngine.XR.ARFoundation.ARPlaneManager, Unity.XR.ARFoundation");
        Type arRaycastManagerType = ResolveType("UnityEngine.XR.ARFoundation.ARRaycastManager, Unity.XR.ARFoundation");
        Type arCameraManagerType = ResolveType("UnityEngine.XR.ARFoundation.ARCameraManager, Unity.XR.ARFoundation");
        Type arCameraBackgroundType = ResolveType("UnityEngine.XR.ARFoundation.ARCameraBackground, Unity.XR.ARFoundation");

        if (arSessionType == null || arPlaneManagerType == null || arRaycastManagerType == null)
        {
            return;
        }

        GameObject arSession = GameObject.Find("AR Session");
        if (arSession == null)
        {
            arSession = new GameObject("AR Session");
        }

        EnsureComponent(arSession, arSessionType);
        if (arInputManagerType != null)
        {
            EnsureComponent(arSession, arInputManagerType);
        }

        GameObject arRuntimeRoot = ResolveArOriginRoot();
        if (arRuntimeRoot == null)
        {
            arRuntimeRoot = new GameObject("AR Runtime Root");
            EnsureComponent(arRuntimeRoot, ResolveType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils"));
        }

        EnsureComponent(arRuntimeRoot, arPlaneManagerType);
        EnsureComponent(arRuntimeRoot, arRaycastManagerType);
        ARPlaneManager planeManager = arRuntimeRoot.GetComponent<ARPlaneManager>();
        if (planeManager != null)
        {
            planeManager.enabled = true;
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;

            // Assign a procedural plane prefab so detected planes are visible
            // and raycasting against PlaneWithinPolygon works correctly.
            if (planeManager.planePrefab == null)
            {
                planeManager.planePrefab = BuildArPlanePrefab();
            }
        }
        ARRaycastManager raycastManager = arRuntimeRoot.GetComponent<ARRaycastManager>();
        if (raycastManager != null)
        {
            raycastManager.enabled = true;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            if (arCameraManagerType != null)
            {
                EnsureComponent(mainCamera.gameObject, arCameraManagerType);
            }

            if (arCameraBackgroundType != null)
            {
                EnsureComponent(mainCamera.gameObject, arCameraBackgroundType);
            }
        }

        WireCameraToXrOrigin(arRuntimeRoot, mainCamera);
    }

    private static GameObject ResolveArOriginRoot()
    {
        XROrigin xrOrigin = UnityEngine.Object.FindObjectOfType<XROrigin>();
        if (xrOrigin != null)
        {
            return xrOrigin.gameObject;
        }

        GameObject namedXrOrigin = GameObject.Find("XR Origin (AR Rig)");
        if (namedXrOrigin != null)
        {
            return namedXrOrigin;
        }

        GameObject namedRuntimeRoot = GameObject.Find("AR Runtime Root");
        return namedRuntimeRoot;
    }

    private static void WireCameraToXrOrigin(GameObject arRoot, Camera mainCamera)
    {
        if (arRoot == null || mainCamera == null)
        {
            return;
        }

        XROrigin xrOrigin = arRoot.GetComponent<XROrigin>();
        if (xrOrigin == null)
        {
            return;
        }

        Transform cameraOffset = EnsureCameraOffset(arRoot.transform);
        if (mainCamera.transform.parent != cameraOffset)
        {
            // Keep AR camera local to XROrigin like the AR template setup.
            mainCamera.transform.SetParent(cameraOffset, false);
        }
        mainCamera.transform.localPosition = Vector3.zero;
        mainCamera.transform.localRotation = Quaternion.identity;

        xrOrigin.Camera = mainCamera;
        xrOrigin.CameraFloorOffsetObject = cameraOffset.gameObject;
    }

    private static Transform EnsureCameraOffset(Transform arRoot)
    {
        if (arRoot == null)
        {
            return null;
        }

        Transform existing = arRoot.Find("Camera Offset");
        if (existing != null)
        {
            return existing;
        }

        GameObject offsetObject = new GameObject("Camera Offset");
        Transform offsetTransform = offsetObject.transform;
        offsetTransform.SetParent(arRoot, false);
        offsetTransform.localPosition = Vector3.zero;
        offsetTransform.localRotation = Quaternion.identity;
        return offsetTransform;
    }

    private static Type ResolveType(string qualifiedTypeName)
    {
        return Type.GetType(qualifiedTypeName);
    }

    private static Component EnsureComponent(GameObject target, Type componentType)
    {
        if (target == null || componentType == null)
        {
            return null;
        }

        Component existing = target.GetComponent(componentType);
        if (existing != null)
        {
            return existing;
        }

        return target.AddComponent(componentType);
    }

    /// <summary>
    /// Builds a lightweight semi-transparent quad prefab that ARPlaneManager uses
    /// to visualise each detected surface. The quad is scaled by the plane mesh visualizer.
    /// </summary>
    private static GameObject BuildArPlanePrefab()
    {
        var prefab = new GameObject("AR Plane Visualizer");

        // MeshFilter + MeshRenderer for the plane quad
        var mf = prefab.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildQuadMesh();

        var mr = prefab.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
        {
            // Fallback to a simple unlit transparent shader that always exists
            mat = new Material(Shader.Find("Sprites/Default"));
        }
        mat.color = new Color(0.18f, 0.75f, 0.82f, 0.32f);   // semi-transparent teal
        mat.SetFloat("_Mode", 3f);   // Transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.renderQueue = 3000;
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // ARPlaneMeshVisualizer drives the mesh to match the detected plane polygon.
        prefab.AddComponent<ARPlaneMeshVisualizer>();

        // LineRenderer outlines the plane boundary (optional, looks nice).
        var lr = prefab.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.startWidth = 0.02f;
        lr.endWidth = 0.02f;
        var lineMat = new Material(Shader.Find("Sprites/Default"));
        lineMat.color = new Color(0.18f, 0.85f, 0.92f, 0.75f);
        lr.sharedMaterial = lineMat;

        // Keep prefab hidden in hierarchy; ARPlaneManager will instantiate it.
        prefab.SetActive(false);

        Debug.Log("[SignBridge] AR plane visualizer prefab created procedurally.");
        return prefab;
    }

    private static Mesh BuildQuadMesh()
    {
        var mesh = new Mesh { name = "ARPlaneQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f,  0.5f),
            new Vector3( 0.5f, 0f,  0.5f),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        return mesh;
    }
}
