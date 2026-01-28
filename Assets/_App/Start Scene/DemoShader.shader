Shader "Custom/DemoShader"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _Color ("Circle Color", Color) = (1, 0.5, 0, 1)
        _BackgroundColor ("Background Color", Color) = (0.1, 0.1, 0.15, 1)
        _Radius ("Circle Radius", Range(0, 1)) = 0.3
        _EdgeSoftness ("Edge Softness", Range(0, 0.1)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half4 _Color;
                half4 _BackgroundColor;
                float _Radius;
                float _EdgeSoftness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 UVGradient(Varyings IN)
            {
                  return half4(IN.uv, 0, 1);
            }

            half4 TextureColor(Varyings IN)
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                return color;
            }


            half4 GridPattern(Varyings IN)
            {
                // Get screen position
                float2 screenPos = IN.positionHCS.xy;
                
                // Calculate grid size (10 pixels per cell)
                float gridSize = 10.0;
                
                // Create grid pattern
                float2 grid = frac(screenPos / gridSize);
                float gridLine = step(0.95, max(grid.x, grid.y));
                
                return half4(gridLine.xxx, 1.0);                
            }


            half4 Other(Varyings IN)
            {
               // Calculate aspect ratio using _ScaledScreenParams
                float aspectRatio = _ScaledScreenParams.x / _ScaledScreenParams.y;
                
                // Center the UVs (0 to 1 becomes -0.5 to 0.5)
                float2 centeredUV = IN.uv - 0.5;
                
                // Apply aspect ratio correction to prevent stretching
                // This makes circles stay circular regardless of screen aspect ratio
                centeredUV.x *= aspectRatio;
                
                // Calculate distance from center
                float dist = length(centeredUV);
                
                // Create smooth circle
                float circle = 1.0 - smoothstep(_Radius - _EdgeSoftness, _Radius + _EdgeSoftness, dist);
                
                // Mix colors
                half4 finalColor = lerp(_BackgroundColor, _Color, circle);
                
                return finalColor;                
            }

            half4 Other2(Varyings IN)
            {
                // Center the UVs (0 to 1 becomes -1 to 1)
                float2 centeredUV = (IN.uv - 0.5) * 2.0;

               // Calculate aspect ratio using _ScaledScreenParams
                float aspectRatio = _ScaledScreenParams.x / _ScaledScreenParams.y;
                
                // Apply aspect ratio correction to prevent stretching
                // This makes circles stay circular regardless of screen aspect ratio
                centeredUV.x *= aspectRatio;
                
                float d = length(centeredUV);

                d -= 0.5;
                
                d = abs(d);

//                d = step(0.1, d);
                d = smoothstep(0, 0.1, d);
                
                return half4(d, d, d, 1);
            }
            

            half4 frag(Varyings IN) : SV_Target
            {
                return Other2(IN);
                return GridPattern(IN);
                return TextureColor(IN);
//                return UVGradient(IN);
            }

            
            ENDHLSL
        }
    }
}
