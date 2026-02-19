using UnityEngine;

/// <summary>
/// Hand collision detection helper for ChunaPathEvaluator.
/// Pure math/physics calculations - stateless.
/// Supports sphere, box, and palm-only collision shapes.
/// </summary>
public class HandCollisionDetector
{
    /// <summary>
    /// Check collision between hand and patient bounds using the specified shape.
    /// </summary>
    public bool CheckHandCollision(
        Transform handTransform, Collider handCollider, Bounds patientBounds, bool isLeftHand,
        ChunaPathEvaluator.HandCollisionShape shape, float colliderScale,
        float defaultRadius, float forwardOffset,
        float palmWidth, float palmThickness, float palmHeight, float fingerLength)
    {
        Vector3 handCenter;
        Vector3 handForward = handTransform.forward;

        // Determine hand center position
        if (handCollider != null)
        {
            handCenter = handCollider.bounds.center;
        }
        else
        {
            handCenter = handTransform.position;
        }

        // Apply forward offset
        handCenter += handForward * forwardOffset;

        switch (shape)
        {
            case ChunaPathEvaluator.HandCollisionShape.Sphere:
                return CheckSphereCollision(handCenter, handCollider, patientBounds, colliderScale, defaultRadius);

            case ChunaPathEvaluator.HandCollisionShape.Box:
                Vector3 boxSize = new Vector3(
                    palmWidth * colliderScale,
                    palmThickness * colliderScale,
                    (palmHeight + fingerLength) * colliderScale
                );
                Vector3 boxCenter = handCenter + handForward * (fingerLength * 0.3f * colliderScale);
                return CheckBoxCollision(boxCenter, boxSize, handTransform.rotation, patientBounds);

            case ChunaPathEvaluator.HandCollisionShape.PalmOnly:
                Vector3 palmSize = new Vector3(
                    palmWidth * colliderScale,
                    palmThickness * colliderScale,
                    palmHeight * colliderScale
                );
                return CheckBoxCollision(handCenter, palmSize, handTransform.rotation, patientBounds);

            default:
                return CheckSphereCollision(handCenter, handCollider, patientBounds, colliderScale, defaultRadius);
        }
    }

    /// <summary>
    /// Sphere collision check.
    /// </summary>
    public bool CheckSphereCollision(Vector3 handCenter, Collider handCollider, Bounds patientBounds, float colliderScale, float defaultRadius)
    {
        float handRadius;
        if (handCollider != null)
        {
            handRadius = Mathf.Max(handCollider.bounds.extents.x,
                handCollider.bounds.extents.y, handCollider.bounds.extents.z) * colliderScale;
        }
        else
        {
            handRadius = defaultRadius * colliderScale;
        }

        float patientRadius = Mathf.Max(patientBounds.extents.x, patientBounds.extents.y, patientBounds.extents.z);
        float distance = Vector3.Distance(handCenter, patientBounds.center);

        return distance <= (handRadius + patientRadius);
    }

    /// <summary>
    /// Box collision check (OBB vs AABB simplified).
    /// </summary>
    public bool CheckBoxCollision(Vector3 boxCenter, Vector3 boxSize, Quaternion boxRotation, Bounds patientBounds)
    {
        Vector3 halfSize = boxSize * 0.5f;
        Vector3[] corners = new Vector3[8];
        int idx = 0;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 localCorner = new Vector3(halfSize.x * x, halfSize.y * y, halfSize.z * z);
                    corners[idx++] = boxCenter + boxRotation * localCorner;
                }
            }
        }

        // Check if any corner is inside patient bounds
        foreach (var corner in corners)
        {
            if (patientBounds.Contains(corner))
                return true;
        }

        // Reverse check: patient center inside hand box
        Vector3 patientCenterLocal = Quaternion.Inverse(boxRotation) * (patientBounds.center - boxCenter);
        if (Mathf.Abs(patientCenterLocal.x) <= halfSize.x &&
            Mathf.Abs(patientCenterLocal.y) <= halfSize.y &&
            Mathf.Abs(patientCenterLocal.z) <= halfSize.z)
        {
            return true;
        }

        // Expanded bounds check
        Bounds expandedBounds = patientBounds;
        expandedBounds.Expand(boxSize.magnitude * 0.5f);
        if (expandedBounds.Contains(boxCenter))
        {
            return true;
        }

        return false;
    }
}
