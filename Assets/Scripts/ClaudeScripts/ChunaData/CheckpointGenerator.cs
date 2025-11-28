using System.Collections.Generic;
using UnityEngine;
using static HandPoseDataLoader;

/// <summary>
/// CSV 데이터 기반 체크포인트 자동 생성기
/// 에디터 및 런타임에서 체크포인트를 생성하고 관리
/// </summary>
public class CheckpointGenerator : MonoBehaviour
{
    [Header("=== CSV 설정 ===")]
    [Tooltip("CSV 파일 이름 (Resources/HandPoseData/ 내)")]
    [SerializeField] private string csvFileName = "등척성운동";

    [Header("=== 생성 설정 ===")]
    [Tooltip("체크포인트 생성 모드")]
    [SerializeField] private GenerationMode generationMode = GenerationMode.FixedInterval;

    [Tooltip("고정 간격 (프레임)")]
    [SerializeField] private int fixedFrameInterval = 10;

    [Tooltip("고정 개수 (총 체크포인트 수)")]
    [SerializeField] private int fixedCheckpointCount = 5;

    [Tooltip("이동 거리 기반 (미터)")]
    [SerializeField] private float distanceThreshold = 0.1f;

    [Tooltip("회전 기반 (도)")]
    [SerializeField] private float rotationThreshold = 30f;

    [Header("=== 체크포인트 속성 ===")]
    [Tooltip("트리거 반경 (미터)")]
    [SerializeField] private float triggerRadius = 0.08f;

    [Tooltip("홀드 시간 (초)")]
    [SerializeField] private float holdTime = 0.5f;

    [Tooltip("필요 유사도")]
    [SerializeField] [Range(0f, 1f)] private float requiredSimilarity = 0.6f;

    [Header("=== 기준점 ===")]
    [Tooltip("체크포인트 위치 오프셋")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;

    [Tooltip("환자 기준점 (옵션)")]
    [SerializeField] private Transform patientReference;

    [Header("=== 출력 ===")]
    [SerializeField] private Transform checkpointParent;
    [SerializeField] private List<PathCheckpoint> generatedCheckpoints = new List<PathCheckpoint>();

    [Header("=== 시각화 ===")]
    [SerializeField] private bool drawPathGizmo = true;
    [SerializeField] private Color pathColor = Color.cyan;
    [SerializeField] private Color checkpointColor = Color.green;

    // 로드된 데이터
    private List<PoseFrame> loadedFrames = new List<PoseFrame>();

    /// <summary>
    /// 생성 모드
    /// </summary>
    public enum GenerationMode
    {
        [Tooltip("고정 프레임 간격")]
        FixedInterval,

        [Tooltip("고정 개수 (균등 분배)")]
        FixedCount,

        [Tooltip("이동 거리 기반")]
        DistanceBased,

        [Tooltip("회전 변화 기반")]
        RotationBased,

        [Tooltip("거리+회전 복합")]
        Combined,

        [Tooltip("시작/중간/끝만")]
        StartMiddleEnd,

        [Tooltip("수동 지정")]
        Manual
    }

    /// <summary>
    /// CSV 로드 및 체크포인트 생성
    /// </summary>
    [ContextMenu("Generate Checkpoints")]
    public void GenerateCheckpoints()
    {
        // CSV 로드
        LoadCSV();

        if (loadedFrames == null || loadedFrames.Count == 0)
        {
            Debug.LogError("[CheckpointGenerator] 프레임 데이터가 없습니다!");
            return;
        }

        // 기존 체크포인트 정리
        ClearCheckpoints();

        // 부모 오브젝트 확인
        if (checkpointParent == null)
        {
            GameObject parent = new GameObject($"Checkpoints_{csvFileName}");
            parent.transform.SetParent(transform);
            checkpointParent = parent.transform;
        }

        // 모드에 따라 생성
        List<int> checkpointFrameIndices = GetCheckpointFrameIndices();

        foreach (int frameIndex in checkpointFrameIndices)
        {
            CreateCheckpointAtFrame(frameIndex, checkpointFrameIndices.IndexOf(frameIndex));
        }

        Debug.Log($"<color=green>[CheckpointGenerator] {generatedCheckpoints.Count}개 체크포인트 생성 완료</color>");
    }

    /// <summary>
    /// CSV 파일 로드
    /// </summary>
    private void LoadCSV()
    {
        HandPoseDataLoader loader = new HandPoseDataLoader();
        var result = loader.LoadFromResources($"HandPoseData/{csvFileName}");

        if (!result.success)
        {
            Debug.LogError($"[CheckpointGenerator] CSV 로드 실패: {result.errorMessage}");
            return;
        }

        loadedFrames = result.frames;
        Debug.Log($"[CheckpointGenerator] {loadedFrames.Count} 프레임 로드됨");
    }

    /// <summary>
    /// 체크포인트 프레임 인덱스 계산
    /// </summary>
    private List<int> GetCheckpointFrameIndices()
    {
        List<int> indices = new List<int>();

        switch (generationMode)
        {
            case GenerationMode.FixedInterval:
                for (int i = 0; i < loadedFrames.Count; i += fixedFrameInterval)
                {
                    indices.Add(i);
                }
                // 마지막 프레임 추가
                if (indices[indices.Count - 1] != loadedFrames.Count - 1)
                {
                    indices.Add(loadedFrames.Count - 1);
                }
                break;

            case GenerationMode.FixedCount:
                int interval = Mathf.Max(1, loadedFrames.Count / (fixedCheckpointCount - 1));
                for (int i = 0; i < fixedCheckpointCount - 1; i++)
                {
                    indices.Add(i * interval);
                }
                indices.Add(loadedFrames.Count - 1);
                break;

            case GenerationMode.DistanceBased:
                indices.Add(0); // 시작
                Vector3 lastPos = GetHandCenterPosition(loadedFrames[0]);
                for (int i = 1; i < loadedFrames.Count; i++)
                {
                    Vector3 currentPos = GetHandCenterPosition(loadedFrames[i]);
                    if (Vector3.Distance(lastPos, currentPos) >= distanceThreshold)
                    {
                        indices.Add(i);
                        lastPos = currentPos;
                    }
                }
                if (indices[indices.Count - 1] != loadedFrames.Count - 1)
                {
                    indices.Add(loadedFrames.Count - 1);
                }
                break;

            case GenerationMode.RotationBased:
                indices.Add(0);
                Quaternion lastRot = loadedFrames[0].leftRootRotation;
                for (int i = 1; i < loadedFrames.Count; i++)
                {
                    Quaternion currentRot = loadedFrames[i].leftRootRotation;
                    if (Quaternion.Angle(lastRot, currentRot) >= rotationThreshold)
                    {
                        indices.Add(i);
                        lastRot = currentRot;
                    }
                }
                if (indices[indices.Count - 1] != loadedFrames.Count - 1)
                {
                    indices.Add(loadedFrames.Count - 1);
                }
                break;

            case GenerationMode.Combined:
                indices.Add(0);
                Vector3 lastPosC = GetHandCenterPosition(loadedFrames[0]);
                Quaternion lastRotC = loadedFrames[0].leftRootRotation;
                for (int i = 1; i < loadedFrames.Count; i++)
                {
                    Vector3 currentPos = GetHandCenterPosition(loadedFrames[i]);
                    Quaternion currentRot = loadedFrames[i].leftRootRotation;

                    bool distanceMet = Vector3.Distance(lastPosC, currentPos) >= distanceThreshold;
                    bool rotationMet = Quaternion.Angle(lastRotC, currentRot) >= rotationThreshold;

                    if (distanceMet || rotationMet)
                    {
                        indices.Add(i);
                        lastPosC = currentPos;
                        lastRotC = currentRot;
                    }
                }
                if (indices[indices.Count - 1] != loadedFrames.Count - 1)
                {
                    indices.Add(loadedFrames.Count - 1);
                }
                break;

            case GenerationMode.StartMiddleEnd:
                indices.Add(0);
                indices.Add(loadedFrames.Count / 2);
                indices.Add(loadedFrames.Count - 1);
                break;

            case GenerationMode.Manual:
                // 수동 모드는 인스펙터에서 직접 추가
                Debug.Log("[CheckpointGenerator] 수동 모드: 인스펙터에서 직접 체크포인트를 추가하세요.");
                break;
        }

        return indices;
    }

    /// <summary>
    /// 손 중심 위치 계산
    /// </summary>
    private Vector3 GetHandCenterPosition(PoseFrame frame)
    {
        return (frame.leftRootPosition + frame.rightRootPosition) / 2f;
    }

    /// <summary>
    /// 특정 프레임에 체크포인트 생성
    /// </summary>
    private void CreateCheckpointAtFrame(int frameIndex, int checkpointIndex)
    {
        PoseFrame frame = loadedFrames[frameIndex];

        // 위치 계산
        Vector3 position = GetHandCenterPosition(frame) + positionOffset;
        if (patientReference != null)
        {
            position = patientReference.TransformPoint(position);
        }

        // 이름 결정
        string cpName;
        bool isStart = (checkpointIndex == 0);
        bool isEnd = (frameIndex == loadedFrames.Count - 1 || checkpointIndex == GetCheckpointFrameIndices().Count - 1);

        if (isStart)
            cpName = "시작";
        else if (isEnd)
            cpName = "종료";
        else
            cpName = $"체크포인트 {checkpointIndex}";

        // 게임 오브젝트 생성
        GameObject cpObj = new GameObject($"CP_{checkpointIndex}_{cpName}");
        cpObj.transform.SetParent(checkpointParent);
        cpObj.transform.position = position;

        // 컴포넌트 추가 및 초기화
        PathCheckpoint checkpoint = cpObj.AddComponent<PathCheckpoint>();
        checkpoint.Initialize(
            index: checkpointIndex,
            name: cpName,
            position: position,
            leftHandPos: patientReference != null
                ? patientReference.TransformPoint(frame.leftRootPosition)
                : frame.leftRootPosition + positionOffset,
            leftHandRot: frame.leftRootRotation,
            rightHandPos: patientReference != null
                ? patientReference.TransformPoint(frame.rightRootPosition)
                : frame.rightRootPosition + positionOffset,
            rightHandRot: frame.rightRootRotation,
            holdTime: holdTime,
            similarity: requiredSimilarity
        );

        checkpoint.SetTriggerRadius(triggerRadius);

        generatedCheckpoints.Add(checkpoint);
    }

    /// <summary>
    /// 체크포인트 정리
    /// </summary>
    [ContextMenu("Clear Checkpoints")]
    public void ClearCheckpoints()
    {
        foreach (var cp in generatedCheckpoints)
        {
            if (cp != null)
            {
                if (Application.isPlaying)
                    Destroy(cp.gameObject);
                else
                    DestroyImmediate(cp.gameObject);
            }
        }
        generatedCheckpoints.Clear();

        Debug.Log("[CheckpointGenerator] 체크포인트 정리 완료");
    }

    /// <summary>
    /// ChunaPathEvaluator에 체크포인트 전달
    /// </summary>
    public void ApplyToEvaluator(ChunaPathEvaluator evaluator)
    {
        if (evaluator == null)
        {
            Debug.LogError("[CheckpointGenerator] ChunaPathEvaluator가 null입니다!");
            return;
        }

        // Evaluator에 직접 체크포인트 설정하는 메서드 필요
        // 현재는 Evaluator가 자체적으로 생성하므로 이 메서드는 참고용
        Debug.Log($"[CheckpointGenerator] {generatedCheckpoints.Count}개 체크포인트 준비됨");
    }

    /// <summary>
    /// 생성된 체크포인트 가져오기
    /// </summary>
    public List<PathCheckpoint> GetGeneratedCheckpoints()
    {
        return generatedCheckpoints;
    }

    /// <summary>
    /// 로드된 프레임 데이터 가져오기
    /// </summary>
    public List<PoseFrame> GetLoadedFrames()
    {
        if (loadedFrames == null || loadedFrames.Count == 0)
        {
            LoadCSV();
        }
        return loadedFrames;
    }

    void OnDrawGizmos()
    {
        if (!drawPathGizmo || loadedFrames == null || loadedFrames.Count == 0) return;

        // 경로 그리기
        Gizmos.color = pathColor;
        for (int i = 1; i < loadedFrames.Count; i++)
        {
            Vector3 prevPos = GetHandCenterPosition(loadedFrames[i - 1]) + positionOffset;
            Vector3 currPos = GetHandCenterPosition(loadedFrames[i]) + positionOffset;

            if (patientReference != null)
            {
                prevPos = patientReference.TransformPoint(prevPos - positionOffset) + positionOffset;
                currPos = patientReference.TransformPoint(currPos - positionOffset) + positionOffset;
            }

            Gizmos.DrawLine(prevPos, currPos);
        }

        // 체크포인트 위치 표시
        Gizmos.color = checkpointColor;
        List<int> indices = GetCheckpointFrameIndices();
        foreach (int idx in indices)
        {
            if (idx >= 0 && idx < loadedFrames.Count)
            {
                Vector3 pos = GetHandCenterPosition(loadedFrames[idx]) + positionOffset;
                if (patientReference != null)
                {
                    pos = patientReference.TransformPoint(pos - positionOffset) + positionOffset;
                }
                Gizmos.DrawWireSphere(pos, triggerRadius);
            }
        }
    }
}
