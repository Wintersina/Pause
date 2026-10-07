Shader "Pause/WorldRailRepeat"
{
    Properties
    {
        _MainTex ("Rail", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Overlap ("Vertical overlap", Range(0, .1)) = .03
        _BlackCutout ("Remove black exterior matte", Range(0,1)) = 0
        // Dark inner edge (WorldPainter.RailEdge): the band of the rail that
        // faces the lane is shaded toward near-black so the rail reads as a
        // recessed wall. Texture-space u of the art's inner (lane-facing)
        // silhouette and outer silhouette; both walls sample the same u (the
        // right one is mirrored by its UV scale), so one band serves both.
        _EdgeInnerU ("Inner silhouette u", Range(0,1)) = 1
        _EdgeOuterU ("Outer silhouette u", Range(0,1)) = 0
        _EdgeDark ("Edge darkening strength", Range(0,1)) = 0
        _EdgeWidth ("Edge band, share of rail width", Range(0,.5)) = .16
        _EdgeSteps ("Edge falloff steps", Range(1,8)) = 4
        _EdgeShadow ("Shadow into the gaps / lane, alpha", Range(0,1)) = 0
        _EdgeShadowWidth ("Shadow past the silhouette, share of rail width", Range(0,.2)) = .05
        _EdgeLampKeep ("Neon lamps keep their light", Range(0,1)) = .85
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
            float _EdgeInnerU, _EdgeOuterU, _EdgeDark, _EdgeWidth, _EdgeSteps;
            float _EdgeShadow, _EdgeShadowWidth, _EdgeLampKeep;
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
                float peak = max(c.r, max(c.g, c.b));
                float matte = smoothstep(.005, .015, peak);
                c.a *= lerp(1, matte, _BlackCutout);

                // Inner-edge shade. d: how far into the rail from its inner
                // silhouette, in rail widths (< 0 is past it, in the lane).
                // Stepped in _EdgeSteps bands so the falloff stays pixel art.
                float railW = max(_EdgeInnerU - _EdgeOuterU, .0001);
                float d = (_EdgeInnerU - frac(i.uv.x)) / railW;
                float band = saturate(1 - d / max(_EdgeWidth, .0001));
                band = ceil(band * _EdgeSteps - .0001) / _EdgeSteps;
                band *= step(0, d);
                // saturated, bright pixels are the neon lamps: keep them lit
                float sat = peak - min(c.r, min(c.g, c.b));
                float lamp = smoothstep(.55, .8, peak) * smoothstep(.35, .6, sat);
                float shade = _EdgeDark * band * (1 - _EdgeLampKeep * lamp);
                c.rgb *= 1 - shade;
                // The gaps between the edge's cables, and a thin strip past
                // the silhouette, fill with a stepped near-black shadow: the
                // dark transition from the wall into the starfield.
                float past = saturate(1 + d / max(_EdgeShadowWidth, .0001));
                past = ceil(past * _EdgeSteps - .0001) / _EdgeSteps;
                float shadowA = _EdgeShadow * (d >= 0 ? band : past) * step(-_EdgeShadowWidth, d);
                float a = c.a + shadowA * (1 - c.a);
                c.rgb = (c.rgb * c.a + float3(.012, .01, .02) * shadowA * (1 - c.a)) / max(a, .0001);
                c.a = a;
                return c * _Color;
            }
            ENDCG
        }
    }
}
