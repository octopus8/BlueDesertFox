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

/**********************************************************************************************/            
            
            float3 palette(float t, float3 a, float3 b, float3 c, float3 d)
            {
                return a + b*cos(PI * 2*(c*t+d));
            }

            
            half4 ComplexPattern0(Varyings IN)
            {
                // Center the UVs (0 to 1 becomes -1 to 1)
                float2 centeredUV = (IN.uv - 0.5) * 2.0;

               // Calculate aspect ratio using _ScaledScreenParams
                float aspectRatio = _ScaledScreenParams.x / _ScaledScreenParams.y;
                
                // Apply aspect ratio correction to prevent stretching
                // This makes circles stay circular regardless of screen aspect ratio
                centeredUV.x *= aspectRatio;

                float3 finalColor = float3(0, 0, 0);
                
                float2 centeredUV2 = centeredUV;

                float3 a = float3(0.5, 0.5, 0.5);
                float3 b = float3(0.5, 0.5, 0.5);
                float3 c = float3(1.0, 1.0, 1.0);
                float3 d = float3(0.263, 0.416, 0.557);
                
                for (float i=0; i<2; ++i)
                {
                    centeredUV = frac(centeredUV * 1.5) - 0.5;
                    
                    float dist = length(centeredUV) * exp(-length(centeredUV2));
                    dist = sin(dist * 8 + _Time.y) / 8;
                    dist = abs(dist);
                    dist = pow(0.01 / dist, 1.2);
                    float3 color = palette(length(centeredUV2) + i*0.4 + _Time.y * 0.4, a, b, c, d);
                    finalColor += color * dist;
                }
                return half4(finalColor, 1.0);
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

            
            half4 NeonCircle(Varyings IN)
            {
                // Center the UVs (0 to 1 becomes -1 to 1)
                float2 centeredUV = (IN.uv - 0.5) * 2.0;

               // Calculate aspect ratio using _ScaledScreenParams
                float aspectRatio = _ScaledScreenParams.x / _ScaledScreenParams.y;
                
                // Apply aspect ratio correction to prevent stretching
                // This makes circles stay circular regardless of screen aspect ratio
                centeredUV.x *= aspectRatio;

                // Get the distance of the pixel from the center.
                float dist = length(centeredUV);

                // Subtract the radius.
                float radius = 0.5;
                dist -= radius;

                // Use the absolute value of the distance.
                dist = abs(dist);

                // Convert the dist value to 0 or 1 using the circle width.
                float circleWidth = 0.1;
                float circleFadePercent = 0.99;
                float circleFadeWidth = circleFadePercent * circleWidth;
                dist = smoothstep(circleWidth - circleFadeWidth, circleWidth, dist);
                
                // Apply neon glow to fade.
                float v = circleFadeWidth;
                dist = v / dist - v;

                // Increase contrast.
                dist = pow(dist, 1.2);
                
                // Apply color.
                float3 color = float3(0.01, 0.2, 0.3) * 5.0;
                color *= dist;

                // Return the color.
                return half4(color, dist);
            }
            

            half4 Rings(Varyings IN)
            {
                // Center the UVs (0 to 1 becomes -1 to 1)
                float2 centeredUV = (IN.uv - 0.5) * 2.0;

               // Calculate aspect ratio using _ScaledScreenParams
                float aspectRatio = _ScaledScreenParams.x / _ScaledScreenParams.y;
                
                // Apply aspect ratio correction to prevent stretching
                // This makes circles stay circular regardless of screen aspect ratio
                centeredUV.x *= aspectRatio;

                // Get the distance of the pixel from the center.
                float dist = length(centeredUV);
                
                // Subtract the radius.
                float radius = 0.5;
                dist -= radius;

                // Create rings.
                int ringCount = 1;
                float v = PI * ringCount;
                dist = sin(dist*v)/v;
                
                // Use the absolute value of the distance.
                dist = abs(dist);

                // Convert the dist value to 0 or 1 using the circle width.
                float circleWidth = 0.01;
                dist = step(circleWidth, dist);  // This is like smoothstep but with fade width of zero.

                // Return the color.
                return half4(dist, dist, dist, 1.0);
            }

            
            half4 Circle(Varyings IN)
            {
                // Center the UVs (0 to 1 becomes -1 to 1)
                float2 centeredUV = (IN.uv - 0.5) * 2.0;

               // Calculate aspect ratio using _ScaledScreenParams
                float aspectRatio = _ScaledScreenParams.x / _ScaledScreenParams.y;
                
                // Apply aspect ratio correction to prevent stretching
                // This makes circles stay circular regardless of screen aspect ratio
                centeredUV.x *= aspectRatio;

                // Get the distance of the pixel from the center.
                float dist = length(centeredUV);

                // Subtract the radius.
                float radius = 0.5;
                dist -= radius;

                // Use the absolute value of the distance.
                dist = abs(dist);

                // Convert the dist value to 0 or 1 using the circle width.
                float circleWidth = 0.1;
                float circleFadeWidth = 0;
                dist = smoothstep(circleWidth - circleFadeWidth, circleWidth, dist);
//                dist = step(circleWidth, dist);  // This is like smoothstep but with fade width of zero.

                // Return the color.
                return half4(dist, dist, dist, 1.0);
            }

            
            half4 FadeFromCenter(Varyings IN)
            {
                // Center the UVs (0 to 1 becomes -1 to 1)
                float2 centeredUV = (IN.uv - 0.5) * 2.0;

               // Calculate aspect ratio using _ScaledScreenParams
                float aspectRatio = _ScaledScreenParams.x / _ScaledScreenParams.y;
                
                // Apply aspect ratio correction to prevent stretching
                // This makes circles stay circular regardless of screen aspect ratio
                centeredUV.x *= aspectRatio;

                float dist = length(centeredUV);
                return half4(dist, dist, dist, 1.0);
            }


            half4 TextureColor(Varyings IN)
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                return color;
            }


            half4 UVGradient(Varyings IN)
            {
                  return half4(IN.uv, 0, 1);
            }
            
/**********************************************************************************************/            
            

            half4 frag(Varyings IN) : SV_Target
            {
                return GridPattern(IN);
                return NeonCircle(IN);
                return Rings(IN);
                return Circle(IN);
                return FadeFromCenter(IN);
                return TextureColor(IN);
                return UVGradient(IN);
            }

            
            ENDHLSL
        }
    }
}
