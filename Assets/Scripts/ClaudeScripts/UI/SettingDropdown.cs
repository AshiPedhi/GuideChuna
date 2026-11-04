using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quest 설정 창 꼴 드롭다운 — 행 오른쪽 알약(«초급 ▼»)을 누르면 바로 아래에 목록이 펼쳐진다
/// (2026-10-28 사용자 «메타퀘스트 설정 메뉴처럼 타이틀 아래 설명 · 우측 드롭박스» · 참조 = v2.7 설정 창 Text size 행).
///
/// ★TMP_Dropdown을 쓰지 않는다 — 목록을 별도 Canvas로 띄워서 판(UISet PointableCanvas)의 손 · 레이 입력을 못 받을 수 있다.
///   목록은 같은 판 안의 자식이고, 펼칠 때 맨 뒤 형제로 올려 위에 그린다.
/// ★막힌 항목(<see cref="disabled"/>)은 보이되 누를 수 없다(XR «준비 중»).
/// </summary>
public class SettingDropdown : MonoBehaviour
{
    [SerializeField] private Button head;
    [SerializeField] private TMP_Text caption;
    [SerializeField] private GameObject list;
    [SerializeField] private Button[] options;
    [SerializeField] private string[] labels;
    [SerializeField] private bool[] disabled;
    [Tooltip("고른 항목 칸 색(흰 알약) · 안 고른 칸은 투명")]
    [SerializeField] private Color selectedFill = new Color(0.965f, 0.965f, 0.965f);
    [SerializeField] private Color selectedInk = new Color(0.153f, 0.153f, 0.153f);
    [SerializeField] private Color normalInk = new Color(0.94f, 0.94f, 0.94f);

    public event Action<int> Changed;
    public int Value { get; private set; }

    private static SettingDropdown open;   // 한 번에 하나만 펼친다

    private void Awake()
    {
        if (head != null) head.onClick.AddListener(ToggleList);
        for (int i = 0; options != null && i < options.Length; i++)
        {
            int k = i;
            if (options[i] == null) continue;
            bool off = disabled != null && i < disabled.Length && disabled[i];
            options[i].interactable = !off;
            if (!off) options[i].onClick.AddListener(() => Pick(k));
        }
        if (list != null) list.SetActive(false);
    }

    private void OnDisable() { Close(); }

    /// <summary>알리지 않고 값만 맞춘다(팝업을 열 때 초기화용).</summary>
    public void SetValueWithoutNotify(int i)
    {
        Value = i;
        if (caption != null && labels != null && i >= 0 && i < labels.Length) caption.text = labels[i];
        for (int j = 0; options != null && j < options.Length; j++)
        {
            if (options[j] == null) continue;
            var img = options[j].targetGraphic as Image;
            if (img != null) img.color = j == i ? selectedFill : new Color(1, 1, 1, 0f);
            var t = options[j].GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.color = j == i ? selectedInk : normalInk;
        }
        Close();
    }

    private void ToggleList()
    {
        if (list == null) return;
        if (list.activeSelf) { Close(); return; }
        if (open != null && open != this) open.Close();
        list.transform.SetAsLastSibling();
        transform.SetAsLastSibling();   // 아래 행보다 위에 그리고 먼저 맞게
        list.SetActive(true);
        open = this;
    }

    public void Close()
    {
        if (list != null) list.SetActive(false);
        if (open == this) open = null;
    }

    private void Pick(int i)
    {
        SetValueWithoutNotify(i);
        Changed?.Invoke(i);
    }
}
