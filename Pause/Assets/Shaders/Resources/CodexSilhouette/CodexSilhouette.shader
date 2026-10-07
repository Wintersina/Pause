// "Who's that Pokemon?" silhouette for undiscovered codex art.
//
// Outputs one flat colour -- the Graphic's colour (Image.color, i.e.
// CodexUi.Silhouette) -- inside the sprite's alpha mask (a hard cutoff at
// alpha 0.5, so near-solid bodies come out fully solid), ignoring the texture's
// RGB entirely, so no interior line, shade or hue of the drawing survives:
// only its outline shape. Works for any sprite (the art never has to be
// re-exported), with the CodexAnimator swapping frames underneath it.
//
// A UI shader (the uGUI default's stencil + RectMask2D clipping), so the
// round world masks and the scrolling grid still clip it. Plain CG with no
// platform-specific features: compiles for GLES3, Vulkan, Metal and the
// editor alike. Loaded from Resources (CodexUi.SilhouetteMaterial), which
// also keeps it in every build.
Shader "Pause/UI/CodexSilhouette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _MainTex_ST;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // The shape only: the drawing's alpha, never its colour --
                // and cut hard, so the silhouette is solid ink. Painted art
                // often has near-solid bodies (alpha 240-254, not 255) and
                // soft glow halos; a plain alpha*ink would let the backdrop
                // bleed through the body and smear the halo into a grey fog.
                // Anything at least half covered is solid; a narrow ramp
                // around 0.5 keeps the bilinear-filtered edge anti-aliased.
                fixed a = saturate((tex2D(_MainTex, IN.texcoord).a - 0.5) * 8.0 + 0.5) * IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(a - 0.001);
                #endif

                // premultiplied (Blend One OneMinusSrcAlpha)
                return fixed4(IN.color.rgb * a, a);
            }
        ENDCG
        }
    }
}
