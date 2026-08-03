using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 로비 카드(ScenarioLaunchButton)의 scenarioIndex가 TrainingScene의
/// ScenarioBootstrapper.scenarioConfigs[] 순서와 맞는지 점검하고, 누락된 카드를 추가한다.
///
/// ★왜 필요한가: scenarioConfigs 배열 중간에 시나리오를 삽입하면 뒤쪽 카드 인덱스가 전부
///   한 칸씩 밀린다. 라벨은 그대로라 "카드에 적힌 술기"와 "실제로 열리는 술기"가 달라지는데,
///   씬을 눈으로 봐서는 알 수 없다(인덱스는 인스펙터 숫자, 라벨은 프리팹 오버라이드).
///   2026-08-01에 두개골PJ를 idx 7에 삽입하면서 카드 8~11이 실제로 어긋났던 전례가 있다.
///
/// ★비파괴 원칙: 이 도구는 기존 오브젝트를 절대 삭제/이동하지 않는다.
///   - [점검]은 읽기 전용 리포트만 출력한다.
///   - [누락 카드 추가]는 이미 같은 인덱스의 카드가 있으면 아무것도 하지 않는다(멱등).
///     추가할 때도 기존 카드를 복제할 뿐 원본은 건드리지 않으며, Undo로 되돌릴 수 있다.
/// </summary>
public static class LobbyCardAuditTool
{
    private const string LobbyScenePath = "Assets/Scenes/lobby.unity";

    // ScenarioBootstrapper.scenarioConfigs[] 순서. 배열을 바꾸면 여기도 같이 고칠 것.
    private static readonly string[] ExpectedScenarios =
    {
        "상부승모근", "견갑거근", "대흉근", "흉쇄유돌근", "사각근",
        "두개골교정", "두개골PM교정", "두개골PJ교정",
        "앙와위_흉추_굴곡변위", "앙와위_흉추_신전변위",
        "제1늑골_앙와위", "제2늑골_상방변위", "경추ROM측정",
    };

    [MenuItem("GuideChuna/로비 카드 점검 (읽기 전용)")]
    public static void Audit()
    {
        if (!TryGetCards(out var cards)) return;

        var byIndex = new Dictionary<int, List<ScenarioLaunchButton>>();
        foreach (var c in cards)
        {
            if (!byIndex.TryGetValue(c.ScenarioIndex, out var list))
                byIndex[c.ScenarioIndex] = list = new List<ScenarioLaunchButton>();
            list.Add(c);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[로비 카드 점검] 카드 {cards.Count}개 / 시나리오 {ExpectedScenarios.Length}개");
        sb.AppendLine();

        for (int i = 0; i < ExpectedScenarios.Length; i++)
        {
            if (!byIndex.TryGetValue(i, out var list))
            {
                sb.AppendLine($"  ✗ idx {i,2}  카드 없음 → '{ExpectedScenarios[i]}'는 로비에서 진입 불가");
                continue;
            }

            foreach (var card in list)
            {
                string label = ReadLabel(card.gameObject);
                bool dup = list.Count > 1;
                sb.AppendLine($"  {(dup ? "!" : "•")} idx {i,2}  라벨='{label}'  →  진입='{ExpectedScenarios[i]}'"
                              + (dup ? "   (같은 인덱스 카드가 여러 개)" : ""));
            }
        }

        foreach (var kv in byIndex.Where(kv => kv.Key < 0 || kv.Key >= ExpectedScenarios.Length))
            sb.AppendLine($"  ✗ idx {kv.Key} 는 범위 밖(0~{ExpectedScenarios.Length - 1}) — 클릭 시 기본 시나리오로 폴백됨");

        sb.AppendLine();
        sb.AppendLine("※ 라벨과 '진입'이 다르면 카드 인덱스를 고쳐야 한다. 라벨은 사람이 읽는 값일 뿐 동작에 관여하지 않는다.");
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 사용법: Hierarchy에서 '흉추교정(신전변위)' 카드를 Ctrl+D로 복제한 뒤,
    ///        복제본을 선택한 상태로 이 메뉴를 실행한다.
    ///
    /// ★왜 복제를 스크립트로 안 하는가: 이 카드는 프리팹 인스턴스이고 오버라이드가 80건
    ///   (레이아웃 앵커·폰트에셋·스프라이트·색·활성상태) + 추가 컴포넌트 4개다.
    ///   Object.Instantiate로 만들면 겉모습은 같아도 프리팹 연결이 끊긴다.
    ///   Ctrl+D는 프리팹 연결과 오버라이드를 모두 보존하므로 복제는 사람이 하는 편이 낫다.
    ///   이 메뉴는 그 다음의 실수하기 쉬운 부분(인덱스 숫자·라벨)만 처리한다.
    /// </summary>
    [MenuItem("GuideChuna/로비 카드 - 선택한 카드를 idx 8 (흉추 굴곡변위)로 설정")]
    public static void ConfigureSelectedAsFlexionCard()
    {
        const int NewIndex = 8;   // 앙와위_흉추_굴곡변위

        var go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogError("[로비 카드] 선택된 오브젝트가 없습니다. 복제한 카드를 Hierarchy에서 선택한 뒤 실행하세요.");
            return;
        }

        var btn = go.GetComponent<ScenarioLaunchButton>();
        if (btn == null)
        {
            Debug.LogError($"[로비 카드] '{go.name}'에 ScenarioLaunchButton이 없습니다. 카드 루트를 선택했는지 확인하세요.");
            return;
        }

        if (!TryGetCards(out var cards)) return;

        var conflict = cards.FirstOrDefault(c => c != btn && c.ScenarioIndex == NewIndex);
        if (conflict != null)
        {
            Debug.LogError($"[로비 카드] 이미 idx {NewIndex} 카드가 있습니다('{conflict.gameObject.name}'). "
                           + "중복 배선을 막기 위해 중단합니다.");
            return;
        }

        Undo.RecordObject(btn, "로비 카드 인덱스");
        var so = new SerializedObject(btn);
        so.FindProperty("scenarioIndex").intValue = NewIndex;
        so.ApplyModifiedProperties();

        // 라벨: '신전'이 들어간 텍스트만 '굴곡'으로 바꾼다(제목 '흉추교정'은 그대로 둔다).
        int replaced = 0;
        foreach (var tmp in go.GetComponentsInChildren<TMP_Text>(true))
        {
            if (string.IsNullOrEmpty(tmp.text) || !tmp.text.Contains("신전")) continue;
            Undo.RecordObject(tmp, "로비 카드 라벨");
            tmp.text = tmp.text.Replace("신전", "굴곡");
            EditorUtility.SetDirty(tmp);
            replaced++;
        }

        EditorSceneManager.MarkSceneDirty(go.scene);

        Debug.Log($"[로비 카드] '{go.name}' → idx {NewIndex}(앙와위_흉추_굴곡변위) 설정 완료. 라벨 {replaced}개 갱신.\n"
                  + (replaced == 0 ? "※ '신전'이 든 라벨이 없어 텍스트는 그대로입니다. 라벨을 직접 확인하세요.\n" : "")
                  + "→ 씬을 저장하세요(Ctrl+S). 되돌리려면 Ctrl+Z.");
    }

    private static bool TryGetCards(out List<ScenarioLaunchButton> cards)
    {
        cards = new List<ScenarioLaunchButton>();

        Scene lobby = default;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.path == LobbyScenePath) { lobby = s; break; }
        }

        if (!lobby.IsValid() || !lobby.isLoaded)
        {
            Debug.LogError($"[로비 카드] {LobbyScenePath} 가 열려 있지 않습니다. 로비 씬을 연 뒤 다시 실행하세요.");
            return false;
        }

        foreach (var root in lobby.GetRootGameObjects())
            cards.AddRange(root.GetComponentsInChildren<ScenarioLaunchButton>(true));

        cards = cards.OrderBy(c => c.ScenarioIndex).ToList();
        if (cards.Count == 0)
        {
            Debug.LogError("[로비 카드] ScenarioLaunchButton이 하나도 없습니다.");
            return false;
        }
        return true;
    }

    private static string ReadLabel(GameObject card)
    {
        var texts = card.GetComponentsInChildren<TMP_Text>(true)
                        .Select(t => t.text)
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t.Replace("\n", " ").Trim())
                        .ToArray();
        return texts.Length == 0 ? "(라벨 없음)" : string.Join(" / ", texts);
    }
}
