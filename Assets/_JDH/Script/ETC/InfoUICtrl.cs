using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class InfoUICtrl : MonoBehaviour
{
    [Header("UI추가 요소")]
    public TextMeshProUGUI infotext;
    public Image[] step_state;
    public Image[] fmb_state;
    public GameObject info_menu;
    public Button button_use;
    public Color waiting;
    public Color doing;
    public Color done;
    public Color fmb_active;
}
