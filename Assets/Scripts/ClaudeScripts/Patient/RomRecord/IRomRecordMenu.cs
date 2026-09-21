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

    /// <summary>판을 손목 위에 둔다. <paramref name="hold"/>면 그 자리에 멈춘다(가림으로 튀지 않게).</summary>
    void Follow(bool wristValid, Vector3 wrist, Transform eye, bool hold);

    /// <summary>매 프레임. 눌림 표시·반복·길게 누르기 진행을 돌린다.</summary>
    void Tick();

    /// <summary>판 중심까지 거리 — 누르는 손이 다가오는지 보는 데 쓴다.</summary>
    float Distance(Vector3 p);

    /// <summary>이 자리가 판 근처인가 — 핀치를 막는 데 쓴다.</summary>
    bool Near(Vector3 tip, float margin);

    /// <summary>눌린 버튼 id를 하나 꺼낸다. 없으면 null.</summary>
    string Poll(Vector3 tip, bool tipValid);
}
