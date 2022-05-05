using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[ExecuteAlways, AddComponentMenu("Rendering/Planar Reflections Probe"), RequireComponent(typeof(BoxCollider))]
public class PlanarReflectionsProbe : MonoBehaviour
{
    [Space(10)]
    [Range(0.05f, 1.0f)][SerializeField] float reflectionsQuality = 1f;
    [SerializeField] float farClipPlane = 200;
    [SerializeField] bool renderBackground = true;
    [Space(10)]
    [SerializeField] bool renderInEditor = false;
    [SerializeField] List<MeshRenderer> reflectiveRenderers = new List<MeshRenderer>();

    Vector3 planeNormal => transform.up;
    Vector3 planePosition => transform.position;

    private GameObject _probeGO;
    private Camera _probe;
    private Skybox _probeSkybox;
    private ArrayList _ignoredCameras = new ArrayList();
    BoxCollider collider;

    private void OnEnable()
    {
        if (collider == null)
        {
            collider = GetComponent<BoxCollider>();
        }
        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
    }

    private void OnDisable()
    {
        FinalizeProbe();
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;
    }

    private void OnDestroy()
    {
        FinalizeProbe();
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;
    }

    private void InitializeProbe()
    {
        _probeGO = new GameObject("", typeof(Camera), typeof(Skybox));
        _probeGO.name = "PlanarReflectionCamera";
        _probeGO.hideFlags = HideFlags.HideAndDontSave;
        _probe = _probeGO.GetComponent<Camera>();
        _probeSkybox = _probeGO.GetComponent<Skybox>();
        _probeSkybox.enabled = false;
        _probeSkybox.material = null;
    }

    private void FinalizeProbe()
    {
        if (_probe == null)
        {
            return;
        }
        if (Application.isEditor)
        {
            DestroyImmediate(_probeGO);
        }
        else
        {
            Destroy(_probeGO);
        }
    }

    private bool CheckCamera(Camera cam)
    {
        if (cam.cameraType == CameraType.Reflection)
        {
            return true;
        }
        else if (!renderInEditor && cam.cameraType == CameraType.SceneView)
        {
            return true;
        }
        else if (_ignoredCameras.Contains(cam))
        {
            return true;
        }
        else if (_probe != null && cam == _probe)
        {
            return true;
        }
        return false;
    }

    private void BeginCameraRendering(ScriptableRenderContext src, Camera cam)
    {
        // if camera is out of range, don't render
        if (OutOfRange(cam)) return;
        PreRenderRoutine(src, cam);
    }


    private void EndCameraRendering(ScriptableRenderContext src, Camera cam)
    {
        // if camera is out of range, don't render
        if (OutOfRange(cam)) return;
        PostRenderRoutine(cam);
    }
    private void PreRenderRoutine(ScriptableRenderContext src, Camera cam)
    {
        // Debug.Log("PreRenderRoutine");
        if (CheckCamera(cam))
        {
            return;
        }

        else if (_probe == null)
        {
            InitializeProbe();
        }

        UpdateProbeSettings(cam);
        CreateRenderTexture(cam);
        UpdateProbeTransform(cam, planeNormal);
        CalculateObliqueProjection(planeNormal);
        UniversalRenderPipeline.RenderSingleCamera(src, _probe);
        // Assign render texture to all objects in reflection objects list
        for (int i = 0; i < reflectiveRenderers.Count; i++)
        {
            MeshRenderer renderer = reflectiveRenderers[i];
            if (renderer != null)
            {
                // RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                // RenderSettings.customReflection = _probe.targetTexture;
                renderer.sharedMaterial.SetTexture("_PlanarReflectionTex", _probe.targetTexture);
            }
        }
    }

    private void PostRenderRoutine(Camera cam)
    {
        // Debug.Log("PostRenderRoutine");
        if (CheckCamera(cam) || _probe == null)
        {
            return;
        }

        _probe.targetTexture.Release();
        _probe.targetTexture = null;
    }

    private bool OutOfRange(Camera cam)
    {
        // check if camera is inside of box collider
        if (collider != null)
        {
            if (!collider.bounds.Contains(cam.transform.position))
            {
                return true;
            }
        }
        return false;
    }

    private void UpdateProbeSettings(Camera cam)
    {
        _probe.CopyFrom(cam);
        _probe.enabled = false;
        _probe.cameraType = CameraType.Reflection;
        _probe.allowHDR = false;
        _probe.allowMSAA = false;
        _probe.usePhysicalProperties = false;
        _probe.farClipPlane = farClipPlane;
        _probeSkybox.material = null;
        _probeSkybox.enabled = false;
        if (renderBackground)
        {
            _probe.clearFlags = cam.clearFlags;
            Skybox camSkybox = cam.GetComponent<Skybox>();
            if (camSkybox != null)
            {
                _probeSkybox.material = camSkybox.material;
                _probeSkybox.enabled = camSkybox.enabled;
            }
        }
        else
        {
            _probe.clearFlags = CameraClearFlags.Nothing;
        }
    }

    private void CreateRenderTexture(Camera cam)
    {
        int width = (int)((float)cam.pixelWidth * reflectionsQuality);
        width = Mathf.Clamp(width, 1, 8192);
        int height = (int)(width / cam.aspect);
        RenderTexture renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, 10);
        // RenderTexture renderTexture = new RenderTexture(width, height, 24, UnityEngine.Experimental.Rendering.GraphicsFormat.RGBA_DXT5_SRGB, 10);
        renderTexture.useMipMap = true;
        _probe.targetTexture = renderTexture;
        _probe.targetTexture.Create();
        // renderTexture.GenerateMips();
    }

    // The probe's camera position should be the the current camera's position
    // mirrored by the reflecting plane. Its rotation mirrored too.
    private void UpdateProbeTransform(Camera cam, Vector3 normal)
    {
        Vector3 proj = normal * Vector3.Dot(normal, cam.transform.position - planePosition);
        _probe.transform.position = cam.transform.position - 2 * proj;

        Vector3 probeForward = Vector3.Reflect(cam.transform.forward, normal);
        Vector3 probeUp = Vector3.Reflect(cam.transform.up, normal);
        _probe.transform.LookAt(
            _probe.transform.position + probeForward, probeUp);
    }

    // The clip plane should coincide with the plane with reflections.
    private void CalculateObliqueProjection(Vector3 normal)
    {
        Matrix4x4 viewMatrix = _probe.worldToCameraMatrix;
        Vector3 viewPosition = viewMatrix.MultiplyPoint(planePosition);
        Vector3 viewNormal = viewMatrix.MultiplyVector(normal);
        Vector4 plane = new Vector4(
            viewNormal.x, viewNormal.y, viewNormal.z,
            -Vector3.Dot(viewPosition, viewNormal));
        _probe.projectionMatrix = _probe.CalculateObliqueMatrix(plane);
    }

    ///////////////////////////////////////////////////////////////////////////



    ///////////////////////////////////////////////////////////////////////////
    // Public methods.                                                       //
    ///////////////////////////////////////////////////////////////////////////

    public void IgnoreCamera(Camera cam)
    {
        if (!_ignoredCameras.Contains(cam))
        {
            _ignoredCameras.Add(cam);
        }
    }

    public void UnignoreCamera(Camera cam)
    {
        if (_ignoredCameras.Contains(cam))
        {
            _ignoredCameras.Remove(cam);
        }
    }

    public void ClearIgnoredList()
    {
        _ignoredCameras.Clear();
    }

    public bool IsIgnoring(Camera cam)
    {
        return _ignoredCameras.Contains(cam);
    }
}
