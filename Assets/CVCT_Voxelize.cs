using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[ExecuteInEditMode]
public class CVCT_Voxelize : MonoBehaviour
{
    public Light directionalLight;
    public int gridResolution;
    public float gridPixelSize;
    public int gridCascades;
    public LayerMask cullingMask = -1;

    public Shader gvShader;
    public ComputeShader gvCompute;
    public bool drawGizmosGV, drawGizmosLight;
    public int drawCascade;

    Camera _projectCam;
    Matrix4x4 _world_to_light_local_matrix;


    void UpdateVoxelization()
    {
        var cam = FetchProjectionCamera();
        var trackTransform = directionalLight.transform;
        cam.transform.SetPositionAndRotation(trackTransform.position, trackTransform.rotation);

        Shader.SetGlobalInt("_CVCT_GridResolution", gridResolution);

#if UNITY_EDITOR
        {
            int write_random_kernel = gvCompute.FindKernel("DebugWriteRandomKernel");
            int thread_groups = (gridResolution + 3) / 4;
            int thread_groups_y = (gridResolution * gridCascades + 3) / 4;
            gvCompute.SetTexture(write_random_kernel, "CVCT_tex3d", _tex3d_gv);
            gvCompute.Dispatch(write_random_kernel, thread_groups, thread_groups_y, thread_groups);
        }
#endif

        int clear_kernel = gvCompute.FindKernel("ClearKernel");
        int repack_lvl0_kernel = gvCompute.FindKernel("RepackLvl0Kernel");
        int repack_upscale_kernel = gvCompute.FindKernel("RepackAndUpscaleKernel");

        var cb_gv = new ComputeBuffer(gridResolution * gridResolution * gridResolution, 4);
        var dummy_target = RenderTexture.GetTemporary(gridResolution, gridResolution, 0,
                                                      RenderTextureFormat.R8);
        gvCompute.SetInt("GridResolution", gridResolution);
        gvCompute.SetInt("GridHalfResolution", gridResolution / 2);
        gvCompute.SetBuffer(clear_kernel, "CVCT_gv", cb_gv);
        gvCompute.SetBuffer(repack_lvl0_kernel, "CVCT_gv", cb_gv);
        gvCompute.SetTexture(repack_lvl0_kernel, "CVCT_tex3d", _tex3d_gv);
        gvCompute.SetBuffer(repack_upscale_kernel, "CVCT_gv", cb_gv);
        gvCompute.SetTexture(repack_upscale_kernel, "CVCT_tex3d", _tex3d_gv);

        for (int i = 0; i < gridCascades; i++)
        {
            float half_size = 0.5f * gridResolution * gridPixelSize;
            half_size *= Mathf.Pow(2f, i);

            /* First step: render into the GV (geometry volume).  Here, there is no depth map
             * and the fragment shader writes into the ComputeBuffer cb_gv.  At the end we
             * copy (and pack) the information into the more compact _tex3d_gv.
             */
            int thread_groups = (gridResolution * gridResolution * gridResolution * 63) / 64;
            gvCompute.Dispatch(clear_kernel, thread_groups, 1, 1);

            cam.orthographicSize = half_size;
            cam.nearClipPlane = -half_size;
            cam.farClipPlane = half_size;
            cam.targetTexture = dummy_target;
            Graphics.SetRandomWriteTarget(1, cb_gv);

            cam.RenderWithShader(gvShader, "RenderType");

            thread_groups = (gridResolution + 3) / 4;
            if (i != 0)
                gvCompute.SetInts("GridCascadeBase", i * gridResolution, (i - 1) * gridResolution);
            gvCompute.Dispatch(i == 0 ? repack_lvl0_kernel : repack_upscale_kernel,
                               thread_groups, thread_groups, thread_groups);
        }

        RenderTexture.ReleaseTemporary(dummy_target);
        cb_gv.Release();

        /* the scale in _world_to_light_local_matrix is such that the 0th cascade
         * corresponds to the box from (-1,-1,-1) to (+1,+1,+1).
         */
        float sz = 0.5f * gridResolution * gridPixelSize;
        var mat = Matrix4x4.Scale(Vector3.one / sz) * cam.transform.worldToLocalMatrix;
        _world_to_light_local_matrix = mat;
    }

    Camera FetchProjectionCamera()
    {
        if (_projectCam == null)
        {
            // Create the shadow rendering camera
            GameObject go = new GameObject("CVCT project cam (not saved)");
            go.hideFlags = HideFlags.DontSave;

            _projectCam = go.AddComponent<Camera>();
            _projectCam.orthographic = true;
            _projectCam.enabled = false;
            _projectCam.aspect = 1;
            _projectCam.clearFlags = CameraClearFlags.Nothing;
            /* Obscure: if the main camera is stereo, then this one will be confused in
             * the SetTargetBuffers() mode unless we force it to not be stereo */
            _projectCam.stereoTargetEye = StereoTargetEyeMask.None;
        }
        _projectCam.cullingMask = cullingMask;
        return _projectCam;
    }


    /*********************************************************************/

    void UpdateTracing()
    {
        int tracing_kernel = gvCompute.FindKernel("TracingKernel");
        gvCompute.SetInt("GridResolution", gridResolution);
        gvCompute.SetInt("GridHalfResolution", gridResolution / 2);
        gvCompute.SetTexture(tracing_kernel, "CVCT_tex3d", _tex3d_gv);
        gvCompute.SetTexture(tracing_kernel, "LightTex3d", _tex3d_light);

        for (int i = 0; i < gridCascades; i++)
        {
            int thread_groups = (gridResolution + 3) / 4;
            gvCompute.SetInts("GridCascadeBase", i * gridResolution, (gridCascades - 1) * gridResolution);
            gvCompute.Dispatch(tracing_kernel, thread_groups, thread_groups, thread_groups);
        }
    }


    void SetGlobalUniforms()
    {
        var v = Vector4.zero;
        v.x = gridResolution;
        v.y = 1f / gridCascades;

        Shader.SetGlobalVector("_CVCT_GridResolution", v);
        Shader.SetGlobalMatrix("_CVCT_WorldToLightLocalMatrix", _world_to_light_local_matrix);
        Shader.SetGlobalTexture("_CVCT_LightTex3d", _tex3d_light);
    }


    /*********************************************************************/

    RenderTexture _tex3d_gv;
    RenderTexture _tex3d_light;

    private void Start()
    {
        DestroyTargets();
        UpdateLights();
    }

    void DestroyTarget(ref RenderTexture tex)
    {
        if (tex)
            DestroyImmediate(tex);
        tex = null;
    }

    void DestroyTargets()
    {
        DestroyTarget(ref _tex3d_gv);
        DestroyTarget(ref _tex3d_light);
    }

    RenderTexture CreateTex3dGV()
    {
        var desc = new RenderTextureDescriptor(gridResolution, gridResolution * gridCascades, RenderTextureFormat.R8);
        desc.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        desc.volumeDepth = gridResolution;
        desc.enableRandomWrite = true;
        desc.useMipMap = false;
        desc.autoGenerateMips = false;

        RenderTexture tg = new RenderTexture(desc);
        tg.wrapMode = TextureWrapMode.Clamp;
        tg.filterMode = FilterMode.Bilinear;
        return tg;
    }

    RenderTexture CreateTex3dLight()
    {
        var desc = new RenderTextureDescriptor(gridResolution, gridResolution * gridCascades, RenderTextureFormat.R8);
        desc.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        desc.volumeDepth = gridResolution;
        desc.enableRandomWrite = true;
        desc.useMipMap = false;
        desc.autoGenerateMips = false;

        RenderTexture tg = new RenderTexture(desc);
        tg.wrapMode = TextureWrapMode.Clamp;
        tg.filterMode = FilterMode.Bilinear;
        return tg;
    }

    public void UpdateLights()
    {
        if (_tex3d_gv != null && _tex3d_gv.width == gridResolution && _tex3d_gv.height == gridResolution * gridCascades &&
            _tex3d_light != null && _tex3d_light.width == gridResolution && _tex3d_light.height == gridResolution * gridCascades)
        {
            /* already up-to-date */
        }
        else
        {
            DestroyTargets();

            if (gridResolution <= 0 || gridCascades <= 0)
                return;
            Debug.Log("Creating render textures");
            _tex3d_gv = CreateTex3dGV();
            _tex3d_light = CreateTex3dLight();
        }

        _tex3d_gv.Create();
        UpdateVoxelization();

        _tex3d_light.Create();
        UpdateTracing();

        SetGlobalUniforms();
    }

#if UNITY_EDITOR
    void Update()
    {
        UpdateLights();
    }

    void OnDrawGizmos()
    {
        if (drawGizmosGV)
            DrawGizmos(_tex3d_gv, false);
        if (drawGizmosLight)
            DrawGizmos(_tex3d_light, true);
    }

    float[] DrawGizmosExtract(RenderTexture rt, int cascade)
    {
        int debug_kernel = gvCompute.FindKernel("DebugKernel");
        gvCompute.SetTexture(debug_kernel, "ExtractSource", rt);

        int N = gridResolution * gridResolution * gridResolution;
        var buffer = new ComputeBuffer(N, 4, ComputeBufferType.Default);
        gvCompute.SetBuffer(debug_kernel, "ExtractTexture", buffer);
        gvCompute.SetInt("GridResolution", gridResolution);
        gvCompute.SetInts("GridCascadeBase", gridResolution * cascade);
        //gvCompute.SetInt("ExtractMode", mode);
        int thread_groups = (gridResolution + 3) / 4;
        gvCompute.Dispatch(debug_kernel, thread_groups, thread_groups, thread_groups);

        var array = new float[N];
        buffer.GetData(array);
        buffer.Release();
        return array;
    }

    void DrawGizmos(RenderTexture rt, bool is_light)
    {
        if (!rt)
            return;

        int cascade = drawCascade;
        if (cascade < 0)
            cascade = 0;
        if (cascade >= gridCascades)
            cascade = gridCascades - 1;
        var array = DrawGizmosExtract(rt, cascade);

        var mat = directionalLight.transform.localToWorldMatrix;
        mat = mat * Matrix4x4.Scale(Vector3.one * gridPixelSize * Mathf.Pow(2f, cascade))
                  * Matrix4x4.Translate(Vector3.one * gridResolution * -0.5f);
        Gizmos.matrix = mat;

        /*float half_size = 0.5f * gridResolution * gridPixelSize;
        float pixel_size = gridPixelSize;
        Vector3 org = tr.position - half_size * (tr.right + tr.up + tr.forward);
        Vector3 pix_x = tr.right * pixel_size;
        Vector3 pix_y = tr.up * pixel_size;
        Vector3 pix_z = tr.forward * pixel_size;*/

        Gizmos.color = Color.white;
        Gizmos.DrawLine(Vector3.zero, new Vector3(gridResolution, 0, 0));
        Gizmos.DrawLine(Vector3.zero, new Vector3(0, gridResolution, 0));
        Gizmos.DrawLine(Vector3.zero, new Vector3(0, 0, gridResolution));

        var gizmos = new List<System.Tuple<Vector3, float, Color32>>();

        const float dd = 0.5f;
        int index = 0;
        for (int z = 0; z < gridResolution; z++)
            for (int y = 0; y < gridResolution; y++)
                for (int x = 0; x < gridResolution; x++)
                {
                    float entry = array[index++];
                    if (!is_light)
                    {
                        if (entry == 1f)
                            continue;
                        gizmos.Add(System.Tuple.Create(
                            new Vector3(x + dd, y + dd, z + dd),
                            (1 - entry) * 0.5f,
                            (Color32)new Color(0.5f, 0.5f, 0.5f, 1)));
                    }
                    else
                    {
                        if (entry == 1f)
                            continue;
                        gizmos.Add(System.Tuple.Create(
                            new Vector3(x + dd, y + dd, z + dd),
                            0.3f,
                            (Color32)new Color(entry, entry, entry, 1)));
                    }
                }

        var camera_pos = UnityEditor.SceneView.lastActiveSceneView.camera.transform.position;
        gizmos.Sort((g1, g2) =>
        {
            var d1 = Vector3.Distance(camera_pos, mat.MultiplyPoint(g1.Item1));
            var d2 = Vector3.Distance(camera_pos, mat.MultiplyPoint(g2.Item1));
            return d2.CompareTo(d1);
        });
        foreach (var g in gizmos)
        {
            Gizmos.color = g.Item3;
            Gizmos.DrawCube(g.Item1, g.Item2 * Vector3.one);
        }
    }
#endif
}
