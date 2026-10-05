Shader "Pause/WorldRailRepeat"
{
    Properties
    {
        _MainTex ("Rail", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Overlap ("Vertical overlap", Range(0, .1)) = .03
        _BlackCutout ("Remove black exterior matte", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Overlap, _BlackCutout;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                // Overlap the final 3% with the first 3%. At the UV wrap,
                // both sides sample y=.97, so the scroll remains continuous.
                float period = 1 - _Overlap;
                float y = frac(i.uv.y) * period;
                fixed4 c = tex2D(_MainTex, float2(i.uv.x, y));
                if (_Overlap > 0 && y < _Overlap)
                {
                    fixed4 tail = tex2D(_MainTex, float2(i.uv.x, y + period));
                    float t = smoothstep(0, _Overlap, y);
                    // Blend premultiplied color to preserve alpha cutouts.
                    float a = lerp(tail.a, c.a, t);
                    c.rgb = lerp(tail.rgb * tail.a, c.rgb * c.a, t) / max(a, .0001);
                    c.a = a;
                }
                float matte = smoothstep(.005, .015, max(c.r, max(c.g, c.b)));
                c.a *= lerp(1, matte, _BlackCutout);
                return c * _Color;
            }
            ENDCG
        }
    }
}
