using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ReplacePos : MonoBehaviour
{
    public GameObject all;

    // Update is called once per frame
    void Update()
    {
        all.transform.position = GetComponent<Transform>().position;
        all.transform.rotation = GetComponent<Transform>().rotation;
    }
}
