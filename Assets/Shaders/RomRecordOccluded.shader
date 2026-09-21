// 실측 기록 표시물용 — 현실에 가려지면 <b>지우지 않고 흐려지는</b> 셰이더(2026-09-21 신설).
//
// ★왜 만들었나(사용자 09-21): 패스스루에서 표시물이 "현실 위에 붕 떠" 보인다.
//   "사람 위에 십자선을 그어도 몸통을 관통하는 십자선이 아니라 그냥 위에 올려진 십자선 같다."
//   그 정체는 <b>폐색(occlusion)이 없는 것</b>이다 — 가상 물체가 현실 물체 뒤로 들어가지 않고
//   언제나 덧그려지니, 뇌가 "몸 안"이 아니라 "몸 앞 유리창의 그림"으로 읽는다.
//
// ★그런데 <b>완전히 가리면 안 된다.</b> 기준축·십자선은 몸을 관통해 보여야 하는 것이라
//   가려진 부분이 사라지면 안 되고 <b>흐리게 비쳐야</b> 한다(사용자 확인: "아예 약간 흐린색으로").
//   Meta가 주는 OcclusionUnlit은 가려지면 0으로 만들어 버린다(META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY).
//   그래서 가려짐 값(0~1)을 직접 받아 _OccludedAlpha까지만 낮춘다.
//     _OccludedAlpha = 0   → 완전히 가림(Meta 기본과 같다)
//     _OccludedAlpha = 0.2 → 가려진 부분이 20%로 비친다(몸 안을 지나가는 느낌)
//     _OccludedAlpha = 1   → 폐색 없음(종전과 같다)
//
// ★Built-in 렌더 파이프라인용이다. Meta 패키지에 BiRP 폴더가 따로 있어서 쓸 수 있다
//   (`Shaders/EnvironmentDepth/BiRP/EnvironmentOcclusionBiRP.cginc`).
// ★깊이 텍스처는 씬의 EnvironmentDepthManager가 전역으로 올려 준다. 그것이 없거나
//   기기가 지원하지 않으면 키워드가 안 켜지고 가려짐 값이 1.0이라 <b>종전과 똑같이</b> 보인다.
// ★Sprites/Default를 대신하는 자리라 <b>정점 색을 곱한다</b> — 조작 판이 그라데이션을
//   정점 색으로 구워 쓰기 때문이다(RomRecordWristMenu).
Shader "GuideChuna/RomRecord/Occluded"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        // ★가려진 곳에 남길 밝기·투명도. 0이면 완전히 사라지고 1이면 폐색이 없는 것과 같다.
        _OccludedAlpha ("Occluded Alpha", Range(0,1)) = 0.18
        _EnvironmentDepthBias ("Environment Depth Bias", Float) = 0.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        LOD 100

        Cull Off                 // 양면 — 판이나 선을 뒤에서 봐도 보여야 한다
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.5
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "UnityCG.cginc"
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/BiRP/EnvironmentOcclusionBiRP.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float4 vertex : SV_POSITION;
                META_DEPTH_VERTEX_OUTPUT(1)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _OccludedAlpha;
            float _EnvironmentDepthBias;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);   // 스테레오(양눈) 지원에 필요하다
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                META_DEPTH_INITIALIZE_VERTEX_OUTPUT(o, v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                fixed4 c = tex2D(_MainTex, i.uv) * _Color * i.color;

                // ★가려짐 값: 1이면 안 가림, 0이면 현실 물체 뒤다.
                //   키워드가 안 켜져 있으면(미지원 기기·매니저 없음) 매크로가 1.0을 준다 — 종전과 같아진다.
                float occ = META_DEPTH_GET_OCCLUSION_VALUE(i, _EnvironmentDepthBias);

                // 가려진 곳을 0으로 만들지 않고 _OccludedAlpha까지만 낮춘다 — 그래야 "관통"으로 읽힌다.
                c.a *= lerp(_OccludedAlpha, 1.0, saturate(occ));
                return c;
            }
            ENDCG
        }
    }

    // ★어떤 이유로든 위 패스를 못 쓰면 평범한 투명 스프라이트로 떨어진다(표시물이 통째로 사라지지 않게).
    Fallback "Sprites/Default"
}
