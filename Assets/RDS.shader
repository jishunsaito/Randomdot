Shader "Unlit/RDS"
{
    Properties
    {
        _EyeSign ("Eye Sign (+1 = Left, -1 = Right)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        LOD 100

        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "RDSUnlit"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // =====================================================
            // Material側で定義する値
            // =====================================================
            float _EyeSign;

            // =====================================================
            // C#側から受け取る値
            // Propertiesには書かない
            // =====================================================
            float _WidthPx;
            float _HeightPx;

            float _CircleCenterXPx;
            float _CircleCenterYPx;
            float _CircleRadiusPx;

            float _HalfDisparityPx;

            float _PWhite;
            float _BackgroundSeed;
            float _ObjectSeed;

            float _ShowCircleGuide;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float hash21(float2 p)
            {
                p = floor(p);
                float h = dot(p, float2(127.1, 311.7));
                return frac(sin(h) * 43758.5453123);
            }

            float random_dot(float2 pixel, float seed, float pWhite)
            {
                float r = hash21(pixel + seed);
                return step(1.0 - pWhite, r);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float widthPx = max(_WidthPx, 1.0);
                float heightPx = max(_HeightPx, 1.0);

                // uv -> pixel座標
                // pixel.x: 左から右
                // pixel.y: 上から下
                float2 pixel;
                pixel.x = input.uv.x * widthPx;
                pixel.y = (1.0 - input.uv.y) * heightPx;

                // 背景ランダムドット
                float bg = random_dot(pixel, _BackgroundSeed, _PWhite);

                // L Material: _EyeSign = +1
                // R Material: _EyeSign = -1
                float shiftPx = _EyeSign * _HalfDisparityPx;

                // この目に表示される円中心
                float2 shiftedCenter = float2(
                    _CircleCenterXPx + shiftPx,
                    _CircleCenterYPx
                );

                float2 diff = pixel - shiftedCenter;
                float dist2 = dot(diff, diff);

                float insideCircle = step(dist2, _CircleRadiusPx * _CircleRadiusPx);

                // 円内部のランダムドットは，
                // シフト前の座標を参照することで左右で同じパターンにする
                float2 sourcePixel = pixel - float2(shiftPx, 0.0);
                float obj = random_dot(sourcePixel, _ObjectSeed, _PWhite);

                float value = lerp(bg, obj, insideCircle);

                // デバッグ用の円輪郭
                if (_ShowCircleGuide > 0.5)
                {
                    float d = sqrt(dist2);
                    float edge = 1.0 - smoothstep(1.0, 3.0, abs(d - _CircleRadiusPx));
                    value = lerp(value, 1.0, edge * 0.7);
                }

                return half4(value, value, value, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}