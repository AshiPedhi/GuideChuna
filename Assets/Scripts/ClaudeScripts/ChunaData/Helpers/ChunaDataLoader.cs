using System.Collections.Generic;
using UnityEngine;
using static HandPoseDataLoader;

/// <summary>
/// CSV data loading and checkpoint generation helper for ChunaPathEvaluator.
/// Handles frame loading, movement analysis, preset application, and difficulty sync.
/// </summary>
public class ChunaDataLoader
{
    private readonly ChunaPathEvaluator owner;

    public ChunaDataLoader(ChunaPathEvaluator owner)
    {
        this.owner = owner;
    }

    /// <summary>
    /// CSV 파일 로드 및 체크포인트 생성
    /// </summary>
    public void LoadAndGenerateCheckpoints(string csvFileName)
    {
        if (owner.ShowDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] CSV 로드: {csvFileName}</color>");

        owner.CurrentProcedureName = csvFileName;

        HandPoseDataLoader loader = new HandPoseDataLoader();
        var result = loader.LoadFromResources($"HandPoseData/{csvFileName}");

        if (!result.success)
        {
            ChunaLogger.LogError($"[ChunaPathEvaluator] CSV 로드 실패: {result.errorMessage}");
            return;
        }

        owner.LoadedFrames = result.frames;

        // ★ 기준점 로컬 좌표 → 월드 좌표 변환 (로드 시 일괄 변환)
        ConvertFramesToWorldSpace();

        if (owner.ShowDebugLogs)
            ChunaLogger.Log($"[ChunaPathEvaluator] {owner.LoadedFrames.Count}개 프레임 로드됨");

        // ★ 핸드데이터 이동량 계산 (시작-끝 프레임 간)
        CalculateHandDataMovement();

        // ★ 측굴 자동 프리셋 적용
        ApplyLateralBendingPresetIfNeeded(csvFileName);

        // 리밋 체커에 첫 프레임의 회전을 기준점으로 설정
        SetLimitCheckerReferenceFromFirstFrame();

        if (owner.ShowDebugLogs)
        {
            ChunaLogger.Log($"<color=green>[ChunaPathEvaluator] 데이터 로드 완료</color>");
            ChunaLogger.Log($"  - 프레임 수: {owner.LoadedFrames.Count}개");
        }
    }

    /// <summary>
    /// 핸드데이터 시작-끝 프레임 간 이동량/회전량 계산
    /// </summary>
    private void CalculateHandDataMovement()
    {
        var loadedFrames = owner.LoadedFrames;
        if (loadedFrames == null || loadedFrames.Count < 2)
        {
            ChunaLogger.LogWarning("[ChunaPathEvaluator] 프레임이 2개 미만이라 이동량 계산 불가");
            return;
        }

        PoseFrame firstFrame = loadedFrames[0];
        PoseFrame lastFrame = loadedFrames[loadedFrames.Count - 1];

        // ★ 양손 이동량 비교 → 더 많이 움직인 손 기준
        Vector3 rightMoveVec = lastFrame.rightRootPosition - firstFrame.rightRootPosition;
        Vector3 leftMoveVec = lastFrame.leftRootPosition - firstFrame.leftRootPosition;
        float rightDist = rightMoveVec.magnitude;
        float leftDist = leftMoveVec.magnitude;

        owner.IsLeftHandDominant = leftDist > rightDist;

        if (owner.IsLeftHandDominant)
        {
            owner.HandDataMovementVector = leftMoveVec;
            owner.HandDataTotalDistance = leftDist;
            ChunaLogger.Log($"<color=cyan>[HandData] 왼손 이동량이 더 큼: L={leftDist:F3}m > R={rightDist:F3}m → 왼손 기준</color>");
        }
        else
        {
            owner.HandDataMovementVector = rightMoveVec;
            owner.HandDataTotalDistance = rightDist;
            ChunaLogger.Log($"<color=cyan>[HandData] 오른손 기준: R={rightDist:F3}m, L={leftDist:F3}m</color>");
        }

        // ★ 주동수에 높은 가중치 적용 (주동수 70%, 보조수 30%)
        if (owner.IsLeftHandDominant)
        {
            owner.LeftHandSimilarityWeight = 0.7f;
            owner.RightHandSimilarityWeight = 0.3f;
        }
        else
        {
            owner.LeftHandSimilarityWeight = 0.3f;
            owner.RightHandSimilarityWeight = 0.7f;
        }
        // comparator에도 가중치 동기화
        if (owner.PoseComparator != null)
        {
            var settings = owner.PoseComparator.GetSettings();
            settings.leftHandWeight = owner.LeftHandSimilarityWeight;
            settings.rightHandWeight = owner.RightHandSimilarityWeight;
        }
        ChunaLogger.Log($"<color=cyan>[HandData] 유사도 가중치: L={owner.LeftHandSimilarityWeight:P0}, R={owner.RightHandSimilarityWeight:P0}</color>");

        // 이동 축 계산 (정규화)
        if (owner.HandDataTotalDistance > 0.001f)
        {
            owner.MovementAxis = owner.HandDataMovementVector.normalized;
        }
        else
        {
            owner.MovementAxis = Vector3.forward;
        }

        // ★ 양손 회전량 비교 → 더 많이 회전한 손 기준
        float rightRot = Quaternion.Angle(firstFrame.rightRootRotation, lastFrame.rightRootRotation);
        float leftRot = Quaternion.Angle(firstFrame.leftRootRotation, lastFrame.leftRootRotation);
        owner.HandDataTotalRotation = Mathf.Max(rightRot, leftRot);

        // 위치 기반 vs 회전 기반 결정
        // ★ CSV에서 지정한 타입이 있으면 그것을 사용, 없으면 자동 감지
        if (!string.IsNullOrEmpty(owner.SpecifiedMovementType))
        {
            // StartsWith로 비교 (혹시 모를 공백/특수문자 대비)
            owner.IsPositionBasedMovement = owner.SpecifiedMovementType.StartsWith("position");
            string moveType = owner.IsPositionBasedMovement ? "위치 기반" : "회전 기반";
            ChunaLogger.Log($"<color=magenta>[HandData Analysis] ★ {moveType} (CSV 지정: '{owner.SpecifiedMovementType}')</color>");
        }
        else
        {
            // 자동 감지: 이동 거리가 5cm 미만이고 회전이 15도 이상이면 회전 기반으로 판단
            owner.IsPositionBasedMovement = owner.HandDataTotalDistance >= 0.05f || owner.HandDataTotalRotation < 15f;
            if (owner.ShowDebugLogs)
            {
                string moveType = owner.IsPositionBasedMovement ? "위치 기반" : "회전 기반";
                ChunaLogger.Log($"<color=magenta>[HandData Analysis] {moveType} (자동 감지)</color>");
            }
        }

        // 항상 출력 (디버깅용)
        ChunaLogger.Log($"<color=cyan>[HandData] L: {firstFrame.leftRootPosition}→{lastFrame.leftRootPosition} ({leftDist:F3}m), R: {firstFrame.rightRootPosition}→{lastFrame.rightRootPosition} ({rightDist:F3}m)</color>");
        ChunaLogger.Log($"<color=cyan>[HandData] 이동 거리: {owner.HandDataTotalDistance:F3}m, 회전 각도: {owner.HandDataTotalRotation:F1}°</color>");
        ChunaLogger.Log($"<color=cyan>[HandData] 이동 축: {owner.MovementAxis}, 판정: {(owner.IsPositionBasedMovement ? "위치기반" : "회전기반")}</color>");

        // ★ 피벗 기반 총 각도 자동 계산
        CalculatePivotBasedAngle();
    }

    /// <summary>
    /// 피벗 기반으로 핸드데이터의 총 각도 계산 (첫 프레임 ~ 마지막 프레임)
    /// </summary>
    private void CalculatePivotBasedAngle()
    {
        var loadedFrames = owner.LoadedFrames;
        if (loadedFrames == null || loadedFrames.Count < 2) return;
        if (owner.PivotTransform == null)
        {
            ChunaLogger.LogWarning("[ChunaPathEvaluator] 피벗이 설정되지 않아 각도 자동 계산 불가");
            return;
        }

        Vector3 pivotPos = owner.PivotTransform.position;

        // ★ 양손 중 더 많이 움직인 손 기준으로 피벗 각도 계산
        PoseFrame first = loadedFrames[0];
        PoseFrame last = loadedFrames[loadedFrames.Count - 1];
        float rightDist = (last.rightRootPosition - first.rightRootPosition).magnitude;
        float leftDist = (last.leftRootPosition - first.leftRootPosition).magnitude;

        Vector3 startPos, endPos;
        if (leftDist > rightDist)
        {
            startPos = first.leftRootPosition;
            endPos = last.leftRootPosition;
            ChunaLogger.Log($"<color=cyan>[Pivot Angle] 왼손 기준 (L={leftDist:F3}m > R={rightDist:F3}m)</color>");
        }
        else
        {
            startPos = first.rightRootPosition;
            endPos = last.rightRootPosition;
            ChunaLogger.Log($"<color=cyan>[Pivot Angle] 오른손 기준 (R={rightDist:F3}m, L={leftDist:F3}m)</color>");
        }

        // 피벗에서 시작/끝 위치로의 방향
        Vector3 startDir = (startPos - pivotPos).normalized;
        Vector3 endDir = (endPos - pivotPos).normalized;

        // 평면 법선 기준 각도 계산
        Vector3 planeNormal = GetPivotPlaneNormal(owner.PivotPlaneAxis);

        // 부호 있는 각도 계산
        float signedAngle = Vector3.SignedAngle(startDir, endDir, planeNormal);
        owner.CalculatedDataAngle = Mathf.Abs(signedAngle);

        // 각도가 너무 작으면 (직선 이동) 손목 회전 각도 사용
        if (owner.CalculatedDataAngle < 5f)
        {
            owner.CalculatedDataAngle = owner.HandDataTotalRotation;
        }

        // 자동 계산 활성화 시 targetAngle 설정 (비율 적용)
        if (owner.AutoCalculateTargetAngle && owner.CalculatedDataAngle > 0.1f)
        {
            owner.TargetAngle = owner.CalculatedDataAngle * owner.DefaultGuideRatio;
            ChunaLogger.Log($"<color=green>[ChunaPathEvaluator] ★ 목표 각도 자동 설정: {owner.TargetAngle:F1}° (데이터 {owner.CalculatedDataAngle:F1}° × 비율 {owner.DefaultGuideRatio:P0})</color>");
        }

        ChunaLogger.Log($"<color=magenta>[HandData Angle] 피벗 기준 총 각도: {owner.CalculatedDataAngle:F1}° (시작→끝 프레임)</color>");
    }

    /// <summary>
    /// ★ 피벗 각도 측정 평면의 법선 축 반환 (공유 유틸리티)
    /// </summary>
    public static Vector3 GetPivotPlaneNormal(ChunaPathEvaluator.RotationDetectionAxis pivotPlaneAxis)
    {
        switch (pivotPlaneAxis)
        {
            case ChunaPathEvaluator.RotationDetectionAxis.Y:
                return Vector3.up;      // Y축 기준 평면 (XZ 평면에서 각도 측정)
            case ChunaPathEvaluator.RotationDetectionAxis.Z:
                return Vector3.forward; // Z축 기준 평면 (XY 평면에서 각도 측정) - 측굴용
            case ChunaPathEvaluator.RotationDetectionAxis.X:
                return Vector3.right;   // X축 기준 평면 (YZ 평면에서 각도 측정)
            default:
                return Vector3.forward; // 기본: 측굴용 (Z축)
        }
    }

    /// <summary>
    /// 운동 종류 감지 후 자동으로 프리셋 적용 (측굴, 회전)
    /// </summary>
    private void ApplyLateralBendingPresetIfNeeded(string csvFileName)
    {
        if (!owner.AutoApplyLateralBendingPreset) return;
        if (string.IsNullOrEmpty(csvFileName)) return;

        // 측굴 키워드 감지
        bool isLateralBending = csvFileName.Contains("측굴") ||
                                csvFileName.ToLower().Contains("lateral") ||
                                csvFileName.ToLower().Contains("sidebend");

        // 회전 키워드 감지
        bool isRotation = csvFileName.Contains("회전") ||
                          csvFileName.ToLower().Contains("rotation") ||
                          csvFileName.ToLower().Contains("rotate");

        if (isLateralBending)
        {
            // 측굴 감지됨 - 프리셋 적용
            ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] ★ 측굴 운동 감지 - 프리셋 적용</color>");

            // 기본 가이드 비율을 제한장벽 확인 모드로 설정 (0~0.5)
            owner.DefaultGuideRatio = owner.LateralBending_LimitCheckRatio;

            // 스트레칭 모드 범위 설정 - 통합 설정(stretchingStart/End/HoldStart) 사용
            // ★ Inspector에서 직접 조절하므로 여기서 덮어쓰지 않음

            // targetAngle 재계산 (비율 적용)
            if (owner.AutoCalculateTargetAngle && owner.CalculatedDataAngle > 0.1f)
            {
                owner.TargetAngle = owner.CalculatedDataAngle * owner.DefaultGuideRatio;
            }

            ChunaLogger.Log($"<color=cyan>  - 제한장벽 확인: 0 ~ {owner.LateralBending_LimitCheckRatio:P0} ({owner.CalculatedDataAngle * owner.LateralBending_LimitCheckRatio:F1}°)</color>");
            ChunaLogger.Log($"<color=cyan>  - 스트레칭: 가이드 {owner.StretchingGuideStart:P0}~{owner.StretchingGuideEnd:P0}, 적정범위 {owner.StretchingHoldStartRatio:P0}~{owner.StretchingGuideEnd:P0}</color>");
            ChunaLogger.Log($"<color=cyan>  - 재평가: 0 ~ {owner.LateralBending_ReEvalRatio:P0} ({owner.CalculatedDataAngle * owner.LateralBending_ReEvalRatio:F1}°)</color>");
        }
        else if (isRotation)
        {
            // 회전 감지됨 - 가이드 0 ~ 0.4
            ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] ★ 회전 운동 감지 - 가이드 범위 설정</color>");

            owner.DefaultGuideRatio = 0.4f;

            // ★ 가이드 재생 범위: 0 ~ 0.4
            owner.RuntimeGuideStartRatio = owner.GuideRotation_Start;
            owner.RuntimeGuideEndRatio = owner.GuideRotation_End;

            // targetAngle 재계산 (비율 적용)
            if (owner.AutoCalculateTargetAngle && owner.CalculatedDataAngle > 0.1f)
            {
                owner.TargetAngle = owner.CalculatedDataAngle * owner.DefaultGuideRatio;
            }

            ChunaLogger.Log($"<color=cyan>  - 가이드 범위: {owner.GuideRotation_Start:P0} ~ {owner.GuideRotation_End:P0} ({owner.TargetAngle:F1}°)</color>");
        }
    }

    /// <summary>
    /// 리밋 체커에 PathEvaluator 참조 설정
    /// </summary>
    private void SetLimitCheckerReferenceFromFirstFrame()
    {
        if (owner.LimitChecker == null)
            return;

        // PathEvaluator 참조 설정 (프레임 비율 기반 체크용)
        owner.LimitChecker.SetPathEvaluator(owner);

        if (owner.ShowDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] 리밋 체커에 PathEvaluator 참조 설정 완료</color>");
    }

    /// <summary>
    /// 리밋 체커 임계값을 적정범위 끝(runtimeGuideEndRatio) 기반으로 자동 설정
    /// </summary>
    internal void UpdateLimitCheckerFromGuideEnd()
    {
        if (owner.LimitChecker == null) return;

        float dangerThreshold = owner.RuntimeGuideEndRatio;
        // ★ 스트레칭 모드: 진행도가 StartHold(stretchingStart) 기준 상대값이므로 임계점도 오프셋 차감
        if (owner.IsStretchingMode)
            dangerThreshold -= owner.StretchingGuideStart;
        float warningThreshold = dangerThreshold * 0.7f;
        owner.LimitChecker.SetRatioThresholds(warningThreshold, dangerThreshold);

        if (owner.ShowDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] 리밋 체커 임계값 자동 설정 — warning:{warningThreshold:P0}, danger:{dangerThreshold:P0} (guideEnd:{owner.RuntimeGuideEndRatio:P0}, stretching:{owner.IsStretchingMode})</color>");
    }

    /// <summary>
    /// 난이도 프리셋에서 가이드 핸드 표시/투명도/색상 피드백 동기화
    /// </summary>
    internal void SyncWithDifficultySettings()
    {
        var dm = ChunaTraining.DifficultyManager.Instance;
        if (dm == null) return;

        owner.ShowGuideHandsField = dm.ShowGuideHands;

        // 투명도를 guideHandColor.a에 반영
        if (dm.GuideHandOpacity > 0f)
        {
            var color = owner.GuideHandColor;
            color.a = dm.GuideHandOpacity;
            owner.GuideHandColor = color;
        }

        // 홀드 시간 동기화 (등척성운동은 CSV duration 우선 — 덮어쓰지 않음)
        if (dm.RequiredHoldTime > 0f)
        {
            if (!owner.IsStartHoldOnly)
                owner.StartHoldDuration = dm.RequiredHoldTime;
            owner.MidHoldDuration = dm.RequiredHoldTime;
        }

        if (owner.ShowDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] 난이도 동기화 — 가이드핸드:{owner.ShowGuideHandsField}, 투명도:{owner.GuideHandColor.a:F2}, 홀드:{dm.RequiredHoldTime:F1}s</color>");
    }

    /// <summary>
    /// 로드된 프레임의 기준점 로컬 좌표를 월드 좌표로 일괄 변환
    /// </summary>
    private void ConvertFramesToWorldSpace()
    {
        var loadedFrames = owner.LoadedFrames;
        if (loadedFrames == null || loadedFrames.Count == 0) return;
        if (owner.ReferenceTransform == null)
        {
            if (owner.ShowDebugLogs)
                ChunaLogger.LogWarning("[ChunaPathEvaluator] referenceTransform 없음 - 프레임 좌표를 그대로 사용");
            return;
        }

        Vector3 refPos = owner.ReferenceTransform.position;
        Quaternion refRot = owner.ReferenceTransform.rotation;

        for (int i = 0; i < loadedFrames.Count; i++)
        {
            var frame = loadedFrames[i];

            // 왼손 루트
            frame.leftRootPosition = refPos + refRot * frame.leftRootPosition;
            frame.leftRootRotation = refRot * frame.leftRootRotation;

            // 오른손 루트
            frame.rightRootPosition = refPos + refRot * frame.rightRootPosition;
            frame.rightRootRotation = refRot * frame.rightRootRotation;
        }

        if (owner.ShowDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] {loadedFrames.Count}개 프레임 월드 좌표 변환 완료 (ref: {owner.ReferenceTransform.name}, pos={refPos}, rot={refRot.eulerAngles})</color>");
    }
}
