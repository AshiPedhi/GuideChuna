using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 두개골/환자 관련 씬·에셋 설정을 코드로 맞추는 도구 모음.
///
/// ★비파괴 원칙: 오브젝트를 만들거나 지우지 않는다. 지정한 필드 값만 바꾸고 Undo로 되돌릴 수 있다.
///   각 메뉴는 "점검"(읽기 전용)과 "적용"이 쌍으로 있으니 적용 전에 점검부터 실행할 것.
/// </summary>
public static class CranialSetupTools
{
    // ── ① 환자 애니 클립 루트 트랜스폼 통일 ──────────────────────────────
    //
    // 증상: 동작 중 클립이 바뀔 때 환자가 살짝 내려왔다가 어떤 클립에서는 정상 위치로 돌아온다.
    // 원인: 클립마다 Root Transform 설정이 다르다(2026-08-03 실측).
    //         굴곡신전                      : Bake Y=ON,  BasedUpon XZ=Original
    //         idle                          : Bake Y=OFF, BasedUpon XZ=Original
    //         양반다리·양손깍지·기대기·PM고개회전·PM중립 : Bake Y=OFF, BasedUpon XZ=Center of Mass
    //       기준이 섞여 있으면 전환할 때 루트 높이·수평 기준이 튄다.
    // 해결: 전 클립을 동일 설정으로 맞춘다(= 굴곡신전 기준).
    //         Root Transform Position Y  : Bake Into Pose ON,  Based Upon = Original
    //         Root Transform Position XZ : Bake Into Pose ON,  Based Upon = Original
    //         Root Transform Rotation    : Bake Into Pose ON,  Based Upon = Original
    //       Bake Into Pose = 루트 이동을 포즈에 구워 넣어 오브젝트가 스스로 움직이지 않게 한다.
    //       (이 프로젝트는 Apply Root Motion을 꺼서 쓰므로 전부 구워 두는 쪽이 안전하다.)

    /// 대상 클립 이름. 환자 애니메이터가 쓰는 클립을 여기 나열한다.
    private static readonly string[] PatientClips =
    {
        "idle", "굴곡신전", "PM고개회전", "PM중립",
        "양반다리", "양손깍지", "기대기",
    };

    [MenuItem("GuideChuna/환자 애니 루트 설정 점검 (읽기 전용)")]
    public static void AuditClipRootSettings()
    {
        var sb = new StringBuilder("[환자 애니 루트 설정 점검]\n");
        sb.AppendLine("클립                 BakeY  BakeXZ  BakeRot  OrigY  OrigXZ  OrigRot   상태");
        bool anyBad = false;
        foreach (var (name, clip) in FindPatientClips())
        {
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            bool ok = s.loopBlendPositionY && s.loopBlendPositionXZ && s.loopBlendOrientation &&
                      s.keepOriginalPositionY && s.keepOriginalPositionXZ && s.keepOriginalOrientation;
            if (!ok) anyBad = true;
            sb.AppendLine(string.Format("{0,-20} {1,-6} {2,-7} {3,-8} {4,-6} {5,-7} {6,-8}  {7}",
                name, s.loopBlendPositionY, s.loopBlendPositionXZ, s.loopBlendOrientation,
                s.keepOriginalPositionY, s.keepOriginalPositionXZ, s.keepOriginalOrientation,
                ok ? "OK" : "← 불일치"));
        }
        sb.AppendLine();
        sb.AppendLine(anyBad
            ? "→ 불일치 클립이 있습니다. [GuideChuna/환자 애니 루트 설정 통일 적용] 을 실행하세요."
            : "→ 전 클립이 동일합니다. 애니 전환 시 위치가 튄다면 다른 원인입니다.");
        Debug.Log(sb.ToString());
    }

    [MenuItem("GuideChuna/환자 애니 루트 설정 통일 적용")]
    public static void UnifyClipRootSettings()
    {
        var clips = FindPatientClips();
        if (clips.Count == 0)
        {
            Debug.LogError("[환자 애니] 대상 클립을 찾지 못했습니다. PatientClips 목록을 확인하세요.");
            return;
        }
        if (!EditorUtility.DisplayDialog("환자 애니 루트 설정 통일",
                string.Format("클립 {0}개의 Root Transform 설정을\n" +
                              "Bake Into Pose = 켬 / Based Upon = Original 로 통일합니다.\n\n{1}\n\n" +
                              "되돌리려면 Ctrl+Z 또는 버전관리로 복원하세요.",
                              clips.Count, string.Join(", ", clips.Select(c => c.name))),
                "적용", "취소"))
            return;

        int changed = 0;
        foreach (var (name, clip) in clips)
        {
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            bool before = s.loopBlendPositionY && s.loopBlendPositionXZ && s.loopBlendOrientation &&
                          s.keepOriginalPositionY && s.keepOriginalPositionXZ && s.keepOriginalOrientation;
            if (before) continue;

            Undo.RecordObject(clip, "환자 애니 루트 설정 통일");
            s.loopBlendPositionY = true;
            s.loopBlendPositionXZ = true;
            s.loopBlendOrientation = true;
            s.keepOriginalPositionY = true;
            s.keepOriginalPositionXZ = true;
            s.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(clip, s);
            EditorUtility.SetDirty(clip);
            changed++;
            Debug.Log($"  · {name} 설정 통일");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[환자 애니] {changed}개 클립을 통일했습니다(대상 {clips.Count}개). " +
                  "Play로 동작 전환 시 위치가 튀지 않는지 확인하세요.");
    }

    private static List<(string name, AnimationClip clip)> FindPatientClips()
    {
        var result = new List<(string, AnimationClip)>();
        foreach (var n in PatientClips)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{n} t:AnimationClip"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != n) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null && !result.Any(x => x.Item2 == clip))
                    result.Add((n, clip));
            }
        }
        return result;
    }


    // ── ② PJ 리그 호흡 오버라이드 배선 ───────────────────────────────────
    //
    // PJ 지시문: "숨을 끝까지 내쉬는 동안 신전·내전 → 완전히 내쉰 후 손을 바꾸고 크게 들이마시게 한다"
    //   = 길게 1회. 그런데 세 리그 모두 오버라이드가 0이라 공유 HUD 기본값(3회)으로 돌고 있었다.

    private const int PJ_BREATHS = 1;
    private const float PJ_INHALE = 6f;
    private const float PJ_EXHALE = 6f;

    [MenuItem("GuideChuna/두개골 PJ 호흡 설정 (1회 · 들숨 6초 · 날숨 6초 · 날숨부터)")]
    public static void ApplyPjBreathing()
    {
        var rigs = Object.FindObjectsByType<CranialAdjustmentController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var pj = rigs.FirstOrDefault(r => r.name.Contains("PJ"));
        if (pj == null)
        {
            Debug.LogError("[PJ 호흡] 이름에 'PJ'가 든 CranialAdjustmentController를 찾지 못했습니다. " +
                           "TrainingScene이 열려 있는지 확인하세요. (찾은 리그: " +
                           string.Join(", ", rigs.Select(r => r.name)) + ")");
            return;
        }

        var so = new SerializedObject(pj);
        var count = so.FindProperty("breathCountOverride");
        var inhale = so.FindProperty("inhaleSecondsOverride");
        var exhale = so.FindProperty("exhaleSecondsOverride");
        var phase = so.FindProperty("breathStartPhaseOverride");
        if (count == null || inhale == null || exhale == null || phase == null)
        {
            Debug.LogError("[PJ 호흡] 필드를 찾지 못했습니다(스크립트가 바뀌었을 수 있음).");
            return;
        }

        Undo.RecordObject(pj, "PJ 호흡 오버라이드");
        count.intValue = PJ_BREATHS;
        inhale.floatValue = PJ_INHALE;
        exhale.floatValue = PJ_EXHALE;
        phase.enumValueIndex = (int)BreathingSyncHUD.StartPhase.Exhale;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(pj);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pj.gameObject.scene);

        Debug.Log($"[PJ 호흡] '{pj.name}' 설정 완료 — 호흡 {PJ_BREATHS}회 / 들숨 {PJ_INHALE}초 / " +
                  $"날숨 {PJ_EXHALE}초 / 날숨부터 시작.\n→ 씬을 저장하세요(Ctrl+S). 되돌리려면 Ctrl+Z.");
        Selection.activeGameObject = pj.gameObject;
        EditorGUIUtility.PingObject(pj.gameObject);
    }

    // ── ③ 진단 자세별 가이드손 클립 배선 ────────────────────────────────
    //
    // PM·PJ의 진단1 단계는 자세가 2개다(ⓐ왼손 후두부+오른손 3점 / ⓑ왼손 3점+오른손 후두부).
    // 자세마다 다른 녹화를 써야 하는데 이 값은 CSV가 아니라 컨트롤러의
    // diagnosisStages[].poses[].guideClipName 에 있다(씬 직렬화) → 도구로 넣는다.
    //
    // ★어느 녹화가 어느 자세인지는 이름이 아니라 **손목 월드좌표 측정**으로 정했다.
    //   'PM 진단 좌' = 오른손이 바깥(측두골, X +0.188) → ⓐ
    //   'PM 진단 우' = 왼손이 바깥(측두골, X -0.170) → ⓑ
    //   (환자 모델이 X축 미러링돼 있어 L/R 이름으로 좌우를 추론하면 안 된다.)
    private const string CLIP_POSE_A = "PM 진단 좌";
    private const string CLIP_POSE_B = "PM 진단 우";

    [MenuItem("GuideChuna/두개골 진단 자세별 가이드손 클립 배선 (PM·PJ)")]
    public static void ApplyDiagnosisGuideClips()
    {
        var rigs = Object.FindObjectsByType<CranialAdjustmentController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var targets = rigs.Where(r => r.name.Contains("PM") || r.name.Contains("PJ")).ToList();
        if (targets.Count == 0)
        {
            Debug.LogError("[진단 가이드손] PM·PJ 리그를 찾지 못했습니다. TrainingScene이 열려 있는지 확인하세요.");
            return;
        }

        int done = 0;
        var sb = new StringBuilder("[진단 자세별 가이드손 클립 배선]\n");
        foreach (var rig in targets)
        {
            var so = new SerializedObject(rig);
            var stages = so.FindProperty("diagnosisStages");
            if (stages == null || !stages.isArray) { sb.AppendLine($"  {rig.name}: diagnosisStages 없음"); continue; }

            for (int i = 0; i < stages.arraySize; i++)
            {
                var stage = stages.GetArrayElementAtIndex(i);
                string stageId = stage.FindPropertyRelative("stageId").stringValue;
                var poses = stage.FindPropertyRelative("poses");
                if (poses == null || poses.arraySize < 2) continue;   // 자세 2개짜리(좌·우)만 대상

                Undo.RecordObject(rig, "진단 가이드손 클립");
                poses.GetArrayElementAtIndex(0).FindPropertyRelative("guideClipName").stringValue = CLIP_POSE_A;
                poses.GetArrayElementAtIndex(1).FindPropertyRelative("guideClipName").stringValue = CLIP_POSE_B;
                sb.AppendLine($"  {rig.name} / {stageId}: 자세①={CLIP_POSE_A}, 자세②={CLIP_POSE_B}");
                done++;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(rig);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        }
        sb.AppendLine();
        sb.AppendLine(done > 0
            ? "→ 씬을 저장하세요(Ctrl+S). 되돌리려면 Ctrl+Z."
            : "→ 자세가 2개인 진단 단계를 찾지 못했습니다.");
        Debug.Log(sb.ToString());
    }

    [MenuItem("GuideChuna/두개골 진단 자세·가이드손 점검 (읽기 전용)")]
    public static void AuditDiagnosisGuideClips()
    {
        var sb = new StringBuilder("[두개골 진단 단계 구성]\n");
        foreach (var rig in Object.FindObjectsByType<CranialAdjustmentController>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(rig);
            var stages = so.FindProperty("diagnosisStages");
            if (stages == null || !stages.isArray) continue;
            sb.AppendLine($"— {rig.name}");
            for (int i = 0; i < stages.arraySize; i++)
            {
                var st = stages.GetArrayElementAtIndex(i);
                var poses = st.FindPropertyRelative("poses");
                sb.AppendLine($"   stageId={st.FindPropertyRelative("stageId").stringValue} " +
                              $"유지={st.FindPropertyRelative("holdSeconds").floatValue}초 자세={poses.arraySize}개");
                for (int j = 0; j < poses.arraySize; j++)
                {
                    var p = poses.GetArrayElementAtIndex(j);
                    string clip = p.FindPropertyRelative("guideClipName").stringValue;
                    sb.AppendLine($"      [{j}] {p.FindPropertyRelative("label").stringValue}  " +
                                  $"가이드손='{(string.IsNullOrEmpty(clip) ? "(없음 — substep 공용 사용)" : clip)}'");
                }
            }
        }
        Debug.Log(sb.ToString());
    }

    [MenuItem("GuideChuna/두개골 리그 호흡 설정 점검 (읽기 전용)")]
    public static void AuditBreathing()
    {
        var rigs = Object.FindObjectsByType<CranialAdjustmentController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sb = new StringBuilder("[두개골 리그 호흡 설정]\n");
        sb.AppendLine("리그                              횟수  들숨   날숨   시작위상");
        foreach (var r in rigs)
        {
            var so = new SerializedObject(r);
            sb.AppendLine(string.Format("{0,-32} {1,-5} {2,-6} {3,-6} {4}",
                r.name,
                so.FindProperty("breathCountOverride").intValue,
                so.FindProperty("inhaleSecondsOverride").floatValue,
                so.FindProperty("exhaleSecondsOverride").floatValue,
                (BreathingSyncHUD.StartPhase)so.FindProperty("breathStartPhaseOverride").enumValueIndex));
        }
        var hud = Object.FindFirstObjectByType<BreathingSyncHUD>();
        sb.AppendLine();
        sb.AppendLine(hud != null
            ? $"공유 HUD 기본값: 호흡 {hud.RequiredBreaths}회 (오버라이드가 0이면 이 값이 쓰인다)"
            : "공유 HUD를 찾지 못했습니다.");
        Debug.Log(sb.ToString());
    }
}
