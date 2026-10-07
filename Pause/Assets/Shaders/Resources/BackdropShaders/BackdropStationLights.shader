// Blinking lights on Space's stations (SpaceStationLights): one small mesh
// per station cell, four vertices per light, all four at the light's centre
// on the station art. The vertex stage turns each light into a hard-edged
// square snapped to whole screen pixels (at least _MinPx wide), so the lights
// read as pixel-art lamps at every depth tier, never as soft glow blobs.
//
// Per light (TEXCOORD1): x = pattern, y = phase seed (0..1), z = size factor.
// Per station (MaterialPropertyBlock): _BlinkTime -- the director's clock,
// integrated from SCALED dt (so a paused game freezes every lamp mid-blink)
// plus the station's own offset, wrapped at 240 s (a whole number of every
// period below, so the wrap is seamless); _Tint -- depth-tier light and
// cross-fade alpha; _Size -- a light's nominal width in world units.
//
// Level() is mirrored in SpaceStationLights.Level for the tests; keep the two
// in step.
Shader "Pause/BackdropStationLights"
{
    Properties
    {
        _BlinkTime ("Blink clock (s)", Float) = 0
        _Tint ("Tint (rgb light, a fade)", Color) = (1, 1, 1, 1)
        _Size ("Light width (world units)", Float) = 0.02
        _MinPx ("Smallest light (screen px)", Float) = 2
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
        Blend One One

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
                float2 corner : TEXCOORD0;      // 0 / 1 per axis
                float4 light : TEXCOORD1;       // pattern, seed, size factor
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            float _BlinkTime, _Size, _MinPx;
            fixed4 _Tint;

            // Hard steps only (pixel style): 1, a dim step or two, or off.
            float Level(float pattern, float t, float seed)
            {
                if (pattern < 0.5)                  // pulse: 1.2 s, three steps
                {
                    float f = frac(t / 1.2);
                    return f < 0.45 ? 1.0 : (f < 0.6 ? 0.5 : 0.12);
                }
                if (pattern < 1.5)                  // double blink every 2.4 s
                {
                    float s = t - 2.4 * floor(t / 2.4);
                    return (s < 0.12 || (s >= 0.26 && s < 0.38)) ? 1.0 : 0.0;
                }
                if (pattern < 2.5)                  // strobe: long on, short off (1.6 s)
                {
                    float s = t - 1.6 * floor(t / 1.6);
                    return s < 1.3 ? 1.0 : 0.1;
                }
                if (pattern < 3.5)                  // window flicker: a new level every 0.25 s
                {
                    float slot = floor(t * 4.0);
                    float h = frac(sin(fmod(slot, 960.0) * 12.9898 + seed * 78.233) * 43758.5453);
                    return h < 0.2 ? 0.15 : (h < 0.5 ? 0.55 : 1.0);
                }
                float b = t - 1.5 * floor(t / 1.5);   // beacon: a 0.15 s flash every 1.5 s
                return b < 0.15 ? 1.0 : 0.0;
            }

            v2f vert (appdata v)
            {
                v2f o;
                float4 centre = UnityObjectToClipPos(v.vertex);
                float2 pix = (centre.xy / centre.w * 0.5 + 0.5) * _ScreenParams.xy;
                // world units -> screen pixels (orthographic: w = 1)
                float ppu = 0.5 * _ScreenParams.y * abs(UNITY_MATRIX_P[1][1]) / centre.w;
                float side = max(_MinPx, round(_Size * v.light.z * ppu));
                float2 p = floor(pix - side * 0.5 + 0.5) + v.corner * side;
                o.pos = float4((p / _ScreenParams.xy * 2.0 - 1.0) * centre.w, centre.z, centre.w);
                float t = _BlinkTime + v.light.y * 10.0;
                float k = Level(v.light.x, t, v.light.y);
                o.color = fixed4(v.color.rgb * _Tint.rgb * (k * _Tint.a), 1);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(i.color.rgb, 0);
            }
            ENDCG
        }
    }
}
