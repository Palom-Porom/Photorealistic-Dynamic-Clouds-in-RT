Shader "Hidden/CloudTAA"
{
    Properties {}
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "CloudTAA"
            ZWrite Off ZTest Always Blend Off Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            Texture2D _LowResTex;
            Texture2D _HistoryTex;
            SamplerState sampler_LinearClamp;

            float4x4 _PrevViewProj;
            float4x4 _InvViewProj;
            float3 _CameraPosition;
            int _FrameIndex;

            static const uint2 Bayer4x4[16] = {
                uint2(0,0), uint2(2,2), uint2(0,2), uint2(2,0),
                uint2(1,1), uint2(3,3), uint2(1,3), uint2(3,1),
                uint2(0,1), uint2(2,3), uint2(0,3), uint2(2,1),
                uint2(1,0), uint2(3,2), uint2(1,2), uint2(3,0)
            };

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                uint2 fullResId = uint2(uv * _ScreenSize.xy);
                uint2 offset = Bayer4x4[_FrameIndex];
                bool isCurrentFrame = (fullResId.x % 4 == offset.x) && (fullResId.y % 4 == offset.y);

                // 1. БЕРЕМ АКТУАЛЬНУЮ ДИСТАНЦИЮ ТЕКУЩЕГО КАДРА
                // _LowResTex имеет размер 1/4 от экрана, поэтому делим координаты на 4
                uint2 lowResId = fullResId / 4;
                float4 currentData = _LowResTex.Load(int3(lowResId, 0));
                float currentDist = currentData.a;

                // 2. РЕПРОЕКЦИЯ ДЛЯ ВСЕХ ПИКСЕЛЕЙ
                // Если это небо (dist < 0.1), используем огромную дистанцию. 
                // Это заставит небо вращаться вместе с камерой (как скайбокс).
                float reprojDist = currentDist > 0.1 ? currentDist : 100000.0;

                float2 ndc;
                ndc.x = uv.x * 2.0 - 1.0;
                ndc.y = (1.0 - uv.y) * 2.0 - 1.0; 

                float4 clipPos = float4(ndc, 1.0, 1.0);
                float4 worldPos = mul(_InvViewProj, clipPos);
                float3 viewDir = normalize(worldPos.xyz / worldPos.w - _CameraPosition);
                
                // Находим, где эта точка находилась в мире
                float3 cloudWorldPos = _CameraPosition + viewDir * reprojDist;
                
                // Проецируем мировые координаты в предыдущий кадр
                float4 prevClip = mul(_PrevViewProj, float4(cloudWorldPos, 1.0));
                float2 prevNdc = prevClip.xy / prevClip.w;
                
                float2 prevUV;
                prevUV.x = (prevNdc.x + 1.0) * 0.5;
                prevUV.y = 1.0 - ((prevNdc.y + 1.0) * 0.5);

                // 3. ЧТЕНИЕ ИСТОРИИ
                float4 historyData = currentData; // Fallback на актуальный кадр, если вышли за границы
                if (prevUV.x >= 0.0 && prevUV.x <= 1.0 && prevUV.y >= 0.0 && prevUV.y <= 1.0)
                {
                    historyData = _HistoryTex.SampleLevel(sampler_LinearClamp, prevUV, 0);
                }

                // 4. ПРОВЕРКА ОККЛЮЗИИ (Устранение дыр и размазываний)
                float histDist = historyData.a;
                bool isCloudNow = currentDist > 0.1;
                bool wasCloudThen = histDist > 0.1;
                
                // Если пиксель сменил состояние (было небо - стало облако, или наоборот), 
                // история невалидна. Мы моментально заливаем эту "дыру" актуальными данными (currentData).
                if (isCloudNow != wasCloudThen) 
                {
                    historyData = currentData;
                }

                // 5. ФИНАЛЬНЫЙ БЛЕНДИНГ
                if (isCurrentFrame)
                {
                    // 0.9 = мягкое сглаживание. Если шлейфы всё ещё заметны, снизь до 0.85
                    float blendWeight = 0.90; 
                    
                    // Небо мы заменяем сразу без смешивания, чтобы исключить гостинг
                    if (!isCloudNow) blendWeight = 1.0; 
                    
                    return lerp(historyData, currentData, blendWeight);
                }
                
                return historyData;
            }
            ENDHLSL
        }
    }
}