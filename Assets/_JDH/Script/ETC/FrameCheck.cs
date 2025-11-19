using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FrameCheck : MonoBehaviour
{
    [Range(1, 100)]
    public int fFont_Size;
    [Range(0, 1)]
    public float Red, Green, Blue;

    float deltaTime = 0.0f;

    public TMPro.TextMeshProUGUI fps_display;
    UnityEngine.UI.Text uut;

    private void Start()
    {
        fFont_Size = fFont_Size == 0 ? 50 : fFont_Size;

        StartCoroutine(FPS_Dis());
    }

    void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
    }

    IEnumerator FPS_Dis()
    {
        while (true)
        {
            float fps = 1.0f / Time.deltaTime;
            fps_display.text = fps.ToString();

            yield return new WaitForEndOfFrame();
        }
    }
    /*
    void OnGUI()
    {
        int w = Screen.width, h = Screen.height;

        GUIStyle style = new GUIStyle();

        Rect rect = new Rect(0, 0, w, h * 0.02f);
        style.alignment = TextAnchor.UpperLeft;
        style.fontSize = h * 2 / fFont_Size;
        style.normal.textColor = new Color(Red, Green, Blue, 1.0f);
        string text = string.Format("{0:0.0} ms ({1:0.} fps)", msec, fps);
        GUI.Label(rect, text, style);
    }*/
}
