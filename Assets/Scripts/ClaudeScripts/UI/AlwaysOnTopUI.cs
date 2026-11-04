using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 이 오브젝트 아래 UI를 3D 물체(환자 등)에 가리지 않게 늘 위에 그린다 (2026-10-30 사용자 «리모컨은 환자 모델 위로 오버레이» → «항상 앞에 그리기»).
/// ★10-30 기기 실측: 재질의 unity_GUIZTestMode를 Always로 바꿔도 파묻혔다 — 캔버스가 그릴 때 그 값을 스스로 덮어쓴다(월드 공간 = 깊이 비교).
///   → ZTest Always를 셰이더에 박은 사본(Assets/Art/OnTopUI)으로 갈아 끼운다: 둥근 판(Unlit/RoundedBoxUI) · 글자(TMP Mobile SDF).
///   셰이더는 직렬화 필드로 물려 빌드에 들어간다(Shader.Find만 쓰면 빌드에서 빠질 수 있다). 그리는 순서도 투명 물체 뒤로(renderQueue).
/// ★재질은 시작할 때 한 번만 복제한다(Update 할당 없음). 원래 재질 에셋은 건드리지 않는다.
/// </summary>
public class AlwaysOnTopUI : MonoBehaviour
{
    [SerializeField] private Shader roundedBoxOnTop;
    [SerializeField] private Shader tmpOnTop;
    [SerializeField] private int renderQueue = 4000;

    private void Awake()
    {
        int box = 0, text = 0, other = 0;
        foreach (var g in GetComponentsInChildren<Graphic>(true))
        {
            if (g is TMPro.TMP_Text tmp)
            {
                if (tmpOnTop == null) continue;
                var fm = tmp.fontMaterial;   // TMP는 fontMaterial 읽기로 인스턴스를 만든다
                fm.shader = tmpOnTop;
                fm.renderQueue = renderQueue;
                text++;
                continue;
            }
            var src = g.material;
            if (src == null || roundedBoxOnTop == null) continue;
            if (src.shader != null && src.shader.name == "Unlit/RoundedBoxUI")
            {
                g.material = new Material(src) { name = src.name + " (위에 그리기)", shader = roundedBoxOnTop, renderQueue = renderQueue };
                box++;
            }
            else other++;
        }
        Debug.Log($"[AlwaysOnTopUI] {name} — 위에 그리기: 둥근 판 {box} · 글자 {text} · 그 밖(그대로) {other} · 셰이더 {(roundedBoxOnTop != null)}/{(tmpOnTop != null)}");
    }
}
