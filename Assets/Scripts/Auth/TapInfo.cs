using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TapInfo : MonoBehaviour
{
    public int tapNum;
    public void ChangeTap()
    {
        if (GetComponent<Toggle>().isOn)
        {
            GetComponent<Toggle>().isOn = true;
        }

        for (int i = 0; i < AuthUI.instance.contents.Count; i++)
        {
            AuthUI.instance.contents[i].SetActive(false);
        }

        AuthUI.instance.contents[tapNum].SetActive(true);
        AuthUI.instance.scrollRect.content = AuthUI.instance.contents[tapNum].GetComponent<RectTransform>();
    }
}
