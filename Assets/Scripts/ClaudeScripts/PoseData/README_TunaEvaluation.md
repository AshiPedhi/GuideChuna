# 추나 평가 시스템 사용 가이드

## 개요

추나 시술 교육용 평가 시스템입니다. 일반 프레임 통과 모드와 추나 평가 모드를 쉽게 전환할 수 있습니다.

## 모드 비교

### 일반 프레임 통과 모드 (기본)
- ✅ 프레임 유사도 체크
- ✅ 연속 프레임 통과 시스템
- ✅ 색상 피드백 (빨강→노랑→초록)
- ✅ UI 진행률 표시

### 추나 평가 모드
- ✅ **일반 모드의 모든 기능 포함**
- ✅ 4개 카테고리 점수 (총 100점)
  - 경로 준수도: 40점
  - 안전성: 30점
  - 정확도: 20점
  - 안정성: 10점
- ✅ 제한장벽(45도) 감지 및 경고
- ✅ 체크포인트 검증
- ✅ 안전 위반 기록
- ✅ 경로 이탈 감지
- ✅ 최종 평가 리포트
- ✅ 등급 산출 (A+~F)

---

## 빠른 시작 (Unity Inspector에서)

### 1. TunaEvaluationSetup 컴포넌트 추가

```
1. HandPoseTrainingController가 있는 GameObject 선택
2. Add Component → TunaEvaluationSetup
```

### 2. 모드 선택

**일반 모드 (기본)**:
```
Enable Tuna Evaluation: ☐ (체크 해제)
```

**추나 평가 모드**:
```
Enable Tuna Evaluation: ☑ (체크)
```

### 3. 구간 설정 (추나 평가 모드만)

```
Total Frames: 100            (CSV 총 프레임 수)
Number Of Segments: 3        (구간 개수)
Checkpoint Frames: "30,60,90" (체크포인트 프레임)
Max Rotation Angle: 45       (제한장벽 각도)
Max Distance: 0.3            (최대 이동 거리)
```

### 4. 자동 적용

```
▶ Play 모드 시작 시 자동으로 설정 적용
```

---

## 수동 설정 (고급)

### TunaEvaluator에 직접 구간 추가

1. **TunaEvaluator 컴포넌트 추가**
   ```
   Add Component → TunaEvaluator
   ```

2. **구간 설정**
   ```csharp
   Motion Segments:

   - 구간 1:
     Segment Name: "경추 회전"
     Start Frame: 0
     End Frame: 30
     Check Safety Limits: ☑
     Left Hand Max Rotation: 45
     Right Hand Max Rotation: 45
     Require Path Following: ☑
     Path Tolerance: 0.05
     Is Checkpoint: ☑
     Required Hold Time: 2.0

   - 구간 2:
     Segment Name: "경추 신전"
     Start Frame: 31
     End Frame: 60
     ...
   ```

3. **HandPoseTrainingController 설정**
   ```
   Enable Tuna Evaluation: ☑
   Tuna Evaluator: (TunaEvaluator 컴포넌트 드래그)
   Tuna Result UI: (TunaResultUI 컴포넌트 드래그)
   ```

---

## 구간 설정 예시

### 예시 1: 3개 구간 (기본)

```
총 프레임: 90
구간 1: 0-29 (준비 자세)
구간 2: 30-59 (시술 진행) - 체크포인트
구간 3: 60-89 (마무리) - 체크포인트
```

### 예시 2: 5개 구간 (상세)

```
총 프레임: 150
구간 1: 0-29 (접촉 및 평가)
구간 2: 30-59 (저항점 탐색)
구간 3: 60-89 (제한장벽 도달) - 체크포인트
구간 4: 90-119 (시술 수행) - 체크포인트
구간 5: 120-149 (정리 및 확인)
```

---

## 평가 항목 상세

### 1. 경로 준수도 (40점)
- **측정**: 프레임마다 가이드 경로와의 거리 오차
- **기준**: `pathTolerance` (기본 5cm)
- **계산**: (경로 내 프레임 수 / 전체 프레임) × 40

### 2. 안전성 (30점)
- **측정**: 제한장벽(45도) 위반 횟수
- **기준**: `maxRotation`, `maxDistance`
- **계산**: 30 - (위반 횟수 × 감점)

### 3. 정확도 (20점)
- **측정**: 체크포인트 통과 여부
- **기준**: `checkpointSimilarityThreshold` (기본 80%)
- **계산**: (통과한 체크포인트 / 전체 체크포인트) × 20

### 4. 안정성 (10점)
- **측정**: 체크포인트 유지 시간
- **기준**: `requiredHoldTime` (기본 2초)
- **계산**: (실제 유지 시간 / 필요 시간) × 10

---

## 등급 기준

| 점수 | 등급 |
|------|------|
| 90점 이상 | A+ |
| 85점 이상 | A |
| 80점 이상 | B+ |
| 75점 이상 | B |
| 70점 이상 | C+ |
| 65점 이상 | C |
| 60점 이상 | D |
| 60점 미만 | F |

---

## Context Menu 기능

### Apply Settings
```
우클릭 → Apply Settings
현재 설정을 즉시 적용
```

### Clear Segments
```
우클릭 → Clear Segments
모든 구간 초기화
```

---

## 디버그 로그

```csharp
Show Setup Logs: ☑
```

활성화 시 콘솔에 다음 정보 출력:
- 모드 전환 확인
- 구간 설정 내역
- 평가 시작/종료
- 점수 계산 결과

---

## 최종 결과 리포트 예시

```
========== 추나 시술 평가 결과 ==========

총점: 87.5/100 (87.5%) - A등급
경로 준수: 36.0/40
안전성: 27.0/30
정확도: 16.0/20
안정성: 8.5/10

수행 시간: 45.2초

[안전 위반 내역: 2건]
  - 프레임 25: Right RotationExceeded - 48.5 (한계: 45.0)
  - 프레임 67: Left DistanceExceeded - 0.32 (한계: 0.30)

[체크포인트 결과]
  - 구간 2: 통과 (유사도 85%, 유지 2.1초) - 16점
  - 구간 4: 통과 (유사도 92%, 유지 2.5초) - 20점

=======================================
```

---

## 문제 해결

### Q: 평가가 동작하지 않아요
A:
1. `Enable Tuna Evaluation`이 체크되어 있는지 확인
2. TunaEvaluator 컴포넌트가 추가되어 있는지 확인
3. Console에서 "[TunaSetup]" 로그 확인

### Q: 구간이 설정되지 않아요
A:
1. `Total Frames`가 올바른지 확인
2. `Number Of Segments`가 0이 아닌지 확인
3. Context Menu → Clear Segments 후 재시도

### Q: 일반 모드로 돌아가려면?
A:
1. `Enable Tuna Evaluation` 체크 해제
2. Play 모드 재시작

---

## 파일 구조

```
PoseData/
├── HandPoseTrainingController.cs  (메인 컨트롤러)
├── TunaEvaluator.cs               (평가 로직)
├── TunaMotionData.cs              (데이터 구조)
├── TunaEvaluationSetup.cs         (설정 도우미) ★
├── TunaEvaluationUI.cs            (실시간 UI)
├── TunaResultUI.cs                (결과 UI)
└── README_TunaEvaluation.md       (이 파일)
```

---

## 참고 사항

- 추나 평가 모드는 일반 모드의 모든 기능을 포함합니다
- 평가 중에도 프레임 통과 시스템은 동일하게 동작합니다
- 리플레이 기능은 나중에 추가될 예정입니다
- 평가 결과는 Console과 UI 양쪽에 표시됩니다
