using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Experimental.Rendering;

class CloudRenderPass : CustomPass
{
    public ComputeShader cloudCompute;
    public Material blitMaterial;

    [Header("Planet")]
    public float planetRadius = 1000f;

    [Header("Cloud Layer")]
    public float cloudMinHeight = 400f;
    public float cloudMaxHeight = 600f;

    [Header("Raymarch")]
    public float coarseStep = 50f;
    public float fineStepFactor = 0.125f; // 1/8
    public int minCoarseSteps = 64;
    public int maxCoarseSteps = 128;
    
    [Header("Base Noise")]
    public Texture3D baseNoiseTex;
    public float baseNoiseScale = 0.001f;
    public float baseNoiseThreshold = 0.5f;

    [Header("Detail Noise")]
    public Texture3D detailNoiseTex;
    public float detailNoiseScale = 0.001f;
    public float detailNoiseThreshold = 0.5f;
    
    [Header("Curl Noise")]
    public Texture2D curlNoiseTex;
    public float curlNoiseThreshold = 0.5f;
    
    [Header("Wind")]
    public float2 windDirection = new float2(1f, 0f);
    public float windSpeed = 5f;
    
    private RTHandle _target;
    private int _kernelIndex = -1;

    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        _kernelIndex = cloudCompute.FindKernel("CSMain");

        if (_kernelIndex == -1)
        {
            Debug.LogError($"Kernel 'CSMain' not found in {cloudCompute.name}");
            return;
        }

        _target = RTHandles.Alloc(
            Vector2.one,
            dimension: TextureDimension.Tex2D,
            colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
            enableRandomWrite: true,
            useDynamicScale: true,
            name: "CloudRT"
        );
    }

    protected override void Execute(CustomPassContext ctx)
    {
        if (cloudCompute == null || blitMaterial == null)
            return;

        var cmd = ctx.cmd;
        var cam = ctx.hdCamera.camera;

        cmd.SetComputeTextureParam(cloudCompute, _kernelIndex, "Result", _target);

        cmd.SetComputeVectorParam(cloudCompute, "_CameraPosition", cam.transform.position);
        Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
        Matrix4x4 view = cam.worldToCameraMatrix;
        Matrix4x4 invViewProj = (gpuProj * view).inverse;
        cmd.SetComputeMatrixParam(cloudCompute, "_InvViewProj", invViewProj);

        cmd.SetComputeFloatParam(cloudCompute, "_PlanetRadius", planetRadius);
        cmd.SetComputeFloatParam(cloudCompute, "_CloudMinHeight", cloudMinHeight);
        cmd.SetComputeFloatParam(cloudCompute, "_CloudMaxHeight", cloudMaxHeight);

        cmd.SetComputeFloatParam(cloudCompute, "_CoarseStep", coarseStep);
        cmd.SetComputeFloatParam(cloudCompute, "_FineStepFactor", fineStepFactor);
        cmd.SetComputeIntParam(cloudCompute, "_CoarseStepsMin", maxCoarseSteps);
        cmd.SetComputeIntParam(cloudCompute, "_CoarseStepsMax", minCoarseSteps);
        
        cmd.SetComputeTextureParam(
            cloudCompute,
            _kernelIndex,
            "_BaseNoiseTex",
            baseNoiseTex
        );
        cmd.SetComputeTextureParam(
            cloudCompute,
            _kernelIndex,
            "_DetailNoiseTex",
            detailNoiseTex
        );
        cmd.SetComputeTextureParam(
            cloudCompute,
            _kernelIndex,
            "_CurlNoiseTex",
            curlNoiseTex
        );

        cmd.SetComputeFloatParam(cloudCompute, "_BaseNoiseScale", baseNoiseScale);
        cmd.SetComputeFloatParam(cloudCompute, "_CurlNoiseThreshold", curlNoiseThreshold);
        cmd.SetComputeFloatParam(cloudCompute, "_DetailNoiseThreshold", detailNoiseThreshold);
        cmd.SetComputeFloatParam(cloudCompute, "_BaseNoiseThreshold", baseNoiseThreshold);
        
        cmd.SetComputeFloatParam(cloudCompute, "_Time", Time.time);
        cmd.SetComputeFloatParam(cloudCompute, "_WindSpeed", windSpeed);
        cmd.SetComputeVectorParam(cloudCompute, "_WindDirection", new Vector4(windDirection.x, windDirection.y, 0, 0).normalized);

        Debug.Log($"Camera X: {cam.transform.position.x}, Camera Y: {cam.transform.position.y}, Camera Z: {cam.transform.position.z}");

        int x = Mathf.CeilToInt(_target.rt.width / 8.0f);
        int y = Mathf.CeilToInt(_target.rt.height / 8.0f);
        cmd.DispatchCompute(cloudCompute, _kernelIndex, x, y, 1);

        Debug.Log($"{x} and {y}");
        
        blitMaterial.SetTexture("_Source", _target);
        HDUtils.DrawFullScreen(
            cmd,
            blitMaterial,
            ctx.cameraColorBuffer
        );
    }

    protected override void Cleanup()
    {
        _target?.Release();
    }
}

