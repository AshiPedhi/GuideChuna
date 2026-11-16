using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using Oculus.Interaction.Surfaces;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★로비에서 컨트롤러로 가리켜 고르기(11-16 사용자 지시 «컨트롤러도 동작되게»).
///   원인(11-16 진단): 로비의 OVRControllers에는 누르기(ControllerPokeInteractor)만 있고 레이저(ControllerRayInteractor)가 없었다(git 이력 0).
///   그래서 멀리 있는 판은 컨트롤러로 못 골랐다. XR-ROM은 OVRInput을 직접 읽어서 됐다.
///
///   씬을 텍스트로 고치지 않으려고(절대규칙 1) <b>런타임에 붙인다</b> — lobby 씬이 열릴 때(Start 전) 한 번:
///   ① 컨트롤러 누르기 인터랙터마다 옆에 레이저(ISDK ControllerRayInteractor 프리팹 사본 · Resources/ControllerRay)를 붙이고
///      같은 컨트롤러에 잇고, 누르기가 든 InteractorGroup에 같이 넣는다(누르기와 레이저가 서로 양보).
///   ② 누르기만 받는 판(PokeInteractable만 있고 같은 판에 RayInteractable이 없는 것 — 시나리오 선택 팝업 둘)에
///      같은 면 · 같은 판(PointableCanvas)으로 RayInteractable을 붙인다. 로비 메인 판은 이미 있어 건드리지 않는다.
///   고르기 = 트리거(프리팹 기본 TriggerSelector). 로비 밖 씬에는 붙이지 않는다(실습 씬은 11-16 범위 밖 · XR-ROM은 따로 읽는다).
///   로그 [로비레이저] — 붙인 개수를 남긴다(0이면 배선이 바뀐 것).
/// </summary>
public static class LobbyControllerRay
{
    private const string LobbySceneName = "lobby";
    private const string PrefabPath = "ControllerRay/LobbyControllerRay";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // sceneLoaded는 새 씬의 Awake · OnEnable 뒤, Start 전에 온다 — InteractorGroup이 Start에서 목록을 굳히기 전에 넣을 수 있다.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != LobbySceneName) return;

        var prefab = Resources.Load<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("[로비레이저] 프리팹을 못 찾았다: Resources/" + PrefabPath + " — 레이저 없이 진행");
            return;
        }

        int rays = 0, groups = 0, surfaces = 0;
        var pokes = Object.FindObjectsByType<PokeInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (PokeInteractor poke in pokes)
        {
            if (poke.gameObject.scene != scene) continue;
            ControllerRef pokeRef = poke.GetComponentInParent<ControllerRef>(true);
            if (pokeRef == null) continue;   // 손 누르기 — 컨트롤러가 아니다
            Transform pokeRoot = pokeRef.transform;
            if (pokeRoot.parent == null) continue;

            // 비활성 임시 부모 아래서 만들어 Awake · OnEnable 전에 컨트롤러를 잇는다(빈 컨트롤러로 깨어나지 않게).
            var holder = new GameObject("[로비레이저] 준비");
            holder.SetActive(false);
            GameObject ray = Object.Instantiate(prefab, holder.transform, false);
            ray.name = "ControllerRayInteractor (로비 · 런타임)";
            ray.GetComponent<ControllerRef>().InjectController(pokeRef);   // ControllerRef도 IController — 누르기와 같은 컨트롤러
            ray.transform.SetParent(pokeRoot.parent, false);
            Object.Destroy(holder);
            rays++;

            InteractorGroup group = pokeRoot.parent.GetComponentInParent<InteractorGroup>(true);
            IInteractor rayInteractor = ray.GetComponentInChildren<RayInteractor>(true);
            if (group != null && rayInteractor != null && group.Interactors != null)
            {
                var list = new List<IInteractor>(group.Interactors.Count + 1);
                for (int i = 0; i < group.Interactors.Count; i++) list.Add(group.Interactors[i]);
                list.Add(rayInteractor);
                group.InjectInteractors(list);
                groups++;
            }
        }

        // 누르기만 받는 판에 레이저 받기를 붙인다.
        var rayables = Object.FindObjectsByType<RayInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        // ★꺼져 있는 판(팝업)은 Awake 전이라 속성이 비어 있다 — 직렬화 필드를 직접 읽는다.
        var covered = new HashSet<Object>();
        foreach (RayInteractable r in rayables)
        {
            Object pe = Field(r, typeof(PointerInteractable<RayInteractor, RayInteractable>), "_pointableElement");
            if (pe != null) covered.Add(pe);
        }
        var pokeables = Object.FindObjectsByType<PokeInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (PokeInteractable p in pokeables)
        {
            if (p.gameObject.scene != scene) continue;
            Object pe = Field(p, typeof(PointerInteractable<PokeInteractor, PokeInteractable>), "_pointableElement");
            var patch = Field(p, typeof(PokeInteractable), "_surfacePatch") as ISurfacePatch;
            if (pe == null || patch == null || covered.Contains(pe)) continue;
            var r = p.gameObject.AddComponent<RayInteractable>();
            r.InjectSurface(patch);
            r.InjectOptionalPointableElement(pe as IPointableElement);
            covered.Add(pe);
            surfaces++;
        }

        Debug.Log($"[로비레이저] 컨트롤러 레이저 {rays}개(그룹에 넣음 {groups}) · 레이저 받는 판 추가 {surfaces}개");
    }

    /// <summary>ISDK 컴포넌트의 private 직렬화 필드(Object 참조)를 읽는다. 씬이 열릴 때 한 번만 부른다.</summary>
    private static Object Field(object target, System.Type declaring, string name)
    {
        var f = declaring.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (f == null) { Debug.LogWarning($"[로비레이저] 필드 없음: {declaring.Name}.{name} (SDK가 바뀌었나)"); return null; }
        return f.GetValue(target) as Object;
    }
}
