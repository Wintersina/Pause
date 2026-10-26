// The kill cam archive's GPU copy (KillCamArchive.DrawPage) of a sprite into
// the saved clip's atlas at fewer texels than its source: a 4 x 4 box of taps
// over the destination texel's footprint, averaged with alpha weighting, so a
// 1-2 px line of the art (a trim, a core, a lamp) thins and dims instead of
// dropping out the way a single point sample would. The taps stay inside the
// sprite's own rect (_Rect, in uv): nothing of its neighbours in the source
// sheet bleeds in. Output is straight alpha, written as is (Blend Off).
Shader "Hidden/Pause/KillCamDownsample"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Step ("One destination texel, in source uv", Vector) = (0, 0, 0, 0)
        _Rect ("The sprite's rect in source uv (xMin, yMin, xMax, yMax)", Vector) = (0, 0, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _Step;
            float4 _Rect;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 lo = _Rect.xy + _MainTex_TexelSize.xy * .5;
                float2 hi = _Rect.zw - _MainTex_TexelSize.xy * .5;
                float3 rgb = 0;
                float a = 0;
                [unroll] for (int y = 0; y < 4; y++)
                {
                    [unroll] for (int x = 0; x < 4; x++)
                    {
                        float2 uv = clamp(i.uv + (float2(x, y) - 1.5) * .25 * _Step.xy, lo, hi);
                        float4 c = tex2Dlod(_MainTex, float4(uv, 0, 0));
                        rgb += c.rgb * c.a;
                        a += c.a;
                    }
                }
                return a > 1e-5 ? float4(rgb / a, a / 16) : float4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
