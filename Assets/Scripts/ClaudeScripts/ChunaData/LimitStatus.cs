using UnityEngine;

public enum LimitStatus
{
    [Tooltip("안전 - 한계 내")]
    Safe,

    [Tooltip("경고 - 한계 근접")]
    Warning,

    [Tooltip("위험 - 한계 임박")]
    Danger,

    [Tooltip("초과 - 한계 초과")]
    Exceeded
}
