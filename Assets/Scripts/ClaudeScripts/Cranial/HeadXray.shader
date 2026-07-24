// GuideChuna/HeadXray
// Built-in RP 전용. 원래 "피부 텍스처(색)"를 그대로 보여주되 반투명하게만 만든다.
// (파란 홀로그램 아님 — 피부색 유지) 가장자리(실루엣)는 약간 더 진하게 해 머리 형태가 읽히게 함.
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
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos        : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 worldNormal: TEXCOORD1;
                float3 viewDir    : TEXCOORD2;
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
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
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
