// One full-screen layer of the planetfall (Planetfall): a cloud deck, the
// speed streaks, the planet's close-approach limb. A plain sprite quad whose
// texture coordinates are scaled and offset (_UV: xy scale, zw offset), so a
// tileable texture imported with wrap Repeat scrolls, tiles and zooms across
// ONE quad -- no leap-frogged copies, no per-frame mesh work: the director
// only writes _UV.
//
// _Fade fades the quad's own bottom edge (on the unscrolled uv, so it stays
// put while the art scrolls): alpha *= saturate((uv.y - _Fade.x) / _Fade.y).
// The limb uses it to melt its straight bottom edge into the planet disc
// beneath it. The default (-1, 1) leaves every pixel alone.
//
// _Clip (world x, y, radius, softness) cuts everything outside a circle; a
// radius <= 0 (the default) leaves every pixel alone. The planet disc uses it
// to lose its aurora spikes and ring tips above the horizon once the limb
// takes over.
//
// Output is premultiplied: _DstBlend OneMinusSrcAlpha (10) is ordinary
// alpha blending, One (1) is additive (the streaks).
Shader "Pause/PlanetfallLayer"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _UV ("UV scale (xy) and offset (zw)", Vector) = (1, 1, 0, 0)
        _Fade ("Bottom fade: start v, width", Vector) = (-1, 1, 0, 0)
        _Clip ("Clip circle: world xy, radius, softness", Vector) = (0, 0, 0, .02)
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 10
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One [_DstBlend]

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
                float2 raw : TEXCOORD1;
                float2 world : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _UV;
            float4 _Fade;
            float4 _Clip;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * _UV.xy + _UV.zw;
                o.raw = v.uv;
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                c.a *= saturate((i.raw.y - _Fade.x) / max(_Fade.y, 1e-4));
                if (_Clip.z > 0)
                    c.a *= saturate((_Clip.z - distance(i.world, _Clip.xy)) / max(_Clip.w, 1e-4));
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
