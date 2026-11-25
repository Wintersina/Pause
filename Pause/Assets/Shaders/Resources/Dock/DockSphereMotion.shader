// Reproject adjacent baked sphere views onto the SAME moving surface point. A whole-frame dissolve
// gives two stationary copies of a landmark; reprojection moves that landmark around the sphere.
Shader "Pause/DockSphereMotion"
{
    Properties
    {
        [PerRendererData] _MainTex ("Rotation atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Frame ("Current view", Float) = 0
        _Phase ("Fraction of view step", Range(0,1)) = 0
        _DiscShare ("Disc diameter within frame", Float) = 0.91
        _Relight ("Keep illumination stationary", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="False" }
        Cull Off ZWrite Off Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            sampler2D _MainTex;
            fixed4 _Color;
            float _Frame, _Phase, _DiscShare, _Relight;
            v2f vert(appdata v)
            {
                v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            float2 atlasUV(float frame, float2 uv)
            {
                return (float2(fmod(frame,4.0),3.0-floor(frame/4.0))+uv)/4.0;
            }
            float illumination(float3 n)
            {
                return .16+.84*saturate(dot(n,normalize(float3(-.5,.62,.6))));
            }
            float4 viewAt(float frame, float longitude, float latitudeX, float y, float targetShade)
            {
                float3 n=float3(sin(longitude)*latitudeX,y,cos(longitude)*latitudeX);
                float4 c=tex2D(_MainTex,atlasUV(frame,.5+n.xy*(_DiscShare*.5)));
                // A view cannot supply a point beyond its own horizon. The adjacent view supplies it.
                c.a*=step(.00001,n.z);
                float correction=targetShade/max(.12,illumination(n));
                c.rgb*=lerp(1.0,correction,_Relight);
                return c;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float2 uv=frac(i.uv*4.0);
                float2 xy=(uv-.5)/(_DiscShare*.5);
                float r2=dot(xy,xy);
                clip(1.0-r2);
                float z=sqrt(max(0.0,1.0-r2));
                float longitude=atan2(xy.x,z);
                float latitudeX=sqrt(max(0.0,1.0-xy.y*xy.y));
                float stepAngle=6.28318530718/16.0;
                float shade=illumination(float3(xy,z));
                float4 a=viewAt(_Frame,longitude+_Phase*stepAngle,latitudeX,xy.y,shade);
                float4 b=viewAt(fmod(_Frame+1.0,16.0),longitude- (1.0-_Phase)*stepAngle,latitudeX,xy.y,shade);
                float wa=max(.0001,1.0-_Phase)*a.a;
                float wb=max(.0001,_Phase)*b.a;
                float w=wa+wb;
                float3 rgb=(a.rgb*wa+b.rgb*wb)/max(.00001,w);
                return fixed4(rgb*i.color.rgb,step(.00001,w)*i.color.a);
            }
            ENDCG
        }
    }
}
