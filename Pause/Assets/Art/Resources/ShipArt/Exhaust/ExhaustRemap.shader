// Engine exhaust drawn in a non-stock ship skin: the default sprite shader
// plus ExhaustRemap.cginc's palette shift (outer / mid / dark bands move
// partway toward the skin, the hot core stays). A stock-skin exhaust never
// uses this material (ExhaustRemap.Apply puts the default sprite material
// back), so stock looks exactly as drawn.
//
// Lives in Resources so builds include it (found by name at runtime).
Shader "Pause/ExhaustRemap"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _ExOn ("Remap on", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
            #include "ExhaustRemap.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float4 c = tex2D(_MainTex, IN.texcoord);
                c.rgb = ExhaustRemap(c.rgb);
                c *= IN.color;
                c.rgb *= c.a;
                return c;
            }
        ENDCG
        }
    }
}
