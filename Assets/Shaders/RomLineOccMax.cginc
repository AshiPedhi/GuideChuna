// XR-ROM 벽면UI 폐색 — 가림 거리 상한(2026-11-17 신설).
//
// ★왜(사용자 11-17 기기 확인 «너 말대로 거리 때문이네»): 벽면UI 판은 측정자 앞 3.2m에 선다(RomLineScene 씬 값).
//   Meta 폐색은 «현실이 가상보다 가까우면 가린다»라서, 방 벽 · 가구가 3.2m 안에 있으면 판이 통째로 가려져 안 보였다.
// ★고침: 현실 깊이가 상한(_OccluderMaxDistance, 기본 1.8m)보다 먼 자리는 가리지 않는다.
//   환자는 측정자 앞 0.4~1.2m, 손은 더 가깝다 → 환자 · 손은 가리고 방 벽 · 가구는 무시한다(계산 · 기기 미검증).
// ★현실 깊이(m) = Meta EnvironmentOcclusion.cginc의 SampleEnvironmentDepthLinear(uv)(31행) —
//   uv는 CalculateEnvironmentDepthOcclusion(82행)과 같은 식(_EnvironmentDepthReprojectionMatrices 재투영)으로 구한다.
// ★상한 근처 0.15m는 서서히 바꿔 경계가 딱딱하게 끊기지 않게 한다. 키워드가 꺼지면(깊이 없음) 1.0 = 종전 그대로.
#ifndef ROMLINE_OCC_MAX_INCLUDED
#define ROMLINE_OCC_MAX_INCLUDED

float _OccluderMaxDistance;

float RomLineOcclusion(float3 posWorld, float bias)
{
#if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
    float occ = CalculateEnvironmentDepthOcclusion(posWorld, bias);
    float4 ds = mul(_EnvironmentDepthReprojectionMatrices[unity_StereoEyeIndex], float4(posWorld, 1.0));
    float2 uv = (ds.xy / ds.w + 1.0f) * 0.5f;
    float envDepth = SampleEnvironmentDepthLinear(uv);                       // 현실 깊이(m)
    float maxD = _OccluderMaxDistance > 0.01 ? _OccluderMaxDistance : 1.8;   // 값이 안 들어온 머티리얼도 1.8로
    float far = saturate((envDepth - maxD) / 0.15);                          // 상한보다 멀면 1 = 가리지 않는다
    return lerp(saturate(occ), 1.0, far);
#else
    return 1.0;
#endif
}

#endif
