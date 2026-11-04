// 로딩 가림막 (2026-10-30 사용자 «생성될 때 딜레이 · 이런 거 다 잡으라고 로딩창 해 놓은 것») — 머리를 감싼 구의 안쪽을 단색으로 칠한다.
// 깊이 비교 없이(ZTest Always) 씬의 모든 것보다 늦게(Queue 4500) 그려 자리 잡는 과정을 가린다. 가운데 판(4600)은 그 위에 그린다.
Shader "GuideChuna/LoadingVeil"
{
    Properties
    {
        _Color ("Color", Color) = (0.05, 0.05, 0.06, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay+500" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always
        Cull Front
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
}
