// ★[빌드 가드] 2026-09-09 신설 — 사용자 지시: "빌드할 때마다 시나리오 포스 끄고 키기 귀찮다."
//
// `ScenarioBootstrapper.forceDefaultIndex`는 <b>에디터 테스트용</b>이다(필드 툴팁 원문).
// 켜 두면 로비 선택을 무시하고 `defaultScenarioIndex`로 바로 들어간다 —
// 에디터에서는 편하지만 <b>빌드에 섞여 나가면 로비가 죽은 앱</b>이 된다.
//
// ★★<b>씬 파일은 안 건드린다.</b> `IProcessSceneWithReport`는 빌드에 들어가는
//   <b>임시 사본</b>을 고친다. 그래서 에디터의 체크는 켠 채로 두고 빌드만 꺼진다 —
//   사람이 껐다 켰다 할 일이 없어진다. 이게 이 훅을 고른 이유다.
//   (`IPreprocessBuildWithReport`에서 실제 씬을 고치면 디스크에 저장해야 하고,
//    그러면 절대규칙 1·2가 걸리는 자리가 된다. 그 길로 안 간다.)
//
// ★이 콜백은 <b>Play 모드에 들어갈 때도</b> 불린다. 그때는 report가 null이라 그걸로 가른다.
//   안 가르면 Play를 누를 때마다 포스가 꺼져 에디터 테스트가 안 된다.

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ScenarioForceIndexBuildGuard : IProcessSceneWithReport
{
    public int callbackOrder => 0;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        // ★빌드일 때만 한다. Play 모드 진입에서는 report가 null이다.
        if (report == null) return;

        int changed = 0;

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                if (mb.GetType().Name != "ScenarioBootstrapper") continue;

                // ★private [SerializeField]라 SerializedObject로 만진다.
                //   리플렉션보다 이쪽이 안전하다 — 직렬화 자체를 고치므로 빌드 결과에 반영된다.
                var so = new SerializedObject(mb);
                var p = so.FindProperty("forceDefaultIndex");
                if (p == null)
                {
                    Debug.LogWarning($"[빌드가드] {mb.name}: forceDefaultIndex 프로퍼티가 없다 — "
                                     + "필드 이름이 바뀌었는지 확인해라.");
                    continue;
                }
                if (!p.boolValue) continue;   // 이미 꺼져 있으면 조용히 지나간다

                p.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                changed++;
                Debug.Log($"[빌드가드] {scene.name}/{mb.name} — forceDefaultIndex를 껐다(빌드 사본만). "
                          + "★프로젝트 씬 파일은 안 바뀐다.");
            }
        }

        if (changed == 0 && scene.name == "TrainingScene")
            Debug.Log("[빌드가드] TrainingScene — forceDefaultIndex는 이미 꺼져 있었다.");
    }
}
