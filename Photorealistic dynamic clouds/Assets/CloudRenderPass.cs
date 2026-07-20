using System;
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
    
    [Header("Coverage")]
    public Texture2D coverageTex;
    public float coverageScale = 0.001f;
    public float2 coverageOffset = 0.0f;
    
    [Header("Day/Night Cycle")]
    [Range(0f, 1f)]
    public float timeOfDay = 0.5f; // 0 -> 0.5 -> 1 = Рассвет -> Полдень -> Закат
    public bool autoPlayTime = false;
    public float dayDurationSeconds = 60f; 
    
    [Header("Lightning & Sky")]
    public Gradient lightColorGradient;
    public Gradient zenithColorGradient;
    public Gradient horizonColorGradient;
    public float lightStep = 25f;
    
    [Header("Wind")]
    public float2 windDirection = new float2(1f, 0f);
    public float windSpeed = 5f;
    
    private RTHandle _target;
    private int _kernelIndex = -1;

    private float currentTime = 0f;
    private float _lastTime = 0f;
    private float morningAngle;
    private float eveningAngle;
    private float angle;
    private float3 _computedLightDir;
    private Color _computedLightColor;
    
    private RTHandle _lowResTarget;
    private RTHandle[] _historyTargets = new RTHandle[2];
    private int _pingPongIndex = 0;

    private Material _taaMaterial;
    private Matrix4x4 _prevViewProj;
    private bool _firstFrame = true;

    float remap(float v, float a, float b, float c, float d)
    {
        return c + (v - a) * (d - c) / (b - a);
    }
    
    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        _kernelIndex = cloudCompute.FindKernel("CSMain");

        // Текстура уменьшенного разрешения для Compute Shader
        _lowResTarget = RTHandles.Alloc(
            Vector2.one * 0.25f, // 1/4 от экрана!
            dimension: TextureDimension.Tex2D,
            colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
            enableRandomWrite: true,
            useDynamicScale: true,
            name: "CloudLowResRT"
        );

        // Два буфера для истории полного размера (пинг-понг)
        for (int i = 0; i < 2; i++)
        {
            _historyTargets[i] = RTHandles.Alloc(
                Vector2.one,
                dimension: TextureDimension.Tex2D,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                useDynamicScale: true,
                name: $"CloudHistoryRT_{i}"
            );
        }
    
        if (_taaMaterial == null)
            _taaMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/CloudTAA"));
        
        _lastTime = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        morningAngle = Mathf.Asin(-0.1f); 
        eveningAngle = Mathf.PI - morningAngle;
        angle = Mathf.Lerp(morningAngle, eveningAngle, timeOfDay);
    }

    private void UpdateDayNightCycle()
    {
        currentTime = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        float deltaTime = currentTime - _lastTime;
        _lastTime = currentTime;

        if (autoPlayTime)
        {
            timeOfDay += deltaTime / dayDurationSeconds;
            if (timeOfDay > 1.0f)
                timeOfDay = 0.0f;
        }

        morningAngle = Mathf.Acos(-1.0f); 
        eveningAngle = Mathf.Acos(1.0f);
        angle = Mathf.Lerp(eveningAngle, morningAngle, timeOfDay);
        
        _computedLightDir = math.normalizesafe(new float3(-Mathf.Cos(angle), Mathf.Sin(angle), 0.2f));
        _computedLightColor = lightColorGradient != null ? lightColorGradient.Evaluate(remap(angle, 0.0f, 3.14f, 0.0f, 1.0f)) : Color.white;

        // Debug.Log($"morning = {morningAngle}; evening = {eveningAngle}; angle = {angle}; dir = {_computedLightColor}");
    }
    
    protected override void Execute(CustomPassContext ctx)
    {
        if (cloudCompute == null || blitMaterial == null || _taaMaterial == null) return;

        var cmd = ctx.cmd;
        var cam = ctx.hdCamera.camera;
        
        UpdateDayNightCycle();
        
        Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
        Matrix4x4 view = cam.worldToCameraMatrix;
        Matrix4x4 vp = gpuProj * view;
        Matrix4x4 invViewProj = vp.inverse;
        // Берем чистую матрицу БЕЗ встроенного микро-сдвига HDRP
        // Matrix4x4 proj = cam.nonJitteredProjectionMatrix; 
        // Matrix4x4 view = cam.worldToCameraMatrix;
        // Matrix4x4 vp = proj * view;
        // Matrix4x4 invViewProj = vp.inverse;
        
        if (_firstFrame)
        {
            _prevViewProj = vp;
            _firstFrame = false;
        }

        // cmd.SetComputeTextureParam(cloudCompute, _kernelIndex, "Result", _target);
        cmd.SetComputeTextureParam(cloudCompute, _kernelIndex, "Result", _lowResTarget);
        
        cmd.SetComputeVectorParam(cloudCompute, "_CameraPosition", cam.transform.position);
        // Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
        // Matrix4x4 view = cam.worldToCameraMatrix;
        // Matrix4x4 invViewProj = (gpuProj * view).inverse;
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
        cmd.SetComputeTextureParam(
            cloudCompute,
            _kernelIndex,
            "_CloudMapTex",
            coverageTex
        );

        cmd.SetComputeFloatParam(cloudCompute, "_BaseNoiseScale", baseNoiseScale);
        cmd.SetComputeFloatParam(cloudCompute, "_CurlNoiseThreshold", curlNoiseThreshold);
        cmd.SetComputeFloatParam(cloudCompute, "_DetailNoiseScale", detailNoiseScale);
        cmd.SetComputeFloatParam(cloudCompute, "_DetailNoiseThreshold", detailNoiseThreshold);
        cmd.SetComputeFloatParam(cloudCompute, "_BaseNoiseThreshold", baseNoiseThreshold);
        
        cmd.SetComputeFloatParam(cloudCompute, "_CloudMapScale", coverageScale);
        cmd.SetComputeVectorParam(cloudCompute, "_CloudMapOffset", new Vector4(coverageOffset.x, coverageOffset.y, 0, 0));
        
        cmd.SetComputeVectorParam(cloudCompute, "_LightDir", new Vector4(_computedLightDir.x, _computedLightDir.y, _computedLightDir.z, 0).normalized);
        cmd.SetComputeVectorParam(cloudCompute, "_LightColor", new Vector4(_computedLightColor.r, _computedLightColor.g, _computedLightColor.b, _computedLightColor.a));
        cmd.SetComputeFloatParam(cloudCompute, "_LightStep", lightStep);
        
        Color zenith = zenithColorGradient != null ? zenithColorGradient.Evaluate(timeOfDay) : new Color(0.1f, 0.35f, 0.8f);
        Color horizon = horizonColorGradient != null ? horizonColorGradient.Evaluate(timeOfDay) : new Color(0.6f, 0.7f, 0.85f);
        cmd.SetComputeVectorParam(cloudCompute, "_ZenithColor", new Vector4(zenith.r, zenith.g, zenith.b, 1f));
        cmd.SetComputeVectorParam(cloudCompute, "_HorizonColor", new Vector4(horizon.r, horizon.g, horizon.b, 1f));
        
        cmd.SetComputeFloatParam(cloudCompute, "_Time", currentTime);
        cmd.SetComputeFloatParam(cloudCompute, "_WindSpeed", windSpeed);
        cmd.SetComputeVectorParam(cloudCompute, "_WindDirection", new Vector4(windDirection.x, windDirection.y, 0, 0).normalized);

        // Debug.Log($"Camera X: {cam.transform.position.x}, Camera Y: {cam.transform.position.y}, Camera Z: {cam.transform.position.z}");

        // int frameIndex = Time.frameCount % 16;
        // cloudCompute.SetInt("_FrameIndex", frameIndex);
        //
        // int x = Mathf.CeilToInt((_target.rt.width / 4.0f) / 8.0f);
        // int y = Mathf.CeilToInt((_target.rt.height / 4.0f) / 8.0f);
        //
        // cmd.DispatchCompute(cloudCompute, _kernelIndex, x, y, 1);
        //
        // // Debug.Log($"{x} and {y}");
        //
        // blitMaterial.SetTexture("_Source", _target);
        // HDUtils.DrawFullScreen(
        //     cmd,
        //     blitMaterial,
        //     ctx.cameraColorBuffer
        // );
        int frameIndex = Time.frameCount % 16;
        cloudCompute.SetInt("_FrameIndex", frameIndex);
    
        // Диспетчеризируем с учетом уменьшенной текстуры
        int x = Mathf.CeilToInt((cam.pixelWidth / 4.0f) / 8.0f);
        int y = Mathf.CeilToInt((cam.pixelHeight / 4.0f) / 8.0f);
        cmd.DispatchCompute(cloudCompute, _kernelIndex, x, y, 1);

        // --- TAA Репроекция ---
        int readIndex = _pingPongIndex;
        int writeIndex = (_pingPongIndex + 1) % 2;
        _pingPongIndex = writeIndex; // Переключаем на следующий кадр

        _taaMaterial.SetTexture("_LowResTex", _lowResTarget);
        _taaMaterial.SetTexture("_HistoryTex", _historyTargets[readIndex]);
        _taaMaterial.SetMatrix("_PrevViewProj", _prevViewProj);
        _taaMaterial.SetMatrix("_InvViewProj", invViewProj);
        _taaMaterial.SetVector("_CameraPosition", cam.transform.position);
        _taaMaterial.SetInt("_FrameIndex", frameIndex);

        // Рисуем собранный TAA-кадр в историю
        CoreUtils.SetRenderTarget(cmd, _historyTargets[writeIndex]);
        CoreUtils.DrawFullScreen(cmd, _taaMaterial);

        // Выводим финальную картинку на экран твоим старым материалом
        blitMaterial.SetTexture("_Source", _historyTargets[writeIndex]);
        HDUtils.DrawFullScreen(cmd, blitMaterial, ctx.cameraColorBuffer);

        // Запоминаем матрицу для следующего кадра
        _prevViewProj = vp;
    }

    protected override void Cleanup()
    {
        _lowResTarget?.Release();
        _historyTargets[0]?.Release();
        _historyTargets[1]?.Release();
    }
}

