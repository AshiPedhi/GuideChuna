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
/// 핸드 포즈 CSV 데이터 리샘플링 에디터 도구
/// 불균일한 프레임 간격을 균등 간격으로 재생성
/// </summary>
public class HandPoseResampler : EditorWindow
{
    // 설정값
    private string sourceFilePath = "";
    private string outputFileName = "";
    private int targetFrameCount = 100;
    private bool useDistanceBased = true;  // true: 거리 기반, false: 각도 기반
    private bool preserveOriginalTiming = false;

    // 미리보기 정보
    private int originalFrameCount = 0;
    private float totalDistance = 0f;
    private float totalAngle = 0f;
    private float avgFrameDistance = 0f;
    private float maxFrameDistance = 0f;
    private float minFrameDistance = 0f;
    private bool isAnalyzed = false;

    // 스크롤 위치
    private Vector2 scrollPosition;

    // CSV 파싱 데이터
    private List<FrameData> parsedFrames = new List<FrameData>();

    [MenuItem("Tools/Chuna/Hand Pose Resampler")]
    public static void ShowWindow()
    {
        var window = GetWindow<HandPoseResampler>("Hand Pose Resampler");
        window.minSize = new Vector2(450, 500);
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("핸드 포즈 CSV 리샘플러", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "녹화된 CSV 데이터의 프레임 간격을 균등하게 재배치합니다.\n" +
            "불균일한 녹화 속도로 인한 프레임 간격 차이를 보정합니다.",
            MessageType.Info);

        EditorGUILayout.Space(15);

        // ===== 소스 파일 선택 =====
        EditorGUILayout.LabelField("1. 소스 파일 선택", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        sourceFilePath = EditorGUILayout.TextField("CSV 파일 경로", sourceFilePath);
        if (GUILayout.Button("찾아보기", GUILayout.Width(80)))
        {
            string initialPath = string.IsNullOrEmpty(sourceFilePath)
                ? Application.dataPath + "/Resources/HandPoseData"
                : Path.GetDirectoryName(sourceFilePath);

            string path = EditorUtility.OpenFilePanel("CSV 파일 선택", initialPath, "csv");
            if (!string.IsNullOrEmpty(path))
            {
                sourceFilePath = path;
                isAnalyzed = false;

                // 기본 출력 파일명 설정
                string baseName = Path.GetFileNameWithoutExtension(path);
                outputFileName = baseName + "_resampled";
            }
        }
        EditorGUILayout.EndHorizontal();

        // Resources 폴더 바로가기
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("빠른 선택:", GUILayout.Width(70));
        if (GUILayout.Button("측굴", GUILayout.Width(60)))
            SelectResourceFile("측굴");
        if (GUILayout.Button("건측회전", GUILayout.Width(70)))
            SelectResourceFile("건측회전");
        if (GUILayout.Button("환측회전", GUILayout.Width(70)))
            SelectResourceFile("환측회전");
        if (GUILayout.Button("등척성운동", GUILayout.Width(80)))
            SelectResourceFile("등척성운동");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // ===== 분석 버튼 =====
        GUI.enabled = !string.IsNullOrEmpty(sourceFilePath) && File.Exists(sourceFilePath);
        if (GUILayout.Button("데이터 분석", GUILayout.Height(30)))
        {
            AnalyzeCSV();
        }
        GUI.enabled = true;

        // ===== 분석 결과 표시 =====
        if (isAnalyzed)
        {
            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("2. 분석 결과", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"원본 프레임 수: {originalFrameCount}");
            EditorGUILayout.LabelField($"총 이동 거리: {totalDistance:F4} m");
            EditorGUILayout.LabelField($"총 회전 각도: {totalAngle:F1}°");
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("프레임 간 거리 분포:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"  평균: {avgFrameDistance:F4} m");
            EditorGUILayout.LabelField($"  최소: {minFrameDistance:F4} m");
            EditorGUILayout.LabelField($"  최대: {maxFrameDistance:F4} m");

            // 불균일도 경고
            if (maxFrameDistance > avgFrameDistance * 3f)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    $"프레임 간격이 불균일합니다!\n" +
                    $"최대 간격({maxFrameDistance:F4}m)이 평균({avgFrameDistance:F4}m)의 {maxFrameDistance/avgFrameDistance:F1}배입니다.",
                    MessageType.Warning);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(15);

            // ===== 리샘플링 설정 =====
            EditorGUILayout.LabelField("3. 리샘플링 설정", EditorStyles.boldLabel);

            targetFrameCount = EditorGUILayout.IntSlider("목표 프레임 수", targetFrameCount, 30, 500);

            float newAvgDistance = totalDistance / (targetFrameCount - 1);
            EditorGUILayout.LabelField($"  → 예상 평균 간격: {newAvgDistance:F4} m");

            EditorGUILayout.Space(5);
            useDistanceBased = EditorGUILayout.Toggle("거리 기반 리샘플링", useDistanceBased);
            EditorGUILayout.HelpBox(
                useDistanceBased
                    ? "이동 거리를 기준으로 균등 분배 (측굴, 회전 동작에 권장)"
                    : "시간(타임스탬프)을 기준으로 균등 분배",
                MessageType.None);

            EditorGUILayout.Space(10);
            outputFileName = EditorGUILayout.TextField("출력 파일명", outputFileName);

            EditorGUILayout.Space(15);

            // ===== 리샘플링 실행 =====
            EditorGUILayout.LabelField("4. 실행", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
            if (GUILayout.Button("리샘플링 실행", GUILayout.Height(40)))
            {
                ExecuteResampling();
            }
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.EndScrollView();
    }

    private void SelectResourceFile(string fileName)
    {
        string resourcePath = Application.dataPath + "/Resources/HandPoseData/" + fileName + ".csv";
        if (File.Exists(resourcePath))
        {
            sourceFilePath = resourcePath;
            outputFileName = fileName + "_resampled";
            isAnalyzed = false;
        }
        else
        {
            Debug.LogWarning($"파일을 찾을 수 없습니다: {resourcePath}");
        }
    }

    /// <summary>
    /// CSV 파일 분석
    /// </summary>
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

            // 거리/각도 계산
            CalculateMetrics();

            isAnalyzed = true;
            Debug.Log($"<color=green>[HandPoseResampler] 분석 완료: {originalFrameCount} 프레임</color>");
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("파싱 오류", e.Message, "확인");
            Debug.LogError($"[HandPoseResampler] CSV 파싱 오류: {e}");
        }
    }

    /// <summary>
    /// 프레임 간 거리/각도 메트릭 계산
    /// </summary>
    private void CalculateMetrics()
    {
        totalDistance = 0f;
        totalAngle = 0f;
        List<float> distances = new List<float>();

        for (int i = 1; i < parsedFrames.Count; i++)
        {
            // 오른손 Wrist 위치 기준 거리 계산
            Vector3 prevPos = parsedFrames[i - 1].rightWristWorldPos;
            Vector3 currPos = parsedFrames[i].rightWristWorldPos;

            float dist = Vector3.Distance(prevPos, currPos);
            distances.Add(dist);
            totalDistance += dist;

            // 회전 각도 계산
            Quaternion prevRot = parsedFrames[i - 1].rightWristWorldRot;
            Quaternion currRot = parsedFrames[i].rightWristWorldRot;
            float angle = Quaternion.Angle(prevRot, currRot);
            totalAngle += angle;
        }

        if (distances.Count > 0)
        {
            avgFrameDistance = distances.Average();
            minFrameDistance = distances.Min();
            maxFrameDistance = distances.Max();
        }
    }

    /// <summary>
    /// 리샘플링 실행
    /// </summary>
    private void ExecuteResampling()
    {
        if (parsedFrames.Count < 2)
        {
            EditorUtility.DisplayDialog("오류", "분석된 데이터가 없습니다.", "확인");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("리샘플링", "데이터 처리 중...", 0.1f);

            // 누적 거리 계산
            List<float> cumulativeDistances = new List<float> { 0f };
            for (int i = 1; i < parsedFrames.Count; i++)
            {
                Vector3 prevPos = parsedFrames[i - 1].rightWristWorldPos;
                Vector3 currPos = parsedFrames[i].rightWristWorldPos;
                float dist = Vector3.Distance(prevPos, currPos);
                cumulativeDistances.Add(cumulativeDistances[i - 1] + dist);
            }

            EditorUtility.DisplayProgressBar("리샘플링", "새 프레임 생성 중...", 0.3f);

            // 균등 간격 포인트 계산
            List<FrameData> resampledFrames = new List<FrameData>();
            float targetSpacing = totalDistance / (targetFrameCount - 1);

            for (int i = 0; i < targetFrameCount; i++)
            {
                float targetDist = i * targetSpacing;

                // 해당 거리에 해당하는 두 프레임 찾기
                int lowerIdx = 0;
                for (int j = 0; j < cumulativeDistances.Count - 1; j++)
                {
                    if (cumulativeDistances[j] <= targetDist && cumulativeDistances[j + 1] >= targetDist)
                    {
                        lowerIdx = j;
                        break;
                    }
                }

                // 마지막 프레임 처리
                if (targetDist >= totalDistance)
                {
                    lowerIdx = parsedFrames.Count - 2;
                }

                int upperIdx = Mathf.Min(lowerIdx + 1, parsedFrames.Count - 1);

                // 보간 비율 계산
                float segmentStart = cumulativeDistances[lowerIdx];
                float segmentEnd = cumulativeDistances[upperIdx];
                float segmentLength = segmentEnd - segmentStart;
                float t = segmentLength > 0.0001f ? (targetDist - segmentStart) / segmentLength : 0f;
                t = Mathf.Clamp01(t);

                // 새 프레임 생성 (보간)
                FrameData newFrame = InterpolateFrames(parsedFrames[lowerIdx], parsedFrames[upperIdx], t, i);
                resampledFrames.Add(newFrame);

                EditorUtility.DisplayProgressBar("리샘플링",
                    $"새 프레임 생성 중... ({i + 1}/{targetFrameCount})",
                    0.3f + 0.5f * i / targetFrameCount);
            }

            EditorUtility.DisplayProgressBar("리샘플링", "CSV 저장 중...", 0.9f);

            // CSV 저장
            SaveResampledCSV(resampledFrames);

            EditorUtility.ClearProgressBar();

            // 결과 요약
            Debug.Log($"<color=green>[HandPoseResampler] 리샘플링 완료!</color>\n" +
                     $"원본: {originalFrameCount} 프레임 → 결과: {targetFrameCount} 프레임\n" +
                     $"균등 간격: {targetSpacing:F4} m");

            EditorUtility.DisplayDialog("완료",
                $"리샘플링이 완료되었습니다.\n\n" +
                $"원본: {originalFrameCount} 프레임\n" +
                $"결과: {targetFrameCount} 프레임\n" +
                $"균등 간격: {targetSpacing:F4} m",
                "확인");
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("오류", e.Message, "확인");
            Debug.LogError($"[HandPoseResampler] 리샘플링 오류: {e}");
        }
    }

    /// <summary>
    /// 두 프레임 사이를 보간
    /// </summary>
    private FrameData InterpolateFrames(FrameData a, FrameData b, float t, int newFrameIndex)
    {
        FrameData result = new FrameData
        {
            frameIndex = newFrameIndex,
            timestamp = Mathf.Lerp(a.timestamp, b.timestamp, t),
            leftJoints = new List<JointData>(),
            rightJoints = new List<JointData>()
        };

        // 왼손 조인트 보간
        for (int j = 0; j < a.leftJoints.Count && j < b.leftJoints.Count; j++)
        {
            JointData jointA = a.leftJoints[j];
            JointData jointB = b.leftJoints[j];

            JointData newJoint = new JointData
            {
                jointId = jointA.jointId,
                localPosition = Vector3.Lerp(jointA.localPosition, jointB.localPosition, t),
                localRotation = Quaternion.Slerp(jointA.localRotation, jointB.localRotation, t),
                worldPosition = Vector3.Lerp(jointA.worldPosition, jointB.worldPosition, t),
                worldRotation = Quaternion.Slerp(jointA.worldRotation, jointB.worldRotation, t)
            };
            result.leftJoints.Add(newJoint);
        }

        // 오른손 조인트 보간
        for (int j = 0; j < a.rightJoints.Count && j < b.rightJoints.Count; j++)
        {
            JointData jointA = a.rightJoints[j];
            JointData jointB = b.rightJoints[j];

            JointData newJoint = new JointData
            {
                jointId = jointA.jointId,
                localPosition = Vector3.Lerp(jointA.localPosition, jointB.localPosition, t),
                localRotation = Quaternion.Slerp(jointA.localRotation, jointB.localRotation, t),
                worldPosition = Vector3.Lerp(jointA.worldPosition, jointB.worldPosition, t),
                worldRotation = Quaternion.Slerp(jointA.worldRotation, jointB.worldRotation, t)
            };
            result.rightJoints.Add(newJoint);
        }

        // Wrist 위치 캐시 업데이트
        if (result.rightJoints.Count > 1)
        {
            result.rightWristWorldPos = result.rightJoints[1].worldPosition;
            result.rightWristWorldRot = result.rightJoints[1].worldRotation;
        }

        return result;
    }

    /// <summary>
    /// 리샘플링된 데이터를 CSV로 저장
    /// </summary>
    private void SaveResampledCSV(List<FrameData> frames)
    {
        StringBuilder sb = new StringBuilder();
        CultureInfo invariant = CultureInfo.InvariantCulture;

        // 헤더
        sb.AppendLine("FrameIndex,HandType,JointID,LocalPosX,LocalPosY,LocalPosZ," +
                     "LocalRotX,LocalRotY,LocalRotZ,LocalRotW,Timestamp," +
                     "WorldPosX,WorldPosY,WorldPosZ,WorldRotX,WorldRotY,WorldRotZ,WorldRotW");

        foreach (var frame in frames)
        {
            // 왼손 데이터
            foreach (var joint in frame.leftJoints)
            {
                sb.AppendFormat(invariant,
                    "{0},Left,{1},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F3},{10},{11},{12},{13},{14},{15},{16}\n",
                    frame.frameIndex,
                    joint.jointId,
                    joint.localPosition.x, joint.localPosition.y, joint.localPosition.z,
                    joint.localRotation.x, joint.localRotation.y, joint.localRotation.z, joint.localRotation.w,
                    frame.timestamp,
                    joint.jointId == 1 ? joint.worldPosition.x.ToString("F4", invariant) : "",
                    joint.jointId == 1 ? joint.worldPosition.y.ToString("F4", invariant) : "",
                    joint.jointId == 1 ? joint.worldPosition.z.ToString("F4", invariant) : "",
                    joint.jointId == 1 ? joint.worldRotation.x.ToString("F4", invariant) : "",
                    joint.jointId == 1 ? joint.worldRotation.y.ToString("F4", invariant) : "",
                    joint.jointId == 1 ? joint.worldRotation.z.ToString("F4", invariant) : "",
                    joint.jointId == 1 ? joint.worldRotation.w.ToString("F4", invariant) : ""
                );
            }

            // 오른손 데이터
            foreach (var joint in frame.rightJoints)
            {
                sb.AppendFormat(invariant,
                    "{0},Right,{1},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F3},{10},{11},{12},{13},{14},{15},{16}\n",
                    frame.frameIndex,
                    joint.jointId,
                    joint.localPosition.x, joint.localPosition.y, joint.localPosition.z,
                    joint.localRotation.x, joint.localRotation.y, joint.localRotation.z, joint.localRotation.w,
                    frame.timestamp,
                    joint.jointId == 1 ? joint.worldPosition.x.ToString("F4", invariant) : "",
                    joint.jointId == 1 ? joint.worldPosition.y.ToString("F4", invariant) : "",
                    joint.jointId == 1 ? joint.worldPosition.z.ToString("F4", invariant) : "",
                    joint.jointId == 1 ? joint.worldRotation.x.ToString("F4", invariant) : "",
                    joint.jointId == 1 ? joint.worldRotation.y.ToString("F4", invariant) : "",
                    joint.jointId == 1 ? joint.worldRotation.z.ToString("F4", invariant) : "",
                    joint.jointId == 1 ? joint.worldRotation.w.ToString("F4", invariant) : ""
                );
            }
        }

        // 저장 경로 결정 (원본과 같은 폴더)
        string outputPath = Path.Combine(Path.GetDirectoryName(sourceFilePath), outputFileName + ".csv");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);

        Debug.Log($"<color=cyan>[HandPoseResampler] 저장 완료: {outputPath}</color>");

        // 프로젝트 내 파일이면 Refresh
        if (outputPath.StartsWith(Application.dataPath))
        {
            AssetDatabase.Refresh();
        }
    }

    #region CSV Parsing

    private List<FrameData> ParseCSV(string filePath)
    {
        var frames = new Dictionary<int, FrameData>();
        var lines = File.ReadAllLines(filePath);

        if (lines.Length < 2)
            throw new Exception("CSV 파일이 비어있습니다.");

        // 헤더 분석
        string[] header = lines[0].Split(',');
        int frameIdxCol = Array.IndexOf(header, "FrameIndex");
        int handTypeCol = Array.IndexOf(header, "HandType");
        int jointIdCol = Array.IndexOf(header, "JointID");
        int localPosXCol = Array.IndexOf(header, "LocalPosX");
        int localRotXCol = Array.IndexOf(header, "LocalRotX");
        int timestampCol = Array.IndexOf(header, "Timestamp");
        int worldPosXCol = Array.FindIndex(header, h => h.Contains("WorldPosX") || h.Contains("RootPosX"));

        CultureInfo invariant = CultureInfo.InvariantCulture;

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            string[] cols = lines[i].Split(',');
            if (cols.Length < 11) continue;

            int frameIndex = int.Parse(cols[frameIdxCol]);
            string handType = cols[handTypeCol];
            int jointId = int.Parse(cols[jointIdCol]);

            // 프레임 데이터 가져오거나 생성
            if (!frames.TryGetValue(frameIndex, out FrameData frame))
            {
                frame = new FrameData
                {
                    frameIndex = frameIndex,
                    timestamp = float.Parse(cols[timestampCol], invariant),
                    leftJoints = new List<JointData>(),
                    rightJoints = new List<JointData>()
                };
                frames[frameIndex] = frame;
            }

            // 조인트 데이터 파싱
            JointData joint = new JointData
            {
                jointId = jointId,
                localPosition = new Vector3(
                    float.Parse(cols[localPosXCol], invariant),
                    float.Parse(cols[localPosXCol + 1], invariant),
                    float.Parse(cols[localPosXCol + 2], invariant)
                ),
                localRotation = new Quaternion(
                    float.Parse(cols[localRotXCol], invariant),
                    float.Parse(cols[localRotXCol + 1], invariant),
                    float.Parse(cols[localRotXCol + 2], invariant),
                    float.Parse(cols[localRotXCol + 3], invariant)
                )
            };

            // World/Root 위치 파싱 (Wrist만)
            if (worldPosXCol >= 0 && jointId == 1 && cols.Length > worldPosXCol + 6)
            {
                if (!string.IsNullOrEmpty(cols[worldPosXCol]))
                {
                    joint.worldPosition = new Vector3(
                        float.Parse(cols[worldPosXCol], invariant),
                        float.Parse(cols[worldPosXCol + 1], invariant),
                        float.Parse(cols[worldPosXCol + 2], invariant)
                    );
                    joint.worldRotation = new Quaternion(
                        float.Parse(cols[worldPosXCol + 3], invariant),
                        float.Parse(cols[worldPosXCol + 4], invariant),
                        float.Parse(cols[worldPosXCol + 5], invariant),
                        float.Parse(cols[worldPosXCol + 6], invariant)
                    );

                    // Wrist 위치 캐시
                    if (handType == "Right")
                    {
                        frame.rightWristWorldPos = joint.worldPosition;
                        frame.rightWristWorldRot = joint.worldRotation;
                    }
                }
            }

            // 핸드 타입별로 저장
            if (handType == "Left")
                frame.leftJoints.Add(joint);
            else if (handType == "Right")
                frame.rightJoints.Add(joint);
        }

        // 프레임 인덱스 순으로 정렬
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

        // 빠른 접근용 캐시
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
