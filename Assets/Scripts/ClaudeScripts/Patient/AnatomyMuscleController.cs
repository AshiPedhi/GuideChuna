using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 시나리오별 근육 오브젝트 활성화/비활성화 관리
/// 한쪽 모델에서 근육을 할당하면, 다른 모델에서 동일 경로로 자동 매칭
/// </summary>
public class AnatomyMuscleController : MonoBehaviour
{
    [Header("=== 모델 루트 ===")]
    [Tooltip("투시용 해부학 모델 루트")]
    [SerializeField] private Transform fluoroscopyModelRoot;
    [Tooltip("관찰용 해부학 모델 루트")]
    [SerializeField] private Transform observationModelRoot;

    [Header("=== 근육 그룹 ===")]
    [SerializeField] private List<MuscleGroup> muscleGroups = new List<MuscleGroup>();

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    private string currentScenario = "";

    [Serializable]
    public class MuscleGroup
    {
        [Tooltip("시나리오 이름 (ScenarioConfig.scenarioName과 일치)")]
        public string scenarioName;

        [Tooltip("활성화할 근육 오브젝트들 (한쪽 모델에서만 할당)")]
        public List<GameObject> muscleObjects = new List<GameObject>();

        [HideInInspector]
        public List<GameObject> mirroredObjects = new List<GameObject>();
    }

    /// <summary>
    /// 시나리오에 맞는 근육 그룹 활성화 (나머지 비활성화)
    /// </summary>
    public void ApplyScenario(string scenarioName)
    {
        if (string.IsNullOrEmpty(scenarioName)) return;

        currentScenario = scenarioName;

        foreach (var group in muscleGroups)
        {
            bool isActive = string.Equals(group.scenarioName, scenarioName, StringComparison.OrdinalIgnoreCase);
            SetGroupActive(group, isActive);
        }

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[AnatomyMuscleController] 시나리오 '{scenarioName}' 근육 그룹 적용</color>");
    }

    /// <summary>
    /// 미러링 캐시 구축 (Awake 또는 외부 호출)
    /// </summary>
    public void BuildMirrorCache()
    {
        if (fluoroscopyModelRoot == null || observationModelRoot == null)
        {
            ChunaLogger.LogWarning("[AnatomyMuscleController] 모델 루트가 할당되지 않았습니다!");
            return;
        }

        // 할당된 오브젝트가 어느 모델 하위인지 판별
        Transform sourceRoot = null;
        Transform targetRoot = null;

        foreach (var group in muscleGroups)
        {
            group.mirroredObjects.Clear();

            foreach (var obj in group.muscleObjects)
            {
                if (obj == null)
                {
                    group.mirroredObjects.Add(null);
                    continue;
                }

                // 소스/타겟 루트 결정
                if (sourceRoot == null)
                {
                    if (IsChildOf(obj.transform, fluoroscopyModelRoot))
                    {
                        sourceRoot = fluoroscopyModelRoot;
                        targetRoot = observationModelRoot;
                    }
                    else if (IsChildOf(obj.transform, observationModelRoot))
                    {
                        sourceRoot = observationModelRoot;
                        targetRoot = fluoroscopyModelRoot;
                    }
                }

                // 상대 경로로 반대쪽 모델에서 동일 오브젝트 탐색
                GameObject mirrored = FindMirroredObject(obj.transform, sourceRoot, targetRoot);
                group.mirroredObjects.Add(mirrored);

                if (mirrored == null && showDebugLogs)
                {
                    string path = GetRelativePath(obj.transform, sourceRoot);
                    ChunaLogger.LogWarning($"[AnatomyMuscleController] 미러 오브젝트 못 찾음: {path}");
                }
            }
        }

        if (showDebugLogs)
        {
            int total = 0, mirrored = 0;
            foreach (var g in muscleGroups)
            {
                total += g.muscleObjects.Count;
                foreach (var m in g.mirroredObjects)
                    if (m != null) mirrored++;
            }
            ChunaLogger.Log($"<color=cyan>[AnatomyMuscleController] 미러 캐시 구축 완료: {mirrored}/{total} 매칭</color>");
        }
    }

    private void Awake()
    {
        BuildMirrorCache();
    }

    private void SetGroupActive(MuscleGroup group, bool active)
    {
        foreach (var obj in group.muscleObjects)
        {
            if (obj != null) obj.SetActive(active);
        }
        foreach (var obj in group.mirroredObjects)
        {
            if (obj != null) obj.SetActive(active);
        }
    }

    private GameObject FindMirroredObject(Transform source, Transform sourceRoot, Transform targetRoot)
    {
        if (sourceRoot == null || targetRoot == null) return null;

        string relativePath = GetRelativePath(source, sourceRoot);
        if (string.IsNullOrEmpty(relativePath)) return null;

        Transform found = targetRoot.Find(relativePath);
        return found != null ? found.gameObject : null;
    }

    private string GetRelativePath(Transform child, Transform root)
    {
        if (child == root) return "";

        var parts = new List<string>();
        Transform current = child;
        while (current != null && current != root)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        if (current != root) return null; // root의 자식이 아님

        parts.Reverse();
        return string.Join("/", parts);
    }

    private bool IsChildOf(Transform child, Transform parent)
    {
        Transform current = child;
        while (current != null)
        {
            if (current == parent) return true;
            current = current.parent;
        }
        return false;
    }

    // === Public API ===

    public string CurrentScenario => currentScenario;

    public MuscleGroup FindGroup(string scenarioName)
    {
        return muscleGroups.Find(g =>
            string.Equals(g.scenarioName, scenarioName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 모든 근육 비활성화
    /// </summary>
    public void HideAll()
    {
        foreach (var group in muscleGroups)
            SetGroupActive(group, false);
    }
}
