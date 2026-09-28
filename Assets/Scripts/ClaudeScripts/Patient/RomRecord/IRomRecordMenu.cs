using TMPro;
using UnityEngine;

/// <summary>
/// 실측 기록의 손목 메뉴 — 구현이 둘이라 본체가 갈아 끼울 수 있게 낸 문(2026-09-21).
///
/// ★구현 1 <see cref="RomRecordWristMenu"/> — Quad + 월드 TMP를 코드로 그리고, 검지 끝 위치를 직접 재서 누름을 판정한다(09-18).
/// ★구현 2 <see cref="RomRecordMenuUI"/> — Meta Interaction SDK의 UI Set(프리팹)과 Poke 상호작용을 쓴다(09-21 사용자 지시).
///
/// 둘의 차이는 <b>누름을 누가 판정하나</b>다. 구현 1은 매 프레임 스스로 재고, 구현 2는 ISDK가 알려 준다.
/// 그래서 구현 2는 받은 눌림을 잠깐 들고 있다가 <see cref="Poll"/>에서 넘긴다 — 본체 쪽 흐름은 그대로 둔다.
/// </summary>
public interface IRomRecordMenu
{
    bool Visible { get; }

    /// <summary>방금 넘긴 눌림이 반복 입력인가 — 소리를 가볍게 낸다.</summary>
    bool LastRepeat { get; }

    void Build(Transform parent, string name, int capacity, TMP_FontAsset font, Material mat);

    /// <summary>단계가 바뀔 때(또는 고른 상태가 바뀔 때) 부른다.</summary>
    void SetLayout(string headerText, RomMenuItem[] items);

    void SetHeader(string text);

    /// <summary>
    /// key가 붙은 글자 칸 하나의 글자만 바꾼다(2026-09-22). ★배치를 다시 짜지 않는다 —
    /// SetLayout은 쿨다운을 다시 걸어 반복 버튼(±1°)을 끊으므로, 값이 바뀔 때마다 부르면 안 된다.
    /// </summary>
    void SetText(string key, string text);

    /// <summary>
    /// 판 잡기 하이라이트(2026-09-22 사용자: "옮길 수 있는 상태에 들어왔다, 이대로 잡으면 잡힌다를 알려 달라").
    /// 0 없음 · 1 지금 오므리면 판이 잡힌다 · 2 잡는 중. 같은 값이면 아무것도 안 한다(매 프레임 불러도 된다).
    /// </summary>
    void SetHighlight(int level);

    /// <summary>판을 손목 위에 둔다. <paramref name="hold"/>면 그 자리에 멈춘다(가림으로 튀지 않게).</summary>
    void Follow(bool wristValid, Vector3 wrist, Transform eye, bool hold);

    // ── 공간 고정 모드(2026-09-21) ──────────────────────────────────
    // ★손목을 따라다니면 기록하는 손과 판이 같은 자리에 있어 서로 간섭한다. 진행Root처럼 허공에 세운다.
    bool FixedInSpace { get; set; }
    bool Placed { get; }
    Vector3 Position { get; }

    /// <summary>그 자리에 세운다(위치는 한 번만 — 자리가 바뀌면 손이 헛짚는다).</summary>
    void PlaceAt(Vector3 pos, Transform eye);

    /// <summary>판을 통째로 옮긴다(잡아 끌기).</summary>
    void MoveTo(Vector3 pos);

    /// <summary>
    /// 판을 숨긴다(2026-09-28 — 바늘 미세 조정 판이 고른 바늘이 없을 때 사라져야 한다).
    /// 다시 보이게 하려면 <see cref="PlaceAt"/>을 부른다.
    /// </summary>
    void Hide();

    /// <summary>매 프레임. 눌림 표시·반복·길게 누르기 진행을 돌린다.</summary>
    void Tick();

    /// <summary>판 중심까지 거리 — 누르는 손이 다가오는지 보는 데 쓴다.</summary>
    float Distance(Vector3 p);

    /// <summary>이 자리가 판 근처인가 — 핀치를 막는 데 쓴다.</summary>
    bool Near(Vector3 tip, float margin);

    /// <summary>눌린 버튼 id를 하나 꺼낸다. 없으면 null.</summary>
    string Poll(Vector3 tip, bool tipValid);
}
