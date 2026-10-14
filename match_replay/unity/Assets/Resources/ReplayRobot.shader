Shader "TCR/Replay robot CAD"
{
    Properties { _Color("Tint", Color)=(1,1,1,1) _Metallic("Metal",Range(0,1))=.25 _Glossiness("Smoothness",Range(0,1))=.22 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.5
        struct Input { float4 paint; };
        fixed4 _Color; half _Metallic,_Glossiness;
        void vert(inout appdata_full v,out Input o) { UNITY_INITIALIZE_OUTPUT(Input,o);o.paint=v.color; }
        void surf(Input i,inout SurfaceOutputStandard o) { o.Albedo=i.paint.rgb*_Color.rgb;o.Metallic=_Metallic;o.Smoothness=_Glossiness;o.Alpha=1; }
        ENDCG
    }
    FallBack "Standard"
}
