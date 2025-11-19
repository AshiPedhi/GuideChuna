using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Minimax : MonoBehaviour
{
    public GameObject menu;
    public bool menu_off;
    public TextMeshPro buttonText;

    private void Start()
    {
        buttonText.text = "메뉴 닫기";
    }

    public void ControllMenu()
    {
        if(menu_off)
        {
            menu.SetActive(true);
            menu_off = false;

            buttonText.text = "메뉴 닫기";
        }
        else
        {
            menu.SetActive(false);
            menu_off = true;

            buttonText.text = "메뉴 열기";
        }
    }
}
