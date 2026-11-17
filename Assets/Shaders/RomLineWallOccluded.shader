// XR-ROM 벽면UI(판 바탕 · 칸 · 각도기 선)용 — 현실(환자 · 손)이 앞에 있으면 그 자리만 가려지는 셰이더(2026-11-17 신설 · 폐색 시험 빌드).
//
// ★왜(사용자 11-17): 벽면UI가 환자 뒤에 있는데 언제나 덧그려져 환자를 가린다. «폐색으로 해야겠다.»
//   벽면은 환자보다 멀리 있으니 Meta Depth API가 «현실이 더 가깝다»를 알려 주면 그 자리만 비우면 된다.
// ★실측랩의 RomRecordOccluded(09-21 · 기기에서 폐색 확인)와 같은 방식이다. 다른 점은 둘:
//   ① 가려진 곳은 옅게 남긴다(_OccludedAlpha 0.25 — 11-17 사용자 «살짝 보이게». 처음엔 0 = 완전히 비웠다).
//   ② 관제 미러링 카메라(벽면 · 겹치기)는 눈 카메라가 아니라 깊이 재투영이 맞지 않는다 →
//      RomLineMirror가 Render() 앞뒤로 전역 _RomLineOccOff를 1/0으로 바꿔 그때는 폐색을 끈다.
// ★깊이 매니저가 없거나 미지원 기기면 키워드가 안 켜져 가려짐 값이 1.0 — 종전(Sprites/Default)과 똑같이 보인다.
// ★Sprites/Default를 대신하므로 정점 색 × _Color를 곱한다(RomLineView가 r.material.color로 색을 바꾼다).
Shader "GuideChuna/RomLine/WallOccluded"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _OccludedAlpha ("Occluded Alpha", Range(0,1)) = 0.25   // ★11-17 사용자: 가려진 곳도 살짝 보이게(종전 0 = 완전히 비움)
        _EnvironmentDepthBias ("Environment Depth Bias", Float) = 0.0
        _OccluderMaxDistance ("Occluder Max Distance (m)", Float) = 1.8   // 11-17 — 이보다 먼 현실(방 벽 · 가구)은 가리지 않는다
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        LOD 100
        Cull Off
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
            #include "Assets/Shaders/RomLineOccMax.cginc"

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
            float _RomLineOccOff;   // 전역 — 미러링 카메라가 찍는 동안 1(폐색 끔)

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
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
                float occ = 1.0;
                #if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
                occ = RomLineOcclusion(i.posWorld, _EnvironmentDepthBias);   // 11-17 가림 거리 상한 포함(RomLineOccMax.cginc)
                #endif
                occ = lerp(saturate(occ), 1.0, saturate(_RomLineOccOff));
                c.a *= lerp(_OccludedAlpha, 1.0, occ);
                return c;
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
