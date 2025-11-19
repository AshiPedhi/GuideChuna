using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EventManager : MonoBehaviour
{
    public OVRPassthroughLayer passthroughLayer;
    public GameObject BackObj;
    public bool realOn = true;
    void Start()
    {
        GameObject ovrCameraRig = GameObject.Find("OVRCameraRig");
        if (ovrCameraRig == null)
        {
            Debug.LogError("Scene does not contain an OVRCameraRig");
            return;
        }

        passthroughLayer = ovrCameraRig.GetComponent<OVRPassthroughLayer>();
        if (passthroughLayer == null)
        {
            passthroughLayer = ovrCameraRig.AddComponent<OVRPassthroughLayer>();
            // Debug.LogError("OVRCameraRig does not contain an OVRPassthroughLayer component");
        }

        //passthroughLayer.hidden = true;


        // Passthrough 활성화 . 내가 추가함
        if (passthroughLayer != null)
        {
                passthroughLayer.hidden = !realOn;
                passthroughLayer.enabled = realOn;
                //OVRManager.instance.isInsightPassthroughEnabled = realOn;
        }

        // BackObj 비활성화
        if (BackObj != null)
        {
            BackObj.SetActive(!realOn);
        }

        if (FindAnyObjectByType<Scenario>() != null)
        {
            Scenario scm = FindAnyObjectByType<Scenario>();
            foreach (GameObject human in scm.human) human.GetComponent<SkinnedMeshRenderer>().enabled = !realOn;
            foreach (GameObject bantu in scm.humanBantu) bantu.GetComponent<SkinnedMeshRenderer>().enabled = realOn;
        }


        if (realOn)
        {
            // 카메라 설정   
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.backgroundColor = new Color(0, 0, 0, 0);
                mainCamera.clearFlags = CameraClearFlags.SolidColor;
            }
        }
    }

    //void Update()
    //{
    //    if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
    //    {
    //        passthroughLayer.hidden = !passthroughLayer.hidden;
    //    }

    //    float thumbstickX = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch).x;
    //    passthroughLayer.textureOpacity = thumbstickX * 0.5f + 0.5f;
    //}

    public void OnPassthrough()
    {
        passthroughLayer.hidden = !passthroughLayer.hidden;
        BackObj.SetActive(passthroughLayer.hidden);
        Camera.main.backgroundColor = new Color(0, 0, 0, passthroughLayer.hidden ? 1 : 0);
        Camera.main.clearFlags = passthroughLayer.hidden ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
    }
}
