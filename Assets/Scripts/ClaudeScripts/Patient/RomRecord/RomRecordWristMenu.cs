using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 실측 기록의 손목 메뉴 — <b>양 손목에 같은 메뉴</b>가 뜨고 <b>반대 손 검지</b>로 누른다(2026-09-18 사용자 안).
/// 압박 중에는 누르는 손의 손목 메뉴를 자유 손이 누른다 — 누르는 손을 머리에서 뗄 필요가 없다.
///
/// ★메뉴판은 손목 위에 떠서 <b>눈을 향해 선다</b>(손목 뼈의 축 방향에 기대지 않는다 — 그 방향은 Play에서 재 본 적이 없다).
/// ★버튼은 단계가 바뀔 때만 다시 쓴다. 매 프레임 문자열을 만들지 않는다.
/// </summary>
public class RomRecordWristMenu
{
    private class Btn
    {
        public Transform tr;
        public TextMeshPro label;
        public string id;
        public bool repeat;
        public bool inside;
        public float nextRepeat;
    }

    private readonly List<Btn> pool = new List<Btn>();
    private int activeCount;
    private Transform root;
    private float cooldownUntil;

    public float buttonSize = 0.02f;
    public float gap = 0.027f;
    public int columns = 4;
    public float lift = 0.06f;            // 손목에서 위로(m)
    public float towardEye = 0.02f;       // 눈 쪽으로(m)
    public float pressRadius = 0.018f;
    public float holdDelay = 0.5f;        // 누르고 있으면 이 뒤부터 반복
    public float repeatInterval = 0.12f;
    public float cooldown = 0.35f;        // 반복 안 하는 버튼은 한 번 누른 뒤 이만큼 잠근다

    public void Build(Transform parent, string name, int capacity, TMP_FontAsset font, Material mat)
    {
        root = new GameObject("[실측기록] " + name).transform;
        root.SetParent(parent, false);
        for (int i = 0; i < capacity; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());   // 물리는 안 쓴다 — 거리로만 판정한다
            go.transform.SetParent(root, false);
            go.transform.localScale = Vector3.one * buttonSize;
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;

            var labGo = new GameObject("글자");
            labGo.transform.SetParent(root, false);
            var t = labGo.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            t.fontSize = 0.9f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.rectTransform.sizeDelta = new Vector2(0.05f, 0.012f);
            t.textWrappingMode = TextWrappingModes.NoWrap;

            pool.Add(new Btn { tr = go.transform, label = t });
        }
    }

    /// <summary>단계가 바뀔 때 부른다. ids와 labels는 같은 길이. repeat는 누르고 있으면 반복되는 버튼.</summary>
    public void SetButtons(string[] ids, string[] labels, bool[] repeat, Color[] tints)
    {
        activeCount = Mathf.Min(ids.Length, pool.Count);
        // ★단계가 바뀌면 버튼 자리가 달라진다. 방금 누른 손가락이 새 버튼 위에 남아 곧바로 눌리지 않게 잠깐 잠근다.
        cooldownUntil = Time.unscaledTime + cooldown;
        for (int i = 0; i < pool.Count; i++)
        {
            bool on = i < activeCount;
            pool[i].tr.gameObject.SetActive(on);
            pool[i].label.gameObject.SetActive(on);
            if (!on) continue;
            pool[i].id = ids[i];
            pool[i].label.text = labels[i];
            pool[i].repeat = repeat != null && repeat[i];
            pool[i].inside = false;
            var r = pool[i].tr.GetComponent<Renderer>();
            if (tints != null) r.material.color = tints[i];   // ★인스턴스 머티리얼 — 공유 머티리얼을 물들이지 않는다
        }
    }

    public void SetLabel(string id, string text)
    {
        for (int i = 0; i < activeCount; i++)
            if (pool[i].id == id) { pool[i].label.text = text; return; }
    }

    public void SetVisible(bool on)
    {
        if (root != null && root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
    }

    /// <summary>손목 위에 눈을 향해 격자로 놓는다.</summary>
    public void Place(Vector3 wrist, Transform eye)
    {
        if (eye == null) return;
        Vector3 toEye = eye.position - wrist;
        Vector3 basePos = wrist + Vector3.up * lift + toEye.normalized * towardEye;

        Vector3 right = eye.right, up = eye.up;
        int rows = Mathf.CeilToInt(activeCount / (float)columns);
        for (int i = 0; i < activeCount; i++)
        {
            int c = i % columns, rI = i / columns;
            Vector3 p = basePos + right * ((c - (columns - 1) * 0.5f) * gap) + up * (((rows - 1) - rI) * gap);
            pool[i].tr.position = p;
            pool[i].label.transform.position = p + up * (buttonSize * 0.9f);
            pool[i].label.transform.rotation = Quaternion.LookRotation(p - eye.position, up);
        }
    }

    /// <summary>이 자리가 메뉴 근처인가 — 핀치를 막는 데 쓴다(버튼 누르려던 손이 핀치로 읽히지 않게).</summary>
    public bool Near(Vector3 tip, float margin)
    {
        for (int i = 0; i < activeCount; i++)
            if ((pool[i].tr.position - tip).sqrMagnitude < margin * margin) return true;
        return false;
    }

    /// <summary>반대 손 검지로 눌린 버튼 id. 없으면 null.</summary>
    public string Poll(Vector3 tip, bool tipValid)
    {
        float now = Time.unscaledTime;
        for (int i = 0; i < activeCount; i++)
        {
            Btn b = pool[i];
            bool inNow = tipValid && (b.tr.position - tip).sqrMagnitude < pressRadius * pressRadius;
            // ★나갈 때는 조금 더 멀어져야 나간 것으로 본다 — 경계에서 떨려 연타되지 않게.
            bool outNow = !tipValid || (b.tr.position - tip).sqrMagnitude > (pressRadius * 1.4f) * (pressRadius * 1.4f);

            if (!b.inside)
            {
                if (!inNow) continue;
                if (now < cooldownUntil) continue;
                b.inside = true;
                b.nextRepeat = now + holdDelay;
                if (!b.repeat) cooldownUntil = now + cooldown;
                return b.id;
            }

            if (outNow) { b.inside = false; continue; }
            if (b.repeat && now >= b.nextRepeat)
            {
                b.nextRepeat = now + repeatInterval;
                return b.id;
            }
        }
        return null;
    }
}
