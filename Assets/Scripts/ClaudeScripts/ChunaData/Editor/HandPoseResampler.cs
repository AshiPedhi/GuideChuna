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
/// - 미리보기: VR 없이 에디터에서 핸드 포즈 확인
/// </summary>
public class HandPoseResampler : EditorWindow
{
    // ===== 탭 관리 =====
    private enum EditorTab { Preview, Resample, Trim, Scale, Transform, Preset }
    private EditorTab currentTab = EditorTab.Preview;
    private string[] tabNames = { "미리보기", "리샘플링", "트리밍", "각도 스케일", "변환", "프리셋" };

    // ===== 미리보기 설정 =====
    private bool isPlaying = false;
    private float playbackSpeed = 1.0f;
    private int currentFrameIndex = 0;
    private double playbackStartTime = 0;
    private float playbackStartTimestamp = 0;
    private float previewScale = 1.0f;
    private Vector3 previewOffset = new Vector3(0, 1, 0);
    private bool showLeftHand = true;
    private bool showRightHand = true;
    private bool showBones = true;
    private bool showJoints = true;
    private float jointSize = 0.008f;
    private float boneThickness = 3f;
    private Color leftHandColor = new Color(0.2f, 0.6f, 1f, 1f);
    private Color rightHandColor = new Color(1f, 0.4f, 0.3f, 1f);
    private bool loopPlayback = true;

    // ===== 변환 미리보기 설정 =====
    private bool previewTransform = false;  // 변환 탭의 설정을 미리보기에 적용

    // ===== 환자 모델 따라가기 =====
    private bool followPatient = false;  // 환자 모델 위치 따라가기
    private Transform patientTransform = null;  // 환자 모델 Transform
    private Vector3 recordedPatientOffset = Vector3.zero;  // 녹화 시 환자 위치 오프셋

    // ===== 비교 모드 설정 =====
    private bool compareMode = false;
    private string compareFilePath = "";
    private List<FrameData> compareFrames = new List<FrameData>();
    private bool isCompareAnalyzed = false;
    private Color compareLeftColor = new Color(0.2f, 1f, 0.5f, 0.7f);
    private Color compareRightColor = new Color(1f, 0.8f, 0.2f, 0.7f);
    private Vector3 compareOffset = new Vector3(0.3f, 0, 0);
    private bool syncByFrame = true; // true: 프레임 동기화, false: 시간 동기화

    // 핸드 조인트 연결 정의 (parent → child)
    private static readonly int[][] fingerBones = new int[][]
    {
        // Thumb: 2→3→4→5
        new int[] { 2, 3, 4, 5 },
        // Index: 6→7→8→9→10
        new int[] { 6, 7, 8, 9, 10 },
        // Middle: 11→12→13→14→15
        new int[] { 11, 12, 13, 14, 15 },
        // Ring: 16→17→18→19→20
        new int[] { 16, 17, 18, 19, 20 },
        // Pinky: 21→22→23→24→25
        new int[] { 21, 22, 23, 24, 25 }
    };

    // 손목에서 각 손가락 시작점으로의 연결
    private static readonly int[] wristToFingerStart = new int[] { 2, 6, 11, 16, 21 };

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
        window.minSize = new Vector2(500, 700);
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= OnEditorUpdate;
        isPlaying = false;
    }

    private void OnEditorUpdate()
    {
        if (!isPlaying || parsedFrames.Count < 2) return;

        // 경과 시간 계산
        double currentTime = EditorApplication.timeSinceStartup;
        float elapsedTime = (float)(currentTime - playbackStartTime) * playbackSpeed;
        float targetTimestamp = playbackStartTimestamp + elapsedTime;

        float firstTimestamp = parsedFrames[0].timestamp;
        float lastTimestamp = parsedFrames[parsedFrames.Count - 1].timestamp;
        float totalDuration = lastTimestamp - firstTimestamp;

        // 루프 처리
        if (targetTimestamp > lastTimestamp)
        {
            if (loopPlayback)
            {
                // 처음부터 다시 시작
                playbackStartTime = currentTime;
                playbackStartTimestamp = firstTimestamp;
                currentFrameIndex = 0;
            }
            else
            {
                currentFrameIndex = parsedFrames.Count - 1;
                isPlaying = false;
            }
            SceneView.RepaintAll();
            Repaint();
            return;
        }

        // 현재 시간에 맞는 프레임 찾기 (이진 검색으로 최적화)
        int newFrameIndex = FindFrameAtTimestamp(targetTimestamp);

        // 프레임이 변경되었으면 업데이트
        if (newFrameIndex != currentFrameIndex)
        {
            currentFrameIndex = newFrameIndex;
            SceneView.RepaintAll();
            Repaint();
        }
    }

    /// <summary>
    /// 주어진 타임스탬프에 해당하는 프레임 인덱스를 찾습니다.
    /// 이진 검색을 사용하여 효율적으로 검색합니다.
    /// </summary>
    private int FindFrameAtTimestamp(float targetTimestamp)
    {
        if (parsedFrames.Count == 0) return 0;
        if (targetTimestamp <= parsedFrames[0].timestamp) return 0;
        if (targetTimestamp >= parsedFrames[parsedFrames.Count - 1].timestamp)
            return parsedFrames.Count - 1;

        int left = 0;
        int right = parsedFrames.Count - 1;

        while (left < right)
        {
            int mid = (left + right + 1) / 2;
            if (parsedFrames[mid].timestamp <= targetTimestamp)
            {
                left = mid;
            }
            else
            {
                right = mid - 1;
            }
        }

        return left;
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!isAnalyzed || parsedFrames.Count == 0) return;
        if (currentTab != EditorTab.Preview) return;

        FrameData frame = parsedFrames[Mathf.Clamp(currentFrameIndex, 0, parsedFrames.Count - 1)];

        // 메인 데이터 - 왼손 그리기
        if (showLeftHand && frame.leftJoints.Count > 0)
        {
            DrawHand(frame.leftJoints, leftHandColor, true, Vector3.zero);
        }

        // 메인 데이터 - 오른손 그리기
        if (showRightHand && frame.rightJoints.Count > 0)
        {
            DrawHand(frame.rightJoints, rightHandColor, false, Vector3.zero);
        }

        // 비교 모드: 두 번째 데이터셋 그리기
        if (compareMode && isCompareAnalyzed && compareFrames.Count > 0)
        {
            int compareIndex = GetCompareFrameIndex();
            FrameData compareFrame = compareFrames[Mathf.Clamp(compareIndex, 0, compareFrames.Count - 1)];

            // 비교 데이터 - 왼손 그리기
            if (showLeftHand && compareFrame.leftJoints.Count > 0)
            {
                DrawHand(compareFrame.leftJoints, compareLeftColor, true, compareOffset);
            }

            // 비교 데이터 - 오른손 그리기
            if (showRightHand && compareFrame.rightJoints.Count > 0)
            {
                DrawHand(compareFrame.rightJoints, compareRightColor, false, compareOffset);
            }
        }

        // 프레임 정보 표시
        Handles.BeginGUI();
        float infoHeight = compareMode && isCompareAnalyzed ? 110 : 80;
        GUILayout.BeginArea(new Rect(10, 10, 220, infoHeight));
        GUI.backgroundColor = new Color(0, 0, 0, 0.7f);
        GUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label($"Frame: {currentFrameIndex + 1}/{parsedFrames.Count}", EditorStyles.whiteLabel);
        GUILayout.Label($"Time: {frame.timestamp:F2}s", EditorStyles.whiteLabel);
        GUILayout.Label(isPlaying ? "▶ Playing" : "⏸ Paused", EditorStyles.whiteLabel);

        if (compareMode && isCompareAnalyzed)
        {
            int compareIndex = GetCompareFrameIndex();
            GUILayout.Label($"Compare: {compareIndex + 1}/{compareFrames.Count}", EditorStyles.whiteLabel);
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    private int GetCompareFrameIndex()
    {
        if (compareFrames.Count == 0) return 0;

        if (syncByFrame)
        {
            // 프레임 인덱스 동기화 (비율 기반)
            float ratio = (float)currentFrameIndex / Mathf.Max(1, parsedFrames.Count - 1);
            return Mathf.RoundToInt(ratio * (compareFrames.Count - 1));
        }
        else
        {
            // 시간 동기화
            float currentTime = parsedFrames[currentFrameIndex].timestamp;
            for (int i = 0; i < compareFrames.Count; i++)
            {
                if (compareFrames[i].timestamp >= currentTime)
                {
                    return i;
                }
            }
            return compareFrames.Count - 1;
        }
    }

    private void DrawHand(List<JointData> joints, Color color, bool isLeft, Vector3 additionalOffset)
    {
        if (joints.Count < 26) return;

        // 조인트 위치 계산 (로컬 → 월드)
        Vector3[] worldPositions = CalculateWorldPositions(joints, isLeft, additionalOffset);

        Handles.color = color;

        // 조인트 그리기
        if (showJoints)
        {
            for (int i = 0; i < worldPositions.Length; i++)
            {
                Handles.SphereHandleCap(0, worldPositions[i], Quaternion.identity, jointSize * previewScale, EventType.Repaint);
            }
        }

        // 본(뼈) 그리기
        if (showBones)
        {
            // 손목에서 각 손가락 시작점으로
            foreach (int fingerStart in wristToFingerStart)
            {
                if (fingerStart < worldPositions.Length)
                {
                    Handles.DrawLine(worldPositions[0], worldPositions[fingerStart], boneThickness);
                }
            }

            // 각 손가락 본
            foreach (int[] finger in fingerBones)
            {
                for (int i = 0; i < finger.Length - 1; i++)
                {
                    int from = finger[i];
                    int to = finger[i + 1];
                    if (from < worldPositions.Length && to < worldPositions.Length)
                    {
                        Handles.DrawLine(worldPositions[from], worldPositions[to], boneThickness);
                    }
                }
            }
        }
    }

    private Vector3[] CalculateWorldPositions(List<JointData> joints, bool isLeft, Vector3 additionalOffset)
    {
        Vector3[] positions = new Vector3[joints.Count];

        // 변환 탭 미리보기가 활성화된 경우, 변환 설정을 먼저 적용
        Quaternion transformRotation = previewTransform ? Quaternion.Euler(rotationOffset) : Quaternion.identity;
        Vector3 transformOffset = previewTransform ? positionOffset : Vector3.zero;
        float transformScale = previewTransform ? uniformScale : 1f;

        // 환자 모델 위치 따라가기
        Vector3 patientOffset = Vector3.zero;
        if (followPatient && patientTransform != null)
        {
            patientOffset = patientTransform.position - recordedPatientOffset;
        }

        // Joint 1에 월드 위치가 있으면 사용, 없으면 previewOffset 사용
        Vector3 totalOffset = previewOffset + additionalOffset + patientOffset;
        Vector3 rootPos = totalOffset;
        Quaternion rootRot = Quaternion.identity;

        var wristJoint = joints.FirstOrDefault(j => j.jointId == 1);
        if (wristJoint != null && wristJoint.worldPosition != Vector3.zero)
        {
            // 변환 설정이 활성화되면 월드 위치에 변환 적용
            Vector3 transformedWorldPos = wristJoint.worldPosition;
            Quaternion transformedWorldRot = wristJoint.worldRotation;

            if (previewTransform)
            {
                transformedWorldPos = transformRotation * (transformedWorldPos * transformScale) + transformOffset;
                transformedWorldRot = transformRotation * transformedWorldRot;
            }

            rootPos = transformedWorldPos * previewScale + totalOffset;
            rootRot = transformedWorldRot;
        }

        // 각 조인트의 월드 위치 계산
        // 간단한 방법: 로컬 포지션을 누적하여 월드 위치 계산
        Dictionary<int, Vector3> calculatedPos = new Dictionary<int, Vector3>();
        Dictionary<int, Quaternion> calculatedRot = new Dictionary<int, Quaternion>();

        // Joint 0 (WristRoot)
        var joint0 = joints.FirstOrDefault(j => j.jointId == 0);
        if (joint0 != null)
        {
            calculatedPos[0] = rootPos;
            calculatedRot[0] = rootRot;
        }

        // 손가락별로 계산
        CalculateFingerPositions(joints, calculatedPos, calculatedRot, new int[] { 2, 3, 4, 5 }, 0, totalOffset); // Thumb
        CalculateFingerPositions(joints, calculatedPos, calculatedRot, new int[] { 6, 7, 8, 9, 10 }, 0, totalOffset); // Index
        CalculateFingerPositions(joints, calculatedPos, calculatedRot, new int[] { 11, 12, 13, 14, 15 }, 0, totalOffset); // Middle
        CalculateFingerPositions(joints, calculatedPos, calculatedRot, new int[] { 16, 17, 18, 19, 20 }, 0, totalOffset); // Ring
        CalculateFingerPositions(joints, calculatedPos, calculatedRot, new int[] { 21, 22, 23, 24, 25 }, 0, totalOffset); // Pinky

        // 결과 배열에 복사
        for (int i = 0; i < joints.Count; i++)
        {
            int jointId = joints[i].jointId;
            if (calculatedPos.ContainsKey(jointId))
            {
                positions[i] = calculatedPos[jointId];
            }
            else
            {
                positions[i] = rootPos + joints[i].localPosition * previewScale;
            }
        }

        return positions;
    }

    private void CalculateFingerPositions(List<JointData> joints, Dictionary<int, Vector3> positions,
        Dictionary<int, Quaternion> rotations, int[] fingerJoints, int parentId, Vector3 totalOffset)
    {
        int currentParent = parentId;

        foreach (int jointId in fingerJoints)
        {
            var joint = joints.FirstOrDefault(j => j.jointId == jointId);
            if (joint == null) continue;

            Vector3 parentPos = positions.ContainsKey(currentParent) ? positions[currentParent] : totalOffset;
            Quaternion parentRot = rotations.ContainsKey(currentParent) ? rotations[currentParent] : Quaternion.identity;

            Vector3 worldPos = parentPos + parentRot * (joint.localPosition * previewScale);
            Quaternion worldRot = parentRot * joint.localRotation;

            positions[jointId] = worldPos;
            rotations[jointId] = worldRot;

            currentParent = jointId;
        }
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
        }

        EditorGUILayout.Space(15);

        // 탭 선택 (항상 표시)
        currentTab = (EditorTab)GUILayout.Toolbar((int)currentTab, tabNames);
        EditorGUILayout.Space(10);

        // 미리보기 탭은 분석 전에도 접근 가능 (데이터 없으면 안내 메시지)
        if (currentTab == EditorTab.Preview)
        {
            DrawPreviewTab();
        }
        else if (isAnalyzed)
        {
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
        else
        {
            EditorGUILayout.HelpBox("CSV 파일을 선택하고 '데이터 분석' 버튼을 눌러주세요.", MessageType.Warning);
        }

        EditorGUILayout.EndScrollView();
    }

    #region UI Drawing

    private void DrawPreviewTab()
    {
        EditorGUILayout.LabelField("핸드 포즈 미리보기", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "VR 없이 에디터에서 핸드 포즈를 확인합니다.\n" +
            "Scene 뷰에서 핸드 스켈레톤을 볼 수 있습니다.\n" +
            "키보드: Space=재생/정지, ←→=프레임 이동",
            MessageType.Info);

        // 데이터가 없으면 안내 메시지
        if (!isAnalyzed || parsedFrames.Count == 0)
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "미리보기를 사용하려면:\n" +
                "1. 위에서 CSV 파일을 선택하세요\n" +
                "2. '데이터 분석' 버튼을 클릭하세요\n" +
                "3. Scene 뷰를 열어두세요",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.Space(10);

        // ===== 재생 컨트롤 =====
        EditorGUILayout.LabelField("재생 컨트롤", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // 재생/정지 버튼
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
        if (GUILayout.Button(isPlaying ? "⏸ 정지" : "▶ 재생", GUILayout.Height(35)))
        {
            TogglePlayback();
        }

        GUI.backgroundColor = new Color(0.8f, 0.8f, 0.3f);
        if (GUILayout.Button("⏮ 처음", GUILayout.Height(35), GUILayout.Width(60)))
        {
            currentFrameIndex = 0;
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("⏭ 끝", GUILayout.Height(35), GUILayout.Width(60)))
        {
            currentFrameIndex = Mathf.Max(0, parsedFrames.Count - 1);
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // 타임라인 슬라이더
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("프레임:", GUILayout.Width(50));
        int newFrame = EditorGUILayout.IntSlider(currentFrameIndex, 0, Mathf.Max(0, parsedFrames.Count - 1));
        if (newFrame != currentFrameIndex)
        {
            currentFrameIndex = newFrame;
            SceneView.RepaintAll();
        }
        EditorGUILayout.EndHorizontal();

        // 시간 표시
        if (parsedFrames.Count > 0 && currentFrameIndex < parsedFrames.Count)
        {
            float currentTime = parsedFrames[currentFrameIndex].timestamp;
            float totalTime = parsedFrames[parsedFrames.Count - 1].timestamp;
            EditorGUILayout.LabelField($"시간: {currentTime:F2}s / {totalTime:F2}s");

            // 프로그레스 바
            Rect progressRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(8));
            EditorGUI.DrawRect(progressRect, new Color(0.2f, 0.2f, 0.2f));
            float progress = totalTime > 0 ? currentTime / totalTime : 0;
            progressRect.width *= progress;
            EditorGUI.DrawRect(progressRect, new Color(0.3f, 0.7f, 1f));
        }

        EditorGUILayout.Space(5);

        // 재생 옵션
        EditorGUILayout.BeginHorizontal();
        playbackSpeed = EditorGUILayout.Slider("재생 속도", playbackSpeed, 0.1f, 3f);
        EditorGUILayout.EndHorizontal();

        loopPlayback = EditorGUILayout.Toggle("반복 재생", loopPlayback);

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // ===== 표시 설정 =====
        EditorGUILayout.LabelField("표시 설정", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        showLeftHand = EditorGUILayout.Toggle("왼손", showLeftHand, GUILayout.Width(100));
        leftHandColor = EditorGUILayout.ColorField(leftHandColor);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        showRightHand = EditorGUILayout.Toggle("오른손", showRightHand, GUILayout.Width(100));
        rightHandColor = EditorGUILayout.ColorField(rightHandColor);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        showJoints = EditorGUILayout.Toggle("조인트 표시", showJoints);
        showBones = EditorGUILayout.Toggle("본(뼈) 표시", showBones);

        if (showJoints)
        {
            jointSize = EditorGUILayout.Slider("조인트 크기", jointSize, 0.002f, 0.03f);
        }
        if (showBones)
        {
            boneThickness = EditorGUILayout.Slider("본 두께", boneThickness, 1f, 10f);
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // ===== 비교 모드 =====
        EditorGUILayout.LabelField("비교 모드", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        compareMode = EditorGUILayout.Toggle("비교 모드 활성화", compareMode);

        if (compareMode)
        {
            EditorGUILayout.Space(5);

            // 비교 파일 선택
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("비교 파일:", GUILayout.Width(60));
            compareFilePath = EditorGUILayout.TextField(compareFilePath);
            if (GUILayout.Button("찾기", GUILayout.Width(50)))
            {
                string initialPath = string.IsNullOrEmpty(compareFilePath)
                    ? Application.dataPath + "/Resources/HandPoseData"
                    : Path.GetDirectoryName(compareFilePath);

                string path = EditorUtility.OpenFilePanel("비교할 CSV 파일 선택", initialPath, "csv");
                if (!string.IsNullOrEmpty(path))
                {
                    compareFilePath = path;
                    isCompareAnalyzed = false;
                }
            }
            EditorGUILayout.EndHorizontal();

            // 빠른 선택 버튼
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("빠른 선택:", GUILayout.Width(60));
            if (GUILayout.Button("측굴", GUILayout.Width(55))) SelectCompareFile("측굴");
            if (GUILayout.Button("건측회전", GUILayout.Width(60))) SelectCompareFile("건측회전");
            if (GUILayout.Button("환측회전", GUILayout.Width(60))) SelectCompareFile("환측회전");
            if (GUILayout.Button("등척성", GUILayout.Width(50))) SelectCompareFile("등척성운동");
            EditorGUILayout.EndHorizontal();

            // 분석 버튼
            GUI.enabled = !string.IsNullOrEmpty(compareFilePath) && File.Exists(compareFilePath);
            GUI.backgroundColor = isCompareAnalyzed ? Color.gray : new Color(0.5f, 0.8f, 1f);
            if (GUILayout.Button(isCompareAnalyzed ? "비교 데이터 로드됨" : "비교 데이터 로드", GUILayout.Height(25)))
            {
                LoadCompareData();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            if (isCompareAnalyzed)
            {
                EditorGUILayout.LabelField($"  → {compareFrames.Count} 프레임 로드됨", EditorStyles.miniLabel);

                EditorGUILayout.Space(5);

                // 동기화 방식
                syncByFrame = EditorGUILayout.Toggle("프레임 동기화", syncByFrame);
                EditorGUILayout.LabelField(syncByFrame ? "  (프레임 비율로 동기화)" : "  (타임스탬프로 동기화)", EditorStyles.miniLabel);

                EditorGUILayout.Space(5);

                // 비교 데이터 색상
                EditorGUILayout.LabelField("비교 데이터 색상:", EditorStyles.miniLabel);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("왼손", GUILayout.Width(40));
                compareLeftColor = EditorGUILayout.ColorField(compareLeftColor);
                EditorGUILayout.LabelField("오른손", GUILayout.Width(45));
                compareRightColor = EditorGUILayout.ColorField(compareRightColor);
                EditorGUILayout.EndHorizontal();

                // 비교 오프셋
                compareOffset = EditorGUILayout.Vector3Field("비교 위치 오프셋", compareOffset);
            }
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // ===== 변환 설정 =====
        EditorGUILayout.LabelField("미리보기 변환", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        previewScale = EditorGUILayout.Slider("스케일", previewScale, 0.1f, 5f);
        previewOffset = EditorGUILayout.Vector3Field("오프셋", previewOffset);

        EditorGUILayout.Space(5);

        // 변환 탭 설정 미리보기 토글
        previewTransform = EditorGUILayout.Toggle("변환 탭 설정 미리보기", previewTransform);

        if (previewTransform)
        {
            EditorGUILayout.HelpBox(
                $"변환 탭 설정이 적용됩니다:\n" +
                $"• 회전: ({rotationOffset.x:F1}°, {rotationOffset.y:F1}°, {rotationOffset.z:F1}°)\n" +
                $"• 위치: ({positionOffset.x:F3}m, {positionOffset.y:F3}m, {positionOffset.z:F3}m)\n" +
                $"• 스케일: {uniformScale:F2}x",
                MessageType.Info);
        }

        EditorGUILayout.Space(5);

        // ===== 환자 모델 따라가기 =====
        EditorGUILayout.LabelField("환자 모델 따라가기", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        followPatient = EditorGUILayout.Toggle("환자 위치 추적", followPatient);

        if (GUILayout.Button("환자 찾기", GUILayout.Width(70)))
        {
            FindPatientInScene();
        }
        EditorGUILayout.EndHorizontal();

        if (followPatient)
        {
            if (patientTransform != null)
            {
                EditorGUILayout.LabelField($"  → {patientTransform.name} (위치: {patientTransform.position.x:F2}, {patientTransform.position.y:F2}, {patientTransform.position.z:F2})", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("환자 모델을 찾을 수 없습니다.\n'환자 찾기' 버튼을 클릭하거나\nScene에 'Patient' 태그 오브젝트가 있는지 확인하세요.", MessageType.Warning);
            }

            recordedPatientOffset = EditorGUILayout.Vector3Field("녹화 시 환자 오프셋", recordedPatientOffset);
            EditorGUILayout.LabelField("  (녹화 당시 환자 위치, 보통 0,0,0)", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(5);

        if (GUILayout.Button("Scene 뷰로 포커스"))
        {
            FocusSceneViewOnHands();
        }

        EditorGUILayout.EndVertical();

        // Scene 뷰 업데이트 요청
        if (GUI.changed)
        {
            SceneView.RepaintAll();
        }

        // 키보드 단축키 처리
        HandleKeyboardShortcuts();
    }

    private void SelectCompareFile(string fileName)
    {
        string resourcePath = Application.dataPath + "/Resources/HandPoseData/" + fileName + ".csv";
        if (File.Exists(resourcePath))
        {
            compareFilePath = resourcePath;
            isCompareAnalyzed = false;
        }
        else
        {
            Debug.LogWarning($"파일을 찾을 수 없습니다: {resourcePath}");
        }
    }

    private void LoadCompareData()
    {
        try
        {
            compareFrames = ParseCSV(compareFilePath);
            if (compareFrames.Count > 0)
            {
                isCompareAnalyzed = true;
                Debug.Log($"<color=green>[비교 모드] 로드 완료: {compareFrames.Count} 프레임</color>");
            }
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("오류", $"비교 파일 로드 실패: {e.Message}", "확인");
            Debug.LogError($"[비교 모드] 로드 오류: {e}");
        }
    }

    private void TogglePlayback()
    {
        isPlaying = !isPlaying;
        if (isPlaying)
        {
            // 끝에 도달했으면 처음부터
            if (currentFrameIndex >= parsedFrames.Count - 1)
            {
                currentFrameIndex = 0;
            }
            // 현재 프레임의 타임스탬프부터 재생 시작
            playbackStartTime = EditorApplication.timeSinceStartup;
            playbackStartTimestamp = parsedFrames[currentFrameIndex].timestamp;
        }
        SceneView.RepaintAll();
    }

    private void FocusSceneViewOnHands()
    {
        if (parsedFrames.Count == 0) return;

        FrameData frame = parsedFrames[currentFrameIndex];
        Vector3 focusPos = previewOffset;

        // 오른손 월드 위치가 있으면 사용
        if (frame.rightJoints.Count > 0)
        {
            var wrist = frame.rightJoints.FirstOrDefault(j => j.jointId == 1);
            if (wrist != null && wrist.worldPosition != Vector3.zero)
            {
                focusPos = wrist.worldPosition * previewScale + previewOffset;
            }
        }

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.LookAt(focusPos, Quaternion.Euler(30, -45, 0), 0.5f);
        }
    }

    private void FindPatientInScene()
    {
        // Patient 태그로 찾기
        GameObject patient = GameObject.FindGameObjectWithTag("Patient");
        if (patient != null)
        {
            patientTransform = patient.transform;
            Debug.Log($"<color=green>[HandPoseResampler] 환자 모델 찾음: {patient.name}</color>");
            return;
        }

        // 태그로 못 찾으면 이름으로 찾기
        string[] patientNames = { "Patient", "환자", "Chuna_Patient", "PatientModel" };
        foreach (var name in patientNames)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                patientTransform = obj.transform;
                Debug.Log($"<color=green>[HandPoseResampler] 환자 모델 찾음 (이름): {obj.name}</color>");
                return;
            }
        }

        Debug.LogWarning("<color=yellow>[HandPoseResampler] Scene에서 환자 모델을 찾을 수 없습니다.</color>");
        patientTransform = null;
    }

    private void HandleKeyboardShortcuts()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown) return;

        switch (e.keyCode)
        {
            case KeyCode.Space:
                TogglePlayback();
                e.Use();
                break;

            case KeyCode.LeftArrow:
                if (currentFrameIndex > 0)
                {
                    currentFrameIndex--;
                    SceneView.RepaintAll();
                    Repaint();
                }
                e.Use();
                break;

            case KeyCode.RightArrow:
                if (currentFrameIndex < parsedFrames.Count - 1)
                {
                    currentFrameIndex++;
                    SceneView.RepaintAll();
                    Repaint();
                }
                e.Use();
                break;

            case KeyCode.Home:
                currentFrameIndex = 0;
                SceneView.RepaintAll();
                Repaint();
                e.Use();
                break;

            case KeyCode.End:
                currentFrameIndex = Mathf.Max(0, parsedFrames.Count - 1);
                SceneView.RepaintAll();
                Repaint();
                e.Use();
                break;
        }
    }

    private void DrawHeader()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("핸드 포즈 CSV 에디터", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "녹화된 CSV 데이터를 편집하고 미리봅니다.\n" +
            "• 미리보기: VR 없이 Scene 뷰에서 재생/확인\n" +
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
                // trimEndFrame을 "포함"하도록 수정 (<=)
                for (int i = trimStartFrame; i <= trimEndFrame && i < parsedFrames.Count; i++)
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

            // ★ 중요: timestamp를 0부터 시작하도록 재조정
            float startTimestamp = result[0].timestamp;
            for (int i = 0; i < result.Count; i++)
            {
                result[i].timestamp -= startTimestamp;
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

            // 시작 위치와 방향
            Vector3 startPos = parsedFrames[0].rightWristWorldPos;
            Vector3 endPos = parsedFrames[parsedFrames.Count - 1].rightWristWorldPos;
            Vector3 midPos = parsedFrames[parsedFrames.Count / 2].rightWristWorldPos;

            // 이동 평면의 법선 계산 (세 점으로 평면 결정)
            Vector3 v1 = midPos - startPos;
            Vector3 v2 = endPos - startPos;
            Vector3 planeNormal = Vector3.Cross(v1, v2).normalized;

            // 평면이 거의 평평하면 (직선 이동) Y축 사용
            if (planeNormal.magnitude < 0.1f)
            {
                planeNormal = Vector3.up;
            }

            // 스케일 축 결정 (Auto면 계산된 평면 법선 사용)
            Vector3 axisVector = GetScaleAxisVector();
            if (axisVector == Vector3.zero)
            {
                axisVector = planeNormal;
            }

            // 시작 방향 (피벗에서 첫 프레임으로)
            Vector3 startDir = (startPos - pivot).normalized;
            float startRadius = Vector3.Distance(startPos, pivot);

            Debug.Log($"<color=yellow>[스케일링] 피벗: {pivot}, 축: {axisVector}, 시작반경: {startRadius:F3}m</color>");
            Debug.Log($"<color=yellow>[스케일링] 원본각도: {pivotBasedAngle:F1}° → 목표: {targetAngle:F1}° (비율: {scaleRatio:F2}x)</color>");

            for (int i = 0; i < parsedFrames.Count; i++)
            {
                var frame = CloneFrame(parsedFrames[i]);

                // 현재 프레임의 피벗 기준 방향과 거리
                Vector3 currentPos = parsedFrames[i].rightWristWorldPos;
                Vector3 fromPivot = currentPos - pivot;
                float currentRadius = fromPivot.magnitude;
                Vector3 currentDir = fromPivot.normalized;

                // 시작 방향 대비 현재 각도 (부호 있음)
                float currentAngle = Vector3.SignedAngle(startDir, currentDir, axisVector);

                // 새 각도 = 현재 각도 * 스케일 비율
                float newAngle = currentAngle * scaleRatio;

                // 새 위치 계산: 피벗에서 시작방향을 newAngle만큼 회전
                Quaternion rotation = Quaternion.AngleAxis(newAngle, axisVector);
                Vector3 newDir = rotation * startDir;
                Vector3 newPos = pivot + newDir * currentRadius;

                frame.rightWristWorldPos = newPos;

                // Wrist 조인트도 업데이트
                foreach (var joint in frame.rightJoints)
                {
                    if (joint.jointId == 1)
                    {
                        joint.worldPosition = newPos;
                    }
                }

                frame.frameIndex = i;
                result.Add(frame);

                EditorUtility.DisplayProgressBar("스케일링", $"프레임 {i + 1}/{parsedFrames.Count}", (float)i / parsedFrames.Count);
            }

            // 결과 각도 검증
            Vector3 resultStartDir = (result[0].rightWristWorldPos - pivot).normalized;
            Vector3 resultEndDir = (result[result.Count - 1].rightWristWorldPos - pivot).normalized;
            float resultAngle = Vector3.Angle(resultStartDir, resultEndDir);

            SaveFramesToCSV(result, "_scaled");
            EditorUtility.ClearProgressBar();

            Debug.Log($"<color=green>[스케일링] 완료! 결과 각도: {resultAngle:F1}°</color>");

            EditorUtility.DisplayDialog("완료",
                $"각도 스케일링 완료!\n\n" +
                $"원본 각도: {pivotBasedAngle:F1}°\n" +
                $"목표 각도: {targetAngle:F1}°\n" +
                $"결과 각도: {resultAngle:F1}°\n" +
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

            // 시작/중간/끝 위치로 이동 평면 계산
            Vector3 startPos = parsedFrames[0].rightWristWorldPos;
            Vector3 endPos = parsedFrames[parsedFrames.Count - 1].rightWristWorldPos;
            Vector3 midPos = parsedFrames[parsedFrames.Count / 2].rightWristWorldPos;

            Vector3 v1 = midPos - startPos;
            Vector3 v2 = endPos - startPos;
            Vector3 planeNormal = Vector3.Cross(v1, v2).normalized;
            if (planeNormal.magnitude < 0.1f) planeNormal = Vector3.up;

            Vector3 axisVector = GetScaleAxisVector();
            if (axisVector == Vector3.zero) axisVector = planeNormal;

            Vector3 startDir = (startPos - pivot).normalized;

            List<FrameData> scaled = new List<FrameData>();
            for (int i = 0; i < parsedFrames.Count; i++)
            {
                var frame = CloneFrame(parsedFrames[i]);

                Vector3 currentPos = parsedFrames[i].rightWristWorldPos;
                Vector3 fromPivot = currentPos - pivot;
                float currentRadius = fromPivot.magnitude;

                // 현재 각도 계산 후 스케일 적용
                float currentAngle = Vector3.SignedAngle(startDir, fromPivot.normalized, axisVector);
                float newAngle = currentAngle * scaleRatio;

                Quaternion rotation = Quaternion.AngleAxis(newAngle, axisVector);
                Vector3 newPos = pivot + rotation * startDir * currentRadius;

                frame.rightWristWorldPos = newPos;
                foreach (var joint in frame.rightJoints)
                {
                    if (joint.jointId == 1) joint.worldPosition = newPos;
                }
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
