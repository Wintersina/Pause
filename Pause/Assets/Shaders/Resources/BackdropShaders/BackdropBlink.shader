// A sprite whose small bright, saturated lamps blink (Space wrecks and debris).
// Lamps are found per source texel (saturated and bright); each blinks on its
// own rhythm from a hash of its texel, driven by _Blink, a clock the director
// advances from scaled time (a paused game holds the lamps still).
Shader "Pause/BackdropBlink"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Blink ("Lamp blink clock (s)", Float) = 0
        _BlinkAmount ("Lamp blink strength", Range(0, 1)) = 1
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

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Blink;
            half _BlinkAmount;

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
                fixed4 c = tex2D(_MainTex, i.uv);
                float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
                float lamp = smoothstep(0.38, 0.55, mx) * smoothstep(0.40, 0.65, (mx - mn) / max(mx, 1e-3));
                float2 tc = floor(i.uv * _MainTex_TexelSize.zw);
                float h = frac(sin(dot(tc, float2(12.9898, 78.233))) * 43758.5453);
                float ph = frac(_Blink * (0.30 + 0.45 * frac(h * 7.3)) + h);
                float on = smoothstep(0.0, 0.06, ph) * (1.0 - smoothstep(0.52, 0.60, ph));
                c.rgb *= lerp(1.0, 0.22 + 1.0 * on, lamp * _BlinkAmount);
                c *= i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
