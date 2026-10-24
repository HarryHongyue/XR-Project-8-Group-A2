Shader "Project8/EarthSurface"
{
    // Matte, readable planet surface: equirectangular albedo texture with an
    // ambient floor plus single-light diffuse. No specular highlights, no
    // emission - the continents stay legible in the dim space scene.
    Properties
    {
        _MainTex ("Surface (2:1 equirectangular)", 2D) = "white" {}
        _AmbientFloor ("Ambient Floor", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Matte

        sampler2D _MainTex;
        fixed _AmbientFloor;

        struct Input
        {
            float2 uv_MainTex;
        };

        // Ambient floor keeps the night side readable; diffuse term still gives
        // a day/night gradient from the scene's single directional light.
        half4 LightingMatte (SurfaceOutput s, half3 lightDir, half atten)
        {
            half nl = saturate(dot(s.Normal, lightDir));
            half4 c;
            c.rgb = s.Albedo * (_AmbientFloor + _LightColor0.rgb * nl * atten);
            c.a = 1;
            return c;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = c.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
