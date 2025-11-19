using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class QuikMenuText : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        CurrentUser();
        runtime = 0;
    }

    // Update is called once per frame
    void Update()
    {
        Runtime();
    }

    public TextMeshPro user;
    public TextMeshPro runtimeText;
    public float runtime;

    public void CurrentUser()
    {
        user.text = AuthManager.instance.currentRunUser;
    }

    public void Runtime()
    {
        runtime += Time.deltaTime;

        int m = (int)(runtime / 60);
        int s = (int)(runtime % 60);

        runtimeText.text = m.ToString("D2") + ":" + s.ToString("D2");
    }
}
