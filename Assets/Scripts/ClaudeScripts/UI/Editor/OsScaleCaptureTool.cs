using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// OS «화면 크기» · «텍스트 크기» 단계별로 로비 · 로그인 · 시나리오 선택 팝업을 <b>눈 자리에서</b> 찍는다 (2026-10-28 사용자 «옵션별로 캡처해서 비교 문서화»).
///
/// ★기기와 같은 함수를 탄다 — OsDisplayScale.EditorSimulate + EditorApplyTo(씬 로드 때 Apply) · 조 · 이름 칸은 LoginListLayout.ApplyLabel.
///   미리보기가 따로 계산하면 거짓말을 한다(CLAUDE.md 규칙 9).
/// ★카메라 = CenterEyeAnchor 자리 · 방향, 시야 가로 약 96°(Quest 3S 근사) — 판이 커지면 화면에서도 커져 보인다(판 프레이밍이 아니다).
/// 단계 값은 Captures/OsScale/cases.txt(«이름|글자배율|화면배율» 한 줄씩 · 기기 adb 실측값)에서 읽는다. 다 찍고 나면 전부 되돌린다.
/// </summary>
public static class OsScaleCaptureTool
{
    const int W = 1600, H = 1000;
    const float VFov = 70f;   // 가로 = 2·atan(tan35°·1.6) ≈ 96°

    [MenuItem("GuideChuna/UI 개편/OS 크기 단계별 캡처 (lobby)")]
    public static void CaptureAll()
    {
        var auth = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        if (auth == null) { Debug.LogWarning("[OsScaleCap] 로비가 아니다"); return; }
        string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Captures", "OsScale");
        string casesPath = Path.Combine(root, "cases.txt");
        if (!File.Exists(casesPath)) { Debug.LogWarning($"[OsScaleCap] {casesPath} 없음"); return; }
        var eye = GameObject.Find("CenterEyeAnchor");
        if (eye == null) { Debug.LogWarning("[OsScaleCap] CenterEyeAnchor 없음"); return; }

        var lobby = auth.transform.Find("ContentUIExample-HorizonOS1");
        var panels = new (string key, Transform t)[]
        {
            ("로비", null),
            ("로그인", auth.transform.Find("로그인 한 판 V1.4")),
            ("시나리오선택", auth.transform.Find("모드 선택 팝업")),
            ("로그인안내", auth.transform.Find("로그인 안내 V0.9")),   // 10-29 추가
        };

        // 원래 상태 저장 — 판 활성 · 배율, 글자 크기
        var actives = new List<(GameObject go, bool on)>();
        foreach (Transform c in auth.transform) actives.Add((c.gameObject, c.gameObject.activeSelf));
        foreach (var p in panels) if (p.t != null) { var h = p.t.Find("판"); if (h != null) actives.Add((h.gameObject, h.gameObject.activeSelf)); }
        var scales = new List<(Transform t, Vector3 s)>();
        foreach (Transform c in auth.transform) scales.Add((c, c.localScale));
        // ★채운 칸도 원래 크기를 저장해야 한다 — 안 그러면 단계마다 글자 배율이 누적된다(10-28 첫 캡처에서 1.15 → 1.725로 겹쳐 곱해졌다)
        var fill = FillLogin(panels[1].t);   // 조 15 · 이름 12(미리보기용 · 끝나면 지운다)
        var texts = new List<(TMP_Text t, float s, float mn, float mx)>();
        foreach (var t in auth.GetComponentsInChildren<TMP_Text>(true)) texts.Add((t, t.fontSize, t.fontSizeMin, t.fontSizeMax));

        int mask = 0; foreach (var cv in auth.GetComponentsInChildren<Canvas>(true)) mask |= 1 << cv.gameObject.layer;   // 캔버스 층만 찍는다(뒤 3D 물체 · 손 모델 빼기)
        var dimmers = new List<(CanvasGroup g, float a, bool b)>();
        foreach (var dm in auth.GetComponentsInChildren<LobbyDimmer>(true)) { var g = dm.GetComponent<CanvasGroup>(); dimmers.Add((g, g.alpha, g.blocksRaycasts)); }
        var sb = new StringBuilder("[OsScaleCap] 단계별 캡처\n");
        try
        {
            foreach (var line in File.ReadAllLines(casesPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var f = line.Split('|');
                string name = f[0].Trim();
                float font = float.Parse(f[1]), dens = float.Parse(f[2]);

                foreach (var x in texts) { x.t.fontSize = x.s; x.t.fontSizeMin = x.mn; x.t.fontSizeMax = x.mx; }
                foreach (var x in scales) x.t.localScale = x.s;
                OsDisplayScale.EditorSimulate(font, dens);
                OsDisplayScale.EditorApplyTo(auth.transform);   // ★기기 씬 로드 때와 같은 함수
                foreach (var t in fill) LoginListLayout.ApplyLabel(t as TextMeshProUGUI);   // 실행 중에 만드는 조 · 이름 칸 = 같은 함수

                foreach (var p in panels)
                {
                    foreach (Transform c in auth.transform) c.gameObject.SetActive(false);
                    lobby.gameObject.SetActive(true);   // 팝업도 로비 앞에 뜬 모습 그대로
                    if (p.t != null)
                    {
                        p.t.gameObject.SetActive(true);
                        var h = p.t.Find("판"); if (h != null) h.gameObject.SetActive(true);
                    }
                    // 기기에서 다음 프레임에 일어나는 것 = 레이아웃 재계산 · 이름 알약 폭(LobbyUserNameLabel.Fit) · 딤(LobbyDimmer) — 같은 함수로 맞춘다
                    foreach (var cv in auth.GetComponentsInChildren<Canvas>(false)) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)cv.transform);
                    foreach (var nl in auth.GetComponentsInChildren<LobbyUserNameLabel>(false)) nl.Fit();
                    foreach (var pf in auth.GetComponentsInChildren<PopupTextFit>(false)) pf.Fit();   // 10-29 팝업 세로 맞춤(열릴 때 기기와 같은 함수)
                    foreach (var cv in auth.GetComponentsInChildren<Canvas>(false)) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)cv.transform);
                    foreach (var dm in auth.GetComponentsInChildren<LobbyDimmer>(true)) dm.Snap();
                    string file = Path.Combine(root, $"{name}_{p.key}.png");
                    Shoot(eye.transform, file, mask);
                    sb.Append($"  {name} · {p.key} → {Path.GetFileName(file)}\n");
                }
            }
        }
        finally
        {
            OsDisplayScale.EditorSimulate(1f, 1f);
            foreach (var pf in auth.GetComponentsInChildren<PopupTextFit>(true)) pf.ResetLayout();
            foreach (var d in dimmers) { d.g.alpha = d.a; d.g.blocksRaycasts = d.b; }
            foreach (var x in texts) { if (x.t == null) continue; x.t.fontSize = x.s; x.t.fontSizeMin = x.mn; x.t.fontSizeMax = x.mx; }
            foreach (var x in scales) x.t.localScale = x.s;
            foreach (var o in fill)
            {
                if (o == null) continue;
                var tr = o.transform;   // 격자 바로 아래 = 채운 칸 루트
                while (tr.parent != null && tr.parent.GetComponent<GridLayoutGroup>() == null) tr = tr.parent;
                Object.DestroyImmediate(tr.gameObject);
            }
            for (int i = actives.Count - 1; i >= 0; i--) actives[i].go.SetActive(actives[i].on);
            foreach (var nl in auth.GetComponentsInChildren<LobbyUserNameLabel>(true)) nl.Fit();
            Canvas.ForceUpdateCanvases();
        }
        Debug.Log(sb.ToString());
    }

    static void Shoot(Transform eye, string path, int mask)
    {
        Canvas.ForceUpdateCanvases();
        var camGo = new GameObject("OsScaleCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = VFov; cam.aspect = (float)W / H;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.54f, 0.52f, 1f);   // 패스스루 방 흉내
        cam.nearClipPlane = 0.35f; cam.farClipPlane = 5f;   // 판은 눈에서 0.52~0.6m — 눈 가까이 손 · 컨트롤러 모델을 자른다
        cam.cullingMask = mask;
        camGo.transform.SetPositionAndRotation(eye.position, eye.rotation);
        var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var img = new Texture2D(W, H, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, W, H), 0, 0); img.Apply();
        RenderTexture.active = null;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, img.EncodeToPNG());
        cam.targetTexture = null;
        Object.DestroyImmediate(img); Object.DestroyImmediate(rt); Object.DestroyImmediate(camGo);
    }

    // 로그인 한 판에 조 15 · 이름 12를 잠깐 채운다(LobbyV09Tool.CaptureAll과 같은 프리팹 · 같은 글자). 만든 글자 목록을 돌려준다
    static List<TMP_Text> FillLogin(Transform panel)
    {
        var made = new List<TMP_Text>();
        if (panel == null) return made;
        string[] gs = { "1조", "2조", "3조", "4조", "본과3 A조", "6조", "7조", "8조", "9조", "10조", "11조", "12조", "13조", "14조", "15조" };
        string[] us = { "강민준", "고은서", "김도현", "김서연", "남궁민수아", "박지훈", "서하린", "오지민", "윤서준", "이하은", "정우진", "최유나" };
        var grids = panel.GetComponentsInChildren<GridLayoutGroup>(true);
        for (int gi = 0; gi < grids.Length && gi < 2; gi++)
        {
            bool grade = gi == 0;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/UI/LoginV09/{(grade ? "조항목_V14" : "이름타일_V14")}.prefab");
            int n = grade ? gs.Length : us.Length;
            for (int i = 0; i < n && prefab != null; i++)
            {
                var o = (GameObject)Object.Instantiate(prefab, grids[gi].transform);
                o.hideFlags = HideFlags.DontSave;
                var t = o.GetComponentInChildren<TMP_Text>();
                t.text = grade ? gs[i] : us[i];
                if (grade && i == 0)
                {
                    o.GetComponent<Image>().color = HzUIKit.White;
                    foreach (var x in o.GetComponentsInChildren<TMP_Text>()) x.color = HzUIKit.Ink;
                }
                made.Add(t);
            }
            int rows = Mathf.CeilToInt(n / (float)grids[gi].constraintCount);
            var crt = (RectTransform)grids[gi].transform;
            crt.sizeDelta = new Vector2(crt.sizeDelta.x, rows * (grids[gi].cellSize.y + grids[gi].spacing.y));
        }
        return made;
    }
}
