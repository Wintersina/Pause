// Sprite shader for parked dock ships. Identical to the default sprite
// shader, plus a saturation control so a powered-down hull can sit greyed
// out in its berth and warm back to full colour when it powers up.
//
// _Silhouette (0..1) turns a not-yet-bought hull into a "who's that
// Pokemon?" shadow: at 1 the output is one flat colour (_Ink) times the
// sprite's alpha, ignoring the texture's RGB and the renderer's tint, so only
// the outline shape survives. _Flash (0..1) fills the shape with white; the
// purchase reveal ramps it up over the ink and back down over the colours.
Shader "Pause/DockSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Saturation ("Saturation", Range(0, 1)) = 1
        _Silhouette ("Silhouette", Range(0, 1)) = 0
        _Ink ("Silhouette Ink", Color) = (0.078,0.047,0.078,1)
        _Flash ("Flash", Range(0, 1)) = 0
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
            float _Saturation;
            float _Silhouette;
            fixed4 _Ink;
            float _Flash;

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
                fixed4 c = tex2D(_MainTex, IN.texcoord);
                fixed grey = dot(c.rgb, fixed3(0.299, 0.587, 0.114));
                c.rgb = lerp(grey.xxx, c.rgb, _Saturation);
                fixed a = c.a;
                c *= IN.color;
                // The shape only: the drawing's alpha, never its colour.
                c.rgb = lerp(c.rgb, _Ink.rgb, _Silhouette);
                c.a = lerp(c.a, a * _Ink.a, _Silhouette);
                c.rgb = lerp(c.rgb, fixed3(1, 1, 1), _Flash);
                c.rgb *= c.a;
                return c;
            }
        ENDCG
        }
    }
}
