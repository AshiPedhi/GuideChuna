# 🎬 Hand Pose Data Editor 사용 가이드

## 📦 파일 구성
1. **HandPoseDataEditor.cs** - 메인 편집기 로직
2. **HandPoseEditorUIBuilder.cs** - UI 자동 생성 도구
3. **HandPoseRecorder_Optimized.cs** - 최적화된 녹화 시스템
4. **HandPosePlayer_Optimized.cs** - 부드러운 재생 시스템

## 🚀 설치 방법

### 1. 스크립트 임포트
모든 .cs 파일을 Unity 프로젝트의 Scripts 폴더에 복사

### 2. UI 생성
```
메뉴바 → GameObject → VR Hand Tracking → Hand Pose Editor UI
```

### 3. 컴포넌트 연결
생성된 HandPoseEditorPanel의 Inspector에서:
- **Hand Pose Player**: HandPosePlayer_Optimized 컴포넌트 연결
- **Left/Right Hand Preview**: 미리보기용 HandVisual 연결
- **Left/Right Hand Ghost**: 고스트 핸드용 HandVisual 연결 (선택사항)

## 🎮 주요 기능

### 📹 녹화 (HandPoseRecorder_Optimized)
```csharp
// 녹화 시작
recorder.StartRecording();

// 녹화 중지 및 저장
recorder.StopRecording();

// 설정 조정
recorder.SetAdaptiveSampling(true, 0.05f, 0.3f);  // 적응형 샘플링
recorder.SetKeyframeDetection(true);               // 키프레임 자동 감지
recorder.SetDeltaEncoding(true);                   // 델타 압축
```

**최적화 효과:**
- 파일 크기 85-90% 감소
- 동적 샘플링 (0.05~0.3초)
- 중요 순간 자동 감지

### ✂️ 편집 (HandPoseDataEditor)

#### 타임라인 컨트롤
- **슬라이더 드래그**: 특정 시간으로 이동
- **구간 선택**: 시작점/끝점 슬라이더로 범위 지정
- **키프레임 스냅**: 자동으로 중요 프레임에 맞춤

#### 편집 작업
1. **트림 (Trim)**: 선택한 구간만 남기고 자르기
2. **익스포트 (Export)**: 선택 구간을 별도 파일로 저장
3. **실행 취소/다시 실행**: Ctrl+Z / Ctrl+Y

#### 키보드 단축키
- `Space`: 재생/일시정지
- `S`: 정지
- `[`: 현재 위치를 시작점으로
- `]`: 현재 위치를 끝점으로
- `←/→`: 프레임 단위 이동
- `Shift+←/→`: 키프레임 단위 이동
- `Ctrl+S`: 저장
- `Ctrl+Z`: 실행 취소
- `Ctrl+Y`: 다시 실행

### 🎯 재생 (HandPosePlayer_Optimized)

#### 보간 방식
```csharp
// 보간 타입 설정
player.SetInterpolationType(InterpolationType.CatmullRom);  // 가장 부드러움

// 속도 조절
player.SetPlaybackSpeed(1.5f);  // 1.5배속

// 구간 재생
player.SeekToTime(5.0f);        // 5초 지점으로
player.SeekToProgress(0.5f);    // 50% 지점으로
```

**보간 옵션:**
- **Linear**: 기본 선형 보간
- **Spherical**: 회전에 최적화
- **CatmullRom**: 4점 스플라인 (추천)
- **Cubic**: Hermite S-커브

## 📊 워크플로우 예제

### 시나리오: 추나 동작 녹화 → 편집 → 최종본 생성

```csharp
// 1. 녹화 설정
HandPoseRecorder_Optimized recorder = GetComponent<HandPoseRecorder_Optimized>();
recorder.SetFileName("chuna_technique_01");
recorder.SetRecordingSettings(true, true, 0.15f);  // 양손, 0.15초 간격
recorder.SetPatientModel(patientModel);            // 환자 모델 연결

// 2. 녹화
recorder.StartRecording();
// ... 추나 동작 수행 ...
recorder.StopRecording();

// 3. 편집기에서 로드
HandPoseDataEditor editor = GetComponent<HandPoseDataEditor>();
editor.LoadCSVFile("chuna_technique_01");

// 4. 편집
// - 불필요한 앞뒤 구간 제거
// - 중요 동작만 선택
// - 트림 실행

// 5. 저장
editor.SaveEditedData();  // chuna_technique_01_edited.csv

// 6. 재생 테스트
HandPosePlayer_Optimized player = GetComponent<HandPosePlayer_Optimized>();
player.LoadFromCSV("chuna_technique_01_edited");
player.SetInterpolationType(InterpolationType.CatmullRom);
player.StartPlayback();
```

## 🔧 고급 설정

### 적응형 샘플링 커스터마이징
```csharp
recorder.fastMovementThreshold = 0.5f;   // 빠른 동작 기준 (m/s)
recorder.slowMovementThreshold = 0.1f;   // 느린 동작 기준
recorder.minInterval = 0.05f;            // 최소 간격
recorder.maxInterval = 0.3f;             // 최대 간격
```

### 키프레임 감지 설정
```csharp
recorder.rotationChangeThreshold = 30f;  // 회전 변화 임계값 (도)
recorder.positionChangeThreshold = 0.1f; // 위치 변화 임계값 (m)
recorder.contactDistanceThreshold = 0.05f; // 접촉 감지 거리 (m)
```

### 편집기 UI 커스터마이징
```csharp
editor.keyframeColor = Color.yellow;     // 키프레임 마커 색상
editor.normalFrameColor = Color.gray;    // 일반 프레임 색상
editor.replayHandAlpha = 0.5f;          // 미리보기 투명도
```

## 📝 파일 구조

### CSV 형식 (최적화 버전)
```csv
FrameIndex,HandType,JointID,LocalPosX,LocalPosY,LocalPosZ,
LocalRotX,LocalRotY,LocalRotZ,LocalRotW,Timestamp,
WorldPosX,WorldPosY,WorldPosZ,WorldRotX,WorldRotY,WorldRotZ,WorldRotW,
IsKeyframe,Interval,UseDelta,DeltaPosX,DeltaPosY,DeltaPosZ,
DeltaRotX,DeltaRotY,DeltaRotZ
```

**필드 설명:**
- `IsKeyframe`: 중요 프레임 표시 (1/0)
- `Interval`: 이 프레임의 녹화 간격
- `UseDelta`: 델타 인코딩 사용 여부
- `DeltaPos/Rot`: 이전 프레임과의 차이값

## 💡 성능 팁

### 파일 크기 최소화
1. **적응형 샘플링 활성화**: 필요한 만큼만 데이터 수집
2. **델타 인코딩 사용**: 추가 20-30% 압축
3. **불필요한 구간 제거**: 편집기로 트림

### 재생 품질 향상
1. **CatmullRom 보간 사용**: 가장 자연스러운 움직임
2. **키프레임 기반 보간**: 중요 순간 정확도 향상
3. **적절한 스무스니스 설정**: 0.3~0.7 권장

### VR 성능 최적화
1. **고스트 핸드 비활성화**: 필요시에만 사용
2. **마커 수 제한**: 키프레임만 표시
3. **LOD 설정**: 거리에 따른 품질 조절

## 🐛 문제 해결

### 녹화가 너무 큰 경우
- 녹화 간격 늘리기 (0.2~0.3초)
- 적응형 샘플링 활성화
- 델타 인코딩 사용

### 재생이 끊기는 경우
- 보간 방식을 Linear로 변경
- 재생 속도 낮추기
- 프레임 스킵 허용

### 편집 후 동기화 문제
- 타임스탬프 재정렬 확인
- 프레임 인덱스 검증
- 키프레임 재계산

## 📚 추가 자료
- Unity VR Best Practices
- Meta Quest Hand Tracking Guide
- Catmull-Rom Spline Interpolation

---

**Version**: 1.0.0  
**Last Updated**: 2024  
**Author**: VR Medical Education Team
