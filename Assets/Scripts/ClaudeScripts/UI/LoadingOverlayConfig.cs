using TMPro;
using UnityEngine;

/// <summary>
/// 로딩 가림막(<see cref="LoadingOverlay"/>)이 쓰는 에셋 묶음 — Resources/LoadingOverlay/LoadingOverlayConfig 하나.
/// 가림막은 씬 밖(DontDestroyOnLoad)에서 코드로 만들어 씬 배선이 없으니, 글꼴 · 셰이더 · 로고를 여기서 물려 빌드에 들어가게 한다.
/// 만드는 곳 = 메뉴 «GuideChuna/UI 개편/10-30 로딩 화면 설정 만들기».
/// </summary>
[CreateAssetMenu(menuName = "GuideChuna/Loading Overlay Config")]
public class LoadingOverlayConfig : ScriptableObject
{
    public TMP_FontAsset titleFont;
    public TMP_FontAsset bodyFont;
    public Shader veilShader;
    public Shader roundedBoxOnTop;
    public Shader tmpOnTop;
    public Material roundedBoxMaterial;   // Unlit/RoundedBoxUI 재질(모서리 반지름은 RoundedBoxUIProperties가 넣는다)
    public Sprite logo;
    public Color veilColor = new Color(0.05f, 0.05f, 0.06f, 1f);
    public Color panelColor = new Color(0.11f, 0.11f, 0.12f, 1f);
    public Color accentColor = new Color(0.0f, 0.39f, 0.88f, 1f);   // 로비 «전체» 탭 파랑
}
