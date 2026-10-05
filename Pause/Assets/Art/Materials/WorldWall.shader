// The side walls / rails of the playfield (scene quads leftPipe, rightPipe).
//
// Unlit and alpha-blended: the wall shows its art's own colours, and where
// the art is transparent the backdrop behind shows through. The walls used
// to use the built-in lit, OPAQUE Mobile/(Bumped) Diffuse shaders, which
// ignore alpha -- so a rail texture with transparent margins (Space's pipe
// rails) drew those margins as the black they are stored as.
//
// _UBand picks which horizontal slice of the texture the quad shows: u at
// the quad's left edge and at its right edge (0..1 = the whole tile). A
// rail whose art doesn't fill its cell is slid so its inner edge sits on
// the wall's inner edge, see WorldPainter.FitBand. The tile scrolls in v
// through _MainTex's offset (moveBackGround).
Shader "Pause/WorldWall"
{
    Properties
    {
        _MainTex ("Wall tile", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _UBand ("u at the quad's left / right edge", Vector) = (0, 1, 0, 0)
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
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _UBand;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = float2(lerp(_UBand.x, _UBand.y, v.uv.x), v.uv.y * _MainTex_ST.y + _MainTex_ST.w);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * _Color;
            }
            ENDCG
        }
    }
}
