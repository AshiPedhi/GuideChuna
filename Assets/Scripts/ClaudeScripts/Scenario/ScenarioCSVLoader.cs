using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// CSV 파일에서 시나리오 데이터를 로드하는 클래스
/// 
/// [CSV 구조]
/// scenarioNo,scenarioName,phase,stepName,stepNo,subStepNo,duration,textInstruction,voiceInstruction,handTrackingFileName,conditionType,conditionParams
/// 
/// [수정 내역]
/// - RFC 4180 표준 CSV 파싱 (큰따옴표 안의 줄바꿈 처리) ✅
/// - CSV 컬럼 순서에 정확히 맞게 파싱
/// - 한글 인코딩 자동 감지 (UTF-8, EUC-KR)
/// - 조건 타입 자동 결정 로직 추가
/// </summary>
public class ScenarioCSVLoader : MonoBehaviour
{
    [Header("=== 디버그 설정 ===")]
    [SerializeField] private bool showEncodingDebugLog = true;
    [SerializeField] private bool showParsingDebugLog = true;

    /// <summary>
    /// Resources 폴더에서 CSV 파일 로드
    /// </summary>
    public ScenarioCollection LoadScenarios(string csvFileName = "ScenarioData")
    {
        TextAsset csvFile = Resources.Load<TextAsset>($"Scenarios/{csvFileName}");

        if (csvFile == null)
        {
            Debug.LogError($"[ScenarioLoader] CSV 파일을 찾을 수 없습니다: Resources/Scenarios/{csvFileName}.csv");
            Debug.LogError($"[ScenarioLoader] CSV 파일을 Assets/Resources/Scenarios/ 폴더에 넣어주세요!");
            return null;
        }

        // 바이트 배열에서 올바른 인코딩으로 텍스트 디코딩
        string csvText = DecodeCSVText(csvFile.bytes);

        return ParseCSV(csvText);
    }

    /// <summary>
    /// CSV 바이트 배열을 올바른 인코딩으로 디코딩
    /// UTF-8, EUC-KR 자동 감지
    /// </summary>
    private string DecodeCSVText(byte[] bytes)
    {
        // 1. UTF-8 BOM 체크
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            if (showEncodingDebugLog)
                Debug.Log("[ScenarioLoader] ✓ UTF-8 BOM 감지 - UTF-8로 디코딩");
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        // 2. UTF-8 시도 (BOM 없음)
        try
        {
            string utf8Text = Encoding.UTF8.GetString(bytes);

            // UTF-8 디코딩 오류 체크 (� 문자가 있으면 잘못된 인코딩)
            if (!utf8Text.Contains("�"))
            {
                // 한글이 제대로 디코딩되었는지 확인
                bool hasKorean = ContainsKoreanCharacters(utf8Text);

                if (hasKorean || !ContainsKoreanBytes(bytes))
                {
                    if (showEncodingDebugLog)
                        Debug.Log("[ScenarioLoader] ✓ UTF-8 인코딩 사용");
                    return utf8Text;
                }
            }
        }
        catch
        {
            // UTF-8 디코딩 실패
            if (showEncodingDebugLog)
                Debug.LogWarning("[ScenarioLoader] UTF-8 디코딩 실패");
        }

        // 3. EUC-KR 시도
        try
        {
            Encoding euckr = Encoding.GetEncoding("euc-kr");
            string euckrText = euckr.GetString(bytes);

            if (showEncodingDebugLog)
                Debug.Log("[ScenarioLoader] ✓ EUC-KR 인코딩 사용");
            return euckrText;
        }
        catch
        {
            if (showEncodingDebugLog)
                Debug.LogWarning("[ScenarioLoader] EUC-KR 디코딩 실패");
        }

        // 4. 최후의 수단: 시스템 기본 인코딩
        Debug.LogWarning("[ScenarioLoader] ⚠ 기본 인코딩 사용 (한글이 깨질 수 있음)");
        return Encoding.Default.GetString(bytes);
    }

    /// <summary>
    /// 문자열에 한글 문자가 있는지 확인
    /// 한글 유니코드 범위: AC00-D7A3 (가-힣)
    /// </summary>
    private bool ContainsKoreanCharacters(string text)
    {
        foreach (char c in text)
        {
            if (c >= 0xAC00 && c <= 0xD7A3)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 바이트 배열에 한글 바이트가 있는지 확인
    /// EUC-KR 한글 범위: 첫 바이트 0xB0-0xC8, 두 번째 바이트 0xA1-0xFE
    /// </summary>
    private bool ContainsKoreanBytes(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length - 1; i++)
        {
            // EUC-KR 한글 범위 체크
            if (bytes[i] >= 0xB0 && bytes[i] <= 0xC8 &&
                bytes[i + 1] >= 0xA1 && bytes[i + 1] <= 0xFE)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// CSV 텍스트를 파싱하여 ScenarioCollection 생성
    /// RFC 4180 표준 준수 - 큰따옴표 안의 줄바꿈 처리
    /// </summary>
    private ScenarioCollection ParseCSV(string csvText)
    {
        ScenarioCollection collection = new ScenarioCollection
        {
            scenarios = new List<ScenarioData>()
        };

        // RFC 4180 표준에 맞게 CSV 파싱
        List<List<string>> rows = ParseCSVText(csvText);

        if (rows.Count == 0)
        {
            Debug.LogError("[ScenarioLoader] CSV 데이터가 비어있습니다!");
            return collection;
        }

        // 헤더 제거
        if (rows.Count > 0)
            rows.RemoveAt(0);

        // 이전 값들을 기억 (빈칸 채우기용)
        int lastScenarioNo = 0;
        string lastScenarioName = "";
        string lastPhase = "";

        ScenarioData currentScenario = null;
        PhaseData currentPhase = null;
        StepData currentStep = null;

        int lineNumber = 0;

        foreach (var values in rows)
        {
            lineNumber++;

            if (values.Count < 9) // 최소 9개 컬럼 필요
            {
                if (showParsingDebugLog)
                    Debug.LogWarning($"[ScenarioLoader] 라인 {lineNumber}: 컬럼 수 부족 ({values.Count}개)");
                continue;
            }

            try
            {
                // CSV 컬럼 순서에 맞게 파싱
                int scenarioNo = string.IsNullOrEmpty(values[0]) ? lastScenarioNo : int.Parse(values[0].Trim());
                string scenarioName = string.IsNullOrEmpty(values[1]) ? lastScenarioName : values[1].Trim();
                string phase = string.IsNullOrEmpty(values[2]) ? lastPhase : values[2].Trim();
                string stepName = values[3].Trim();
                int stepNo = int.Parse(values[4].Trim());
                int subStepNo = int.Parse(values[5].Trim());
                int duration = string.IsNullOrEmpty(values[6]) ? 0 : int.Parse(values[6].Trim());
                string textInstruction = values[7].Trim();
                string voiceInstruction = values[8].Trim();

                if (showParsingDebugLog)
                    Debug.Log($"[ScenarioLoader] Row {lineNumber}: {scenarioName} : {phase} : {stepName} : {stepNo}");

                // handTrackingFileName (10번째 컬럼, 선택사항)
                string handTrackingFileName = "";
                if (values.Count >= 10 && !string.IsNullOrEmpty(values[9]))
                {
                    handTrackingFileName = values[9].Trim();
                }

                // conditionType (11번째 컬럼, 선택사항)
                string conditionType = "";
                if (values.Count >= 11 && !string.IsNullOrEmpty(values[10]))
                {
                    conditionType = values[10].Trim();
                }

                // conditionParams (12번째 컬럼, 선택사항)
                string conditionParams = "";
                if (values.Count >= 12 && !string.IsNullOrEmpty(values[11]))
                {
                    conditionParams = values[11].Trim();
                }

                // 조건 타입 자동 결정
                if (string.IsNullOrEmpty(conditionType))
                {
                    conditionType = DetermineConditionType(stepNo, stepName, handTrackingFileName, duration);
                }

                // 현재 값 기억
                lastScenarioNo = scenarioNo;
                lastScenarioName = scenarioName;
                lastPhase = phase;

                // === 시나리오 생성/찾기 ===
                if (currentScenario == null || currentScenario.scenarioNo != scenarioNo)
                {
                    currentScenario = new ScenarioData
                    {
                        scenarioNo = scenarioNo,
                        scenarioName = scenarioName
                    };
                    collection.scenarios.Add(currentScenario);
                    currentPhase = null;
                    currentStep = null;

                    if (showParsingDebugLog)
                        Debug.Log($"[ScenarioLoader] 새 시나리오: {scenarioNo}. {scenarioName}");
                }

                // === Phase 생성/찾기 ===
                if (currentPhase == null || currentPhase.phaseName != phase)
                {
                    currentPhase = currentScenario.phases.FirstOrDefault(p => p.phaseName == phase);

                    if (currentPhase == null)
                    {
                        currentPhase = new PhaseData { phaseName = phase };
                        currentScenario.phases.Add(currentPhase);

                        if (showParsingDebugLog)
                            Debug.Log($"[ScenarioLoader]   새 페이즈: {phase}");
                    }

                    currentStep = null;
                }

                // === Step 생성/찾기 ===
                if (currentStep == null || currentStep.stepNo != stepNo)
                {
                    currentStep = currentPhase.steps.FirstOrDefault(s => s.stepNo == stepNo);

                    if (currentStep == null)
                    {
                        currentStep = new StepData
                        {
                            stepNo = stepNo,
                            stepName = stepName
                        };
                        currentPhase.steps.Add(currentStep);

                        if (showParsingDebugLog)
                            Debug.Log($"[ScenarioLoader]     새 Step: {stepNo}. {stepName}");
                    }
                }

                // === SubStep 추가 ===
                SubStepData subStep = new SubStepData
                {
                    subStepNo = subStepNo,
                    duration = duration,
                    textInstruction = textInstruction,
                    voiceInstruction = voiceInstruction,
                    handTrackingFileName = handTrackingFileName,
                    conditionType = conditionType,
                    conditionParams = conditionParams
                };

                currentStep.subSteps.Add(subStep);

                if (showParsingDebugLog)
                {
                    string handInfo = !string.IsNullOrEmpty(handTrackingFileName) ? $"핸드={handTrackingFileName}" : "핸드없음";
                    Debug.Log($"[ScenarioLoader]       SubStep {subStepNo}: {conditionType}, {handInfo}, dur={duration}초");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ScenarioLoader] 라인 {lineNumber} 파싱 오류: {e.Message}");
            }
        }

        // Phase와 Step 정렬
        foreach (var scenario in collection.scenarios)
        {
            foreach (var phase in scenario.phases)
            {
                phase.steps = phase.steps.OrderBy(s => s.stepNo).ToList();

                foreach (var step in phase.steps)
                {
                    step.subSteps = step.subSteps.OrderBy(ss => ss.subStepNo).ToList();
                }
            }
        }

        Debug.Log($"[ScenarioLoader] ✓ 총 {collection.scenarios.Count}개 시나리오 로드 완료");
        foreach (var scenario in collection.scenarios)
        {
            Debug.Log($"[ScenarioLoader]   시나리오 {scenario.scenarioNo}: {scenario.scenarioName} - {scenario.phases.Count}개 페이즈");
        }

        return collection;
    }

    /// <summary>
    /// RFC 4180 표준 CSV 파싱 - 큰따옴표 안의 줄바꿈 처리
    /// </summary>
    private List<List<string>> ParseCSVText(string csvText)
    {
        List<List<string>> rows = new List<List<string>>();
        List<string> currentRow = new List<string>();
        string currentField = "";
        bool inQuotes = false;

        for (int i = 0; i < csvText.Length; i++)
        {
            char c = csvText[i];

            if (c == '"')
            {
                // 큰따옴표 이스케이프 처리 ("")
                if (inQuotes && i + 1 < csvText.Length && csvText[i + 1] == '"')
                {
                    currentField += '"';
                    i++; // 다음 따옴표 건너뛰기
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                // 필드 구분자
                currentRow.Add(currentField);
                currentField = "";
            }
            else if ((c == '\n' || c == '\r') && !inQuotes)
            {
                // 줄바꿈 (큰따옴표 밖)
                if (c == '\r' && i + 1 < csvText.Length && csvText[i + 1] == '\n')
                {
                    i++; // CRLF의 LF 건너뛰기
                }

                if (currentRow.Count > 0 || !string.IsNullOrEmpty(currentField))
                {
                    currentRow.Add(currentField);
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                    currentField = "";
                }
            }
            else
            {
                // 일반 문자 (큰따옴표 안의 줄바꿈 포함)
                currentField += c;
            }
        }

        // 마지막 필드와 행 추가
        if (currentRow.Count > 0 || !string.IsNullOrEmpty(currentField))
        {
            currentRow.Add(currentField);
            rows.Add(currentRow);
        }

        return rows;
    }

    /// <summary>
    /// 조건 타입 자동 결정
    /// </summary>
    private string DetermineConditionType(int stepNo, string stepName, string handTrackingFileName, int duration)
    {
        // 1. 가이드 Step -> 토글 대기
        if (stepNo == 0 && stepName == "가이드")
        {
            return "Manual";
        }

        // 2. 핸드 트래킹이 있으면 -> HandPose 조건
        if (!string.IsNullOrEmpty(handTrackingFileName))
        {
            return "HandPose";
        }

        // 3. Duration이 있으면 -> Duration 조건
        if (duration > 0)
        {
            return "Duration";
        }

        // 4. 그 외 -> Manual (조건 없음, 자동 진행 또는 토글 대기)
        return "Manual";
    }
}