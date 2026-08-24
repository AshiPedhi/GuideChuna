using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 경추 ROM 클립이 실제로 몇 도를 만드는지 리그에서 직접 잰다.
///
/// 근육값(-1~1)에 한계각을 곱해 계산하는 방식은 두 가지를 확신할 수 없다 —
/// 아바타의 실제 근육 한계, 그리고 ±1을 넘는 값을 Unity가 외삽하는지 자르는지.
/// 그래서 클립을 실제 리그에 샘플링해 머리와 몸통 사이 각도를 재는 쪽이 정확하다.
///
/// ★읽기 전용이다. AnimationMode 안에서만 포즈를 건드리고 끝나면 되돌린다.
///   그래도 측정 후에는 씬을 저장하지 말 것.
/// </summary>
public static class RomClipAngleMeasurer
{
    // c9 리그의 목 사슬. 휴머노이드는 NeckTwist01(Neck)과 Head만 알고 NeckTwist02는 모른다.
    private const string TorsoBone = "CC_Base_Spine02";
    private static readonly string[] NeckChain =
    {
        "CC_Base_NeckTwist01",
        "CC_Base_NeckTwist02",
        "CC_Base_Head",
    };

    [MenuItem("GuideChuna/환자·리그/경추 ROM 클립 각도 측정")]
    public static void Measure()
    {
        GameObject patient = FindPatient();
        if (patient == null)
        {
            EditorUtility.DisplayDialog("경추 ROM 각도 측정",
                "씬에서 환자(Animator + Avatar)를 찾지 못했습니다.\n" +
                "환자 오브젝트를 선택한 뒤 다시 실행하세요.", "확인");
            return;
        }

        Transform torso = patient.transform.Find(FindPath(patient.transform, TorsoBone));
        Transform head = patient.transform.Find(FindPath(patient.transform, "CC_Base_Head"));
        if (torso == null || head == null)
        {
            EditorUtility.DisplayDialog("경추 ROM 각도 측정",
                $"목 사슬을 찾지 못했습니다. ({TorsoBone} / CC_Base_Head)", "확인");
            return;
        }

        List<AnimationClip> clips = LoadRomClips();
        if (clips.Count == 0)
        {
            EditorUtility.DisplayDialog("경추 ROM 각도 측정",
                "'ROM'으로 시작하는 AnimationClip을 찾지 못했습니다.", "확인");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"대상: {patient.name}   기준 몸통: {torso.name}");
        sb.AppendLine($"클립 {clips.Count}개 — 중립(t=0) 대비 끝(t=1) 각도\n");
        sb.AppendLine($"{"클립",-16}{"총 각도",8}{"  주 회전축(몸통 기준)",-24}{"Neck1",8}{"Neck2",8}{"Head",8}");
        sb.AppendLine(new string('-', 78));

        AnimationMode.StartAnimationMode();
        try
        {
            foreach (AnimationClip clip in clips)
            {
                Sample(patient, clip, 0f);
                Quaternion relStart = Quaternion.Inverse(torso.rotation) * head.rotation;
                Quaternion[] boneStart = CaptureChain(patient.transform);

                Sample(patient, clip, clip.length);
                Quaternion relEnd = Quaternion.Inverse(torso.rotation) * head.rotation;
                Quaternion[] boneEnd = CaptureChain(patient.transform);

                // 축에 의존하지 않는 값 — 중립 대비 머리가 몸통에 대해 몇 도 돌았는가
                float total = Quaternion.Angle(relStart, relEnd);

                Quaternion delta = relEnd * Quaternion.Inverse(relStart);
                delta.ToAngleAxis(out _, out Vector3 axis);

                sb.Append($"{clip.name,-16}{total,7:F1}°  {DescribeAxis(axis),-24}");
                for (int i = 0; i < NeckChain.Length; i++)
                {
                    float d = (boneStart[i] == Quaternion.identity && boneEnd[i] == Quaternion.identity)
                        ? -1f
                        : Quaternion.Angle(boneStart[i], boneEnd[i]);
                    sb.Append(d < 0f ? $"{"없음",8}" : $"{d,7:F1}°");
                }
                sb.AppendLine();
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();   // 포즈 원복
        }

        sb.AppendLine();
        sb.AppendLine("· 총 각도 = 머리가 몸통에 대해 돌아간 각. 축 규약과 무관하다.");
        sb.AppendLine("· Neck1/Neck2/Head = 각 뼈의 로컬 회전 변화량.");
        sb.AppendLine("  ★Neck2가 0에 가까우면 휴머노이드가 그 뼈를 모르는 것이다(아바타 매핑에 없음).");
        sb.AppendLine("· 임상 기준: 굴곡 45° · 신전 90° · 측굴 좌우 45° · 회전 좌우 90°");
        sb.AppendLine("· ★측정 후 씬을 저장하지 말 것.");

        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("경추 ROM 각도 측정",
            "측정 완료. Console에 표를 출력했습니다.\n\n★씬은 저장하지 마세요.", "확인");
    }

    private static void Sample(GameObject go, AnimationClip clip, float time)
    {
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(go, clip, time);
        AnimationMode.EndSampling();
    }

    private static Quaternion[] CaptureChain(Transform root)
    {
        var result = new Quaternion[NeckChain.Length];
        for (int i = 0; i < NeckChain.Length; i++)
        {
            Transform t = FindDeep(root, NeckChain[i]);
            result[i] = t != null ? t.localRotation : Quaternion.identity;
        }
        return result;
    }

    /// <summary>회전축을 몸통 기준 평면 이름으로 옮긴다(수치가 아니라 어느 면인지만).</summary>
    private static string DescribeAxis(Vector3 axis)
    {
        axis = axis.normalized;
        float x = Mathf.Abs(axis.x), y = Mathf.Abs(axis.y), z = Mathf.Abs(axis.z);
        if (x >= y && x >= z) return $"x축 우세 ({axis.x:F2}) 굴곡·신전 계열";
        if (y >= x && y >= z) return $"y축 우세 ({axis.y:F2}) 회전 계열";
        return $"z축 우세 ({axis.z:F2}) 측굴 계열";
    }

    private static GameObject FindPatient()
    {
        if (Selection.activeGameObject != null &&
            Selection.activeGameObject.GetComponent<Animator>() != null)
        {
            return Selection.activeGameObject;
        }

        foreach (Animator a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
        {
            if (a.avatar != null && FindDeep(a.transform, "CC_Base_Head") != null)
                return a.gameObject;
        }
        return null;
    }

    private static List<AnimationClip> LoadRomClips()
    {
        var list = new List<AnimationClip>();
        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip ROM"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is AnimationClip c && c.name.StartsWith("ROM") && !c.name.Contains("중립") &&
                    !c.name.Contains("대기") && !list.Contains(c))
                {
                    list.Add(c);
                }
            }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static string FindPath(Transform root, string name)
    {
        Transform t = FindDeep(root, name);
        if (t == null) return name;

        var stack = new Stack<string>();
        while (t != null && t != root)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return string.Join("/", stack);
    }
}
