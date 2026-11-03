// Interstellar-style accretion disc: unlit additive swirl with doppler
// beaming - the side of the disc rotating TOWARD the camera burns brighter
// (white-hot) while the receding side dims to dull red, plus slowly drifting
// streaks so the gas visibly churns. Works with the vertex-coloured meshes
// built by AccretionDress.
Shader "Project8/AccretionDisc"
{
    Properties
    {
        _TintColor ("Tint", Color) = (1,0.75,0.4,1)
        _SwirlSpeed ("Swirl Speed", Float) = 0.08
        _Streaks ("Streak Count", Float) = 9
        _Doppler ("Doppler Strength", Range(0,2)) = 1.1
        _Boost ("HDR Boost", Float) = 1.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _TintColor;
            float _SwirlSpeed, _Streaks, _Doppler, _Boost;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float beam : TEXCOORD1; // doppler factor
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 view = normalize(_WorldSpaceCameraPos.xyz - wpos);
                // tangential flow direction at this vertex (disc spins about Y)
                float3 up = normalize(mul((float3x3)unity_ObjectToWorld, float3(0,1,0)));
                float3 tangent = normalize(cross(up, normalize(wpos -
                    mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz) + 1e-5));
                float d = dot(tangent, view);       // >0 = moving toward camera
                o.beam = saturate(0.5 + d * _Doppler);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // angular coordinate 0..1 scrolls with time -> churning gas
                float a = i.uv.x + _Time.y * _SwirlSpeed * (1.5 - i.uv.y);
                float streak = 0.75
                    + 0.25 * sin(a * 6.2831 * _Streaks + i.uv.y * 22.0)
                    + 0.12 * sin(a * 6.2831 * 3.0 - i.uv.y * 9.0);
                // doppler: approaching side white-hot, receding side deep red
                float heat = lerp(0.35, 1.9, i.beam);
                fixed4 col = i.color * _TintColor;
                col.rgb *= streak * heat * _Boost;
                col.rgb = lerp(col.rgb, col.rgb * float3(1.4f, 0.55f, 0.25f),
                               1.0 - i.beam * 0.75);   // redshift tint
                col.a = saturate(col.a * (0.55 + 0.45 * streak));
                return col;
            }
            ENDCG
        }
    }
    Fallback "Legacy Shaders/Particles/Additive"
}
