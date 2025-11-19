using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChunaAnimCtrl : MonoBehaviour
{
    public Animator anim;

    [Range(0, 1)] public float nomalrize;

    private void Update()
    {
        anim.Play("±¼°î1", -1, nomalrize);
    }
}
