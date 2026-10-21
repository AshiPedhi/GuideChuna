// ★추나 실습 웹 미러링 — 눈 화면(Texture2DArray 슬라이스 0)을 내보낼 그림으로 바꾼다(2026-10-21).
// ★현실 모드(패스스루)일 때 눈 화면의 비워 둔 자리(알파 0)는 헤드셋에서 패스스루가 채우지만, 복사본에는 검게 남는다
//   → 그 자리를 회색(_BgGray)으로 채운다(사용자 선택 1안 · 카메라를 켜거나 장면을 다시 그리지 않는다).
//   out = 눈 화면 + 회색 × (1 − 알파) — 투명 바탕에 섞어 그린 색은 이미 알파가 곱해져 있다고 본다(ROM 겹치기와 같은 규칙).
// 패스 0 = NV12(R8, W × H × 1.5 · 위 H줄 Y · 아래 H/2줄 UV 교차 · BT.601 제한 범위) — 영상(H.264) 경로
// 패스 1 = 감마 RGBA — 사진(JPEG) 경로(영상이 안 될 때)
// 줄 순서 · 뒤집기는 HwMirrorNV12(10-20 기기 실측)와 같다.
Shader "Hidden/ChunaMirrorNV12"
{
    Properties { _MainTex ("Eye", 2DArray) = "" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        UNITY_DECLARE_TEX2DARRAY(_MainTex);
        float _FlipY;
        float _BgGray;      // 비워 둔 자리를 채울 회색(감마 0 ~ 1)
        float4 _OutSize;    // (W, H, 1/W, 1/H) — 패스 0에서 H는 영상 높이(출력 RT 높이의 2/3)

        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        v2f vert(appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }

        float3 Eye(float2 uv)
        {
            if (_FlipY > 0.5) uv.y = 1 - uv.y;
            float4 c = UNITY_SAMPLE_TEX2DARRAY(_MainTex, float3(uv, 0));
            return saturate(LinearToGammaSpace(c.rgb) + _BgGray * (1 - saturate(c.a)));
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            fixed4 frag(v2f i) : SV_Target
            {
                float W = _OutSize.x, H = _OutSize.y;
                float px = floor(i.uv.x * W);
                float py = floor(i.uv.y * H * 1.5);
                if (py < H)
                {
                    float3 c = Eye(float2((px + 0.5) / W, 1 - (py + 0.5) / H));
                    return (16 + dot(c, float3(65.481, 128.553, 24.966))) / 255.0;
                }
                float r = py - H;
                float bx = floor(px / 2);
                float3 c = Eye(float2((bx * 2 + 1) / W, 1 - (r * 2 + 1) / H));
                float u = 128 + dot(c, float3(-37.797, -74.203, 112.0));
                float v = 128 + dot(c, float3(112.0, -93.786, -18.214));
                return (fmod(px, 2) < 0.5 ? u : v) / 255.0;
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            fixed4 frag(v2f i) : SV_Target { return float4(Eye(i.uv), 1); }
            ENDCG
        }
    }
}
