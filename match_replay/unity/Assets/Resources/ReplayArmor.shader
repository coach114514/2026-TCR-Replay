Shader "TCR/Replay armor surface"
{
    Properties
    {
        _Color("Tint",Color)=(1,1,1,1)
        _TeamColor("Lamp color",Color)=(1,.025,.055,1)
        _ArmorLight("Lamp on",Range(0,1))=1
        _Glow("Diffuser emission",Range(0,4))=1.75
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.5
        struct Input { float4 paint;float3 worldPos; };
        fixed4 _Color,_TeamColor;half _ArmorLight,_Glow;
        void vert(inout appdata_full v,out Input o) { UNITY_INITIALIZE_OUTPUT(Input,o);o.paint=v.color; }
        void surf(Input i,inout SurfaceOutputStandard o)
        {
            half lamp=i.paint.a;
            float grain=frac(sin(dot(floor(i.worldPos*24000),float3(12.9898,78.233,37.719)))*43758.5453)-.5;
            half3 surface=i.paint.rgb*(1+grain*.055);
            half3 glass=lerp(half3(.13,.14,.15),_TeamColor.rgb*.55+.12,_ArmorLight);
            o.Albedo=lerp(surface,glass,lamp);
            // Paint and polymer remain visible when a recorded robot is dead.
            // Only the diffuser emits; white identification vinyl does not glow.
            o.Emission=lamp*_ArmorLight*_Glow*lerp(_TeamColor.rgb,half3(1,1,1),i.paint.r*.60);
            half screw=(1-lamp)*step(.22,i.paint.r)*(1-step(.42,i.paint.r));
            o.Metallic=screw*.65;o.Smoothness=lerp(.23,.42,screw)+lamp*.10;o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Standard"
}
