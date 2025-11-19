using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LookAtMe : MonoBehaviour
{
    public GameObject target;
    Vector3 targetPosition;

    // Update is called once per frame
    void Update()
    {
        //targetPosition = new Vector3(target.transform.position.x, transform.position.y, transform.position.z);

        targetPosition = target.transform.position;

        transform.LookAt(targetPosition); 
        transform.eulerAngles = new Vector3(-transform.eulerAngles.x, 0, transform.eulerAngles.z);
        //transform.localRotation = Quaternion.Euler(transform.localRotation.eulerAngles.x , 0, 0);
    }
}
