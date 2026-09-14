/// <summary>
/// 단계 이름(stepName)을 <b>화면에 읽히는 이름</b>으로 바꾼다.
///
/// ★왜 따로 두나 — stepName은 <b>판정 키</b>다(CLAUDE.md 규칙 8).
///   `ApplyGripPair`·`GetEvaluationStepText`·나레이션 파일명·`CervicalRomMeasurementBridge.DirectionOfGripStep`이
///   전부 이름으로 정확히 매칭한다. 그래서 이름 자체는 못 바꾸고, <b>그리는 자리에서만</b> 갈아 끼운다.
///
/// ★2026-09-14 사용자 지시(09-11 지시의 연장) — 경추ROM 화면에서 <b>단면 용어를 말하지 않는다</b>.
///   시상면·관상면·횡단면 대신 교과서 방향 명칭(굴곡·신전·측굴·회전)으로 말한다.
///   09-11엔 지시문·나레이션만 걷었고 <b>단계 제목이 남아 있었다</b>.
///
/// ★여기 없는 이름은 <b>그대로 통과</b>한다 — 13개 술기가 이 UI를 같이 쓰므로,
///   매핑에 걸리는 여섯 이름(경추ROM측정.csv에만 있다)을 뺀 나머지는 손대지 않는다.
/// </summary>
public static class StepDisplayName
{
    public static string Of(string stepName)
    {
        if (string.IsNullOrEmpty(stepName)) return stepName;

        switch (stepName)
        {
            // 파지 3종 — 서는 자리가 아니라 방향 명칭으로 말한다(09-14 사용자 선택).
            case "시상면 파지": return "굴곡·신전 파지";
            case "관상면 파지": return "측굴 파지";
            case "횡단면 파지": return "회전 파지";

            // 결과 기록 3종 — '평가'라는 말은 그대로 두고 면 이름만 걷는다.
            case "시상면평가": return "굴곡·신전 평가";
            case "관상면평가": return "측굴 평가";
            case "횡단면평가": return "회전 평가";
        }

        return stepName;
    }
}
