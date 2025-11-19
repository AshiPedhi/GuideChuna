using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ToStartScene : MonoBehaviour
{
    public GameObject[] mainObj;

    private void Start()
    {
        StartCoroutine(LoadTime());
    }

    IEnumerator LoadTime()
    {
        yield return new WaitForSeconds(0.5f);

        foreach (GameObject obj in mainObj)
        {
            if(obj.name != "UIObject" && obj.name != "각도보여주기")
                obj.SetActive(true);
        }

        yield return new WaitForSeconds(1f);

        foreach (GameObject obj in mainObj)
        {
            if (obj.name == "UIObject" || obj.name == "각도보여주기")
                obj.SetActive(true);
        }

        gameObject.SetActive(false);
    }
}
