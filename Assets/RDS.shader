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
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // =====================================================
            // Material側で設定する値
            // =====================================================
            float _EyeSign;

            // =====================================================
            // C#側から受け取る値
            // =====================================================
            float _WidthPx;
            float _HeightPx;
            float _Pp;

            float _CircleCenterXPx;
            float _CircleCenterYPx;
            float _CircleRadiusPx;

            float _HalfDisparityPx;

            float _BackgroundSeed;
            float _ObjectSeed;

            float _ShowCircleGuide;

            float _CropEnabled;
            float _CropHalfWidthMm;
            float _CropHalfHeightMm;

            float _VirtualOffsetXPx;
            float _VirtualOffsetYPx;

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

            // =====================================================
            // PCG風 uint hash
            // =====================================================

            uint PcgHash(uint x)
            {
                uint state = x * 747796405u + 2891336453u;
                uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
                return (word >> 22u) ^ word;
            }

            uint Hash2D(uint2 p, uint seed)
            {
                uint h = seed;
                h ^= PcgHash(p.x + 0x9E3779B9u);
                h ^= PcgHash(p.y + 0x85EBCA6Bu);
                return PcgHash(h);
            }

            float Random01(uint2 cell, uint seed)
            {
                uint h = Hash2D(cell, seed);
                return (float)(h & 0x00FFFFFFu) / 16777215.0;
            }

            float RandomGray(uint2 cell, uint seed)
            {
                return Random01(cell, seed);
            }

            uint SeedToUint(float seed)
            {
                return (uint)max(seed, 0.0);
            }

            // =====================================================
            // ランダムグレースケールの双線形補間サンプリング
            //
            // 入力pは「整数座標が画素中心」に対応する座標系
            // p=(i,j) なら RandomGray(i,j)
            // p=(i+0.25,j) なら隣接値を25%ブレンド
            // =====================================================

            float SampleRandomGrayBilinear(float2 p, uint seed)
            {
                float2 baseF = floor(p);
                float2 fracF = frac(p);

                int2 baseI = int2(baseF);

                uint2 c00 = uint2(baseI.x,     baseI.y);
                uint2 c10 = uint2(baseI.x + 1, baseI.y);
                uint2 c01 = uint2(baseI.x,     baseI.y + 1);
                uint2 c11 = uint2(baseI.x + 1, baseI.y + 1);

                float v00 = RandomGray(c00, seed);
                float v10 = RandomGray(c10, seed);
                float v01 = RandomGray(c01, seed);
                float v11 = RandomGray(c11, seed);

                float vx0 = lerp(v00, v10, fracF.x);
                float vx1 = lerp(v01, v11, fracF.x);

                return lerp(vx0, vx1, fracF.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float widthPx = max(_WidthPx, 1.0);
                float heightPx = max(_HeightPx, 1.0);
                float pp = max(_Pp, 0.000001);

                // =================================================
                // UV -> 実画面pixel座標
                // フラグメント中心は概ね i+0.5 に位置する
                // =================================================
                float2 pixel;
                pixel.x = input.uv.x * widthPx;
                pixel.y = (1.0 - input.uv.y) * heightPx;

                // =================================================
                // 仮想曲面ディスプレイ座標
                // =================================================
                float2 virtualPixel =
                    pixel - float2(_VirtualOffsetXPx, _VirtualOffsetYPx);

                // =================================================
                // 物理サイズベースのクロップ
                // =================================================
                if (_CropEnabled > 0.5)
                {
                    float xMm = (virtualPixel.x - widthPx * 0.5) * pp;
                    float yMm = (heightPx * 0.5 - virtualPixel.y) * pp;

                    bool outside =
                        (abs(xMm) > _CropHalfWidthMm) ||
                        (abs(yMm) > _CropHalfHeightMm);

                    if (outside)
                    {
                        return half4(0.0, 0.0, 0.0, 1.0);
                    }
                }

                // =================================================
                // ランダムパターン参照座標
                //
                // pixel座標は画素中心が i+0.5 なので，
                // -0.5 して整数座標を画素中心に合わせる
                // =================================================
                float2 baseSampleCoord = virtualPixel - float2(0.5, 0.5);

                // =================================================
                // 背景グレースケール
                // =================================================
                float bg = SampleRandomGrayBilinear(
                    baseSampleCoord,
                    SeedToUint(_BackgroundSeed)
                );

                // =================================================
                // 左右視差
                // =================================================
                float shiftPx = _EyeSign * _HalfDisparityPx;

                float2 shiftedCenter = float2(
                    _CircleCenterXPx + shiftPx,
                    _CircleCenterYPx
                );

                // =================================================
                // 円マスク
                // =================================================
                float2 diff = virtualPixel - shiftedCenter;
                float dist2 = dot(diff, diff);

                float insideCircle = step(
                    dist2,
                    _CircleRadiusPx * _CircleRadiusPx
                );

                // =================================================
                // 円内部グレースケール
                //
                // 円自体は ±shiftPx 移動
                // 円内部パターンは shift を戻した座標で参照
                // これにより左右対応を保つ
                // =================================================
                float2 objectSampleCoord =
                    baseSampleCoord - float2(shiftPx, 0.0);

                float obj = SampleRandomGrayBilinear(
                    objectSampleCoord,
                    SeedToUint(_ObjectSeed)
                );

                float value = lerp(bg, obj, insideCircle);

                // =================================================
                // デバッグ用：円の輪郭
                // =================================================
                if (_ShowCircleGuide > 0.5)
                {
                    float d = sqrt(dist2);
                    float edge = 1.0 - smoothstep(
                        1.0,
                        3.0,
                        abs(d - _CircleRadiusPx)
                    );

                    value = lerp(value, 1.0, edge * 0.7);
                }

                return half4(value, value, value, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}