// Draws an equirectangular panorama, or a cubemap, on a sphere viewed from the inside.
// The direction is computed per pixel, so there are no UV seams or pinched poles.
// Wider-than-2:1 images (partial drone panos) cover only _VCover degrees around the horizon.
// ZWrite is on so the camera's skybox can't draw over the panorama.
// _Alpha < 1 blends it over the AR camera feed (the "Now / Proposed" slider in On-site mode).
Shader "VRLandscape/Panorama"
{
    Properties
    {
        _MainTex ("Equirectangular", 2D) = "black" {}
        _Cube ("Cubemap", CUBE) = "" {}
        _UseCube ("Use Cubemap", Float) = 0
        _Yaw ("Yaw Offset (deg)", Float) = 0
        _Fade ("Fade", Range(0, 1)) = 1
        _VCover ("Vertical Coverage (deg)", Float) = 180
        _Alpha ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Front
        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            samplerCUBE _Cube;
            float _UseCube;
            float _Yaw;
            float _Fade;
            float _VCover;
            float _Alpha;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                fixed4 c;
                if (_UseCube > 0.5)
                {
                    c = texCUBElod(_Cube, float4(d, 0));
                }
                else
                {
                    float lon = atan2(d.x, d.z) - radians(_Yaw);
                    float lat = asin(clamp(d.y, -1.0, 1.0));
                    float cover = radians(clamp(_VCover, 1.0, 180.0));
                    float2 uv = float2(lon / (2.0 * UNITY_PI) + 0.5, lat / cover + 0.5);
                    // Above/below a partial pano's band the top and bottom rows (sky, ground) are extended.
                    c = tex2Dlod(_MainTex, float4(uv.x, saturate(uv.y), 0, 0));
                }
                return fixed4(c.rgb * _Fade, _Alpha);
            }
            ENDCG
        }
    }
}
