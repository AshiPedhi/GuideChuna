using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIHight : MonoBehaviour
{
    public GameObject centerEyeAnchor;

    private void Update()
    {
        transform.localPosition = new Vector3(transform.localPosition.x, centerEyeAnchor.transform.localPosition.y - 0.1f, transform.localPosition.z);
    }
}
