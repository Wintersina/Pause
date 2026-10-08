// Brightens a backdrop sprite at draw time without touching its PNG
// (BackdropGrade.cs holds the same maths on the CPU for the tests).
//
// Value lift, hue- and saturation-preserving: the texel's HSV value v goes
// through a soft "screen" curve f(v) = 1 - (1 - v)^_Lift and the colour is
// scaled by f(v) / v. Near black that is a plain gain of _Lift (shadows keep
// their shape, a dark base stays dark), near white it rolls off to 1 so
// highlights never clip. _Sat then nudges saturation away from grey, and
// _AlphaLift thickens translucent art the same way (1 - (1 - a)^_AlphaLift:
// the cloud ceiling's banks). All three at 1 draw the art as painted.
// The SpriteRenderer colour (tint, fades) multiplies afterwards as usual.
Shader "Pause/BackdropGrade"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Lift ("Value lift", Range(1, 4)) = 1
        _Sat ("Saturation", Range(0, 2)) = 1
        _AlphaLift ("Alpha lift", Range(1, 8)) = 1
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
            float _Lift, _Sat, _AlphaLift;

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
                float4 t = tex2D(_MainTex, i.uv);
                float v = max(t.r, max(t.g, t.b));
                float f = 1.0 - pow(saturate(1.0 - v), _Lift);
                t.rgb *= v > 1e-4 ? f / v : 0.0;
                float l = (t.r + t.g + t.b) / 3.0;
                t.rgb = saturate(l + (t.rgb - l) * _Sat);
                t.a = 1.0 - pow(saturate(1.0 - t.a), _AlphaLift);
                fixed4 c = t * i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
