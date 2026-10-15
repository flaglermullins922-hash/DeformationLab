// DeformationLab/Unlit — 无光照 shader（顶点色 × _Color + _Emission）
// 用于地面 / FFD 控制点 / 格子线框等非模型物体，避免依赖内置 Standard
// （Standard 在未被引用时会被构建剥离，导致 Shader.Find 返回 null）。
Shader "DeformationLab/Unlit"
{
    Properties
    {
        _Color    ("Color", Color) = (1,1,1,1)
        _Emission ("Emission", Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; };
            fixed4 _Color;
            fixed4 _Emission;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                return i.color * _Color + _Emission;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
