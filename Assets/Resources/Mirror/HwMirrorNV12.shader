// ★미러링 시험 ③(2026-10-20) — 눈 화면 사본(Texture2DArray, 슬라이스 _Slice)을 NV12 한 장으로 바꾼다.
// 출력 RT는 R8, 크기 W × (H × 1.5): 위 H줄 = Y, 아래 H/2줄 = UV 교차(짝수 열 U · 홀수 열 V). BT.601 제한 범위.
// _FlipY = 1이면 위아래를 뒤집어 읽는다(기기에서 화면이 뒤집혀 나오면 켠다).
Shader "Hidden/HwMirrorNV12"
{
    Properties { _MainTex ("Eye", 2DArray) = "" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_MainTex);
            float _Slice;
            float _FlipY;
            float _Pattern;     // 1이면 눈 화면 대신 시험 패턴(가로 = 빨강 · 세로 = 초록) — NV12 · 읽기 · 압축 경로만 따로 본다
            float4 _OutSize;    // (W, H, 1/W, 1/H) — H는 영상 높이(출력 RT 높이의 2/3)

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
                if (_Pattern > 0.5) return GammaToLinearSpace(float3(uv.x, uv.y, 0.5));
                if (_FlipY > 0.5) uv.y = 1 - uv.y;
                return UNITY_SAMPLE_TEX2DARRAY(_MainTex, float3(uv, _Slice)).rgb;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float W = _OutSize.x, H = _OutSize.y;
                // 출력 픽셀 좌표. ★읽어 온 데이터는 RT의 v = 0 줄부터 들어온다(10-20 기기: 위부터라고 두었다가 Y와 UV가 엇갈렸다)
                //   → 버퍼 첫 줄 = v 0 줄 = 영상 맨 윗줄로 둔다.
                float px = floor(i.uv.x * W);
                float py = floor(i.uv.y * H * 1.5);
                if (py < H)
                {
                    float3 c = LinearToGammaSpace(Eye(float2((px + 0.5) / W, 1 - (py + 0.5) / H)));
                    float y = 16 + dot(c, float3(65.481, 128.553, 24.966));
                    return y / 255.0;
                }
                float r = py - H;                       // UV 줄(0 .. H/2-1)
                float bx = floor(px / 2);               // 2×2 블록
                float2 uv = float2((bx * 2 + 1) / W, 1 - (r * 2 + 1) / H);
                float3 c = LinearToGammaSpace(Eye(uv));
                float u = 128 + dot(c, float3(-37.797, -74.203, 112.0));
                float v = 128 + dot(c, float3(112.0, -93.786, -18.214));
                return (fmod(px, 2) < 0.5 ? u : v) / 255.0;
            }
            ENDCG
        }

        // 패스 1 — 눈 화면 사본 그대로(진단용 작은 썸네일 · 알파 포함)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            #include "UnityCG.cginc"
            UNITY_DECLARE_TEX2DARRAY(_MainTex);
            float _Slice;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float4 c = UNITY_SAMPLE_TEX2DARRAY(_MainTex, float3(i.uv, _Slice));
                return float4(LinearToGammaSpace(c.rgb), c.a);
            }
            ENDCG
        }
    }
}
