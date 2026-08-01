using UnityEditor;
using UnityEngine;

/// <summary>
/// 진단 파지점 슬롯을 한글 이름("후두부 파지점 / 엄지 파지점 / 검지 파지점 / 새끼 파지점")으로 표시.
/// 인스펙터에서 어느 슬롯에 무엇을 넣어야 하는지 바로 알 수 있게 하기 위한 표시 전용 드로어다.
/// </summary>
[CustomPropertyDrawer(typeof(CranialHandGrips))]
public class CranialHandGripsDrawer : PropertyDrawer
{
    private static readonly GUIContent PalmLabel =
        new GUIContent("손바닥 파지점", "손바닥으로 감싸거나 받치는 자세에서 사용(측두부 감싸기·후두부 베개 둘 다). 안 쓰면 비워 두세요.");
    private static readonly GUIContent ThumbLabel =
        new GUIContent("엄지 파지점", "3점 파지에서 사용. 안 쓰면 비워 두세요.");
    private static readonly GUIContent IndexLabel =
        new GUIContent("검지 파지점", "3점 파지에서 사용. 안 쓰면 비워 두세요.");
    private static readonly GUIContent PinkyLabel =
        new GUIContent("새끼 파지점", "3점 파지에서 사용. 안 쓰면 비워 두세요.");

    private const int SlotCount = 4;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight;
        float gap = EditorGUIUtility.standardVerticalSpacing;
        if (!property.isExpanded) return line;
        return line + SlotCount * (line + gap);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        float line = EditorGUIUtility.singleLineHeight;
        float gap = EditorGUIUtility.standardVerticalSpacing;

        var headerRect = new Rect(position.x, position.y, position.width, line);
        property.isExpanded = EditorGUI.Foldout(headerRect, property.isExpanded, Summary(property, label), true);

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            float y = position.y + line + gap;
            DrawSlot(ref y, position, gap, line, property, "palmGrip", PalmLabel);
            DrawSlot(ref y, position, gap, line, property, "thumbGrip", ThumbLabel);
            DrawSlot(ref y, position, gap, line, property, "indexGrip", IndexLabel);
            DrawSlot(ref y, position, gap, line, property, "pinkyGrip", PinkyLabel);
            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    private static void DrawSlot(ref float y, Rect position, float gap, float line,
                                 SerializedProperty property, string field, GUIContent slotLabel)
    {
        var rect = new Rect(position.x, y, position.width, line);
        EditorGUI.PropertyField(rect, property.FindPropertyRelative(field), slotLabel);
        y += line + gap;
    }

    /// <summary>접었을 때 어떤 슬롯이 채워져 있는지 한 줄로 보여준다(배선 누락 즉시 확인용).</summary>
    private static GUIContent Summary(SerializedProperty property, GUIContent label)
    {
        int filled = 0;
        string parts = "";
        AppendIfSet(property, "palmGrip", "손바닥", ref filled, ref parts);
        AppendIfSet(property, "thumbGrip", "엄지", ref filled, ref parts);
        AppendIfSet(property, "indexGrip", "검지", ref filled, ref parts);
        AppendIfSet(property, "pinkyGrip", "새끼", ref filled, ref parts);

        string suffix = filled == 0 ? "  (미배선 — 이 손은 판정 안 함)" : $"  ({parts})";
        return new GUIContent(label.text + suffix, label.tooltip);
    }

    private static void AppendIfSet(SerializedProperty property, string field, string name,
                                    ref int filled, ref string parts)
    {
        if (property.FindPropertyRelative(field).objectReferenceValue == null) return;
        parts = filled == 0 ? name : parts + "·" + name;
        filled++;
    }
}

/// <summary>
/// 자세(왼손/오른손 파지점 세트) 표시 드로어.
/// 배열 요소 머리글을 "Element 0" 대신 자세 이름으로 보여준다.
/// </summary>
[CustomPropertyDrawer(typeof(CranialDiagnosisPose))]
public class CranialDiagnosisPoseDrawer : PropertyDrawer
{
    private static readonly GUIContent NameLabel =
        new GUIContent("자세 이름", "로그·디버그 표시용. 판정에는 영향 없음.");
    private static readonly GUIContent LeftLabel =
        new GUIContent("왼손", "왼손이 짚어야 하는 파지점들");
    private static readonly GUIContent RightLabel =
        new GUIContent("오른손", "오른손이 짚어야 하는 파지점들");
    private static readonly GUIContent ClipLabel =
        new GUIContent("가이드손 녹화", "이 자세 전용 녹화 파일명(Resources/HandPoseData, 확장자 없이). " +
                                       "비우면 CSV handTrackingFileName의 substep 공용 클립을 쓴다.");
    private static readonly GUIContent RangeLabel =
        new GUIContent("가이드 구간", "클립에서 이 자세에 해당하는 구간(시작~끝, 0~1). " +
                                     "좌→우를 한 클립에 이어 녹화했을 때 앞/뒤를 나눠 쓴다(예: ⓐ=0~0.5, ⓑ=0.5~1). " +
                                     "0~1이면 클립 전체.");

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight;
        float gap = EditorGUIUtility.standardVerticalSpacing;
        if (!property.isExpanded) return line;

        return line + gap
             + line + gap                                                              // 자세 이름
             + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("leftHand"), true) + gap
             + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("rightHand"), true) + gap
             + line + gap                                                              // 가이드손 녹화
             + line;                                                                   // 가이드 구간
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        float line = EditorGUIUtility.singleLineHeight;
        float gap = EditorGUIUtility.standardVerticalSpacing;

        var nameProp = property.FindPropertyRelative("label");
        string header = string.IsNullOrEmpty(nameProp.stringValue) ? label.text : nameProp.stringValue;

        var headerRect = new Rect(position.x, position.y, position.width, line);
        property.isExpanded = EditorGUI.Foldout(headerRect, property.isExpanded, header, true);

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            float y = position.y + line + gap;

            EditorGUI.PropertyField(new Rect(position.x, y, position.width, line), nameProp, NameLabel);
            y += line + gap;

            var leftProp = property.FindPropertyRelative("leftHand");
            float leftH = EditorGUI.GetPropertyHeight(leftProp, true);
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, leftH), leftProp, LeftLabel, true);
            y += leftH + gap;

            var rightProp = property.FindPropertyRelative("rightHand");
            float rightH = EditorGUI.GetPropertyHeight(rightProp, true);
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, rightH), rightProp, RightLabel, true);
            y += rightH + gap;

            // 가이드손 — 이 자세(동작)가 시작될 때 재생되고, 자세가 성립하면 정지한다.
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, line),
                                    property.FindPropertyRelative("guideClipName"), ClipLabel);
            y += line + gap;

            DrawRange(new Rect(position.x, y, position.width, line),
                      property.FindPropertyRelative("guideStartRatio"),
                      property.FindPropertyRelative("guideEndRatio"));

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    /// <summary>시작~끝 비율을 한 줄에 나란히 그린다(0~1 클램프, 시작이 끝을 넘지 않게).</summary>
    private static void DrawRange(Rect rect, SerializedProperty startProp, SerializedProperty endProp)
    {
        float labelW = EditorGUIUtility.labelWidth;
        var labelRect = new Rect(rect.x, rect.y, labelW, rect.height);
        EditorGUI.LabelField(labelRect, RangeLabel);

        float fieldW = (rect.width - labelW - 6f) * 0.5f;
        var startRect = new Rect(rect.x + labelW, rect.y, fieldW, rect.height);
        var endRect = new Rect(rect.x + labelW + fieldW + 6f, rect.y, fieldW, rect.height);

        int prevIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        startProp.floatValue = Mathf.Clamp01(EditorGUI.FloatField(startRect, startProp.floatValue));
        endProp.floatValue = Mathf.Clamp01(EditorGUI.FloatField(endRect, endProp.floatValue));
        if (endProp.floatValue < startProp.floatValue) endProp.floatValue = startProp.floatValue;
        EditorGUI.indentLevel = prevIndent;
    }
}
