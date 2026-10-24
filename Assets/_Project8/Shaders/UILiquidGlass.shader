// Lightweight translucent "smoke glass" for the cockpit UI. Rounded-rect SDF
// clipping in screen-constant pixel units (rect size arrives per-vertex in
// TEXCOORD1), a soft top-edge sheen and a subtle rim - deliberately NOT real
// refraction and no GrabPass, so it stays cheap on Quest.
Shader "Project8/UI/LiquidGlass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _CornerRadius ("Corner Radius (px)", Range(0,80)) = 16
        _Sheen ("Top Sheen", Range(0,1)) = 0.45
        _Rim ("Rim Highlight", Range(0,1)) = 0.5
        _EdgeSoft ("Edge Softness (px)", Range(0.25,8)) = 1.5

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                // Per-graphic data from LiquidGlassGraphic:
                //   xy = rect size in px, zw = reserved
                float4 dims     : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 dims          : TEXCOORD1;
                float4 worldPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _CornerRadius;
            float _Sheen;
            float _Rim;
            float _EdgeSoft;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.dims = v.dims;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 size = IN.dims.xy;
                float radius = min(_CornerRadius, min(size.x, size.y) * 0.5);
                float2 p = (IN.texcoord - 0.5) * size;
                float sd = length(max(abs(p) - (size * 0.5 - radius), 0.0)) - radius;

                // Feathered round-rect coverage: inside -> 1, outside -> 0.
                float cover = 1.0 - smoothstep(-_EdgeSoft, 0.0, sd);

                half4 col = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // Internal smoke gradient - slightly brighter toward the top.
                col.rgb *= lerp(0.82, 1.06, saturate(IN.texcoord.y));

                // Top-edge sheen band.
                float sheen = saturate(1.0 - abs(IN.texcoord.y - 0.93) / 0.07);
                col.rgb += _Sheen * 0.30 * sheen;

                // Thin luminous rim hugging the rounded edge.
                float rim = smoothstep(-5.0, -1.0, sd) * (1.0 - smoothstep(-1.0, 0.0, sd));
                col.rgb += _Rim * 0.18 * rim;

                col.a *= cover;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (col.a - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
