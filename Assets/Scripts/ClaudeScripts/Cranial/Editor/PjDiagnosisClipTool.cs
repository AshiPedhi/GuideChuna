using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// PJ 진단1 스테이지의 <b>자세별 가이드 클립</b>을 OM교정 기반으로 바꾼다.
///
/// ★배경 (2026-08-18 실측)
///   PJ 전용 손 녹화는 하나도 없다(HandPoseData 59개 중 이름에 PJ가 든 파일 0개).
///   진단 자세 ⓐ/ⓑ는 <c>PM 진단 좌</c>/<c>PM 진단 우</c>를 빌려 쓰고 있었다.
///   사용자 결정: OM교정을 기준으로 삼는다 — OM CSV의 "보조수 왼손은 후두골을 받치고,
///   주동수 오른손은 관골궁과 유양돌기를 파지"가 PJ 진단 지시문과 사실상 같은 자세라서다.
///
///   ⓐ 왼손 후두 + 오른손 측두  ←  OM교정 (원본)
///   ⓑ 왼손 측두 + 오른손 후두  ←  OM교정 좌우반전 (시상면 반전 사본)
///
/// ★반전 사본은 어떻게 만들었나
///   HandType을 Left↔Right로 바꾸고, 기준점 상대 루트 위치의 X와 루트 회전의 y·z를 뒤집었다
///   (시상면 반전: q(x,y,z,w) → (x,−y,−z,w)). 손가락 관절 로컬 포즈는 그대로 뒀다 —
///   Meta의 좌/우 손 골격은 서로 미러 바인드라 같은 로컬 회전이 반대 손에서 미러로 나타난다.
///   ★이 부분만은 계산이 아니라 관례에 기댄 것이라 <b>Play에서 손가락 모양을 눈으로 확인</b>해야 한다.
///
/// ★비파괴: 자세의 guideClipName만 바꾼다. 파지점·판정·순서는 건드리지 않는다. Undo로 되돌아간다.
/// </summary>
public static class PjDiagnosisClipTool
{
    private const string ClipA = "OM교정";
    private const string ClipB = "OM교정 좌우반전";

    [MenuItem("GuideChuna/두개골/PJ 진단 가이드 클립을 OM교정 기반으로 교체")]
    public static void Apply()
    {
        CranialAdjustmentController rig = null;
        foreach (CranialAdjustmentController c in Resources.FindObjectsOfTypeAll<CranialAdjustmentController>())
            if (c != null && c.gameObject.scene.IsValid() && c.name.Contains("PJ")) { rig = c; break; }

        if (rig == null)
        {
            Debug.LogError("[PJ 진단 클립] 씬에서 PJ 리그를 찾지 못했습니다. TrainingScene을 연 뒤 다시 실행하세요.");
            return;
        }

        if (Resources.Load<TextAsset>($"HandPoseData/{ClipB}") == null)
        {
            Debug.LogError($"[PJ 진단 클립] 반전 사본 '{ClipB}'을 Resources에서 찾지 못했습니다. " +
                           "Assets/Resources/HandPoseData 임포트가 끝났는지 확인하세요.");
            return;
        }

        var so = new SerializedObject(rig);
        SerializedProperty stages = so.FindProperty("diagnosisStages");
        if (stages == null || !stages.isArray || stages.arraySize == 0)
        {
            Debug.LogError("[PJ 진단 클립] diagnosisStages가 비어 있습니다.");
            return;
        }

        SerializedProperty poses = stages.GetArrayElementAtIndex(0).FindPropertyRelative("poses");
        if (poses == null || !poses.isArray || poses.arraySize < 2)
        {
            Debug.LogError($"[PJ 진단 클립] 진단 자세가 2개여야 합니다(지금 {(poses != null ? poses.arraySize : 0)}개).");
            return;
        }

        var log = new StringBuilder();
        log.AppendLine("[PJ 진단 가이드 클립 교체]");

        Undo.RecordObject(rig, "PJ 진단 가이드 클립");
        for (int i = 0; i < poses.arraySize && i < 2; i++)
        {
            SerializedProperty po = poses.GetArrayElementAtIndex(i);
            SerializedProperty clip = po.FindPropertyRelative("guideClipName");
            string label = po.FindPropertyRelative("label")?.stringValue ?? $"자세{i + 1}";
            string before = clip != null ? clip.stringValue : "(필드 없음)";
            string after = i == 0 ? ClipA : ClipB;
            if (clip != null) clip.stringValue = after;

            // 구간은 클립 전체를 쓴다(예전 값이 남아 앞부분만 재생되는 것 방지).
            SerializedProperty s0 = po.FindPropertyRelative("guideStartRatio");
            SerializedProperty s1 = po.FindPropertyRelative("guideEndRatio");
            if (s0 != null) s0.floatValue = 0f;
            if (s1 != null) s1.floatValue = 1f;

            log.AppendLine($"  자세 {i + 1} '{label}'  {before} → {after}  (구간 0~1)");
        }
        so.ApplyModifiedProperties();
        if (PrefabUtility.IsPartOfPrefabInstance(rig))
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
        EditorUtility.SetDirty(rig);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);

        log.AppendLine();
        log.AppendLine("★Play에서 확인할 것: 자세 ⓑ의 손가락 모양이 뒤집혀 보이지 않는지.");
        log.AppendLine("  (루트 위치·회전은 계산으로 정확히 반전했지만, 손가락 로컬 포즈는 좌/우 골격의 미러 바인드 관례에 기댄 것)");
        log.AppendLine("씬 저장을 잊지 말 것. 되돌리려면 Ctrl+Z.");
        Debug.Log(log.ToString());
    }
}
