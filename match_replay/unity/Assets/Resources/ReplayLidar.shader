Shader "TCR/Avia point cloud"
{
    Properties { _PointSize("Point pixels",Float)=2.2 _Alpha("Opacity",Float)=.9 _ColorMode("Color mode",Float)=0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            ZWrite On ZTest LEqual Cull Off Offset -1,-1 Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Point {float3 position;float range;float4 meta;};
            StructuredBuffer<Point> _Points;
            float _PointSize,_Alpha,_ColorMode;
            struct output {float4 position:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            output vert(uint id:SV_VertexID)
            {
                Point p=_Points[id/6];uint k=id%6;
                float2 uv=k==0?float2(-1,-1):k==1?float2(-1,1):k==2?float2(1,1):k==3?float2(-1,-1):k==4?float2(1,1):float2(1,-1);
                output o;o.position=UnityWorldToClipPos(p.position);o.position.xy+=uv*_PointSize/_ScreenParams.xy*o.position.w;o.uv=uv;
                float v=_ColorMode<.5?saturate(p.position.y/4):saturate(p.range/35);
                float3 color=v<.5?lerp(float3(.08,.55,1),float3(.1,1,.45),v*2):lerp(float3(.1,1,.45),float3(1,.45,.08),(v-.5)*2);
                if(_ColorMode>1.5)color=p.meta.x<.5?float3(1,.16,.2):float3(.1,.55,1);
                o.color=float4(color,_Alpha);return o;
            }
            float4 frag(output i):SV_Target {clip(1-dot(i.uv,i.uv));return i.color;}
            ENDCG
        }
    }
}
