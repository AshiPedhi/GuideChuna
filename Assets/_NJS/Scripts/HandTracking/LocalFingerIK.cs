
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using bid = OVRSkeleton.BoneId;

public class LocalFingerIK : MonoBehaviour
{
    public HandTrackingFingerMap rFingerMap;
    public HandTrackingFingerMap lFingerMap;

    public OVRSkeleton rOvrSkeleton;
    public OVRSkeleton lOvrSkeleton;

    private void SetProperties(HTFinger d1, Transform d2, bool isLeft)
    {
        //d1.position = d2.position;
        d1.transform.rotation = isLeft ? d2.rotation : Quaternion.Euler(-d2.rotation.eulerAngles.x, d2.rotation.eulerAngles.y, -d2.rotation.eulerAngles.z);
        d1.transform.Rotate(d1.offset);
    }

    private void UpdateHand(HandTrackingFingerMap h, OVRSkeleton s, bool isLeft)
    {
        {
            if (s == null || s.Bones.Count == 0)
            {
                return;
            }
            SetProperties(h.Hand_WristRoot, s.Bones[(int)bid.Hand_WristRoot].Transform, isLeft);
            SetProperties(h.Hand_Index1, s.Bones[(int)bid.Hand_Index1].Transform, isLeft);
            SetProperties(h.Hand_Index2, s.Bones[(int)bid.Hand_Index2].Transform, isLeft);
            SetProperties(h.Hand_Index3, s.Bones[(int)bid.Hand_Index3].Transform, isLeft);

            SetProperties(h.Hand_Middle1, s.Bones[(int)bid.Hand_Middle1].Transform, isLeft);
            SetProperties(h.Hand_Middle2, s.Bones[(int)bid.Hand_Middle2].Transform, isLeft);
            SetProperties(h.Hand_Middle3, s.Bones[(int)bid.Hand_Middle3].Transform, isLeft);

            SetProperties(h.Hand_Ring1, s.Bones[(int)bid.Hand_Ring1].Transform, isLeft);
            SetProperties(h.Hand_Ring2, s.Bones[(int)bid.Hand_Ring2].Transform, isLeft);
            SetProperties(h.Hand_Ring3, s.Bones[(int)bid.Hand_Ring3].Transform, isLeft);

            //SetProperties(h.Hand_Pinky0, s.Bones[(int)bid.Hand_Pinky0].Transform);
            SetProperties(h.Hand_Pinky1, s.Bones[(int)bid.Hand_Pinky1].Transform, isLeft);
            SetProperties(h.Hand_Pinky2, s.Bones[(int)bid.Hand_Pinky2].Transform, isLeft);
            SetProperties(h.Hand_Pinky3, s.Bones[(int)bid.Hand_Pinky3].Transform, isLeft);

            //SetProperties(h.Hand_Thumb0, s.Bones[(int)bid.Hand_Thumb0].Transform);
            SetProperties(h.Hand_Thumb1, s.Bones[(int)bid.Hand_Thumb1].Transform, isLeft);
            SetProperties(h.Hand_Thumb2, s.Bones[(int)bid.Hand_Thumb2].Transform, isLeft);
            SetProperties(h.Hand_Thumb3, s.Bones[(int)bid.Hand_Thumb3].Transform, isLeft);
        }
    }
    public void PositionFingers()
    {
        UpdateHand(rFingerMap, rOvrSkeleton, false);
        UpdateHand(lFingerMap, lOvrSkeleton, true);
    }

    private void LateUpdate()
    {
        PositionFingers();
    }
}