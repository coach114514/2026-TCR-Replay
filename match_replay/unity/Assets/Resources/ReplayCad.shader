Shader "TCR/Replay CAD surface"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Rulebook floor", 2D) = "white" {}
        _SurfaceTex ("Powder coat", 2D) = "white" {}
        _TextureEnabled ("Floor texture", Float) = 1
        _SurfaceEnabled ("Surface detail", Float) = 1
        _Team ("Red 0 / Blue 1 / Native 2", Float) = 2
        _Structural ("Neutral field structure", Float) = 0
        _GuideLight ("Recorded guidance 0 off 1 on -1 unknown", Float) = -1
        _RuneSurface ("Rune arm", Float) = 0
        _RuneLight ("Rune arm illumination", Float) = 0.08
        _RunePhase ("Replay phase", Float) = 0
        _RuneFlow ("Activation flow", Float) = 1
        _RuneRadial ("Game radial progress", Float) = 0
        _RuneProgress ("Completed group fraction", Float) = 0
        _RuneOrigin ("Rune hub world position", Vector) = (0,0,0,0)
        _RuneTarget ("Arm target world position", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard vertex:vert fullforwardshadows
        #pragma target 3.5
        #include "UnityCG.cginc"
        sampler2D _MainTex, _SurfaceTex;
        fixed4 _Color;
        float _TextureEnabled, _SurfaceEnabled, _Team, _Structural;
        float _GuideLight,_RuneSurface,_RuneLight,_RunePhase;
        float _RuneFlow,_RuneRadial,_RuneProgress;
        float4 _RuneOrigin,_RuneTarget;
        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal;
            fixed4 paintAndFloor;
        };
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o);
            o.paintAndFloor=fixed4(v.color.rgb,v.texcoord1.x);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed3 paint=_Structural>.5?fixed3(.40,.42,.44):IN.paintAndFloor.rgb;
            float peak=max(paint.r,max(paint.g,paint.b));
            bool red=paint.r>.3 && paint.g<paint.r*.25 && paint.b<paint.r*.25;
            bool blue=paint.b>.3 && paint.r<paint.b*.65 && paint.g<paint.b*.65;
            bool guidance=IN.paintAndFloor.g>.6 && IN.paintAndFloor.r<.1 && IN.paintAndFloor.b<.1;
            // Only colored red/blue accents change team. Green guidance lamps,
            // copper, black armor and metal keep the original source colors.
            if(_Team<1.5 && (red || blue))
                paint=(_Team<.5?fixed3(1,.025,.045):fixed3(.025,.13,1))*peak;
            bool floor=IN.paintAndFloor.a>.5;
            if(floor && _TextureEnabled>.5) paint=tex2D(_MainTex,IN.uv_MainTex).rgb;
            float3 blend=pow(abs(IN.worldNormal),4);
            blend/=max(.0001,blend.x+blend.y+blend.z);
            float3 p=IN.worldPos*3;
            float grain=tex2D(_SurfaceTex,p.yz).r*blend.x+tex2D(_SurfaceTex,p.xz).r*blend.y+tex2D(_SurfaceTex,p.xy).r*blend.z;
            if(!floor && _SurfaceEnabled>.5) paint*=lerp(.92,1.03,grain);
            o.Albedo=paint*_Color.rgb;
            o.Metallic=floor?0:.12;
            o.Smoothness=floor?.08:.24;
            // Static appearance only: this does not claim recorded activation.
            o.Emission=(!floor && (red || blue))?paint*_Color.rgb*.025:0;
            if(guidance && _Team<1.5)
            {
                o.Albedo=paint*_Color.rgb*(_GuideLight>.5?1:_GuideLight<0?.2:.045);
                o.Emission=_GuideLight>.5?fixed3(.015,1,.025)*3:0;
            }
            if(_RuneSurface>.5 && (red || blue))
            {
                float radial=distance(IN.worldPos,_RuneOrigin.xyz);
                float length=max(.01,distance(_RuneTarget.xyz,_RuneOrigin.xyz));
                float progress=_RuneRadial>.5 && radial/length<_RuneProgress ? .85 : 0;
                float light=max(_RuneLight,progress);
                o.Albedo=paint*_Color.rgb*lerp(.06,1,light);
                float flow=_RuneFlow>.5?.78+.22*sin((_RuneRadial>.5?radial:IN.worldPos.y)*28-_RunePhase*10):1;
                o.Emission=paint*_Color.rgb*max(0,light-.1)*flow*2;
            }
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
