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

        Debug.Log($"Camera X: {cam.transform.position.x}, Camera Y: {cam.transform.position.y}, Camera Z: {cam.transform.position.z}");

        int x = Mathf.CeilToInt(_target.rt.width / 8.0f);
        int y = Mathf.CeilToInt(_target.rt.height / 8.0f);
        cmd.DispatchCompute(cloudCompute, _kernelIndex, x, y, 1);

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

