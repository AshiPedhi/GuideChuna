#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// 추나 시술별 기본 한계 데이터 프리셋 생성 에디터
/// </summary>
public class ChunaLimitDataCreator : EditorWindow
{
    [MenuItem("Tools/Chuna/Create Default Limit Data")]
    public static void CreateDefaultLimitData()
    {
        string folderPath = "Assets/Resources/ChunaLimitData";

        // 폴더 생성
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            string parentPath = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(parentPath))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
            AssetDatabase.CreateFolder(parentPath, "ChunaLimitData");
        }

        // 4가지 시술별 한계 데이터 생성
        CreateHealthySideRotationLimit(folderPath);
        CreateAffectedSideRotationLimit(folderPath);
        CreateIsometricExerciseLimit(folderPath);
        CreateLateralFlexionLimit(folderPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("<color=green>[ChunaLimitDataCreator] 4개의 기본 한계 데이터 프리셋이 생성되었습니다.</color>");
        Debug.Log($"경로: {folderPath}");
    }

    /// <summary>
    /// 건측회전 한계 데이터 생성
    /// </summary>
    private static void CreateHealthySideRotationLimit(string folderPath)
    {
        string path = $"{folderPath}/HealthySideRotation_Limit.asset";
        if (AssetDatabase.LoadAssetAtPath<ChunaLimitData>(path) != null)
        {
            Debug.Log("건측회전 한계 데이터가 이미 존재합니다.");
            return;
        }

        ChunaLimitData data = ScriptableObject.CreateInstance<ChunaLimitData>();

        // 시술 정보 설정 (SerializedObject 사용)
        SerializedObject so = new SerializedObject(data);

        so.FindProperty("procedureName").stringValue = "건측회전";
        so.FindProperty("procedureType").enumValueIndex = (int)ChunaType.HealthySideRotation;
        so.FindProperty("description").stringValue = "건강한 쪽으로 목을 회전하는 시술입니다. 회전 범위를 점진적으로 늘려갑니다.";

        // 목 회전 한계 (건측은 더 넓은 범위 허용)
        so.FindProperty("maxNeckFlexion").floatValue = 45f;
        so.FindProperty("maxNeckExtension").floatValue = 40f;
        so.FindProperty("maxNeckRotationLeft").floatValue = 70f;  // 건측이면 더 넓게
        so.FindProperty("maxNeckRotationRight").floatValue = 50f;
        so.FindProperty("maxNeckLateralFlexionLeft").floatValue = 40f;
        so.FindProperty("maxNeckLateralFlexionRight").floatValue = 35f;

        // 손목 한계
        so.FindProperty("maxWristFlexion").floatValue = 80f;
        so.FindProperty("maxWristExtension").floatValue = 70f;
        so.FindProperty("maxWristRadialDeviation").floatValue = 20f;
        so.FindProperty("maxWristUlnarDeviation").floatValue = 30f;
        so.FindProperty("maxWristPronation").floatValue = 80f;
        so.FindProperty("maxWristSupination").floatValue = 80f;

        // 손 위치 한계
        so.FindProperty("maxHandForwardDistance").floatValue = 0.4f;
        so.FindProperty("maxHandBackwardDistance").floatValue = 0.2f;
        so.FindProperty("maxHandLateralDistance").floatValue = 0.3f;
        so.FindProperty("maxHandVerticalDistance").floatValue = 0.3f;

        // 감점 설정
        so.FindProperty("minorViolationDeduction").floatValue = 1f;
        so.FindProperty("moderateViolationDeduction").floatValue = 5f;
        so.FindProperty("severeViolationDeduction").floatValue = 15f;
        so.FindProperty("dangerousViolationDeduction").floatValue = 30f;

        // 경고/되돌리기 설정
        so.FindProperty("warningThresholdRatio").floatValue = 0.8f;
        so.FindProperty("dangerThresholdRatio").floatValue = 0.95f;
        so.FindProperty("enableAutoRevert").boolValue = true;
        so.FindProperty("revertTriggerRatio").floatValue = 1.0f;
        so.FindProperty("revertTargetRatio").floatValue = 0.7f;
        so.FindProperty("revertLerpSpeed").floatValue = 3f;

        so.ApplyModifiedProperties();

        AssetDatabase.CreateAsset(data, path);
        Debug.Log($"생성됨: {path}");
    }

    /// <summary>
    /// 환측회전 한계 데이터 생성
    /// </summary>
    private static void CreateAffectedSideRotationLimit(string folderPath)
    {
        string path = $"{folderPath}/AffectedSideRotation_Limit.asset";
        if (AssetDatabase.LoadAssetAtPath<ChunaLimitData>(path) != null)
        {
            Debug.Log("환측회전 한계 데이터가 이미 존재합니다.");
            return;
        }

        ChunaLimitData data = ScriptableObject.CreateInstance<ChunaLimitData>();
        SerializedObject so = new SerializedObject(data);

        so.FindProperty("procedureName").stringValue = "환측회전";
        so.FindProperty("procedureType").enumValueIndex = (int)ChunaType.AffectedSideRotation;
        so.FindProperty("description").stringValue = "아픈 쪽으로 목을 회전하는 시술입니다. 통증을 유발하지 않는 범위 내에서 수행합니다.";

        // 목 회전 한계 (환측은 더 제한적)
        so.FindProperty("maxNeckFlexion").floatValue = 40f;
        so.FindProperty("maxNeckExtension").floatValue = 35f;
        so.FindProperty("maxNeckRotationLeft").floatValue = 45f;  // 환측이면 더 제한적
        so.FindProperty("maxNeckRotationRight").floatValue = 45f;
        so.FindProperty("maxNeckLateralFlexionLeft").floatValue = 30f;
        so.FindProperty("maxNeckLateralFlexionRight").floatValue = 30f;

        // 손목 한계
        so.FindProperty("maxWristFlexion").floatValue = 70f;
        so.FindProperty("maxWristExtension").floatValue = 60f;
        so.FindProperty("maxWristRadialDeviation").floatValue = 15f;
        so.FindProperty("maxWristUlnarDeviation").floatValue = 25f;
        so.FindProperty("maxWristPronation").floatValue = 70f;
        so.FindProperty("maxWristSupination").floatValue = 70f;

        // 손 위치 한계 (더 제한적)
        so.FindProperty("maxHandForwardDistance").floatValue = 0.3f;
        so.FindProperty("maxHandBackwardDistance").floatValue = 0.15f;
        so.FindProperty("maxHandLateralDistance").floatValue = 0.25f;
        so.FindProperty("maxHandVerticalDistance").floatValue = 0.25f;

        // 감점 설정 (더 엄격)
        so.FindProperty("minorViolationDeduction").floatValue = 2f;
        so.FindProperty("moderateViolationDeduction").floatValue = 8f;
        so.FindProperty("severeViolationDeduction").floatValue = 20f;
        so.FindProperty("dangerousViolationDeduction").floatValue = 40f;

        // 경고/되돌리기 설정 (더 민감)
        so.FindProperty("warningThresholdRatio").floatValue = 0.75f;
        so.FindProperty("dangerThresholdRatio").floatValue = 0.9f;
        so.FindProperty("enableAutoRevert").boolValue = true;
        so.FindProperty("revertTriggerRatio").floatValue = 0.95f;
        so.FindProperty("revertTargetRatio").floatValue = 0.6f;
        so.FindProperty("revertLerpSpeed").floatValue = 4f;

        so.ApplyModifiedProperties();

        AssetDatabase.CreateAsset(data, path);
        Debug.Log($"생성됨: {path}");
    }

    /// <summary>
    /// 등척성운동 한계 데이터 생성
    /// </summary>
    private static void CreateIsometricExerciseLimit(string folderPath)
    {
        string path = $"{folderPath}/IsometricExercise_Limit.asset";
        if (AssetDatabase.LoadAssetAtPath<ChunaLimitData>(path) != null)
        {
            Debug.Log("등척성운동 한계 데이터가 이미 존재합니다.");
            return;
        }

        ChunaLimitData data = ScriptableObject.CreateInstance<ChunaLimitData>();
        SerializedObject so = new SerializedObject(data);

        so.FindProperty("procedureName").stringValue = "등척성운동";
        so.FindProperty("procedureType").enumValueIndex = (int)ChunaType.IsometricExercise;
        so.FindProperty("description").stringValue = "움직임 없이 저항을 가하는 운동입니다. 위치는 고정하고 힘만 가합니다.";

        // 목 회전 한계 (등척성이므로 매우 제한적)
        so.FindProperty("maxNeckFlexion").floatValue = 10f;
        so.FindProperty("maxNeckExtension").floatValue = 10f;
        so.FindProperty("maxNeckRotationLeft").floatValue = 10f;
        so.FindProperty("maxNeckRotationRight").floatValue = 10f;
        so.FindProperty("maxNeckLateralFlexionLeft").floatValue = 10f;
        so.FindProperty("maxNeckLateralFlexionRight").floatValue = 10f;

        // 손목 한계 (손목도 고정)
        so.FindProperty("maxWristFlexion").floatValue = 30f;
        so.FindProperty("maxWristExtension").floatValue = 30f;
        so.FindProperty("maxWristRadialDeviation").floatValue = 10f;
        so.FindProperty("maxWristUlnarDeviation").floatValue = 10f;
        so.FindProperty("maxWristPronation").floatValue = 30f;
        so.FindProperty("maxWristSupination").floatValue = 30f;

        // 손 위치 한계 (최소 움직임)
        so.FindProperty("maxHandForwardDistance").floatValue = 0.05f;
        so.FindProperty("maxHandBackwardDistance").floatValue = 0.05f;
        so.FindProperty("maxHandLateralDistance").floatValue = 0.05f;
        so.FindProperty("maxHandVerticalDistance").floatValue = 0.05f;

        // 힘/압력 한계 (등척성 운동의 핵심)
        so.FindProperty("maxAppliedForce").floatValue = 50f;
        so.FindProperty("recommendedForceMin").floatValue = 15f;
        so.FindProperty("recommendedForceMax").floatValue = 35f;

        // 속도 한계 (등척성이므로 느리게)
        so.FindProperty("maxMovementSpeed").floatValue = 0.1f;
        so.FindProperty("maxRotationSpeed").floatValue = 15f;

        // 감점 설정 (움직임에 매우 엄격)
        so.FindProperty("minorViolationDeduction").floatValue = 3f;
        so.FindProperty("moderateViolationDeduction").floatValue = 10f;
        so.FindProperty("severeViolationDeduction").floatValue = 25f;
        so.FindProperty("dangerousViolationDeduction").floatValue = 50f;

        // 경고/되돌리기 설정 (매우 민감)
        so.FindProperty("warningThresholdRatio").floatValue = 0.7f;
        so.FindProperty("dangerThresholdRatio").floatValue = 0.85f;
        so.FindProperty("enableAutoRevert").boolValue = true;
        so.FindProperty("revertTriggerRatio").floatValue = 0.9f;
        so.FindProperty("revertTargetRatio").floatValue = 0.5f;
        so.FindProperty("revertLerpSpeed").floatValue = 5f;

        so.ApplyModifiedProperties();

        AssetDatabase.CreateAsset(data, path);
        Debug.Log($"생성됨: {path}");
    }

    /// <summary>
    /// 측굴 한계 데이터 생성
    /// </summary>
    private static void CreateLateralFlexionLimit(string folderPath)
    {
        string path = $"{folderPath}/LateralFlexion_Limit.asset";
        if (AssetDatabase.LoadAssetAtPath<ChunaLimitData>(path) != null)
        {
            Debug.Log("측굴 한계 데이터가 이미 존재합니다.");
            return;
        }

        ChunaLimitData data = ScriptableObject.CreateInstance<ChunaLimitData>();
        SerializedObject so = new SerializedObject(data);

        so.FindProperty("procedureName").stringValue = "측굴";
        so.FindProperty("procedureType").enumValueIndex = (int)ChunaType.LateralFlexion;
        so.FindProperty("description").stringValue = "목을 옆으로 굽히는 시술입니다. 어깨가 올라가지 않도록 주의합니다.";

        // 목 회전 한계 (측굴 중심)
        so.FindProperty("maxNeckFlexion").floatValue = 20f;
        so.FindProperty("maxNeckExtension").floatValue = 15f;
        so.FindProperty("maxNeckRotationLeft").floatValue = 20f;
        so.FindProperty("maxNeckRotationRight").floatValue = 20f;
        so.FindProperty("maxNeckLateralFlexionLeft").floatValue = 50f;  // 측굴 방향 넓게
        so.FindProperty("maxNeckLateralFlexionRight").floatValue = 50f;

        // 손목 한계
        so.FindProperty("maxWristFlexion").floatValue = 60f;
        so.FindProperty("maxWristExtension").floatValue = 50f;
        so.FindProperty("maxWristRadialDeviation").floatValue = 20f;
        so.FindProperty("maxWristUlnarDeviation").floatValue = 30f;
        so.FindProperty("maxWristPronation").floatValue = 60f;
        so.FindProperty("maxWristSupination").floatValue = 60f;

        // 손 위치 한계 (측면 이동 허용)
        so.FindProperty("maxHandForwardDistance").floatValue = 0.2f;
        so.FindProperty("maxHandBackwardDistance").floatValue = 0.1f;
        so.FindProperty("maxHandLateralDistance").floatValue = 0.4f;  // 측면 이동 넓게
        so.FindProperty("maxHandVerticalDistance").floatValue = 0.3f;

        // 감점 설정
        so.FindProperty("minorViolationDeduction").floatValue = 1.5f;
        so.FindProperty("moderateViolationDeduction").floatValue = 6f;
        so.FindProperty("severeViolationDeduction").floatValue = 18f;
        so.FindProperty("dangerousViolationDeduction").floatValue = 35f;

        // 경고/되돌리기 설정
        so.FindProperty("warningThresholdRatio").floatValue = 0.78f;
        so.FindProperty("dangerThresholdRatio").floatValue = 0.92f;
        so.FindProperty("enableAutoRevert").boolValue = true;
        so.FindProperty("revertTriggerRatio").floatValue = 1.0f;
        so.FindProperty("revertTargetRatio").floatValue = 0.65f;
        so.FindProperty("revertLerpSpeed").floatValue = 3.5f;

        so.ApplyModifiedProperties();

        AssetDatabase.CreateAsset(data, path);
        Debug.Log($"생성됨: {path}");
    }

    [MenuItem("Tools/Chuna/Open Limit Data Folder")]
    public static void OpenLimitDataFolder()
    {
        string folderPath = "Assets/Resources/ChunaLimitData";
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            Object folder = AssetDatabase.LoadAssetAtPath<Object>(folderPath);
            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }
        else
        {
            Debug.LogWarning("ChunaLimitData 폴더가 존재하지 않습니다. 먼저 'Create Default Limit Data'를 실행하세요.");
        }
    }
}
#endif
