using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 실습 상단 바 + 정보 창 3개를 열린 TrainingScene에 조립한다 (2026-10-27 사용자 요청 5 · 시안 V0.7 그림 4 · 5).
///
/// [상단 바] 옛 정보 패널 왼쪽 메뉴의 토글 5개(골격 · 영상 · 결과 · 설정 · 메인으로)를 <b>옮긴다</b>(복제 아님).
///   InfoPanelController가 들고 있는 토글 참조가 그대로 살아 있다. 판 = UISet EmptyUIBackplateWithCanvas.
///   ★콘텐츠 토글 3개는 ToggleGroup(LeftNav · 끄기 불가)에서 뺀다 — 안 빼면 Unity가 배타를 강제한다.
/// [정보 창] 페이지 3개(골격 · 영상 · 진행 및 결과)를 각자 UISet 창으로 옮긴다. 옛 캔버스 배율을 그대로 써서 실제 크기가 같다.
/// [배선] InfoPanelController.windowArranger = 새 InfoWindowArranger → 펼침 모드가 켜진다(비우면 종전 배타).
///
/// ★비파괴: 이미 «상단 바»가 있으면 아무것도 안 한다. 옛 정보 패널 · 모드 선택 페이지 · 종료 팝업은 그대로 둔다.
/// ★저장은 사람이 한다. ★되돌릴 때는 Ctrl+Z 대신 <b>씬을 저장하지 않고 다시 연다</b>(프리팹 우회 단계가 Undo 두 묶음으로 나뉜다).
/// </summary>
public static class TopBarWindowsSetupTool
{
    const string BarName = "상단 바";
    const string ArrangerName = "정보 창 배치";
    const string BackplateGuid = "6b196de96e7d16b4297c1980179ae439";
    const string KoreanFontGuid = "561f4e50cf36d5a498aa8dd400160e55";

    [MenuItem("GuideChuna/UI 개편/② 상단 바 + 정보 창 만들기 (TrainingScene)")]
    public static void Build()
    {
        if (FindByName(BarName) != null) { Debug.LogWarning($"[TopBarSetup] '{BarName}'이 이미 있다 — 새로 만들지 않는다"); return; }
        var info = Object.FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
        if (info == null) { Debug.LogWarning("[TopBarSetup] InfoPanelController 없음 — TrainingScene을 열고 다시"); return; }
        var backplate = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(BackplateGuid));
        if (backplate == null) { Debug.LogWarning("[TopBarSetup] UISet 프리팹 없음"); return; }

        var so = new SerializedObject(info);
        Toggle tSkel = Ref<Toggle>(so, "skeletonToggle"), tVideo = Ref<Toggle>(so, "expertVideoToggle"), tResult = Ref<Toggle>(so, "resultToggle");
        Toggle tSet = Ref<Toggle>(so, "settingsToggle"), tLobby = Ref<Toggle>(so, "mainMenuToggle");
        GameObject pSkel = Ref<GameObject>(so, "skeletonPage"), pVideo = Ref<GameObject>(so, "expertVideoPage"), pResult = Ref<GameObject>(so, "resultPage");
        GameObject pMode = Ref<GameObject>(so, "modeSelectionPage"), exit = Ref<GameObject>(so, "exitConfirmPopup");
        if (tSkel == null || tVideo == null || tResult == null || pSkel == null || pVideo == null || pResult == null)
        { Debug.LogWarning("[TopBarSetup] InfoPanelController 배선(토글 · 페이지)이 비어 있다 — 중단"); return; }

        var oldCanvas = tSkel.GetComponentInParent<Canvas>(true);
        float canvasScale = oldCanvas != null ? oldCanvas.transform.lossyScale.x : 0.0005f;
        var legacyPanel = oldCanvas != null && oldCanvas.transform.parent != null ? oldCanvas.transform.parent.gameObject : null;   // «정보패널»(PointableCanvas)
        var parent = GameObject.Find("UI Group");

        Undo.SetCurrentGroupName("상단 바 + 정보 창 만들기");
        int group = Undo.GetCurrentGroup();

        // ── 상단 바 ──
        var toggles = new List<Toggle> { tSkel, tVideo, tResult, tSet, tLobby };
        toggles.RemoveAll(t => t == null);
        Vector2 btn = new Vector2(220, 150);
        float barW = 40 + toggles.Count * (btn.x + 16) + 40, barH = btn.y + 40;
        var bar = MakePlate(backplate, BarName, parent, barW, barH, canvasScale, out Transform barContent);
        Undo.AddComponent<TopBarPlacer>(bar);
        var row = new GameObject("버튼 줄", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(row, "row");
        row.transform.SetParent(barContent, false);
        var rrt = (RectTransform)row.transform; Stretch(rrt, 20);
        row.AddComponent<LayoutElement>().ignoreLayout = true;
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16; hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = true; hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        foreach (var t in toggles)
        {
            Undo.SetTransformParent(t.transform, row.transform, "토글 옮기기");
            var le = t.GetComponent<LayoutElement>() ?? Undo.AddComponent<LayoutElement>(t.gameObject);
            Undo.RecordObject(le, "크기"); le.ignoreLayout = false; le.preferredWidth = btn.x; le.preferredHeight = btn.y;
            t.transform.localScale = Vector3.one; t.transform.localRotation = Quaternion.identity;
            var lp = t.transform.localPosition; lp.z = 0; t.transform.localPosition = lp;
            if (t == tSkel || t == tVideo || t == tResult) { Undo.RecordObject(t, "그룹 빼기"); t.group = null; }
        }

        // ── 정보 창 3개 ──
        var arrangerGo = new GameObject(ArrangerName);
        Undo.RegisterCreatedObjectUndo(arrangerGo, "arranger");
        if (parent != null) arrangerGo.transform.SetParent(parent.transform, false);
        var arranger = Undo.AddComponent<InfoWindowArranger>(arrangerGo);
        var pages = new[] { pSkel, pVideo, pResult };
        var frames = new Transform[pages.Length];
        for (int i = 0; i < pages.Length; i++)
        {
            var prt = (RectTransform)pages[i].transform;
            Vector2 size = prt.rect.size;
            if (size.x < 50 || size.y < 50) size = new Vector2(1000, 600);   // 레이아웃이 아직 안 돈 경우
            var frame = MakePlate(backplate, "정보 창 - " + pages[i].name, parent, size.x + 32, size.y + 32, canvasScale, out Transform content);
            frames[i] = frame.transform;
            // ★프리팹 원래 부품인 페이지는 Unity가 옮기기를 조용히 거부한다 — 여기선 건드리지 않고 아래 RebuildLockedPages가 새로 만든다
            if (PrefabUtility.IsPartOfPrefabInstance(pages[i]) && !PrefabUtility.IsAddedGameObjectOverride(pages[i]))
            { Debug.Log($"[TopBarSetup] '{pages[i].name}'은 프리팹 부품 — 새 페이지로 만들어 옮긴다"); continue; }
            bool wasActive = pages[i].activeSelf;
            Undo.SetTransformParent(prt, content, "페이지 옮기기");
            var le = pages[i].GetComponent<LayoutElement>() ?? Undo.AddComponent<LayoutElement>(pages[i]);
            le.ignoreLayout = true;
            Undo.RecordObject(prt, "페이지 자리");
            Stretch(prt, 16);
            prt.localScale = Vector3.one; prt.localRotation = Quaternion.identity;
            pages[i].SetActive(wasActive);
            Debug.Log($"[TopBarSetup] 창 '{frame.name}' {size.x:0}×{size.y:0}px × {canvasScale} = {size.x * canvasScale:F2}×{size.y * canvasScale:F2}m");
        }

        var aso = new SerializedObject(arranger);
        var sl = aso.FindProperty("slots"); sl.arraySize = pages.Length;
        for (int i = 0; i < pages.Length; i++)
        {
            var e = sl.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("page").objectReferenceValue = pages[i];
            e.FindPropertyRelative("frame").objectReferenceValue = frames[i];
        }
        aso.FindProperty("legacyPanel").objectReferenceValue = legacyPanel;
        aso.FindProperty("modeSelectionPage").objectReferenceValue = pMode;
        aso.FindProperty("exitConfirmPopup").objectReferenceValue = exit;
        aso.ApplyModifiedPropertiesWithoutUndo();

        Undo.RecordObject(info, "펼침 배선");
        so.Update();
        so.FindProperty("windowArranger").objectReferenceValue = arranger;
        so.ApplyModifiedProperties();

        Undo.CollapseUndoOperations(group);
        RebuildLockedPages();   // 프리팹에 묶인 페이지(진행 및 결과) — 새로 만들고 참조를 돌린다
        EditorSceneManager.MarkSceneDirty(info.gameObject.scene);
        Debug.Log($"[TopBarSetup] 완료 — 상단 바 토글 {toggles.Count}개 옮김 · 정보 창 {pages.Length}개 · 옛 패널 '{(legacyPanel != null ? legacyPanel.name : "none")}' · 캔버스 배율 {canvasScale} · ★저장은 Ctrl+S (되돌리기 Ctrl+Z 한 번)");
    }

    /// <summary>
    /// 배치기(InfoWindowArranger) 슬롯 기준으로, 자기 창 밖에 있는 페이지를 창 안으로 다시 넣는다.
    /// 10-27 첫 조립 뒤 «진행 및 결과»만 옛 자리에 남아 있었다(오류 로그 없음 · 원인 미확정) — 그 보정용이다.
    /// </summary>
    [MenuItem("GuideChuna/UI 개편/② 정보 창 페이지 다시 넣기 (TrainingScene)")]
    public static void Reseat()
    {
        var arranger = Object.FindFirstObjectByType<InfoWindowArranger>(FindObjectsInactive.Include);
        if (arranger == null) { Debug.LogWarning("[TopBarSetup] InfoWindowArranger 없음"); return; }
        var so = new SerializedObject(arranger);
        var sl = so.FindProperty("slots");
        int moved = 0;
        for (int i = 0; i < sl.arraySize; i++)
        {
            var e = sl.GetArrayElementAtIndex(i);
            var page = e.FindPropertyRelative("page").objectReferenceValue as GameObject;
            var frame = e.FindPropertyRelative("frame").objectReferenceValue as Transform;
            if (page == null || frame == null) continue;
            var back = FindDeep(frame, "UIBackplate");
            if (back == null || page.transform.parent == back) { Debug.Log($"[TopBarSetup] '{page.name}' 제자리"); continue; }
            Debug.Log($"[TopBarSetup] '{page.name}' 부모 {page.transform.parent?.name} → {frame.name}/UIBackplate");
            if (PrefabUtility.IsPartOfPrefabInstance(page))
            {
                var near = PrefabUtility.GetNearestPrefabInstanceRoot(page);
                var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(page);
                bool added = PrefabUtility.IsAddedGameObjectOverride(page);
                Debug.Log($"[TopBarSetup] ★'{page.name}'은 프리팹 인스턴스의 일부다 — 가장 가까운 뿌리 '{near?.name}' · 바깥 뿌리 '{outer?.name}' · 추가된 오브젝트 {added} · 원본 {PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(page)}");
            }
            bool was = page.activeSelf;
            Undo.SetTransformParent(page.transform, back, "페이지 다시 넣기");
            var prt = (RectTransform)page.transform;
            Undo.RecordObject(prt, "페이지 자리");
            Stretch(prt, 16); prt.localScale = Vector3.one; prt.localRotation = Quaternion.identity;
            page.SetActive(was);
            Debug.Log($"[TopBarSetup] → 지금 부모 {page.transform.parent?.name}");
            moved++;
        }
        EditorSceneManager.MarkSceneDirty(arranger.gameObject.scene);
        Debug.Log($"[TopBarSetup] 다시 넣기 {moved}개");
    }

    /// <summary>
    /// 프리팹에 묶여 옮길 수 없는 페이지(«진행 및 결과» = UISet 타일 프리팹의 원래 부품)를 우회한다.
    /// 창 안에 같은 모양의 새 페이지를 만들고, 옛 페이지의 자식(옮길 수 있는 것)을 옮기고,
    /// 씬 안에서 옛 페이지(GameObject · RectTransform · Image)를 가리키던 참조를 전부 새 페이지로 돌린다(로그로 남긴다).
    /// 옛 페이지는 지우지 않는다(프리팹 부품이라 못 지운다) — 이름에 «옛 자리»를 붙이고 꺼 둔다.
    /// </summary>
    [MenuItem("GuideChuna/UI 개편/② 프리팹에 묶인 페이지 새로 만들어 옮기기 (TrainingScene)")]
    public static void RebuildLockedPages()
    {
        var arranger = Object.FindFirstObjectByType<InfoWindowArranger>(FindObjectsInactive.Include);
        if (arranger == null) { Debug.LogWarning("[TopBarSetup] InfoWindowArranger 없음"); return; }
        Undo.SetCurrentGroupName("묶인 페이지 옮기기");
        int group = Undo.GetCurrentGroup();
        var aso = new SerializedObject(arranger);
        var sl = aso.FindProperty("slots");
        for (int i = 0; i < sl.arraySize; i++)
        {
            var e = sl.GetArrayElementAtIndex(i);
            var old = e.FindPropertyRelative("page").objectReferenceValue as GameObject;
            var frame = e.FindPropertyRelative("frame").objectReferenceValue as Transform;
            if (old == null || frame == null) continue;
            var back = FindDeep(frame, "UIBackplate");
            if (back == null || old.transform.parent == back) continue;
            if (!PrefabUtility.IsPartOfPrefabInstance(old) || PrefabUtility.IsAddedGameObjectOverride(old)) continue;

            // 새 페이지 — 같은 이름 · 같은 Image 모양
            var neo = new GameObject(old.name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(neo, "새 페이지");
            neo.layer = old.layer;
            neo.transform.SetParent(back, false);
            var nrt = (RectTransform)neo.transform; Stretch(nrt, 16);
            neo.AddComponent<LayoutElement>().ignoreLayout = true;
            var oldImg = old.GetComponent<Image>();
            Image newImg = null;
            if (oldImg != null) { newImg = neo.AddComponent<Image>(); EditorUtility.CopySerialized(oldImg, newImg); }

            // 자식 옮기기(옮길 수 있는 것만)
            var kids = new List<Transform>();
            foreach (Transform c in old.transform) kids.Add(c);
            foreach (var c in kids)
            {
                if (PrefabUtility.IsPartOfPrefabInstance(c.gameObject) && !PrefabUtility.IsAddedGameObjectOverride(c.gameObject))
                { Debug.LogWarning($"[TopBarSetup] '{old.name}/{c.name}'도 프리팹 부품이라 못 옮긴다 — 옛 자리에 남김"); continue; }
                Undo.SetTransformParent(c, neo.transform, "자식 옮기기");
                Debug.Log($"[TopBarSetup] 자식 옮김 '{c.name}' → 부모 {c.parent.name}");
            }

            // 참조 돌리기 — 씬의 모든 컴포넌트에서 옛 페이지(GO · RT · Image)를 가리키는 칸
            int n = 0;
            foreach (var comp in Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (comp == null || comp.gameObject.scene != old.scene) continue;
                if (comp is Transform) continue;   // ★Transform의 m_Children/m_Father는 계층 자체다 — 건드리면 계층이 꼬인다(10-27에 한 번 밟음)
                var cso = new SerializedObject(comp);
                var it = cso.GetIterator();
                bool dirty = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var v = it.objectReferenceValue;
                    Object to = v == old ? neo : v == old.transform ? neo.transform : (oldImg != null && v == oldImg) ? (Object)newImg : null;
                    if (to == null || comp.gameObject == old) continue;
                    if (!dirty) Undo.RecordObject(comp, "참조 돌리기");
                    it.objectReferenceValue = to; dirty = true; n++;
                    Debug.Log($"[TopBarSetup] 참조 돌림 {comp.gameObject.name}.{comp.GetType().Name}.{it.propertyPath} → 새 '{neo.name}'");
                }
                if (dirty) cso.ApplyModifiedProperties();
            }
            neo.SetActive(old.activeSelf);
            Undo.RecordObject(old, "옛 페이지");
            old.name = old.name + " (옛 자리 · 비어 있음)";
            old.SetActive(false);
            Debug.Log($"[TopBarSetup] '{neo.name}' 새로 만들어 창에 넣음 · 참조 {n}개 돌림 · 옛 것은 '{old.name}'로 꺼 둠");
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(arranger.gameObject.scene);
    }

    /// <summary>10-27 사고 복구: 옛 부모(Background)의 m_Children에 잘못 들어간 새 페이지 Transform을 옛 페이지로 되돌린다.</summary>
    [MenuItem("GuideChuna/UI 개편/② (복구) 옛 부모 자식 목록 되돌리기")]
    public static void RepairChildrenList()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!t.name.EndsWith("(옛 자리 · 비어 있음)") || t.parent == null) continue;
            var parentSo = new SerializedObject(t.parent);
            var kids = parentSo.FindProperty("m_Children");
            bool hasOld = false;
            for (int i = 0; i < kids.arraySize; i++) if (kids.GetArrayElementAtIndex(i).objectReferenceValue == t) hasOld = true;
            for (int i = 0; i < kids.arraySize; i++)
            {
                var el = kids.GetArrayElementAtIndex(i);
                var v = el.objectReferenceValue as Transform;
                if (v == null || v.parent == t.parent) continue;   // 진짜 자식은 그대로
                Debug.Log($"[TopBarSetup] 복구: {t.parent.name}.m_Children[{i}] = '{v.name}'(실제 부모 {v.parent?.name}) → {(hasOld ? "제거" : "옛 페이지로")}");
                if (hasOld) kids.DeleteArrayElementAtIndex(i); else el.objectReferenceValue = t;
                break;
            }
            // 자식 목록에 옛 페이지가 아예 없으면 끝에 다시 넣는다(부모 참조 m_Father와 맞춘다)
            bool nowHas = false;
            for (int i = 0; i < kids.arraySize; i++) if (kids.GetArrayElementAtIndex(i).objectReferenceValue == t) nowHas = true;
            if (!nowHas)
            {
                kids.InsertArrayElementAtIndex(kids.arraySize);
                kids.GetArrayElementAtIndex(kids.arraySize - 1).objectReferenceValue = t;
                Debug.Log($"[TopBarSetup] 복구: {t.parent.name}.m_Children 끝에 '{t.name}' 다시 넣음 (크기 {kids.arraySize})");
            }
            parentSo.ApplyModifiedPropertiesWithoutUndo();
            string list = ""; foreach (Transform c in t.parent) list += c.name + " | ";
            Debug.Log($"[TopBarSetup] 복구 후 {t.parent.name} 자식: {list}");
        }
    }

    static GameObject MakePlate(GameObject backplate, string name, GameObject parent, float w, float h, float scale, out Transform content)
    {
        var root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, name);
        if (parent != null) root.transform.SetParent(parent.transform, false);
        var plate = (GameObject)PrefabUtility.InstantiatePrefab(backplate, root.transform);
        plate.name = "판(UISet)";
        var canvasRoot = (RectTransform)FindDeep(plate.transform, "CanvasRoot");
        canvasRoot.localScale = Vector3.one * scale;
        canvasRoot.sizeDelta = new Vector2(w, h);
        var back = (RectTransform)FindDeep(plate.transform, "UIBackplate");
        back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f); back.anchoredPosition = Vector2.zero; back.sizeDelta = new Vector2(w, h);
        content = back;
        return root;
    }

    static void Stretch(RectTransform rt, float margin)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(margin, margin); rt.offsetMax = new Vector2(-margin, -margin);
    }

    static T Ref<T>(SerializedObject so, string field) where T : Object
    {
        var p = so.FindProperty(field);
        return p != null ? p.objectReferenceValue as T : null;
    }

    static GameObject FindByName(string name)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name && t.parent != null && t.parent.name == "UI Group") return t.gameObject;
        return null;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
        return null;
    }
}
