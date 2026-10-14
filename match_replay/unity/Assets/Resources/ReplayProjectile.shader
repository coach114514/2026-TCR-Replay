Shader "TCR/Replay projectile"
{
    Properties { _Color("Glow",Color)=(.45,1,.035,1) }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f {float4 pos:SV_POSITION;};fixed4 _Color;
            v2f vert(appdata_base v) {v2f o;o.pos=UnityObjectToClipPos(v.vertex);return o;}
            fixed4 frag(v2f i):SV_Target {return _Color;}
            ENDCG
        }
    }
}
