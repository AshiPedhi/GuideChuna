using TMPro;
using UnityEngine;

/// <summary>
/// XR-ROM 벽면UI 폐색(2026-11-17 신설 · 폐색 시험 빌드 · 기기 미검증).
/// ★사용자 11-17: «UI가 환자를 가리는 건 폐색으로 해야겠다.» 벽면UI는 환자 <b>뒤</b>에 서 있어서, 현실 깊이(Meta Depth API)가
///   «환자가 더 가깝다»고 알려 주는 자리만 비우면 환자가 UI 앞에 보인다.
/// ★적용 대상 = 벽면UI(view.Panel 아래 판 · 칸 · 선 · 글자 · 결과 창)뿐이다.
///   컨트롤러UI(손 가까이 · 손에 가려지면 안 된다)와 공간 각도기(환자 반대편 — 환자 뒤에 서면 가려져 안 보이게 된다)는 뺀다.
/// ★깊이는 Vulkan에서만 된다(Meta ProjectSetupDepthAPI). GLES 빌드 · 미지원 기기에서는 키워드가 안 켜져 셰이더가 종전과 똑같이 그린다.
/// ★켠 쪽이 끈다 — EnvironmentDepthManager는 우리가 붙인 것만 OnDestroy에서 끈다(RomLineSession).
/// ★관제 미러링 카메라는 눈 카메라가 아니라 깊이 재투영이 안 맞는다 → RomLineMirror가 Render() 앞뒤로 <see cref="MirrorBegin"/> · <see cref="MirrorEnd"/>.
/// </summary>
public static class RomLineOcclusion
{
    private static readonly int OccOffId = Shader.PropertyToID("_RomLineOccOff");
    private static Meta.XR.EnvironmentDepth.EnvironmentDepthManager added;   // 우리가 붙인 것만 되돌린다

    /// <summary>벽면UI 렌더러의 셰이더를 폐색 셰이더로 바꾼다(색 · 글꼴 · 키워드는 그대로). 바꾼 개수를 돌려준다. 만든 뒤 한 번만 부른다.</summary>
    public static int ApplyToWall(Transform root)
    {
        var wallSrc = Resources.Load<Material>("RomLineUI/RomLineWallOccluded");
        var tmpSrc = Resources.Load<Material>("RomLineUI/RomLineTMPOccluded");
        if (root == null || wallSrc == null || tmpSrc == null)
        {
            Debug.LogWarning($"[선실측] ★폐색 — 셰이더 머티리얼을 못 찾았다(벽면 {(wallSrc != null ? "있음" : "없음")} · 글자 {(tmpSrc != null ? "있음" : "없음")}) — 종전대로 그린다");
            return 0;
        }
        Shader wallSh = wallSrc.shader, tmpSh = tmpSrc.shader;
        int quads = 0, texts = 0;
        foreach (var t in root.GetComponentsInChildren<TextMeshPro>(true))
        {
            Material m = t.fontMaterial;   // 글자마다 인스턴스(한 번) — 원본 글꼴 asset은 건드리지 않는다
            if (m == null || m.shader == tmpSh) continue;
            m.shader = tmpSh;
            texts++;
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.GetComponent<TextMeshPro>() != null || r.GetComponent<TMP_SubMesh>() != null) continue;
            Material m = r.sharedMaterial;
            if (m == null || m.shader == wallSh || m.shader.name != "Sprites/Default") continue;
            r.material.shader = wallSh;   // 인스턴스 — r.material.color로 바꾸던 색은 _Color 그대로 남는다
            quads++;
        }
        Debug.Log($"[선실측] 폐색 — 벽면UI 셰이더 바꿈(판 · 선 {quads}개 · 글자 {texts}개) · 컨트롤러UI · 공간 각도기는 뺌");
        return quads + texts;
    }

    /// <summary>환경 깊이를 켠다. 켜졌으면 true. ★XR이 붙기 전(Start)엔 서브시스템이 없을 수 있어 추적이 붙은 뒤 한 번 더 부른다(09-21 실측랩과 같다).</summary>
    public static bool Enable(GameObject host, string why)
    {
        if (!Meta.XR.EnvironmentDepth.EnvironmentDepthManager.IsSupported)
        {
            Debug.Log($"[선실측] 폐색 — 지원 안 함({why}) · 이 기기 · 그래픽 API에서는 환경 깊이가 없다(Quest 3/3S · Vulkan 전용) — 벽면UI는 종전대로");
            return false;
        }
        var mgr = Object.FindAnyObjectByType<Meta.XR.EnvironmentDepth.EnvironmentDepthManager>();
        if (mgr == null) { mgr = host.AddComponent<Meta.XR.EnvironmentDepth.EnvironmentDepthManager>(); added = mgr; }
        mgr.enabled = true;
        mgr.OcclusionShadersMode = Meta.XR.EnvironmentDepth.OcclusionShadersMode.SoftOcclusion;   // 경계가 딱딱하면 오려 붙인 것처럼 보인다
        Debug.Log($"[선실측] 폐색 — 켬({why}) · 부드러운 폐색 · 손도 깊이에 남긴다");
        return true;
    }

    /// <summary>깊이 텍스처가 실제로 들어왔나(첫 프레임 로그용).</summary>
    public static bool DepthReady => Shader.GetGlobalTexture("_EnvironmentDepthTexture") != null;

    /// <summary>우리가 붙인 깊이 매니저만 끈다(켠 쪽이 끈다).</summary>
    public static void Disable()
    {
        if (added != null) { added.enabled = false; added = null; }
        Shader.SetGlobalFloat(OccOffId, 0f);
    }

    /// <summary>미러링 카메라가 찍는 동안 폐색을 끈다(깊이 재투영이 그 카메라와 안 맞는다).</summary>
    public static void MirrorBegin() => Shader.SetGlobalFloat(OccOffId, 1f);
    public static void MirrorEnd() => Shader.SetGlobalFloat(OccOffId, 0f);
}
