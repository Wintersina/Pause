// A turning planet for the Space backdrop (SpaceDirector), from ONE static
// frame of Codex's planet art instead of a flipbook.
//
// The disc (_Disc: centre and radii in atlas uv) is treated as an
// orthographic view of a sphere. Each pixel inside it is turned into
// latitude / longitude, the longitude is advanced by _Spin, and the surface
// is looked up again in the source disc's central band (|longitude| <=
// _Band, where the art is least foreshortened). The band repeats every
// _Period radians of longitude; a short cross-fade (_Seam) hides where it
// wraps. _Period sets how much the surface is stretched across the disc:
// the band's 2*_Band/(1+_Seam) radians of art are spread over _Period
// radians of globe, so the closer _Period is to that, the closer the art is
// to 1:1 (it used to be pi, a half turn: the art was smeared about 1.7x
// sideways on top of the sprite's own upscale, which is what made the big
// near planets read as blurry). _Period stays a little above the band so no
// surface feature shows twice inside the turning (non-rim) part of the disc.
// So bands, storms,
// craters and the megastructure belt slide across the disc and over the limb
// while the lighting stays put:
//   - each sample's baked light is divided out and the fixed light applied,
//     so the terminator / dark side never moves with the surface;
//   - toward the limb (r > _Rim) and outside the disc the original pixels
//     are used unchanged, so the rim light and halo are part of the same
//     sprite, rigidly attached to the body: nothing can jitter.
// _Spin is integrated by the director from scaled time (no _Time), so a
// paused game shows a still planet. Optional depth haze (off by default; the
// director tints by depth tier): desaturate, dim, fade toward the sky colour.
Shader "Pause/BackdropPlanet"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Disc ("Disc centre (uv) and radii (uv)", Vector) = (0.5, 0.5, 0.45, 0.45)
        _Spin ("Turn (radians of longitude)", Float) = 0
        _Band ("Source half band (radians)", Float) = 1.2
        _Period ("Surface repeat (radians of longitude)", Float) = 2.6
        _Seam ("Wrap cross-fade (fraction)", Float) = 0.2
        _Rim ("Rim kept from the art (r)", Float) = 0.86
        _LightDir ("Light direction", Vector) = (0.55, 0.45, 0.70, 0)
        _Ambient ("Ambient", Float) = 0.38
        _HazeColor ("Haze colour", Color) = (0.045, 0.055, 0.15, 1)
        _Haze ("Haze amount", Range(0, 1)) = 0
        _Desat ("Desaturate", Range(0, 1)) = 0
        _Dim ("Value", Range(0, 1)) = 1
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
            #pragma target 3.0
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
            float4 _Disc, _LightDir;
            float4 _MainTex_TexelSize;
            float _Spin, _Band, _Period, _Seam, _Rim, _Ambient;
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

            float Shade(float3 n)
            {
                return _Ambient + (1.0 - _Ambient) * saturate(dot(n, normalize(_LightDir.xyz)));
            }

            // Surface sample at source longitude `ls`, latitude (sin sl, cos cl),
            // relit from the source's light to the destination's.
            // lod: mip level of the sprite's own footprint (0 when the texture
            // has no mips). Explicit, because the surface uv jumps at the wrap
            // and hardware derivatives would blur a line there.
            float3 Surface(float ls, float sl, float cl, float dst, float lod)
            {
                float3 n = float3(sin(ls) * cl, sl, cos(ls) * cl);
                float2 uv = _Disc.xy + n.xy * _Disc.zw;
                float3 s = tex2Dlod(_MainTex, float4(uv, 0, lod)).rgb;
                return s * clamp(dst / Shade(n), 0.5, 1.8);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 orig = tex2D(_MainTex, i.uv);
                float2 p = (i.uv - _Disc.xy) / _Disc.zw;
                float r = length(p);
                float2 q = p / max(1.0, r / 0.999);         // clamped inside the disc
                float sl = q.y;
                float cl = sqrt(max(1e-4, 1.0 - sl * sl));
                float zz = sqrt(max(0.0, 1.0 - dot(q, q)));
                float lon = atan2(q.x, zz);                 // -pi/2 .. pi/2 on the visible face
                float t = frac((lon + _Spin) / _Period);    // the band repeats every _Period
                float k = 2.0 * _Band / (1.0 + _Seam);
                float dst = Shade(float3(q, zz));
                // Texels per screen pixel of the sprite itself; the surface
                // remap mostly magnifies on top of that, so the sharper mip
                // below it is taken (only matters for far, shrunken bodies).
                float2 fp = max(abs(ddx(i.uv)), abs(ddy(i.uv))) * _MainTex_TexelSize.zw;
                float lod = max(0.0, log2(max(max(fp.x, fp.y), 1e-5)) - 0.5);
                float3 a = Surface(-_Band + k * t, sl, cl, dst, lod);
                float3 b = Surface(-_Band + k * (t + 1.0), sl, cl, dst, lod);
                float3 surf = lerp(b, a, saturate(t / _Seam));
                float keep = smoothstep(_Rim, 1.0, r);      // 1 at the limb and beyond: original art
                fixed4 c = fixed4(lerp(surf, orig.rgb, keep), orig.a) * i.color;
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
