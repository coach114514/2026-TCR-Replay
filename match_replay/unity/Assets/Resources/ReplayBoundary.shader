Shader "TCR/Replay safety boundary"
{
    Properties { _Color ("Boundary tint", Color) = (0.18,0.8,0.85,0.08) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            struct v2f { float4 position:SV_POSITION; };
            v2f vert(float4 position:POSITION) { v2f o; o.position=UnityObjectToClipPos(position); return o; }
            fixed4 frag(v2f i):SV_Target { return _Color; }
            ENDCG
        }
    }
}
