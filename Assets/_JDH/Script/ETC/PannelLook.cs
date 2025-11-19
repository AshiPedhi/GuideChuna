using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PannelLook : MonoBehaviour
{
    public GameObject target;
    public GameObject gParent;
    public GameObject nParent;
    public bool grab = false;

    void Update()
    {
        if(grab)
        {
            Vector3 vector = target.transform.position - transform.position;
            transform.rotation = Quaternion.LookRotation(-vector).normalized;
        }
    }

    public void SelectP()
    {
        grab = true;
        transform.parent.SetParent(gParent.transform);
    }

    public void UnselectP()
    {
        grab = false;
        transform.parent.SetParent(nParent.transform);
    }
}
