"""PostToolUse 훅 — 위험한 자리를 고치면 <b>그 자리에서</b> 검증을 상기시킨다.

2026-09-03 신설. 사용자 지시: "검증을 요구하지 않거나 잊어버리면 한번 되물어주고
자체적으로 검증할 수 있는 시스템을 만들어 줘."

★가볍게 돈다. 전체 스캔(change-impact)은 여기서 안 한다 — 매 편집마다 20~30초는 못 쓴다.
  대신 <b>무엇을 돌려야 하는지</b>를 모델 컨텍스트에 밀어 넣는다. 그러면 잊지 않는다.
★단 하나, <b>직렬화 충돌</b>만은 여기서 바로 잡는다. 그게 09-03에 화면을 통째로 비운 사고였고,
  판정이 싸다(추가된 줄에 초기값 붙은 SerializeField가 있을 때만 씬을 읽는다).
"""
import io
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SCENE = os.path.join(ROOT, "Assets", "Scenes", "TrainingScene.unity")


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return 0

    ti = payload.get("tool_input") or {}
    path = (payload.get("tool_response") or {}).get("filePath") or ti.get("file_path") or ""
    path = path.replace("\\", "/")
    if not path:
        return 0

    notes = []

    # ── .cs ──────────────────────────────────────────────────────────────
    if path.endswith(".cs") and "/Assets/" in path:
        notes.append("`dotnet build Assembly-CSharp.csproj` — ★새 파일·고친 파일의 경고까지 읽는다")
        notes.append("`change-impact` 스킬 — 공유 컴포넌트면 다른 술기도 Play 목록에 넣어 달라고 말한다")

        # ★직렬화 충돌은 여기서 바로 본다. 초기값 붙은 SerializeField가 새로 들어왔을 때만.
        added = ti.get("new_string") or ti.get("content") or ""
        fields = set()
        for m in re.finditer(r"\[SerializeField\][^\n;]*?\s(\w+)\s*=\s*[^=]", added):
            fields.add(m.group(1))
        for m in re.finditer(r"^\s*(?:public|private)\s+[\w<>\[\]\.]+\s+(\w+)\s*=\s*[^=]",
                             added, re.M):
            fields.add(m.group(1))

        if fields and os.path.exists(SCENE):
            try:
                scene = io.open(SCENE, encoding="utf-8", errors="replace").read()
            except Exception:
                scene = ""
            hit = [f for f in sorted(fields)
                   if len(f) >= 4 and re.search(r"^\s+" + re.escape(f) + r":", scene, re.M)]
            if hit:
                notes.insert(0,
                             "★★<b>씬에 이미 직렬화된 필드다 — 코드 기본값이 안 먹는다</b>(규칙 7): "
                             + ", ".join(hit)
                             + " → 인스펙터에서 바꾸라고 안내하거나, 신규 이름의 오버라이드 필드를 판다.")
            elif fields:
                notes.append("신규 `[SerializeField]`에 초기값을 넣었다 — ★되돌릴 여지가 있으면 "
                             "기본값을 false로 두거나 런타임 플래그로 둔다. "
                             "그날 씬이 저장되면 그 값이 굳어 코드로 못 바꾼다(09-03 사고).")

    # ── 시나리오 CSV ─────────────────────────────────────────────────────
    if "/Assets/Resources/Scenarios/" in path and path.endswith(".csv"):
        added = ti.get("new_string") or ti.get("content") or ""
        notes.append("`chuna-scenario-wire` 스킬로 정합 확인")
        # textInstruction 열이 바뀌었을 법하면 재녹음을 콕 집어 말한다
        if added.strip():
            notes.insert(0,
                         "★★<b>문구를 고쳤으면 그 자리에서 `narration-gen`을 돌린다</b>. "
                         "텍스트만 바꾸고 넘어가면 안내문과 음성이 따로 논다 — "
                         "09-03에 31개가 그렇게 낡아 있었다.")

    # ── ScenarioConfig ───────────────────────────────────────────────────
    if "/Assets/Resources/ScenarioConfigs/" in path:
        notes.append("`chuna-scenario-wire` 스킬 — phase 화이트리스트가 CSV와 맞는지 본다")
        notes.append("★이 `.asset`은 Git LFS다. diff가 포인터만 보이니 <b>바이트 증감</b>으로 검증한다.")

    if not notes:
        return 0

    body = ("[검증 게이트] " + os.path.basename(path) + " 을(를) 고쳤다. 이번 턴에 다음을 처리한다:\n"
            + "\n".join(f"  - {n}" for n in notes)
            + "\n완료 보고에 <b>무엇을 검증했고 무엇이 미검증인지</b>를 반드시 적는다.")

    print(json.dumps({
        "suppressOutput": True,
        "hookSpecificOutput": {
            "hookEventName": "PostToolUse",
            "additionalContext": body,
        },
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:
        sys.exit(0)   # ★훅이 작업을 막으면 안 된다. 조용히 물러난다.
