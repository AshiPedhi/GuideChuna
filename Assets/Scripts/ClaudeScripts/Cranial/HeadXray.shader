// GuideChuna/HeadXray
// Built-in RP 전용. 원래 "피부 텍스처(색)"를 그대로 보여주되 반투명하게만 만든다.
// (파란 홀로그램 아님 — 피부색 유지) 가장자리(실루엣)는 약간 더 진하게 해 머리 형태가 읽히게 함.
//
// ★ VR 스테레오(Single Pass Instanced) 대응 필수.
//   이 프로젝트는 OpenXR renderMode=1(SinglePassInstanced)로 양쪽 눈을 인스턴스 2개로 한 번에 그린다.
//   인스턴싱 매크로가 없으면 셰이더가 눈 인덱스를 못 받아 **한쪽 눈에만 그려진다**
//   (증상: 오른쪽 렌즈에서 환자 피부가 사라지고 골격만 보임 → 양안 불일치로 눈이 아픔).
Shader "GuideChuna/HeadXray"
{
    Properties
    {
        _MainTex   ("Skin Diffuse (피부 텍스처)", 2D) = "white" {}
        _Color     ("Tint (보통 흰색=피부색 유지)", Color) = (1, 1, 1, 1)
        _Alpha     ("전체 투명도 (0=완전투명, 1=불투명)", Range(0, 1)) = 0.35
        _RimBoost  ("가장자리 진하게", Range(0, 1))       = 0.4
        _RimPower  ("가장자리 날카로움", Range(0.2, 8))    = 2.5
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing        // ★ 스테레오 인스턴싱 변형 생성
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID      // ★ 어느 눈인지 식별
            };

            struct v2f
            {
                float4 pos        : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 worldNormal: TEXCOORD1;
                float3 viewDir    : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO          // ★ 프래그먼트로 눈 인덱스 전달
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float  _Alpha;
            float  _RimBoost;
            float  _RimPower;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);                     // ★ 눈 인덱스 세팅(뷰/투영 행렬 선택)
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);       // ★ 이게 없으면 한쪽 눈만 그려짐

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                // _WorldSpaceCameraPos는 UNITY_SETUP_INSTANCE_ID 이후에 눈별 값으로 잡힌다.
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = _WorldSpaceCameraPos - worldPos;    // 정규화는 프래그먼트에서(보간 후)
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);    // ★ 프래그먼트에서도 눈 인덱스 복원

                // 피부 텍스처 색 그대로 (Tint=흰색이면 원색 유지)
                fixed3 skin = tex2D(_MainTex, i.uv).rgb * _Color.rgb;

                // 실루엣에 가까울수록(rim=1) 약간 더 불투명 → 머리 윤곽이 읽힘
                float rim = 1.0 - saturate(dot(normalize(i.worldNormal), normalize(i.viewDir)));
                float a = saturate(_Alpha + pow(rim, _RimPower) * _RimBoost);

                return fixed4(skin, a);
            }
            ENDCG
        }
    }

    Fallback Off
}
