// Seamless vertical wrap for a sky tile whose art doesn't wrap on its own
// (BackdropTile, layers with wrapBlend > 0), without touching the PNG.
//
// Each copy shows only the first P = 1 - _Blend of the art (the copy is
// squashed to that height); over its first _Blend the art cross-fades from
// the rows just past P into its own start:
//   v' = uv.y * P;  colour = lerp(tex(v' + P), tex(v'), saturate(v' / _Blend))
// so the bottom row of a copy equals tex(P), the row the copy below ended
// on, and the join is continuous. Only a thin band blends two rows of soft
// nebula, which reads as more nebula.
Shader "Pause/BackdropSkyWrap"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Blend ("Cross-faded fraction", Range(0.01, 0.5)) = 0.1
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
            float _Blend;

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
                float p = 1.0 - _Blend;
                float v = i.uv.y * p;
                fixed4 a = tex2D(_MainTex, float2(i.uv.x, v));
                fixed4 b = tex2D(_MainTex, float2(i.uv.x, v + p));
                fixed4 c = lerp(b, a, saturate(v / _Blend)) * i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
