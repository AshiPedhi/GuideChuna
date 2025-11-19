using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandGrabOnOff : MonoBehaviour
{
    GameObject[] HandGrabInteractables;

    public void SearchHandGrabInteractables()
    {
        HandGrabInteractables = GameObject.FindGameObjectsWithTag("Inter");
    }

    public void HandGrabInteractableOnOff(bool on)
    {
        if(HandGrabInteractables != null)
        {
            foreach (GameObject obj in HandGrabInteractables)
            {
                obj.SetActive(on);
            }
        }
    }

    public void ClearHandGrabInteractables()
    {
        HandGrabInteractables = new GameObject[0];
    }
}