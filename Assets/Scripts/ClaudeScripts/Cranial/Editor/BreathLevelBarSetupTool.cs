using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 바 형태 호흡 게이지를 <see cref="BreathingSyncHUD"/> 밑에 만들어 배선한다.
/// 메뉴: GuideChuna/호흡 바 게이지 만들기
///
/// ★왜 필요한가(2026-08-17 사용자 요구): 링만으로는 "지금 어떤 호흡을 유도해야 하는지"가 안 읽힌다.
/// 08-13 회의의 <b>"급흡기 구분용 3단계 계단식 UI"</b>가 이것이다. 0을 기준선으로 두고
/// 유도해야 할 호흡이 어느 칸인지 눈금으로 보여 준다.
/// <code>
///    2  ─ 크게 마시는 호흡(급흡기)
///    1  ─ 일반 호흡 들숨
///    0  ─ 기준선(평상)
///   -1  ─ 완전히 내쉬기
/// </code>
/// ★레벨은 <b>새 CSV 토큰 없이</b> 기존 값에서 나온다 — <c>firstScale&gt;1</c>인 첫 주기 = 2,
/// 날숨이 <c>fullExhaleSeconds</c> 이상이면 = -1, 그 외 = 1·0.
///
/// ★비파괴 — 이미 만들어 둔 바가 있으면 그것을 다시 쓰고 배선만 고친다.
/// </summary>
public static class BreathLevelBarSetupTool
{
    private const string RootName = "호흡 바 게이지";
    private const string FillName = "채움";

    // 눈금 위치 = (레벨 + 1) / 3
    private static readonly float[] TickLevels = { -1f, 0f, 1f, 2f };
    private static readonly string[] TickLabels = { "완전히 내쉬기", "기준", "들숨", "크게" };

    [MenuItem("GuideChuna/두개골/호흡 바 게이지 만들기")]
    private static void Create()
    {
        var hud = Object.FindFirstObjectByType<BreathingSyncHUD>(FindObjectsInactive.Include);
        if (hud == null)
        {
            EditorUtility.DisplayDialog("호흡 바 게이지",
                "씬에서 BreathingSyncHUD를 찾지 못했습니다.\n호흡 HUD가 있는 씬을 연 뒤 다시 실행하세요.", "확인");
            return;
        }

        // 이미 있으면 재사용(비파괴)
        Transform existing = hud.transform.Find(RootName);
        GameObject root;
        if (existing != null)
        {
            root = existing.gameObject;
        }
        else
        {
            root = new GameObject(RootName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "호흡 바 게이지 만들기");
            root.transform.SetParent(hud.transform, false);

            var rt = (RectTransform)root.transform;
            // 링 옆(왼쪽)에 세로로 세운다. 씬에서 옮기기 쉽게 앵커는 가운데.
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-140f, 0f);
            rt.sizeDelta = new Vector2(26f, 180f);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;

            // 바탕(어둡게)
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);
            bg.raycastTarget = false;
        }

        // 채움 이미지
        Transform fillT = root.transform.Find(FillName);
        Image fill;
        if (fillT != null)
        {
            fill = fillT.GetComponent<Image>();
        }
        else
        {
            var fillGo = new GameObject(FillName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(fillGo, "호흡 바 게이지 만들기");
            fillGo.transform.SetParent(root.transform, false);
            var frt = (RectTransform)fillGo.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(3f, 3f);
            frt.offsetMax = new Vector2(-3f, -3f);
            fill = fillGo.AddComponent<Image>();
        }

        // ★Filled / Vertical 이어야 fillAmount가 '차오르는 높이'가 된다.
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Vertical;
        fill.fillOrigin = (int)Image.OriginVertical.Bottom;
        fill.fillAmount = 1f / 3f;                       // 기준선(레벨 0)
        fill.color = new Color(0.35f, 0.75f, 1f);
        fill.raycastTarget = false;
        if (fill.sprite == null) fill.sprite = BuiltinWhite();

        // 눈금 4개
        for (int i = 0; i < TickLevels.Length; i++)
        {
            string tickName = $"눈금 {TickLabels[i]}";
            if (root.transform.Find(tickName) != null) continue;

            var tick = new GameObject(tickName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(tick, "호흡 바 게이지 만들기");
            tick.transform.SetParent(root.transform, false);

            float y01 = (TickLevels[i] + 1f) / 3f;
            var trt = (RectTransform)tick.transform;
            trt.anchorMin = new Vector2(0f, y01);
            trt.anchorMax = new Vector2(1f, y01);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = new Vector2(0f, Mathf.Approximately(TickLevels[i], 0f) ? 3f : 1.5f);

            var line = tick.AddComponent<Image>();
            // 기준선(0)만 진하게 — 위아래를 가르는 기준이라 눈에 띄어야 한다.
            line.color = Mathf.Approximately(TickLevels[i], 0f)
                ? new Color(1f, 1f, 1f, 0.95f)
                : new Color(1f, 1f, 1f, 0.4f);
            line.raycastTarget = false;
            if (line.sprite == null) line.sprite = BuiltinWhite();
        }

        // HUD에 배선
        var so = new SerializedObject(hud);
        SerializedProperty p = so.FindProperty("levelBar");
        if (p == null)
        {
            EditorUtility.DisplayDialog("호흡 바 게이지",
                "BreathingSyncHUD에 levelBar 필드가 없습니다. 스크립트가 최신인지 확인하세요.", "확인");
            return;
        }
        p.objectReferenceValue = fill;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(hud);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        Debug.Log($"[호흡 바 게이지] '{hud.name}' 아래에 만들고 배선했습니다.\n" +
                  "눈금: -1 완전히 내쉬기 / 0 기준 / 1 들숨 / 2 크게 마시기\n" +
                  "위치·크기는 씬에서 옮기면 됩니다. ★Ctrl+S로 저장하세요.");
    }

    /// <summary>UI 기본 흰색 스프라이트. 없으면 null이어도 Image는 단색으로 그려진다.</summary>
    private static Sprite BuiltinWhite() =>
        AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
}
