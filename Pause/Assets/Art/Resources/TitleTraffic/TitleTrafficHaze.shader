// Depth haze for the home screen's far ship traffic (TitleScreenTraffic).
//
// A plain sprite shader that pulls the sprite toward grey (desaturate), dims
// it, then mixes in the sky's INDIGO_1 haze -- docs/art-style.md 4.1: things
// further back are darker and less saturated than anything in front. Flat
// per-pixel colour math only, no gradients or blur, so the cel art keeps its
// hard shapes and ink; it just sits back in the night air.
//
// Lives in Resources so builds include it (loaded by name at runtime).
Shader "Pause/TitleTrafficHaze"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _HazeColor ("Haze colour", Color) = (0.165, 0.18, 0.42, 1)
        _Haze ("Haze amount", Range(0, 1)) = 0.3
        _Desat ("Desaturate", Range(0, 1)) = 0.45
        _Dim ("Value", Range(0, 1)) = 0.75
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _HazeColor;
            half _Haze, _Desat, _Dim;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                half grey = dot(c.rgb, half3(0.299, 0.587, 0.114));
                c.rgb = lerp(c.rgb, grey.xxx, _Desat) * _Dim;
                c.rgb = lerp(c.rgb, _HazeColor.rgb, _Haze);
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
