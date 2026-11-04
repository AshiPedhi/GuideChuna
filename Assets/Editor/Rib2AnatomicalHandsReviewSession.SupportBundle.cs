using System;
using System.IO;
using UnityEngine;

public sealed partial class Rib2AnatomicalHandsReviewSession
{
    [Serializable] private sealed class SupportBundle
    {
        public float[] supportRootWorldPosition = null, supportRootWorldRotation = null;
        public float[] contactWorld = null, contactNormalWorld = null;
        public int supportContactVertex = -1;
    }
    private bool supportWorldBundleLoaded;
    private Vector3 bundledContactNormalWorld;
    public string SupportBundleHash { get; private set; }

    private void LoadSupportBundle(string filename, string expectedHash)
    {
        if (string.IsNullOrEmpty(filename) || Path.GetFileName(filename) != filename)
            throw new InvalidDataException("Support bundle must be a file inside V03.");
        string path = Absolute(Folder + "/" + filename);
        SupportBundleHash = FileHash(path);
        if (SupportBundleHash != expectedHash) throw new InvalidDataException("Support bundle changed after two-view hand fitting.");
        var bundle = JsonUtility.FromJson<SupportBundle>(File.ReadAllText(path));
        if (bundle == null || bundle.supportRootWorldPosition == null || bundle.supportRootWorldPosition.Length != 3 ||
            bundle.supportRootWorldRotation == null || bundle.supportRootWorldRotation.Length != 4 ||
            bundle.contactWorld == null || bundle.contactWorld.Length != 3 || bundle.contactNormalWorld == null || bundle.contactNormalWorld.Length != 3)
            throw new InvalidDataException("Support bundle needs a complete rigid pose and measured torso contact frame.");
        var geometry = support.ExportGeometry();
        if (bundle.supportContactVertex < 0 || bundle.supportContactVertex >= geometry.vertices.Length)
            throw new InvalidDataException("Support bundle contact vertex is absent from the actual coarse hand mesh.");
        synchronizedSupportVertex = bundle.supportContactVertex;
        synchronizedSupportHeelLocalMetres = geometry.vertices[synchronizedSupportVertex];
        Quaternion rotation = BodycamRotation(bundle.supportRootWorldRotation);
        Vector3 rootPosition = V3(bundle.supportRootWorldPosition), contact = V3(bundle.contactWorld);
        float rootGap = Vector3.Distance(rootPosition + rotation * synchronizedSupportHeelLocalMetres, contact);
        if (float.IsNaN(rootGap) || float.IsInfinity(rootGap) || rootGap > .002f)
            throw new InvalidDataException("Support bundle root pose and actual selected skin vertex disagree by " + rootGap + "m.");
        bundledContactNormalWorld = V3(bundle.contactNormalWorld).normalized;
        if (!(bundledContactNormalWorld.sqrMagnitude > .99f)) throw new InvalidDataException("Support bundle has no measured contact surface normal.");
        synchronizedSupportChestRotation = Quaternion.Inverse(supportBodyFrame.rotation) * rotation;
        supportAnchorLocal = supportBodyFrame.InverseTransformPoint(contact);
        supportNormalLocal = supportBodyFrame.InverseTransformDirection(-bundledContactNormalWorld);
        hasSynchronizedSupport = supportWorldBundleLoaded = true;
        Debug.Log("[Rib2ChestSupport] Static two-view bundle loaded; real contact vertex=" + synchronizedSupportVertex +
            "; pose/contact agreement=" + (rootGap * 1000f).ToString("F5") + "mm. Torso-only Native triangle verification follows at first sample.");
    }

    private void ApplySupportWorldBundle()
    {
        supportContact = supportBodyFrame.TransformPoint(supportAnchorLocal);
        if (!supportAnchorReady)
        {
            BakeTorsoQueries();
            float gap = float.PositiveInfinity;
            Vector3 measuredNormal = bundledContactNormalWorld;
            foreach (var query in torsoOnlyQueries)
            {
                // A fitted contact can be exactly on a mesh vertex or boundary,
                // where a Physics ray misses it. Measure distance to the actual
                // baked, torso-labelled triangles without dropping edge cases.
                Vector3[] normals = query.baked.normals;
                Matrix4x4 normalMatrix = query.source.localToWorldMatrix.inverse.transpose;
                for (int i = 0; i + 2 < query.filteredIndices.Length; i += 3)
                {
                    int ia = query.filteredIndices[i], ib = query.filteredIndices[i + 1], ic = query.filteredIndices[i + 2];
                    Vector3 a = query.source.transform.TransformPoint(query.vertices[ia]);
                    Vector3 b = query.source.transform.TransformPoint(query.vertices[ib]);
                    Vector3 c = query.source.transform.TransformPoint(query.vertices[ic]);
                    Vector3 bary = SupportClosestBarycentric(supportContact, a, b, c);
                    float distance = Vector3.Distance(supportContact, a * bary.x + b * bary.y + c * bary.z);
                    if (distance >= gap) continue;
                    gap = distance;
                    if (normals.Length > Math.Max(ia, Math.Max(ib, ic)))
                        measuredNormal = normalMatrix.MultiplyVector(normals[ia] * bary.x + normals[ib] * bary.y + normals[ic] * bary.z).normalized;
                }
            }
            if (gap > .002f) throw new InvalidDataException("Bundled support contact is not on the actual positively identified torso surface; gap=" + gap + "m.");
            supportNormalLocal = supportBodyFrame.InverseTransformDirection(-measuredNormal);
            supportAnchorReady = true;
            Debug.Log("[Rib2ChestSupport] Bundled contact verified on actual torso-only skin; gap=" + (gap * 1000f).ToString("F5") + "mm. Same material location is held in the chest frame.");
        }
        PlaceSynchronizedSupport();
    }

    private static Vector3 SupportClosestBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return new Vector3(1f, 0f, 0f);
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return new Vector3(0f, 1f, 0f);
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); return new Vector3(1f - v, v, 0f); }
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return new Vector3(0f, 0f, 1f);
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); return new Vector3(1f - w, 0f, w); }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
        { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return new Vector3(0f, 1f - w, w); }
        float sum = va + vb + vc;
        if (Mathf.Abs(sum) < 1e-20f) return new Vector3(1f, 0f, 0f);
        return new Vector3(va / sum, vb / sum, vc / sum);
    }
}
