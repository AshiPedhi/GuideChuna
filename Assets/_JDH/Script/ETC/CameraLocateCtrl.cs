using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraLocateCtrl : MonoBehaviour
{
    GameObject cam;

    private void Awake()
    {
        cam = GameObject.Find("Camera X");

        Debug.Log(cam);

        if (cam == null)
            return;

        cam.transform.SetParent(transform);
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.Euler(0, 0, 0);
    }
}
