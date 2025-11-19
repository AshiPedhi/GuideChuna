using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TraceRP : MonoBehaviour
{
    public GameObject traceTarget;
    public bool tp;
    public bool tr;
    public bool staticZ = false;

    // Update is called once per frame
    void Update()
    {
        if(tp)
        {
            if (staticZ)
                transform.localPosition = new Vector3(traceTarget.transform.localPosition.x, traceTarget.transform.localPosition.y / 2, 0);
            else
                transform.localPosition = new Vector3(traceTarget.transform.localPosition.x, traceTarget.transform.localPosition.y / 2, traceTarget.transform.localPosition.z);
        }

        if (tr)
            transform.localRotation = traceTarget.transform.localRotation;
   }
}
