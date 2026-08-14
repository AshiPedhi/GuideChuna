using UnityEngine;
using TMPro;

/// <summary>
/// Collision detection and related UI/animation helper for ChunaPathEvaluator.
/// Extracted from the "Collision Detection" region.
/// </summary>
public class CollisionDetectionManager
{
    private readonly ChunaPathEvaluator owner;
    private readonly HandCollisionDetector collisionDetector;

    public CollisionDetectionManager(ChunaPathEvaluator owner, HandCollisionDetector collisionDetector)
    {
        this.owner = owner;
        this.collisionDetector = collisionDetector;
    }

    /// <summary>
    /// Input context filled by ChunaPathEvaluator each frame before calling Update methods.
    /// Avoids exposing many individual internal accessors for SerializeField values.
    /// </summary>
    public struct CollisionUpdateContext
    {
        // Hand transforms & colliders
        public Transform leftHandTransform;
        public Transform rightHandTransform;
        public Collider leftHandCollider;
        public Collider rightHandCollider;

        // Patient colliders
        public Collider patientHeadCollider;
        public Collider patientShoulderCollider;
        public Collider patientChestCollider;
        public Collider[] patientBackColliders;
        public Collider[] patientShoulderCollidersExtra;
        public Collider[] patientLeftArmColliders;
        public Collider[] patientRightArmColliders;
        public Collider[] patientKneeColliders;

        // Contact targets
        public ContactTarget[] activeContactTargets;
        public ContactTarget primaryTarget;

        // Hand collision shape settings
        public ChunaPathEvaluator.HandCollisionShape handCollisionShape;
        public float handColliderScale;
        public float defaultHandCollisionRadius;
        public float handCollisionForwardOffset;
        public float palmWidth;
        public float palmThickness;
        public float palmHeight;
        public float fingerLength;

        // Guide hand fade settings
        public bool fadeOnTouch;
        public float touchAlpha;
        public Color guideHandColor;
        public HandTransformMapper leftGuideHand;
        public HandTransformMapper rightGuideHand;

        // Debug UI references
        public bool showDebugUI;
        public bool showDebugLogs;
        public TextMeshProUGUI fpsText;
        public TextMeshProUGUI leftHandDistanceText;
        public TextMeshProUGUI rightHandDistanceText;
        public float fpsUpdateInterval;

        // Wrist bones (for distance calculation)
        public Transform leftWristBone;
        public Transform rightWristBone;

        // Animation
        public Animator patientAnimator;
        public Animator secondaryPatientAnimator;
        public string currentAnimationStateName;
        public float targetAnimationRatio;
        public float animationLerpSpeed;
        public ChunaPathEvaluator.EvaluationPhase currentPhase;
        public float userHandFrameRatio;
    }

    // Debug UI internal state
    private float fpsTimer = 0f;
    private int frameCount = 0;
    private float currentFps = 0f;

    /// <summary>
    /// 충돌 감지 업데이트 (거리 기반, 스케일 적용)
    /// 콜라이더가 없으면 손 Transform 위치 사용
    /// ★ HandCollisionShape에 따라 구형/박스형/손바닥만 감지
    /// </summary>
    public void UpdateCollisionDetection(in CollisionUpdateContext ctx)
    {
        if (ctx.activeContactTargets == null || ctx.activeContactTargets.Length == 0) return;

        // 이전 접촉 상태 저장
        bool wasLeftTouching = owner.IsLeftHandTouchingPatient;
        bool wasRightTouching = owner.IsRightHandTouchingPatient;

        // 왼손 충돌 감지 (모든 activeContactTargets에 대해 OR)
        if (ctx.leftHandTransform != null)
        {
            bool leftTouching = CheckHandTouchForAnyTarget(ctx.leftHandTransform, ctx.leftHandCollider, true, ctx);
            bool leftOnPrimary = CheckHandTouchForSingleTarget(ctx.leftHandTransform, ctx.leftHandCollider, true, ctx.primaryTarget, ctx);
            owner.SetLeftHandTouchState(leftTouching, leftOnPrimary);

            if (leftTouching && !wasLeftTouching && ctx.showDebugLogs)
                ChunaLogger.Log($"<color=green>[Collision] 왼손이 환자에 닿음! (주동수:{leftOnPrimary})</color>");
        }

        // 오른손 충돌 감지
        if (ctx.rightHandTransform != null)
        {
            bool rightTouching = CheckHandTouchForAnyTarget(ctx.rightHandTransform, ctx.rightHandCollider, false, ctx);
            bool rightOnPrimary = CheckHandTouchForSingleTarget(ctx.rightHandTransform, ctx.rightHandCollider, false, ctx.primaryTarget, ctx);
            owner.SetRightHandTouchState(rightTouching, rightOnPrimary);

            if (rightTouching && !wasRightTouching && ctx.showDebugLogs)
                ChunaLogger.Log($"<color=green>[Collision] 오른손이 환자에 닿음! (주동수:{rightOnPrimary})</color>");
        }

        // 접촉 상태 변경 시 가이드 핸드 알파 업데이트
        if (ctx.fadeOnTouch)
        {
            if (wasLeftTouching != owner.IsLeftHandTouchingPatient)
                UpdateGuideHandAlphaForHand(true, owner.IsLeftHandTouchingPatient, ctx);
            if (wasRightTouching != owner.IsRightHandTouchingPatient)
                UpdateGuideHandAlphaForHand(false, owner.IsRightHandTouchingPatient, ctx);
        }

        // 디버그
        if (ctx.showDebugLogs && Time.frameCount % 60 == 0)
        {
            string targets = string.Join("+", ctx.activeContactTargets);
            string lStatus = owner.IsLeftHandTouchingPatient ? "<color=green>접촉</color>" : "<color=red>미접촉</color>";
            string rStatus = owner.IsRightHandTouchingPatient ? "<color=green>접촉</color>" : "<color=red>미접촉</color>";
            if (ctx.leftHandTransform != null)
                ChunaLogger.Log($"<color=cyan>[왼손] {lStatus} 대상:{targets}</color>");
            if (ctx.rightHandTransform != null)
                ChunaLogger.Log($"<color=cyan>[오른손] {rStatus} 대상:{targets}</color>");
        }
    }

    /// <summary>
    /// 모든 activeContactTargets 중 하나라도 접촉하면 true
    /// </summary>
    private bool CheckHandTouchForAnyTarget(Transform handTransform, Collider handCollider, bool isLeftHand, in CollisionUpdateContext ctx)
    {
        foreach (var target in ctx.activeContactTargets)
        {
            if (CheckHandTouchForSingleTarget(handTransform, handCollider, isLeftHand, target, ctx))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 단일 ContactTarget에 대한 손 접촉 체크
    /// </summary>
    private bool CheckHandTouchForSingleTarget(Transform handTransform, Collider handCollider, bool isLeftHand, ContactTarget target, in CollisionUpdateContext ctx)
    {
        switch (target)
        {
            case ContactTarget.Head:
                // 머리만 체크
                if (ctx.patientHeadCollider == null) return false;
                return CheckHandCollision(handTransform, handCollider, ctx.patientHeadCollider.bounds, isLeftHand, ctx);

            case ContactTarget.Chest:
                // 흉부만 체크
                if (ctx.patientChestCollider == null) return false;
                return CheckHandCollision(handTransform, handCollider, ctx.patientChestCollider.bounds, isLeftHand, ctx);

            case ContactTarget.LeftArm:
                // 왼팔 체크 (상완+전완 등 복수 콜라이더)
                if (ctx.patientLeftArmColliders == null || ctx.patientLeftArmColliders.Length == 0) return false;
                foreach (var col in ctx.patientLeftArmColliders)
                {
                    if (col != null && CheckHandCollision(handTransform, handCollider, col.bounds, isLeftHand, ctx))
                        return true;
                }
                return false;

            case ContactTarget.RightArm:
                // 오른팔 체크 (상완+전완 등 복수 콜라이더)
                if (ctx.patientRightArmColliders == null || ctx.patientRightArmColliders.Length == 0) return false;
                foreach (var col in ctx.patientRightArmColliders)
                {
                    if (col != null && CheckHandCollision(handTransform, handCollider, col.bounds, isLeftHand, ctx))
                        return true;
                }
                return false;

            case ContactTarget.Knee:
                // 무릎(좌·우) - 앙와위 준비 동작에서 "무릎을 터치하면 환자가 무릎을 세운다".
                // 어느 쪽 무릎에 닿아도 이 손은 접촉으로 인정한다.
                if (ctx.patientKneeColliders == null || ctx.patientKneeColliders.Length == 0) return false;
                foreach (var kneeCol in ctx.patientKneeColliders)
                {
                    if (kneeCol != null && CheckHandCollision(handTransform, handCollider, kneeCol.bounds, isLeftHand, ctx))
                        return true;
                }
                return false;

            case ContactTarget.Arms:
                // 양팔 - 좌우 어느 팔에 닿아도 인정(팔을 모으게 하는 준비 동작용).
                foreach (var arr in new[] { ctx.patientLeftArmColliders, ctx.patientRightArmColliders })
                {
                    if (arr == null) continue;
                    foreach (var col in arr)
                    {
                        if (col != null && CheckHandCollision(handTransform, handCollider, col.bounds, isLeftHand, ctx))
                            return true;
                    }
                }
                return false;

            case ContactTarget.Shoulder:
                // 어깨만 체크 (양쪽 어깨)
                return CheckShoulderTouch(handTransform, handCollider, isLeftHand, ctx);

            case ContactTarget.Back:
                // 등·허리(흉추) - 복잡추나 흉추/늑골 술기의 실제 시술 부위.
                // 좌·우 복수 콜라이더. 이 손이 그 중 하나에라도 닿으면 이 손은 접촉으로 인정한다
                // (양손 각각 요구는 conditionParams=bothHands가 담당).
                if (ctx.patientBackColliders == null || ctx.patientBackColliders.Length == 0) return false;
                foreach (var backCol in ctx.patientBackColliders)
                {
                    if (backCol != null && CheckHandCollision(handTransform, handCollider, backCol.bounds, isLeftHand, ctx))
                        return true;
                }
                return false;

            case ContactTarget.ChestAndShoulder:
            {
                // 흉부 또는 어깨(양쪽) 체크
                bool touchChest = ctx.patientChestCollider != null &&
                    CheckHandCollision(handTransform, handCollider, ctx.patientChestCollider.bounds, isLeftHand, ctx);
                return touchChest || CheckShoulderTouch(handTransform, handCollider, isLeftHand, ctx);
            }

            case ContactTarget.HeadAndShoulder:
            default:
            {
                // 머리 또는 어깨(양쪽) 체크
                bool touchingHead = ctx.patientHeadCollider != null &&
                    CheckHandCollision(handTransform, handCollider, ctx.patientHeadCollider.bounds, isLeftHand, ctx);
                return touchingHead || CheckShoulderTouch(handTransform, handCollider, isLeftHand, ctx);
            }
        }
    }

    /// <summary>
    /// 어깨 접촉 체크. 씬에는 원래 어깨 콜라이더가 **한쪽만** 있었기 때문에
    /// (단순추나 다른 술기용으로 만들어진 자산) 반대쪽은 patientShoulderCollidersExtra로 보충한다.
    /// 둘 중 하나라도 닿으면 이 손은 어깨 접촉으로 인정.
    /// </summary>
    private bool CheckShoulderTouch(Transform handTransform, Collider handCollider, bool isLeftHand, in CollisionUpdateContext ctx)
    {
        if (ctx.patientShoulderCollider != null &&
            CheckHandCollision(handTransform, handCollider, ctx.patientShoulderCollider.bounds, isLeftHand, ctx))
            return true;

        if (ctx.patientShoulderCollidersExtra != null)
        {
            foreach (var col in ctx.patientShoulderCollidersExtra)
            {
                if (col != null && CheckHandCollision(handTransform, handCollider, col.bounds, isLeftHand, ctx))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// ★ 접촉 상태 변경 시 해당 손의 가이드 핸드 알파값 조절
    /// 특정 손의 가이드 핸드 알파값만 조절
    /// </summary>
    public void UpdateGuideHandAlphaForHand(bool isLeftHand, bool isTouching, in CollisionUpdateContext ctx)
    {
        HandTransformMapper guideHand = isLeftHand ? ctx.leftGuideHand : ctx.rightGuideHand;
        string handName = isLeftHand ? "왼손" : "오른손";

        if (guideHand == null) return;

        // ★접촉하면 <b>완전히 숨긴다</b>(08-11 사용자 지시). 예전에는 touchAlpha(0.15)로 반투명하게만
        //   흐려서 손 위에 잔상이 겹쳐 보였다 — "손을 대면 안 보여야 한다"가 요구사항이다.
        //   손을 떼면 다시 보인다(이때 재생은 하지 않는다 — 재생 제어는 ChunaPathEvaluator 쪽 규약).
        // ★이 단계가 가이드손을 아예 안 쓰기로 하고 숨겨 뒀으면 되살리지 않는다(2026-08-13).
        //   이 함수는 접촉이 풀릴 때마다 SetVisible(true)를 해서, 손 녹화가 없는 단계에서 숨겨 둔
        //   가이드손이 환자를 터치했다 떼는 순간 계속 되살아났다(사용자 지적 3회).
        if (owner.IsGuideHandHidden)
        {
            guideHand.SetVisible(false);
            return;
        }

        if (isTouching)
        {
            guideHand.SetVisible(false);
        }
        else if (owner.GuideHandHasData(isLeftHand))
        {
            // ★녹화가 한 손뿐인 클립에서 반대 손을 되살리지 않도록 데이터 유무를 확인한다.
            guideHand.SetVisible(true);
            guideHand.SetColorAndAlpha(ctx.guideHandColor, ctx.guideHandColor.a);
        }

        if (ctx.showDebugLogs)
            ChunaLogger.Log($"<color=yellow>[GuideHand] {handName} {(isTouching ? "접촉 → 숨김" : "미접촉 → 표시")}</color>");
    }

    /// <summary>
    /// 양손 가이드 핸드 알파값 모두 업데이트 (초기화용)
    /// </summary>
    public void UpdateGuideHandAlphaOnTouch(in CollisionUpdateContext ctx)
    {
        UpdateGuideHandAlphaForHand(true, owner.IsLeftHandTouchingPatient, ctx);
        UpdateGuideHandAlphaForHand(false, owner.IsRightHandTouchingPatient, ctx);
    }

    /// <summary>
    /// ★ 디버그 UI 업데이트 (FPS, 손 거리)
    /// </summary>
    public void UpdateDebugUI(in CollisionUpdateContext ctx)
    {
        // FPS 계산
        frameCount++;
        fpsTimer += Time.unscaledDeltaTime;

        if (fpsTimer >= ctx.fpsUpdateInterval)
        {
            currentFps = frameCount / fpsTimer;
            frameCount = 0;
            fpsTimer = 0f;

            // FPS 텍스트 업데이트
            if (ctx.fpsText != null)
            {
                ctx.fpsText.text = $"FPS: {currentFps:F1}";
            }
        }

        // 왼손 거리 계산 및 표시 (mm 단위)
        if (ctx.leftHandDistanceText != null)
        {
            float leftDistance = CalculateHandToGuideDistance(true, ctx) * 1000f; // m → mm
            string leftTouchStatus = owner.IsLeftHandTouchingPatient ? " [접촉]" : "";
            ctx.leftHandDistanceText.text = $"왼손: {leftDistance:F2}mm{leftTouchStatus}";
        }

        // 오른손 거리 계산 및 표시 (mm 단위)
        if (ctx.rightHandDistanceText != null)
        {
            float rightDistance = CalculateHandToGuideDistance(false, ctx) * 1000f; // m → mm
            string rightTouchStatus = owner.IsRightHandTouchingPatient ? " [접촉]" : "";
            ctx.rightHandDistanceText.text = $"오른손: {rightDistance:F2}mm{rightTouchStatus}";
        }
    }

    /// <summary>
    /// 사용자 손목(wristBone)과 가이드 핸드 Root 간의 거리 계산
    /// </summary>
    public float CalculateHandToGuideDistance(bool isLeftHand, in CollisionUpdateContext ctx)
    {
        Transform wristBone = isLeftHand ? ctx.leftWristBone : ctx.rightWristBone;
        HandTransformMapper guideHand = isLeftHand ? ctx.leftGuideHand : ctx.rightGuideHand;

        // 가이드 핸드가 없거나 비활성화면 0 반환
        if (guideHand == null || guideHand.Root == null || !guideHand.Root.gameObject.activeInHierarchy)
            return 0f;

        // 사용자 손목 본이 없으면 0 반환
        if (wristBone == null)
            return 0f;

        // 사용자 손목 Root ↔ 가이드 핸드 Root 거리 계산
        return Vector3.Distance(wristBone.position, guideHand.Root.position);
    }

    /// <summary>
    /// ★ 손 충돌 감지 (via HandCollisionDetector helper)
    /// </summary>
    private bool CheckHandCollision(Transform handTransform, Collider handCollider, Bounds patientBounds, bool isLeftHand, in CollisionUpdateContext ctx)
    {
        return collisionDetector.CheckHandCollision(
            handTransform, handCollider, patientBounds, isLeftHand,
            ctx.handCollisionShape, ctx.handColliderScale,
            ctx.defaultHandCollisionRadius, ctx.handCollisionForwardOffset,
            ctx.palmWidth, ctx.palmThickness, ctx.palmHeight, ctx.fingerLength);
    }

    /// <summary>
    /// 애니메이션 선형보간 업데이트
    /// ★ 손이 환자에게 닿은 상태에서만 애니메이션 업데이트
    /// </summary>
    public void UpdateAnimationLerp(in CollisionUpdateContext ctx)
    {
        bool isHandTouching = owner.IsLeftHandTouchingPatient || owner.IsRightHandTouchingPatient;

        // 애니메이션 상태 체크 로그
        if (ctx.showDebugLogs && Time.frameCount % 120 == 0)
        {
            ChunaLogger.Log($"<color=yellow>[Animation Check] Animator:{(ctx.patientAnimator != null ? ctx.patientAnimator.name : "NULL")}, 상태이름:'{ctx.currentAnimationStateName}', 단계:{ctx.currentPhase}, 접촉:{isHandTouching}</color>");
        }

        if (ctx.patientAnimator == null || string.IsNullOrEmpty(ctx.currentAnimationStateName)) return;

        // ★ 손이 닿은 상태 + Moving/MidHold 단계에서만 애니메이션 업데이트
        if (isHandTouching && (ctx.currentPhase == ChunaPathEvaluator.EvaluationPhase.Moving || ctx.currentPhase == ChunaPathEvaluator.EvaluationPhase.MidHold))
        {
            float currentRatio = owner.CurrentAnimationRatio;
            currentRatio = Mathf.Lerp(currentRatio, ctx.targetAnimationRatio, Time.deltaTime * ctx.animationLerpSpeed);
            owner.CurrentAnimationRatio = currentRatio;

            ctx.patientAnimator.Play(ctx.currentAnimationStateName, 0, currentRatio);
            ctx.patientAnimator.speed = 0f;

            // ★ 두 번째 환자 모델도 동기화
            if (ctx.secondaryPatientAnimator != null)
            {
                ctx.secondaryPatientAnimator.Play(ctx.currentAnimationStateName, 0, currentRatio);
                ctx.secondaryPatientAnimator.speed = 0f;
            }

            if (ctx.showDebugLogs && Time.frameCount % 30 == 0)
            {
                ChunaLogger.Log($"<color=green>[Animation Lerp] '{ctx.currentAnimationStateName}' @ {currentRatio:P0} → {ctx.targetAnimationRatio:P0} (진행률:{ctx.userHandFrameRatio:P0})</color>");
            }
        }
        else if (ctx.showDebugLogs && Time.frameCount % 60 == 0)
        {
            ChunaLogger.Log($"<color=orange>[Animation Skip] 접촉:{isHandTouching}, 단계:{ctx.currentPhase}, 애니메이션:'{ctx.currentAnimationStateName}'</color>");
        }
    }
}
