// Invisible ground that only shows the shadows cast on it, so virtual trees shade the real ground in AR.
Shader "VRLandscape/ShadowCatcher"
{
    Properties
    {
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest+49" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"

            float _ShadowStrength;

            struct v2f
            {
                float4 pos : SV_POSITION;
                SHADOW_COORDS(0)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed atten = SHADOW_ATTENUATION(i);
                return fixed4(0, 0, 0, (1 - atten) * _ShadowStrength);
            }
            ENDCG
        }
    }
    Fallback "VertexLit"
}
