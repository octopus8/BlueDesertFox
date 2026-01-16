Shader "LiquidForce/CameraFader"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0, 0, 1)
    }
    
    SubShader
    {
        // Set the render queue to Transparent to ensure it draws after opaque objects
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        
        // Disable writing to the depth buffer for proper blending of transparent objects
        ZWrite Off
        
        // Disable depth test
        ZTest Always
        
        // Set up the blending mode: SrcAlpha OneMinusSrcAlpha is standard for smooth transparency
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                // Transform vertex from object space to clip space
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Return the color defined in the properties, including the alpha value
                return _Color;
            }
            ENDCG
        }
    }
}
