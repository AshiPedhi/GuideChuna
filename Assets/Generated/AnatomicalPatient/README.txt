해부학 기반 c9 환자 — 2026-10-22 실험 복제본

열 파일
  Build_20261022_174000/AnatomicalPatientLab.unity
  Build_20261022_174000/AnatomicalPatient_c9.prefab

확인 순서
  1. 현재 작업 씬은 사용자가 보관/저장 여부를 결정한 뒤 위 실험 씬을 연다.
  2. Play는 사람이 시작한다. Game 화면의 Abduction 150, Elbow 100,
     Forearm 80, Neck flexion 30, Neck rotation 45를 차례로 누른다.
  3. Skin/Bones/Muscles로 같은 자세를 비교한다.
     Inspect skin without shirt로 어깨·겨드랑이·목 피부를 본다.
     Scene 뷰에서 뒤로 돌아 견갑골과 등 피부를 확인한다.
  4. 임의 각도는 환자 루트의 AnatomicalPatientRig > Manual Pose에서 조절한다.
     편집 모드에서는 AnatomicalPatientLab의 Evaluate inspector pose를 누른다.
     프리팹 자산 자체에는 자세 버튼을 적용하지 않는다.
  5. Rest T pose로 기준 자세 복원, 콘솔에 AnatomicalRig 오류가 없는지 확인한다.

새 컴포넌트와 저장된 값
  실험 환자 루트 AnatomicalPatientRig:
    Input Source=Manual, Evaluation Mode=LateUpdate, Diagnostics=false.
    Manual Pose는 원본 c9에서 측정한 T 자세. Shoulder Elevation은
    0=아래, 90=수평, 180=위. Elevation Plane은 0=앞, 90=옆.
    Forearm/Humeral Rotation은 근위→원위 축 주위의 수학적 부호이다.
    좌우에 동일한 부호가 같은 임상 용어를 뜻한다고 검증하지 않았다.
    Forearm80 프리셋은 좌 +80/우 -80을 사용하여 양측 같은 방향으로 회전한다.
    팔꿈치 굽힘 방향은 c9 Avatar의 Forearm Stretch=-0.5에서 실측했다.
    원본 T 자세의 작은 과신전 약 -0.823도는 외형 보존용 기준으로만 유지한다.
    Neck Degrees는 x=굴곡, y=오른쪽 측굴, z=오른쪽 회전.
    참조와 기준 좌표는 생성기가 연결했다. 기존 씬 필드 기본값을 바꾸지 않았다.
  AnatomicalPatientLab:
    View=Skin, Hide Upper Clothing=false, Show Desktop Controls=true.
    화면 버튼은 Unity Editor에서만 실행한다. Quest용 UI가 아니다.
  Animator는 c9_AnimationInput에 있으며 원본 c9 Humanoid Avatar를 유지한다.
    Animator Controller는 비어 있고 원본 리그는 입력용이다.

구조
  c9 원본 메시/FBX/Avatar/기존 TrainingScene을 교체하지 않았다.
  피부·얼굴·옷 메시를 별도 에셋으로 복제하고 새 출력 리그에 다시 바인딩했다.
  원본에서 실제 렌더되는 중립 정점을 Bake하여 외형을 보존했다.
  기존 얼굴/체형 blendshape 이름과 순서도 보존했다.
  피부의 상완/전완 twist 가중치를 새 상완골·척골·요골로 나누었다.
  어깨 뒤 피부 일부에 견갑골 영향과 작은 어깨/팔꿈치 부피 보정을 추가했다.
  Shirt는 가까운 Body 삼각형의 가중치와 보정 shape를 보간해 따라간다.
  AnatomyV2_3의 상체 메시 126개를 c9 관절 표지에 맞추어 정렬했다.
  견갑골/쇄골, 요골/척골은 서로 독립된 출력 관절이다.
  경추는 실제 분리된 기하 7개를 아래→위 순서로 배치했다.
  C1~C7 개별 해부학 명칭과 관절면 중심은 임상 대조를 하지 않았다.
  손 골격의 최초 정렬은 실제 baked 중수골의 손바닥·엄지 표지를 c9에 맞춘다.
  상완/척골은 팔꿈치 굽힘면으로, 요골은 남은 전완 회전으로 각각 정렬한다.
  기존의 길이 방향만 맞춘 정렬에서 발생한 손바닥 약 94.925도 오차를 교정했다.

기존 애니메이션/IK 입력 연결
  원본 c9 본을 움직이는 Animator Controller/IK를 c9_AnimationInput 쪽에
  연결한 후 Rig.SetInputSource(AnatomicalPatientRig.InputSource.Animation)을
  호출한다. LateUpdate(실행순서 20000)에서 입력 자세를 읽어 출력에 반영한다.
  기존 VR 파지·시나리오·반사 스케일의 TrainingScene 계층에는 연결하지 않았다.
  기존 팔 길이/목 동작/파지 기준을 함께 확인한 뒤 통합해야 한다.

검증 범위
  Build_20261022_174000/Verification.json에 실제 Unity Editor 측정값을 보관했다.
  중립/표정 정점 비교, 137개 스킨 가중치/본 참조, 어깨 피부 변화,
  좌우 견갑골 대칭/후방 기울기, 요골 단독 회전과 c9 입력 추종,
  복합 목 회전 끝점 일치, 120회 자세 변경/복원 검사를 통과했다.
  Elbow100/Forearm80 양측 전방 굽힘과 100도/90도 각도,
  Forearm80 양측 회전의 대칭도 함께 검증했다.
  Rest/Elbow100/Forearm80에서 실제 골격 정점과 피부 손가락 표지의
  손바닥·엄지 축 오차도 검사한다. PalmAlignmentBefore.json/PalmAlignment.json에
  교정 전후 계측을 보관했다. 정렬 일치는 임상 정확성 전체의 검증을 뜻하지 않는다.
  Runtime/Editor csproj 비증분 빌드 오류 0개, 변경 파일 경고 0개.
  change-impact 결과는 Impact.txt에 보관한다.
  손바닥 정렬 교정의 별도 파급 판독은 PalmImpact.txt에 보관한다.
  Previews 이미지는 동일 런타임 해법의 CPU BakeMesh 정점을 렌더했다.
  여러 자세를 같은 Editor tick에 그리면 native preview가 한 자세 늦는
  현상을 확인하여 촬영 중에만 임시 정적 메시로 바꾸었다.

현재 한계
  어깨 리듬, 견갑골 중심/흉곽 접촉, 전완 회전축, 경추 각도 분배,
  근육 부착/형상과 피부 보정은 제작용 근사다. 환자별 검증 모델이 아니다.
  근육 힘·수동 저항·인대·연골·접촉·연부조직 물리는 구현하지 않았다.
  흉추/요추의 상세 분절은 추가하지 않았다.
  Shirt 겨드랑이에 작은 피부 관통이 남아 있다.
  Play, 기존 VR 파지 통합, Quest 성능, 임상 정확성은 미검증이다.
  아직 기존 실습 환자를 교체할 단계로 판정하지 않았다.

다시 만들기
  GuideChuna > 시험 > 해부학 환자 > 새 재리깅 복제본 생성
  GuideChuna > 시험 > 해부학 환자 > 최신 복제본 검증 및 이미지
  GuideChuna > 시험 > 해부학 환자 > 팔꿈치 방향 계측
  GuideChuna > 시험 > 해부학 환자 > 최신 복제본 팔꿈치 기준 교정
  GuideChuna > 시험 > 해부학 환자 > 피부 골격 손바닥 정렬 계측
  GuideChuna > 시험 > 해부학 환자 > 현재 복제본 골격 손 정렬 재생성
  생성은 편집 모드에서 새 폴더로만 진행한다. 기존 씬을 저장하지 않는다.
  임상 적합성 수치가 아니라 구현 정합성 수치를 검증한다.
