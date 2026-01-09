#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;

/// <summary>
/// 핸드 포즈 CSV 데이터 편집 에디터 도구
/// - 리샘플링: 불균일한 프레임 간격을 균등 간격으로 재생성
/// - 트리밍: 필요한 각도/프레임 범위만 추출
/// - 스케일링: 각도 확대/축소 (피벗 기반)
/// - 변환: 회전, 오프셋 적용
/// - 프리셋: 시술별 자동 설정
/// </summary>
public class HandPoseResampler : EditorWindow
{
    // ===== 탭 관리 =====
    private enum EditorTab { Resample, Trim, Scale, Transform, Preset }
    private EditorTab currentTab = EditorTab.Resample;
    private string[] tabNames = { "리샘플링", "트리밍", "각도 스케일", "변환", "프리셋" };

    // ===== 공통 설정 =====
    private string sourceFilePath = "";
    private string outputFileName = "";
    private Vector2 scrollPosition;
    private bool isAnalyzed = false;

    // ===== 분석 결과 =====
    private List<FrameData> parsedFrames = new List<FrameData>();
    private int originalFrameCount = 0;
    private float totalDistance = 0f;
    private float totalAngle = 0f;
    private float avgFrameDistance = 0f;
    private float maxFrameDistance = 0f;
    private float minFrameDistance = 0f;

    // 피벗 분석 결과
    private Vector3 estimatedPivot = Vector3.zero;
    private float pivotBasedAngle = 0f;

    // ===== 리샘플링 설정 =====
    private int targetFrameCount = 100;
    private bool useDistanceBased = true;

    // ===== 트리밍 설정 =====
    private enum TrimMode { ByFrame, ByAngle }
    private TrimMode trimMode = TrimMode.ByAngle;
    private int trimStartFrame = 0;
    private int trimEndFrame = 100;
    private float trimStartAngle = 0f;
    private float trimEndAngle = 45f;

    // ===== 각도 스케일링 설정 =====
    private float originalAngle = 45f;
    private float targetAngle = 60f;
    private Vector3 customPivot = Vector3.zero;
    private bool useEstimatedPivot = true;
    private enum ScaleAxis { Auto, X, Y, Z }
    private ScaleAxis scaleAxis = ScaleAxis.Auto;

    // ===== 변환 설정 =====
    private Vector3 rotationOffset = Vector3.zero;
    private Vector3 positionOffset = Vector3.zero;
    private float uniformScale = 1f;

    // ===== 프리셋 =====
    private enum ProcedurePreset { Custom, LateralFlexion, HealthyRotation, AffectedRotation, Isometric }
    private ProcedurePreset selectedPreset = ProcedurePreset.Custom;

    [MenuItem("Tools/Chuna/Hand Pose Resampler")]
    public static void ShowWindow()
    {
        var window = GetWindow<HandPoseResampler>("Hand Pose Editor");
        window.minSize = new Vector2(500, 600);
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        DrawHeader();
        DrawFileSelection();
        DrawAnalyzeButton();

        if (isAnalyzed)
        {
            DrawAnalysisResult();
            EditorGUILayout.Space(15);

            // 탭 선택
            currentTab = (EditorTab)GUILayout.Toolbar((int)currentTab, tabNames);
            EditorGUILayout.Space(10);

            switch (currentTab)
            {
                case EditorTab.Resample:
                    DrawResampleTab();
                    break;
                case EditorTab.Trim:
                    DrawTrimTab();
                    break;
                case EditorTab.Scale:
                    DrawScaleTab();
                    break;
                case EditorTab.Transform:
                    DrawTransformTab();
                    break;
                case EditorTab.Preset:
                    DrawPresetTab();
                    break;
            }
        }

        EditorGUILayout.EndScrollView();
    }

    #region UI Drawing

    private void DrawHeader()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("핸드 포즈 CSV 에디터", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "녹화된 CSV 데이터를 편집합니다.\n" +
            "• 리샘플링: 프레임 간격 균등화\n" +
            "• 트리밍: 필요한 구간만 추출\n" +
            "• 스케일: 각도 확대/축소\n" +
            "• 변환: 회전/이동 적용",
            MessageType.Info);
        EditorGUILayout.Space(10);
    }

    private void DrawFileSelection()
    {
        EditorGUILayout.LabelField("소스 파일", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        sourceFilePath = EditorGUILayout.TextField(sourceFilePath);
        if (GUILayout.Button("찾기", GUILayout.Width(50)))
        {
            string initialPath = string.IsNullOrEmpty(sourceFilePath)
                ? Application.dataPath + "/Resources/HandPoseData"
                : Path.GetDirectoryName(sourceFilePath);

            string path = EditorUtility.OpenFilePanel("CSV 파일 선택", initialPath, "csv");
            if (!string.IsNullOrEmpty(path))
            {
                sourceFilePath = path;
                isAnalyzed = false;
                outputFileName = Path.GetFileNameWithoutExtension(path) + "_edited";
            }
        }
        EditorGUILayout.EndHorizontal();

        // 빠른 선택 버튼
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("빠른 선택:", GUILayout.Width(65));
        if (GUILayout.Button("측굴")) SelectResourceFile("측굴");
        if (GUILayout.Button("건측회전")) SelectResourceFile("건측회전");
        if (GUILayout.Button("환측회전")) SelectResourceFile("환측회전");
        if (GUILayout.Button("등척성운동")) SelectResourceFile("등척성운동");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
    }

    private void DrawAnalyzeButton()
    {
        GUI.enabled = !string.IsNullOrEmpty(sourceFilePath) && File.Exists(sourceFilePath);
        GUI.backgroundColor = isAnalyzed ? Color.gray : new Color(0.5f, 0.8f, 1f);
        if (GUILayout.Button(isAnalyzed ? "다시 분석" : "데이터 분석", GUILayout.Height(30)))
        {
            AnalyzeCSV();
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;
    }

    private void DrawAnalysisResult()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("분석 결과", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"프레임 수: {originalFrameCount}", GUILayout.Width(150));
        EditorGUILayout.LabelField($"총 거리: {totalDistance:F3} m", GUILayout.Width(150));
        EditorGUILayout.LabelField($"총 회전: {totalAngle:F1}°");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"피벗 기준 각도: {pivotBasedAngle:F1}°", GUILayout.Width(150));
        EditorGUILayout.LabelField($"추정 피벗: ({estimatedPivot.x:F2}, {estimatedPivot.y:F2}, {estimatedPivot.z:F2})");
        EditorGUILayout.EndHorizontal();

        // 균일도 표시
        float uniformity = avgFrameDistance > 0 ? (1f - (maxFrameDistance - minFrameDistance) / avgFrameDistance) * 100f : 0f;
        uniformity = Mathf.Clamp(uniformity, 0f, 100f);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"프레임 간격 균일도: {uniformity:F0}%", GUILayout.Width(180));
        if (uniformity < 70f)
        {
            EditorGUILayout.LabelField("← 리샘플링 권장", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawResampleTab()
    {
        EditorGUILayout.LabelField("리샘플링 설정", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("프레임 간격을 균등하게 재배치합니다.", MessageType.None);

        targetFrameCount = EditorGUILayout.IntSlider("목표 프레임 수", targetFrameCount, 30, 500);

        float newSpacing = totalDistance / Mathf.Max(1, targetFrameCount - 1);
        EditorGUILayout.LabelField($"  → 예상 균등 간격: {newSpacing:F4} m");

        useDistanceBased = EditorGUILayout.Toggle("거리 기반 (권장)", useDistanceBased);

        DrawOutputAndExecute("리샘플링", ExecuteResampling);
    }

    private void DrawTrimTab()
    {
        EditorGUILayout.LabelField("트리밍 설정", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("필요한 구간만 추출합니다.", MessageType.None);

        trimMode = (TrimMode)EditorGUILayout.EnumPopup("트리밍 기준", trimMode);

        if (trimMode == TrimMode.ByFrame)
        {
            EditorGUILayout.BeginHorizontal();
            trimStartFrame = EditorGUILayout.IntField("시작 프레임", trimStartFrame);
            trimEndFrame = EditorGUILayout.IntField("끝 프레임", trimEndFrame);
            EditorGUILayout.EndHorizontal();

            trimStartFrame = Mathf.Clamp(trimStartFrame, 0, originalFrameCount - 1);
            trimEndFrame = Mathf.Clamp(trimEndFrame, trimStartFrame + 1, originalFrameCount);

            EditorGUILayout.LabelField($"  → {trimEndFrame - trimStartFrame} 프레임 추출");
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            trimStartAngle = EditorGUILayout.FloatField("시작 각도 (°)", trimStartAngle);
            trimEndAngle = EditorGUILayout.FloatField("끝 각도 (°)", trimEndAngle);
            EditorGUILayout.EndHorizontal();

            trimStartAngle = Mathf.Clamp(trimStartAngle, 0f, pivotBasedAngle);
            trimEndAngle = Mathf.Clamp(trimEndAngle, trimStartAngle, pivotBasedAngle);

            // 예상 프레임 수 계산
            float ratio = (trimEndAngle - trimStartAngle) / Mathf.Max(1f, pivotBasedAngle);
            int estimatedFrames = Mathf.RoundToInt(originalFrameCount * ratio);
            EditorGUILayout.LabelField($"  → 약 {estimatedFrames} 프레임 추출 예상");
        }

        DrawOutputAndExecute("트리밍", ExecuteTrimming);
    }

    private void DrawScaleTab()
    {
        EditorGUILayout.LabelField("각도 스케일링 설정", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("피벗 기준으로 각도를 확대/축소합니다.\n손 모양은 유지되고 이동 경로만 조정됩니다.", MessageType.None);

        // 현재 각도 표시
        EditorGUILayout.LabelField($"현재 각도: {pivotBasedAngle:F1}°", EditorStyles.boldLabel);

        EditorGUILayout.Space(5);
        targetAngle = EditorGUILayout.FloatField("목표 각도 (°)", targetAngle);

        float scaleRatio = targetAngle / Mathf.Max(0.1f, pivotBasedAngle);
        EditorGUILayout.LabelField($"  → 스케일 비율: {scaleRatio:F2}x");

        if (scaleRatio > 1.5f)
        {
            EditorGUILayout.HelpBox("1.5배 이상 확대 시 동작이 부자연스러워질 수 있습니다.", MessageType.Warning);
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("피벗 설정", EditorStyles.boldLabel);

        useEstimatedPivot = EditorGUILayout.Toggle("자동 추정 피벗 사용", useEstimatedPivot);

        if (useEstimatedPivot)
        {
            EditorGUILayout.LabelField($"  추정 피벗: ({estimatedPivot.x:F3}, {estimatedPivot.y:F3}, {estimatedPivot.z:F3})");
        }
        else
        {
            customPivot = EditorGUILayout.Vector3Field("커스텀 피벗", customPivot);
        }

        scaleAxis = (ScaleAxis)EditorGUILayout.EnumPopup("스케일 축", scaleAxis);

        DrawOutputAndExecute("각도 스케일링", ExecuteScaling);
    }

    private void DrawTransformTab()
    {
        EditorGUILayout.LabelField("변환 설정", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("전체 데이터에 회전/이동/스케일을 적용합니다.", MessageType.None);

        rotationOffset = EditorGUILayout.Vector3Field("회전 오프셋 (°)", rotationOffset);
        positionOffset = EditorGUILayout.Vector3Field("위치 오프셋 (m)", positionOffset);
        uniformScale = EditorGUILayout.Slider("균등 스케일", uniformScale, 0.5f, 2f);

        if (rotationOffset != Vector3.zero || positionOffset != Vector3.zero || uniformScale != 1f)
        {
            EditorGUILayout.HelpBox(
                $"적용될 변환:\n" +
                $"• 회전: ({rotationOffset.x:F1}°, {rotationOffset.y:F1}°, {rotationOffset.z:F1}°)\n" +
                $"• 이동: ({positionOffset.x:F3}m, {positionOffset.y:F3}m, {positionOffset.z:F3}m)\n" +
                $"• 스케일: {uniformScale:F2}x",
                MessageType.Info);
        }

        DrawOutputAndExecute("변환 적용", ExecuteTransform);
    }

    private void DrawPresetTab()
    {
        EditorGUILayout.LabelField("시술별 프리셋", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("시술에 맞는 설정을 자동으로 적용합니다.", MessageType.None);

        selectedPreset = (ProcedurePreset)EditorGUILayout.EnumPopup("프리셋 선택", selectedPreset);

        EditorGUILayout.Space(10);

        // 프리셋별 설명
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        switch (selectedPreset)
        {
            case ProcedurePreset.LateralFlexion:
                EditorGUILayout.LabelField("측굴 (Lateral Flexion)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("• 목표 각도: 45°");
                EditorGUILayout.LabelField("• 목표 프레임: 100");
                EditorGUILayout.LabelField("• 스케일 축: Z (좌우)");
                break;
            case ProcedurePreset.HealthyRotation:
                EditorGUILayout.LabelField("건측회전 (Healthy Side Rotation)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("• 목표 각도: 60°");
                EditorGUILayout.LabelField("• 목표 프레임: 120");
                EditorGUILayout.LabelField("• 스케일 축: Y (상하)");
                break;
            case ProcedurePreset.AffectedRotation:
                EditorGUILayout.LabelField("환측회전 (Affected Side Rotation)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("• 목표 각도: 60°");
                EditorGUILayout.LabelField("• 목표 프레임: 120");
                EditorGUILayout.LabelField("• 스케일 축: Y (상하)");
                break;
            case ProcedurePreset.Isometric:
                EditorGUILayout.LabelField("등척성운동 (Isometric)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("• 목표 각도: 15°");
                EditorGUILayout.LabelField("• 목표 프레임: 60");
                EditorGUILayout.LabelField("• 스케일 축: Auto");
                break;
            default:
                EditorGUILayout.LabelField("사용자 정의", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("위 탭에서 직접 설정하세요.");
                break;
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        if (selectedPreset != ProcedurePreset.Custom)
        {
            if (GUILayout.Button("프리셋 설정 적용", GUILayout.Height(25)))
            {
                ApplyPreset(selectedPreset);
            }

            EditorGUILayout.Space(10);

            GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
            if (GUILayout.Button("프리셋으로 전체 처리 실행", GUILayout.Height(40)))
            {
                ExecutePresetProcessing();
            }
            GUI.backgroundColor = Color.white;
        }
    }

    private void DrawOutputAndExecute(string actionName, Action executeAction)
    {
        EditorGUILayout.Space(15);
        outputFileName = EditorGUILayout.TextField("출력 파일명", outputFileName);

        EditorGUILayout.Space(10);
        GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
        if (GUILayout.Button($"{actionName} 실행", GUILayout.Height(35)))
        {
            executeAction?.Invoke();
        }
        GUI.backgroundColor = Color.white;
    }

    #endregion

    #region File Operations

    private void SelectResourceFile(string fileName)
    {
        string resourcePath = Application.dataPath + "/Resources/HandPoseData/" + fileName + ".csv";
        if (File.Exists(resourcePath))
        {
            sourceFilePath = resourcePath;
            outputFileName = fileName + "_edited";
            isAnalyzed = false;
        }
        else
        {
            Debug.LogWarning($"파일을 찾을 수 없습니다: {resourcePath}");
        }
    }

    private void AnalyzeCSV()
    {
        try
        {
            parsedFrames = ParseCSV(sourceFilePath);

            if (parsedFrames.Count < 2)
            {
                EditorUtility.DisplayDialog("오류", "프레임이 2개 미만입니다.", "확인");
                return;
            }

            originalFrameCount = parsedFrames.Count;
            trimEndFrame = originalFrameCount;

            CalculateMetrics();
            EstimatePivotAndAngle();

            // 트리밍 기본값 설정
            trimEndAngle = pivotBasedAngle;
            originalAngle = pivotBasedAngle;
            targetAngle = pivotBasedAngle;

            isAnalyzed = true;
            Debug.Log($"<color=green>[HandPoseEditor] 분석 완료: {originalFrameCount} 프레임, 피벗 각도: {pivotBasedAngle:F1}°</color>");
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("파싱 오류", e.Message, "확인");
            Debug.LogError($"[HandPoseEditor] CSV 파싱 오류: {e}");
        }
    }

    private void CalculateMetrics()
    {
        totalDistance = 0f;
        totalAngle = 0f;
        List<float> distances = new List<float>();

        for (int i = 1; i < parsedFrames.Count; i++)
        {
            Vector3 prevPos = parsedFrames[i - 1].rightWristWorldPos;
            Vector3 currPos = parsedFrames[i].rightWristWorldPos;

            float dist = Vector3.Distance(prevPos, currPos);
            distances.Add(dist);
            totalDistance += dist;

            Quaternion prevRot = parsedFrames[i - 1].rightWristWorldRot;
            Quaternion currRot = parsedFrames[i].rightWristWorldRot;
            totalAngle += Quaternion.Angle(prevRot, currRot);
        }

        if (distances.Count > 0)
        {
            avgFrameDistance = distances.Average();
            minFrameDistance = distances.Min();
            maxFrameDistance = distances.Max();
        }
    }

    /// <summary>
    /// 피벗 위치와 총 각도 추정
    /// 첫 프레임과 마지막 프레임의 손목 위치를 기반으로 원호의 중심 추정
    /// </summary>
    private void EstimatePivotAndAngle()
    {
        if (parsedFrames.Count < 2) return;

        Vector3 startPos = parsedFrames[0].rightWristWorldPos;
        Vector3 endPos = parsedFrames[parsedFrames.Count - 1].rightWristWorldPos;
        Vector3 midPos = parsedFrames[parsedFrames.Count / 2].rightWristWorldPos;

        // 세 점을 지나는 원의 중심 추정 (간단한 방법)
        // 시작-중간, 중간-끝 선분의 수직이등분선 교점
        Vector3 mid1 = (startPos + midPos) / 2f;
        Vector3 mid2 = (midPos + endPos) / 2f;

        Vector3 dir1 = (midPos - startPos).normalized;
        Vector3 dir2 = (endPos - midPos).normalized;

        // 평면 법선 (대략적인 이동 평면)
        Vector3 planeNormal = Vector3.Cross(dir1, dir2).normalized;
        if (planeNormal.magnitude < 0.1f)
        {
            planeNormal = Vector3.up; // 거의 직선인 경우
        }

        // 수직 방향
        Vector3 perp1 = Vector3.Cross(dir1, planeNormal).normalized;
        Vector3 perp2 = Vector3.Cross(dir2, planeNormal).normalized;

        // 두 수직이등분선의 교점 찾기 (근사)
        // 간단히 시작점에서 일정 거리에 피벗이 있다고 가정
        float avgRadius = (Vector3.Distance(startPos, midPos) + Vector3.Distance(midPos, endPos)) / 2f;

        // 피벗은 시작점에서 이동 방향의 수직 방향으로 반지름만큼 떨어진 곳
        Vector3 moveDir = (endPos - startPos).normalized;
        Vector3 pivotDir = Vector3.Cross(moveDir, planeNormal).normalized;

        // 반지름 추정 (현의 길이와 호의 관계)
        float chordLength = Vector3.Distance(startPos, endPos);
        float estimatedRadius = totalDistance / 2f; // 단순화된 추정

        estimatedPivot = startPos + pivotDir * estimatedRadius;

        // 피벗 기준 각도 계산
        Vector3 startDir = (startPos - estimatedPivot).normalized;
        Vector3 endDir = (endPos - estimatedPivot).normalized;
        pivotBasedAngle = Vector3.Angle(startDir, endDir);

        // 각도가 너무 작으면 누적 회전 사용
        if (pivotBasedAngle < 5f)
        {
            pivotBasedAngle = totalAngle;
        }
    }

    #endregion

    #region Execute Operations

    private void ExecuteResampling()
    {
        if (parsedFrames.Count < 2) return;

        try
        {
            EditorUtility.DisplayProgressBar("리샘플링", "처리 중...", 0.1f);

            List<float> cumDist = new List<float> { 0f };
            for (int i = 1; i < parsedFrames.Count; i++)
            {
                float dist = Vector3.Distance(
                    parsedFrames[i - 1].rightWristWorldPos,
                    parsedFrames[i].rightWristWorldPos);
                cumDist.Add(cumDist[i - 1] + dist);
            }

            List<FrameData> result = new List<FrameData>();
            float spacing = totalDistance / (targetFrameCount - 1);

            for (int i = 0; i < targetFrameCount; i++)
            {
                float targetDist = i * spacing;
                int lowerIdx = FindLowerIndex(cumDist, targetDist);
                int upperIdx = Mathf.Min(lowerIdx + 1, parsedFrames.Count - 1);

                float t = GetInterpolationT(cumDist, lowerIdx, upperIdx, targetDist);
                result.Add(InterpolateFrames(parsedFrames[lowerIdx], parsedFrames[upperIdx], t, i));

                EditorUtility.DisplayProgressBar("리샘플링", $"프레임 {i + 1}/{targetFrameCount}", (float)i / targetFrameCount);
            }

            SaveFramesToCSV(result, "_resampled");
            EditorUtility.ClearProgressBar();

            ShowCompletionDialog("리샘플링", originalFrameCount, result.Count);
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"리샘플링 오류: {e}");
            EditorUtility.DisplayDialog("오류", e.Message, "확인");
        }
    }

    private void ExecuteTrimming()
    {
        if (parsedFrames.Count < 2) return;

        try
        {
            EditorUtility.DisplayProgressBar("트리밍", "처리 중...", 0.3f);

            List<FrameData> result = new List<FrameData>();

            if (trimMode == TrimMode.ByFrame)
            {
                for (int i = trimStartFrame; i < trimEndFrame && i < parsedFrames.Count; i++)
                {
                    var frame = CloneFrame(parsedFrames[i]);
                    frame.frameIndex = result.Count;
                    result.Add(frame);
                }
            }
            else // ByAngle
            {
                // 각 프레임의 피벗 기준 각도 계산
                Vector3 pivot = useEstimatedPivot ? estimatedPivot : customPivot;
                Vector3 startDir = (parsedFrames[0].rightWristWorldPos - pivot).normalized;

                for (int i = 0; i < parsedFrames.Count; i++)
                {
                    Vector3 currentDir = (parsedFrames[i].rightWristWorldPos - pivot).normalized;
                    float angle = Vector3.Angle(startDir, currentDir);

                    if (angle >= trimStartAngle && angle <= trimEndAngle)
                    {
                        var frame = CloneFrame(parsedFrames[i]);
                        frame.frameIndex = result.Count;
                        result.Add(frame);
                    }
                }
            }

            if (result.Count < 2)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("오류", "트리밍 결과 프레임이 부족합니다.", "확인");
                return;
            }

            SaveFramesToCSV(result, "_trimmed");
            EditorUtility.ClearProgressBar();

            ShowCompletionDialog("트리밍", originalFrameCount, result.Count);
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"트리밍 오류: {e}");
            EditorUtility.DisplayDialog("오류", e.Message, "확인");
        }
    }

    private void ExecuteScaling()
    {
        if (parsedFrames.Count < 2) return;

        try
        {
            EditorUtility.DisplayProgressBar("스케일링", "처리 중...", 0.3f);

            Vector3 pivot = useEstimatedPivot ? estimatedPivot : customPivot;
            float scaleRatio = targetAngle / Mathf.Max(0.1f, pivotBasedAngle);

            List<FrameData> result = new List<FrameData>();

            // 스케일 축 결정
            Vector3 axisVector = GetScaleAxisVector();

            for (int i = 0; i < parsedFrames.Count; i++)
            {
                var frame = CloneFrame(parsedFrames[i]);

                // 오른손 Wrist 위치 스케일링
                Vector3 fromPivot = frame.rightWristWorldPos - pivot;

                // 피벗 기준 각도 스케일링
                if (axisVector != Vector3.zero)
                {
                    // 특정 축 기준 회전 스케일링
                    Quaternion rotation = Quaternion.AngleAxis(
                        Vector3.SignedAngle(
                            (parsedFrames[0].rightWristWorldPos - pivot).normalized,
                            fromPivot.normalized,
                            axisVector) * (scaleRatio - 1f),
                        axisVector);
                    fromPivot = rotation * fromPivot;
                }
                else
                {
                    // 전체 방향으로 스케일링 (거리 기반)
                    fromPivot *= scaleRatio;
                }

                frame.rightWristWorldPos = pivot + fromPivot;

                // 조인트 World Position도 업데이트
                UpdateJointWorldPositions(frame, parsedFrames[i], scaleRatio, pivot, axisVector);

                frame.frameIndex = i;
                result.Add(frame);

                EditorUtility.DisplayProgressBar("스케일링", $"프레임 {i + 1}/{parsedFrames.Count}", (float)i / parsedFrames.Count);
            }

            SaveFramesToCSV(result, "_scaled");
            EditorUtility.ClearProgressBar();

            EditorUtility.DisplayDialog("완료",
                $"각도 스케일링 완료!\n\n" +
                $"원본 각도: {pivotBasedAngle:F1}°\n" +
                $"목표 각도: {targetAngle:F1}°\n" +
                $"스케일 비율: {scaleRatio:F2}x",
                "확인");
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"스케일링 오류: {e}");
            EditorUtility.DisplayDialog("오류", e.Message, "확인");
        }
    }

    private void ExecuteTransform()
    {
        if (parsedFrames.Count < 2) return;

        try
        {
            EditorUtility.DisplayProgressBar("변환", "처리 중...", 0.3f);

            Quaternion rotOffset = Quaternion.Euler(rotationOffset);
            List<FrameData> result = new List<FrameData>();

            for (int i = 0; i < parsedFrames.Count; i++)
            {
                var frame = CloneFrame(parsedFrames[i]);

                // World Position 변환
                frame.rightWristWorldPos = rotOffset * (frame.rightWristWorldPos * uniformScale) + positionOffset;
                frame.rightWristWorldRot = rotOffset * frame.rightWristWorldRot;

                // 모든 조인트 변환
                foreach (var joint in frame.rightJoints)
                {
                    if (joint.worldPosition != Vector3.zero)
                    {
                        joint.worldPosition = rotOffset * (joint.worldPosition * uniformScale) + positionOffset;
                        joint.worldRotation = rotOffset * joint.worldRotation;
                    }
                }

                foreach (var joint in frame.leftJoints)
                {
                    if (joint.worldPosition != Vector3.zero)
                    {
                        joint.worldPosition = rotOffset * (joint.worldPosition * uniformScale) + positionOffset;
                        joint.worldRotation = rotOffset * joint.worldRotation;
                    }
                }

                frame.frameIndex = i;
                result.Add(frame);
            }

            SaveFramesToCSV(result, "_transformed");
            EditorUtility.ClearProgressBar();

            ShowCompletionDialog("변환", originalFrameCount, result.Count);
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"변환 오류: {e}");
            EditorUtility.DisplayDialog("오류", e.Message, "확인");
        }
    }

    private void ApplyPreset(ProcedurePreset preset)
    {
        switch (preset)
        {
            case ProcedurePreset.LateralFlexion:
                targetAngle = 45f;
                targetFrameCount = 100;
                scaleAxis = ScaleAxis.Z;
                outputFileName = "측굴_processed";
                break;
            case ProcedurePreset.HealthyRotation:
                targetAngle = 60f;
                targetFrameCount = 120;
                scaleAxis = ScaleAxis.Y;
                outputFileName = "건측회전_processed";
                break;
            case ProcedurePreset.AffectedRotation:
                targetAngle = 60f;
                targetFrameCount = 120;
                scaleAxis = ScaleAxis.Y;
                outputFileName = "환측회전_processed";
                break;
            case ProcedurePreset.Isometric:
                targetAngle = 15f;
                targetFrameCount = 60;
                scaleAxis = ScaleAxis.Auto;
                outputFileName = "등척성운동_processed";
                break;
        }

        Debug.Log($"<color=cyan>[HandPoseEditor] 프리셋 적용: {preset}</color>");
    }

    private void ExecutePresetProcessing()
    {
        if (parsedFrames.Count < 2) return;

        ApplyPreset(selectedPreset);

        try
        {
            EditorUtility.DisplayProgressBar("프리셋 처리", "1/3: 각도 스케일링...", 0.1f);

            // 1. 각도 스케일링
            Vector3 pivot = estimatedPivot;
            float scaleRatio = targetAngle / Mathf.Max(0.1f, pivotBasedAngle);
            Vector3 axisVector = GetScaleAxisVector();

            List<FrameData> scaled = new List<FrameData>();
            for (int i = 0; i < parsedFrames.Count; i++)
            {
                var frame = CloneFrame(parsedFrames[i]);
                Vector3 fromPivot = frame.rightWristWorldPos - pivot;

                if (axisVector != Vector3.zero)
                {
                    Quaternion rotation = Quaternion.AngleAxis(
                        Vector3.SignedAngle(
                            (parsedFrames[0].rightWristWorldPos - pivot).normalized,
                            fromPivot.normalized,
                            axisVector) * (scaleRatio - 1f),
                        axisVector);
                    fromPivot = rotation * fromPivot;
                }

                frame.rightWristWorldPos = pivot + fromPivot;
                UpdateJointWorldPositions(frame, parsedFrames[i], scaleRatio, pivot, axisVector);
                scaled.Add(frame);
            }

            EditorUtility.DisplayProgressBar("프리셋 처리", "2/3: 리샘플링...", 0.5f);

            // 2. 리샘플링
            float scaledTotalDist = 0f;
            List<float> cumDist = new List<float> { 0f };
            for (int i = 1; i < scaled.Count; i++)
            {
                float dist = Vector3.Distance(scaled[i - 1].rightWristWorldPos, scaled[i].rightWristWorldPos);
                scaledTotalDist += dist;
                cumDist.Add(cumDist[i - 1] + dist);
            }

            List<FrameData> result = new List<FrameData>();
            float spacing = scaledTotalDist / (targetFrameCount - 1);

            for (int i = 0; i < targetFrameCount; i++)
            {
                float targetDist = i * spacing;
                int lowerIdx = FindLowerIndex(cumDist, targetDist);
                int upperIdx = Mathf.Min(lowerIdx + 1, scaled.Count - 1);
                float t = GetInterpolationT(cumDist, lowerIdx, upperIdx, targetDist);
                result.Add(InterpolateFrames(scaled[lowerIdx], scaled[upperIdx], t, i));
            }

            EditorUtility.DisplayProgressBar("프리셋 처리", "3/3: 저장 중...", 0.9f);

            SaveFramesToCSV(result, "_preset");
            EditorUtility.ClearProgressBar();

            EditorUtility.DisplayDialog("프리셋 처리 완료",
                $"처리 완료!\n\n" +
                $"원본: {originalFrameCount} 프레임, {pivotBasedAngle:F1}°\n" +
                $"결과: {result.Count} 프레임, {targetAngle:F1}°\n\n" +
                $"파일: {outputFileName}.csv",
                "확인");
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"프리셋 처리 오류: {e}");
            EditorUtility.DisplayDialog("오류", e.Message, "확인");
        }
    }

    #endregion

    #region Helper Methods

    private int FindLowerIndex(List<float> cumDist, float targetDist)
    {
        for (int j = 0; j < cumDist.Count - 1; j++)
        {
            if (cumDist[j] <= targetDist && cumDist[j + 1] >= targetDist)
                return j;
        }
        return cumDist.Count - 2;
    }

    private float GetInterpolationT(List<float> cumDist, int lowerIdx, int upperIdx, float targetDist)
    {
        float segStart = cumDist[lowerIdx];
        float segEnd = cumDist[upperIdx];
        float segLen = segEnd - segStart;
        return segLen > 0.0001f ? Mathf.Clamp01((targetDist - segStart) / segLen) : 0f;
    }

    private Vector3 GetScaleAxisVector()
    {
        switch (scaleAxis)
        {
            case ScaleAxis.X: return Vector3.right;
            case ScaleAxis.Y: return Vector3.up;
            case ScaleAxis.Z: return Vector3.forward;
            default: return Vector3.zero;
        }
    }

    private void UpdateJointWorldPositions(FrameData frame, FrameData original, float scaleRatio, Vector3 pivot, Vector3 axisVector)
    {
        // Wrist (jointId 1)의 이동량 계산
        Vector3 wristDelta = frame.rightWristWorldPos - original.rightWristWorldPos;

        foreach (var joint in frame.rightJoints)
        {
            if (joint.jointId == 1)
            {
                joint.worldPosition = frame.rightWristWorldPos;
            }
        }
    }

    private FrameData CloneFrame(FrameData source)
    {
        var clone = new FrameData
        {
            frameIndex = source.frameIndex,
            timestamp = source.timestamp,
            rightWristWorldPos = source.rightWristWorldPos,
            rightWristWorldRot = source.rightWristWorldRot,
            leftJoints = new List<JointData>(),
            rightJoints = new List<JointData>()
        };

        foreach (var j in source.leftJoints)
        {
            clone.leftJoints.Add(new JointData
            {
                jointId = j.jointId,
                localPosition = j.localPosition,
                localRotation = j.localRotation,
                worldPosition = j.worldPosition,
                worldRotation = j.worldRotation
            });
        }

        foreach (var j in source.rightJoints)
        {
            clone.rightJoints.Add(new JointData
            {
                jointId = j.jointId,
                localPosition = j.localPosition,
                localRotation = j.localRotation,
                worldPosition = j.worldPosition,
                worldRotation = j.worldRotation
            });
        }

        return clone;
    }

    private FrameData InterpolateFrames(FrameData a, FrameData b, float t, int newIndex)
    {
        var result = new FrameData
        {
            frameIndex = newIndex,
            timestamp = Mathf.Lerp(a.timestamp, b.timestamp, t),
            leftJoints = new List<JointData>(),
            rightJoints = new List<JointData>()
        };

        for (int j = 0; j < a.leftJoints.Count && j < b.leftJoints.Count; j++)
        {
            result.leftJoints.Add(InterpolateJoint(a.leftJoints[j], b.leftJoints[j], t));
        }

        for (int j = 0; j < a.rightJoints.Count && j < b.rightJoints.Count; j++)
        {
            result.rightJoints.Add(InterpolateJoint(a.rightJoints[j], b.rightJoints[j], t));
        }

        if (result.rightJoints.Count > 1)
        {
            result.rightWristWorldPos = result.rightJoints[1].worldPosition;
            result.rightWristWorldRot = result.rightJoints[1].worldRotation;
        }

        return result;
    }

    private JointData InterpolateJoint(JointData a, JointData b, float t)
    {
        return new JointData
        {
            jointId = a.jointId,
            localPosition = Vector3.Lerp(a.localPosition, b.localPosition, t),
            localRotation = Quaternion.Slerp(a.localRotation, b.localRotation, t),
            worldPosition = Vector3.Lerp(a.worldPosition, b.worldPosition, t),
            worldRotation = Quaternion.Slerp(a.worldRotation, b.worldRotation, t)
        };
    }

    private void SaveFramesToCSV(List<FrameData> frames, string suffix)
    {
        StringBuilder sb = new StringBuilder();
        CultureInfo inv = CultureInfo.InvariantCulture;

        sb.AppendLine("FrameIndex,HandType,JointID,LocalPosX,LocalPosY,LocalPosZ," +
                     "LocalRotX,LocalRotY,LocalRotZ,LocalRotW,Timestamp," +
                     "WorldPosX,WorldPosY,WorldPosZ,WorldRotX,WorldRotY,WorldRotZ,WorldRotW");

        foreach (var frame in frames)
        {
            WriteJointsToCSV(sb, frame.leftJoints, "Left", frame.frameIndex, frame.timestamp, inv);
            WriteJointsToCSV(sb, frame.rightJoints, "Right", frame.frameIndex, frame.timestamp, inv);
        }

        string finalName = outputFileName;
        if (!outputFileName.Contains(suffix.TrimStart('_')))
        {
            finalName = outputFileName + suffix;
        }

        string outputPath = Path.Combine(Path.GetDirectoryName(sourceFilePath), finalName + ".csv");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);

        Debug.Log($"<color=cyan>[HandPoseEditor] 저장: {outputPath}</color>");

        if (outputPath.StartsWith(Application.dataPath))
        {
            AssetDatabase.Refresh();
        }
    }

    private void WriteJointsToCSV(StringBuilder sb, List<JointData> joints, string handType, int frameIdx, float timestamp, CultureInfo inv)
    {
        foreach (var joint in joints)
        {
            bool hasWorld = joint.jointId == 1 && joint.worldPosition != Vector3.zero;

            sb.AppendFormat(inv,
                "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F3},{11},{12},{13},{14},{15},{16},{17}\n",
                frameIdx, handType, joint.jointId,
                joint.localPosition.x, joint.localPosition.y, joint.localPosition.z,
                joint.localRotation.x, joint.localRotation.y, joint.localRotation.z, joint.localRotation.w,
                timestamp,
                hasWorld ? joint.worldPosition.x.ToString("F4", inv) : "",
                hasWorld ? joint.worldPosition.y.ToString("F4", inv) : "",
                hasWorld ? joint.worldPosition.z.ToString("F4", inv) : "",
                hasWorld ? joint.worldRotation.x.ToString("F4", inv) : "",
                hasWorld ? joint.worldRotation.y.ToString("F4", inv) : "",
                hasWorld ? joint.worldRotation.z.ToString("F4", inv) : "",
                hasWorld ? joint.worldRotation.w.ToString("F4", inv) : ""
            );
        }
    }

    private void ShowCompletionDialog(string operation, int originalCount, int resultCount)
    {
        EditorUtility.DisplayDialog("완료",
            $"{operation} 완료!\n\n" +
            $"원본: {originalCount} 프레임\n" +
            $"결과: {resultCount} 프레임",
            "확인");
    }

    #endregion

    #region CSV Parsing

    private List<FrameData> ParseCSV(string filePath)
    {
        var frames = new Dictionary<int, FrameData>();
        var lines = File.ReadAllLines(filePath);

        if (lines.Length < 2)
            throw new Exception("CSV 파일이 비어있습니다.");

        string[] header = lines[0].Split(',');
        int frameIdxCol = Array.IndexOf(header, "FrameIndex");
        int handTypeCol = Array.IndexOf(header, "HandType");
        int jointIdCol = Array.IndexOf(header, "JointID");
        int localPosXCol = Array.IndexOf(header, "LocalPosX");
        int localRotXCol = Array.IndexOf(header, "LocalRotX");
        int timestampCol = Array.IndexOf(header, "Timestamp");
        int worldPosXCol = Array.FindIndex(header, h => h.Contains("WorldPosX") || h.Contains("RootPosX"));

        CultureInfo inv = CultureInfo.InvariantCulture;

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            string[] cols = lines[i].Split(',');
            if (cols.Length < 11) continue;

            int frameIndex = int.Parse(cols[frameIdxCol]);
            string handType = cols[handTypeCol];
            int jointId = int.Parse(cols[jointIdCol]);

            if (!frames.TryGetValue(frameIndex, out FrameData frame))
            {
                frame = new FrameData
                {
                    frameIndex = frameIndex,
                    timestamp = float.Parse(cols[timestampCol], inv),
                    leftJoints = new List<JointData>(),
                    rightJoints = new List<JointData>()
                };
                frames[frameIndex] = frame;
            }

            JointData joint = new JointData
            {
                jointId = jointId,
                localPosition = new Vector3(
                    float.Parse(cols[localPosXCol], inv),
                    float.Parse(cols[localPosXCol + 1], inv),
                    float.Parse(cols[localPosXCol + 2], inv)),
                localRotation = new Quaternion(
                    float.Parse(cols[localRotXCol], inv),
                    float.Parse(cols[localRotXCol + 1], inv),
                    float.Parse(cols[localRotXCol + 2], inv),
                    float.Parse(cols[localRotXCol + 3], inv))
            };

            if (worldPosXCol >= 0 && jointId == 1 && cols.Length > worldPosXCol + 6)
            {
                if (!string.IsNullOrEmpty(cols[worldPosXCol]))
                {
                    joint.worldPosition = new Vector3(
                        float.Parse(cols[worldPosXCol], inv),
                        float.Parse(cols[worldPosXCol + 1], inv),
                        float.Parse(cols[worldPosXCol + 2], inv));
                    joint.worldRotation = new Quaternion(
                        float.Parse(cols[worldPosXCol + 3], inv),
                        float.Parse(cols[worldPosXCol + 4], inv),
                        float.Parse(cols[worldPosXCol + 5], inv),
                        float.Parse(cols[worldPosXCol + 6], inv));

                    if (handType == "Right")
                    {
                        frame.rightWristWorldPos = joint.worldPosition;
                        frame.rightWristWorldRot = joint.worldRotation;
                    }
                }
            }

            if (handType == "Left")
                frame.leftJoints.Add(joint);
            else if (handType == "Right")
                frame.rightJoints.Add(joint);
        }

        return frames.Values.OrderBy(f => f.frameIndex).ToList();
    }

    #endregion

    #region Data Classes

    private class FrameData
    {
        public int frameIndex;
        public float timestamp;
        public List<JointData> leftJoints;
        public List<JointData> rightJoints;
        public Vector3 rightWristWorldPos;
        public Quaternion rightWristWorldRot;
    }

    private class JointData
    {
        public int jointId;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 worldPosition;
        public Quaternion worldRotation;
    }

    #endregion
}
#endif
