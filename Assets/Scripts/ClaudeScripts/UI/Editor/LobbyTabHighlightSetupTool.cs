using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로비 카테고리 탭(전체/단순추나/복잡추나/ROM진단)에 LobbyTabHighlight를 배선해,
/// 선택된 탭이 활성 색을 계속 유지하게 만든다.
///
/// ★배경 (2026-08-18 실측)
///   탭은 Meta XR 샘플 프리팹(SecondaryButton_IconAndLabel)이고, 그 Toggle은
///   graphic(체크마크) 슬롯이 비어 있어 isOn 상태를 표시할 그래픽이 없다. 보이는 하이라이트는
///   transition=Animation 이 만드는 상호작용 상태뿐이라 포커스가 떠나면 사라진다.
///   게다가 컨트롤러 클립 5개가 전부 Content/Background 의 m_Color를 애니메이션하므로,
///   Animator를 끄지 않으면 어떤 색을 넣어도 매 프레임 덮어써진다.
///
/// ★비파괴 원칙: 오브젝트를 만들거나 지우지 않는다. 하는 일은 아래 넷뿐이고 전부 Undo로 되돌아간다.
///   1) 탭 Toggle에 LobbyTabHighlight 추가 (이미 있으면 색만 다시 적용 — 멱등)
///   2) Toggle.transition 을 None 으로 (Animator에게 색 소유권을 뺏어오기 위함)
///   3) Animator 컴포넌트를 비활성 (삭제하지 않는다 — 되돌리려면 체크만 다시 켜면 된다)
///   4) Content/Background 의 색을 현재 isOn 상태에 맞게 칠해 씬 뷰에서 바로 보이게 함
/// </summary>
public static class LobbyTabHighlightSetupTool
{
    private const string LobbyScenePath = "Assets/Scenes/lobby.unity";

    [MenuItem("GuideChuna/로비 탭 활성색 적용")]
    public static void Apply()
    {
        if (!TryGetTabs(out var toggles, out var browser)) return;

        int added = 0, reapplied = 0, animatorsOff = 0, transitionsChanged = 0;
        var sb = new StringBuilder();
        sb.AppendLine($"[로비 탭 활성색] 탭 {toggles.Count}개 처리");

        foreach (var toggle in toggles)
        {
            if (toggle == null) continue;
            var go = toggle.gameObject;

            var hl = go.GetComponent<LobbyTabHighlight>();
            if (hl == null)
            {
                hl = Undo.AddComponent<LobbyTabHighlight>(go);
                added++;
            }
            else reapplied++;

            // 배경 참조를 명시적으로 박아둔다 (런타임 자동 탐색에만 기대지 않기 위함)
            var bg = go.transform.Find(LobbyTabHighlight.BackgroundPath)?.GetComponent<Graphic>();
            var so = new SerializedObject(hl);
            so.FindProperty("background").objectReferenceValue = bg;
            so.ApplyModifiedProperties();

            if (toggle.transition != Selectable.Transition.None)
            {
                Undo.RecordObject(toggle, "탭 transition None");
                toggle.transition = Selectable.Transition.None;
                EditorUtility.SetDirty(toggle);
                transitionsChanged++;
            }

            var anim = go.GetComponent<Animator>();
            if (anim != null && anim.enabled)
            {
                Undo.RecordObject(anim, "탭 Animator 비활성");
                anim.enabled = false;
                EditorUtility.SetDirty(anim);
                animatorsOff++;
            }

            if (bg != null)
            {
                Undo.RecordObject(bg, "탭 배경색");
                bg.color = toggle.isOn ? hl.ActiveColor : hl.NormalColor;
                EditorUtility.SetDirty(bg);
            }

            // 탭은 전부 Meta 샘플 프리팹의 인스턴스다. 프리팹 인스턴스는 이 호출을 해줘야
            // 위 변경들이 '오버라이드'로 확실히 등록된다(안 하면 씬 저장 시 되돌아갈 수 있다).
            RecordOverride(toggle);
            RecordOverride(anim);
            RecordOverride(bg);

            sb.AppendLine($"  ✓ {go.name}  isOn={(toggle.isOn ? "선택" : "-")}  " +
                          $"배경={(bg != null ? bg.name : "★없음(Content/Background 경로 확인 필요)")}");
        }

        EditorSceneManager.MarkSceneDirty(browser.gameObject.scene);

        sb.AppendLine();
        sb.AppendLine($"컴포넌트 추가 {added} / 재적용 {reapplied} / transition→None {transitionsChanged} / Animator 끔 {animatorsOff}");
        sb.AppendLine("색을 바꾸려면 각 탭의 LobbyTabHighlight 인스펙터에서 활성 색을 고친 뒤 이 메뉴를 다시 실행할 것.");
        sb.AppendLine("씬 저장을 잊지 말 것. 되돌리려면 Ctrl+Z.");
        Debug.Log(sb.ToString());
    }

    [MenuItem("GuideChuna/로비 탭 활성색 점검 (읽기 전용)")]
    public static void Audit()
    {
        if (!TryGetTabs(out var toggles, out _)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"[로비 탭 활성색 점검] 탭 {toggles.Count}개");
        foreach (var toggle in toggles)
        {
            if (toggle == null) { sb.AppendLine("  ✗ (빈 슬롯)"); continue; }
            var go = toggle.gameObject;
            var hl = go.GetComponent<LobbyTabHighlight>();
            var anim = go.GetComponent<Animator>();
            var bg = go.transform.Find(LobbyTabHighlight.BackgroundPath)?.GetComponent<Graphic>();

            bool ok = hl != null
                      && toggle.transition == Selectable.Transition.None
                      && (anim == null || !anim.enabled)
                      && bg != null;

            sb.AppendLine($"  {(ok ? "✓" : "✗")} {go.name}");
            sb.AppendLine($"       LobbyTabHighlight={(hl != null ? "있음" : "없음")}" +
                          $" / transition={toggle.transition}" +
                          $" / Animator={(anim == null ? "없음" : (anim.enabled ? "★켜짐(색을 덮어씀)" : "꺼짐"))}" +
                          $" / 배경={(bg != null ? $"{bg.name} {ColorUtility.ToHtmlStringRGBA(bg.color)}" : "★못 찾음")}" +
                          $" / isOn={toggle.isOn}");
        }
        Debug.Log(sb.ToString());
    }

    /// <summary>열린 로비 씬의 LobbyBrowser에서 탭 Toggle 목록을 읽는다.
    /// tabs는 private 직렬화 필드라 SerializedObject를 경유한다(런타임 코드는 건드리지 않는다).</summary>
    private static bool TryGetTabs(out List<Toggle> toggles, out LobbyBrowser browser)
    {
        toggles = new List<Toggle>();
        browser = null;

        Scene lobby = default;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.path == LobbyScenePath) { lobby = s; break; }
        }

        if (!lobby.IsValid() || !lobby.isLoaded)
        {
            Debug.LogError($"[로비 탭 활성색] {LobbyScenePath} 가 열려 있지 않습니다. 로비 씬을 연 뒤 다시 실행하세요.");
            return false;
        }

        foreach (var root in lobby.GetRootGameObjects())
        {
            browser = root.GetComponentInChildren<LobbyBrowser>(true);
            if (browser != null) break;
        }

        if (browser == null)
        {
            Debug.LogError("[로비 탭 활성색] 씬에서 LobbyBrowser를 찾지 못했습니다.");
            return false;
        }

        var so = new SerializedObject(browser);
        var tabs = so.FindProperty("tabs");
        if (tabs == null || !tabs.isArray || tabs.arraySize == 0)
        {
            Debug.LogError("[로비 탭 활성색] LobbyBrowser.tabs 가 비어 있습니다. 인스펙터에서 탭 Toggle 배선을 먼저 확인하세요.");
            return false;
        }

        for (int i = 0; i < tabs.arraySize; i++)
        {
            var t = tabs.GetArrayElementAtIndex(i).FindPropertyRelative("toggle");
            toggles.Add(t != null ? t.objectReferenceValue as Toggle : null);
        }
        return true;
    }

    /// <summary>프리팹 인스턴스 위의 속성 변경을 오버라이드로 등록한다. 인스턴스가 아니면 아무 일도 안 한다.</summary>
    private static void RecordOverride(Component c)
    {
        if (c == null) return;
        if (PrefabUtility.IsPartOfPrefabInstance(c))
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
    }
}
