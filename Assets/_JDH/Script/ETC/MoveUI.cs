using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveUI : MonoBehaviour
{
    public GameObject HUD;

    public Transform HUDP;
    public Transform HUUI;

    Transform tf;

    private void Awake()
    {
        tf = transform;
        //transform.position = new Vector3(HUD.transform.position.x, 1, HUD.transform.position.z);
        transform.position = HUD.transform.position;
    }

    Vector3 CAMV, HUIV;

    void Update()
    {
        //transform.position = new Vector3(HUD.transform.position.x, 1, HUD.transform.position.z);
        transform.position = HUD.transform.position;

        CAMV = HUDP.position - tf.position;
        HUIV = HUUI.position - tf.position;

        CAMV = new Vector3(CAMV.x, 0, CAMV.z);
        HUIV = new Vector3(HUIV.x, 0, HUIV.z);

        //Debug.LogWarning("camv : " + CAMV + " huiv : " + HUIV + " �¿찢 : " + Mathf.Rad2Deg * Mathf.Acos(dot / mag));
        
        Quaternion Q = HUD.transform.rotation;
        Q.x = 0;
        Q.z = 0;
    }
}
