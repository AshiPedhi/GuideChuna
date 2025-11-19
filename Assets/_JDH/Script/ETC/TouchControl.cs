using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class TouchControl : MonoBehaviour
{
    public string targetTag;

    [SerializeField]
    UnityEvent WhenOnTriggerEnter;

    [SerializeField]
    UnityEvent WhenOnTriggerStay;

    [SerializeField]
    UnityEvent WhenOnTriggerExit;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            if (WhenOnTriggerEnter != null)
                WhenOnTriggerEnter.Invoke();
        }
    }
    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            if (WhenOnTriggerEnter != null)
                WhenOnTriggerEnter.Invoke();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            if (WhenOnTriggerExit != null)
                WhenOnTriggerExit.Invoke();
        }
    }
}
