Shader "Unlit/InstancedCircle"
{
    Properties
    {
        _Edge ("Edge Softness", Float) = 0.02
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _Edge;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 local : TEXCOORD0;   // -1..1 quad space
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                // Our quad is [-0.5..0.5], map to [-1..1] so radius=1 in shader
                o.local = v.vertex.xy * 2.0;

                // Instance transform (matrix) gives position + scale
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float dist = length(i.local);
                float alpha = 1.0 - smoothstep(1.0 - _Edge, 1.0, dist);
                if (alpha <= 0) discard;

                float4 col = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                col.a *= alpha;
                return col;
            }
            ENDCG
        }
    }
}
