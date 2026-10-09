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
// _Feather > 0 feathers a set piece's plate edge (see the fragment).
// The SpriteRenderer colour (tint, fades) multiplies afterwards as usual.
Shader "Pause/BackdropGrade"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Lift ("Value lift", Range(1, 4)) = 1
        _Sat ("Saturation", Range(0, 2)) = 1
        _AlphaLift ("Alpha lift", Range(1, 8)) = 1
        _Feather ("Plate feather (texels)", Range(0, 24)) = 0
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
            float4 _MainTex_TexelSize;
            float _Lift, _Sat, _AlphaLift, _Feather;

            // Alpha of the piece at uv, clamped to its 256 px atlas cell
            // (the backdrop atlases are 4 x 4 grids of 256 cells) so a
            // neighbouring drawing never bleeds into the feather.
            float CellAlpha(float2 uv, float2 lo, float2 hi)
            {
                return tex2D(_MainTex, clamp(uv, lo, hi)).a;
            }

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
                if (_Feather > 0.0)
                {
                    // Feathered plate edge: the mean alpha of two rings of
                    // neighbours (r and r/2 texels) is ~1 inside the art and
                    // falls off over its outer r texels, so the piece's own
                    // terrain plate melts into the ground under it.
                    float2 px = _MainTex_TexelSize.xy;
                    float2 cell = 256.0 * px;
                    float2 lo = floor(i.uv / cell) * cell + px * 0.5;
                    float2 hi = lo + cell - px;
                    float r = _Feather;
                    float sum = 0.0;
                    const float d = 0.7071;
                    sum += CellAlpha(i.uv + float2( r, 0) * px, lo, hi);
                    sum += CellAlpha(i.uv + float2(-r, 0) * px, lo, hi);
                    sum += CellAlpha(i.uv + float2(0,  r) * px, lo, hi);
                    sum += CellAlpha(i.uv + float2(0, -r) * px, lo, hi);
                    sum += CellAlpha(i.uv + float2( d,  d) * r * px, lo, hi);
                    sum += CellAlpha(i.uv + float2(-d,  d) * r * px, lo, hi);
                    sum += CellAlpha(i.uv + float2( d, -d) * r * px, lo, hi);
                    sum += CellAlpha(i.uv + float2(-d, -d) * r * px, lo, hi);
                    float h = r * 0.5;
                    sum += CellAlpha(i.uv + float2( h, 0) * px, lo, hi);
                    sum += CellAlpha(i.uv + float2(-h, 0) * px, lo, hi);
                    sum += CellAlpha(i.uv + float2(0,  h) * px, lo, hi);
                    sum += CellAlpha(i.uv + float2(0, -h) * px, lo, hi);
                    float edge = saturate(sum / 12.0);
                    t.a *= smoothstep(0.15, 0.95, edge);
                }
                fixed4 c = t * i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
