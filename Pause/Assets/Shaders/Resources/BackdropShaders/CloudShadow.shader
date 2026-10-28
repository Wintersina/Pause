// Cloud shadows (CloudShadows.cs): a pixel-art cloud shadow quad.
//
// _MainTex is the shadow's DENSITY MASK (one channel, point-filtered, one texel = one 4-art-pixel cell): a procedural
// cumulus / stratocumulus / cirrus envelope. _DetailTex is a small tileable fBm texture. Per cell the shader reads the mask
// at the cell's centre (so nothing is ever smoother than a cell), multiplies it by two slowly scrolling detail samples (the
// cloud's internal morphing: lobes swell and thin, wisps break off and rejoin), turns the density into a stepped alpha
// (_Steps levels, ordered 4x4 Bayer dither on the steps' rims: Quantize in CloudShadows.cs is the same maths) and outputs the
// world's dark tint with  alpha = level x vertex alpha.  The vertex alpha is the draw cap (fade x peak, <= 0.5), so a pixel is
// never darker than that; the vertex RGB carries the quad's noise seed (the tint lives on the material).
Shader "Pause/CloudShadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Density mask", 2D) = "black" {}
        _DetailTex ("Detail noise", 2D) = "grey" {}
        _Tint ("Shadow colour", Color) = (0.02, 0.05, 0.12, 1)
        _MaskSize ("Mask size (texels)", Vector) = (512, 512, 0, 0)
        _T ("Morph clock (s)", Float) = 0
        _Steps ("Alpha steps", Float) = 5
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "False" }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            sampler2D _DetailTex;
            fixed4 _Tint;
            float4 _MaskSize;
            float _T;
            float _Steps;

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
                float2 ci = floor(i.uv * _MaskSize.xy);                 // the cell
                float m = tex2D(_MainTex, (ci + 0.5) / _MaskSize.xy).r;
                float2 seed = i.color.rg;
                float2 dp = ci / 170.0;                                  // detail texture spans ~170 cells
                float d1 = tex2D(_DetailTex, dp + seed * 7.31 + _T * float2(0.0042, 0.0027)).r;
                float d2 = tex2D(_DetailTex, dp * 2.3 + seed.yx * 5.17 - _T * float2(0.0031, 0.0054)).r;
                float d = d1 * 0.62 + d2 * 0.38;
                float c = m * (0.35 + 1.15 * d);
                float a = smoothstep(0.18, 0.8, c);

                // ordered 4x4 Bayer dither: 4 * M2(x&1, y&1) + M2(x>>1, y>>1), M2 = [0 2; 3 1]
                float2 b = fmod(ci, 4.0);
                float2 bl = fmod(b, 2.0);
                float2 bh = floor(b * 0.5);
                float ml = lerp(3.0 - bl.x, bl.x, step(abs(bl.x - bl.y), 0.5));
                float mh = lerp(3.0 - bh.x, bh.x, step(abs(bh.x - bh.y), 0.5));
                float th = (4.0 * ml + mh + 0.5) / 16.0;
                float lvl = min(_Steps, floor(a * _Steps + th)) / _Steps;

                float alpha = lvl * i.color.a;
                clip(alpha - 0.002);
                return fixed4(_Tint.rgb, alpha);
            }
            ENDCG
        }
    }
}
