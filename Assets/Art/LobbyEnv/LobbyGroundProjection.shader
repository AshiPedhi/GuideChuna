// 로비 바닥 투영 (2026-10-30 사용자 «스카이박스 바닥을 내가 서 있는 높이로 — 안 되면 바닥만 하이브리드» → A안)
// 하늘과 같은 360° 그림을 실제 바닥(추적 공간 y=0)에 깐 판에 찍는다.
// 판 위 점 P의 색 = 렌더 눈 자리 _EyeCenter(블렌더 카메라 = 바닥 위 1.6m)에서 P를 본 방향의 하늘 그림 색.
// 그래서 눈이 _EyeCenter에 있으면 하늘과 똑같이 보이고, 고개 · 몸을 움직이면 바닥이 진짜 바닥처럼 시차를 갖는다.
// ★좌표 변환 · 회전 · 밝기는 Unity 기본 Skybox/Panoramic과 같은 식(ToRadialCoords · RotateAroundYInDegrees · _Tint × ColorSpaceDouble × _Exposure)
//   — 다르면 바닥과 하늘 경계에서 그림이 어긋난다. 가장자리(_FadeStart → _Radius)는 하늘로 녹인다.
Shader "GuideChuna/LobbyGroundProjection"
{
    Properties
    {
        _MainTex ("파노라마 (위경도 360°)", 2D) = "grey" {}
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Tint ("Tint", Color) = (.5, .5, .5, .5)
        _Rotation ("Rotation", Range(0, 360)) = 0
        _EyeCenter ("렌더 눈 자리(월드)", Vector) = (0, 1.6, 0, 0)
        _Radius ("판 반지름(m)", Float) = 5.5
        _FadeStart ("녹이기 시작(m)", Float) = 4.0
    }
    SubShader
    {
        // 순서 = 하늘(불투명 뒤 2500) 바로 다음 · UI(3000) 전 — Transparent(3000)로 두자 로비 판 위에 겹쳐 그려졌다(10-30 에디터 캡처)
        Tags { "Queue" = "Transparent-499" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half4 _MainTex_HDR;
            half4 _Tint;
            half _Exposure;
            float _Rotation;
            float4 _EyeCenter;
            float _Radius;
            float _FadeStart;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 RotateAroundYInDegrees(float3 v, float degrees)
            {
                float alpha = degrees * UNITY_PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float3(mul(m, v.xz), v.y).xzy;
            }

            float2 ToRadialCoords(float3 coords)
            {
                float3 n = normalize(coords);
                float latitude = acos(n.y);
                float longitude = atan2(n.z, n.x);
                float2 sphereCoords = float2(longitude, latitude) * float2(0.5 / UNITY_PI, 1.0 / UNITY_PI);
                return float2(0.5, 1.0) - sphereCoords;
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 스카이박스는 그림 방향 t를 +_Rotation 돌려 월드에 놓는다 → 월드 방향 w의 그림 방향 = w를 −_Rotation
                float3 w = i.world - _EyeCenter.xyz;
                float3 t = RotateAroundYInDegrees(w, -_Rotation);
                float2 uv = ToRadialCoords(t);
                half4 tex = tex2Dlod(_MainTex, float4(uv, 0, 0));   // lod 0 — 경도 이음매(atan2 끊김)에서 밉이 튀지 않게
                half3 c = DecodeHDR(tex, _MainTex_HDR);
                c = c * _Tint.rgb * unity_ColorSpaceDouble.rgb * _Exposure;

                float r = length(i.world.xz - _EyeCenter.xz);
                half a = 1.0 - smoothstep(_FadeStart, _Radius, r);
                return half4(c, a);
            }
            ENDCG
        }
    }
}
