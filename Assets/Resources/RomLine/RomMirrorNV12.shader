// ★선 실측 관제 미러링 — 영상(H.264) 경로(2026-10-21 사용자 지시: 배터리 · 발열을 줄이려 JPEG 대신 하드웨어 압축).
// 현실 영상(PCA 텍스처) 위에 앱 화면(겹치기 카메라 RT)을 겹치고, 그 자리에서 NV12 한 장으로 바꾼다 — CPU는 손대지 않는다.
// 겹치기 규칙은 종전 CPU 경로(RomLineMirror.OnOverlayRead)와 같다: out = 가상 + 영상 × (1 − 알파) — 감마 색 공간에서.
// 출력 RT는 R8, 크기 W × (H × 1.5): 위 H줄 = Y, 아래 H/2줄 = UV 교차(짝수 열 U · 홀수 열 V). BT.601 제한 범위.
// 줄 순서는 HwMirrorNV12(10-20 기기 실측)와 같다 — 읽어 온 데이터의 첫 줄 = RT v 0 줄 = 영상 맨 윗줄.
Shader "Hidden/RomMirrorNV12"
{
    Properties { _CamTex ("Camera", 2D) = "black" {} _OverlayTex ("Overlay", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _CamTex;
            sampler2D _OverlayTex;
            float _FlipY;       // 1이면 위아래를 뒤집는다(기기에서 거꾸로 나오면 기기 속성으로 켠다)
            float4 _OutSize;    // (W, H, 1/W, 1/H) — H는 영상 높이(출력 RT 높이의 2/3)

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            // 두 텍스처 모두 sRGB라 읽으면 선형 값이 나온다 → 감마로 되돌려 겹친다(CPU 경로의 바이트 값과 같은 공간)
            float3 Mix(float2 uv)
            {
                if (_FlipY > 0.5) uv.y = 1 - uv.y;
                float3 cam = LinearToGammaSpace(tex2D(_CamTex, uv).rgb);
                float4 ov = tex2D(_OverlayTex, uv);
                return saturate(LinearToGammaSpace(ov.rgb) + cam * (1 - ov.a));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float W = _OutSize.x, H = _OutSize.y;
                float px = floor(i.uv.x * W);
                float py = floor(i.uv.y * H * 1.5);
                if (py < H)
                {
                    float3 c = Mix(float2((px + 0.5) / W, 1 - (py + 0.5) / H));
                    return (16 + dot(c, float3(65.481, 128.553, 24.966))) / 255.0;
                }
                float r = py - H;                       // UV 줄(0 .. H/2-1)
                float bx = floor(px / 2);               // 2×2 블록 가운데
                float3 c = Mix(float2((bx * 2 + 1) / W, 1 - (r * 2 + 1) / H));
                float u = 128 + dot(c, float3(-37.797, -74.203, 112.0));
                float v = 128 + dot(c, float3(112.0, -93.786, -18.214));
                return (fmod(px, 2) < 0.5 ? u : v) / 255.0;
            }
            ENDCG
        }
    }
}
