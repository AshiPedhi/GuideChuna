using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Isolated presentation player for the video/text-authored Rib 2 draft.
/// It only writes cloned guide-hand transforms. It has no scenario, scoring,
/// collision, tracking, Resources, or patient IK dependency.
/// Editor previews explicitly call Seek; there is no ExecuteAlways update.
/// </summary>
public sealed class Rib2VideoTextScenePlayer : MonoBehaviour
{
    private const int JointCount = 26;
    private const float CycleStartSeconds = 3.75f;
    private const float MinimumQuaternionLength = 0.000001f;

    [Tooltip("Only the separately authored video_text_demo.csv TextAsset belongs here.")]
    [SerializeField] private TextAsset motionCsv = null;
    [SerializeField] private Transform motionFrame = null;
    [SerializeField] private HandTransformMapper leftGuideHand = null;
    [SerializeField] private HandTransformMapper rightGuideHand = null;
    [Tooltip("Patient wrist target authored under the cloned left guide hand. This player does not run IK.")]
    [SerializeField] private Transform leftGripAnchor = null;
    [Tooltip("Model placement rotation after the authored left wrist rotation. Does not change its trajectory or any pass condition.")]
    [SerializeField] private Vector3 leftRetargetEuler = Vector3.zero;
    [SerializeField] private Vector3 rightRetargetEuler = Vector3.zero;
    [SerializeField] private bool autoPlay = false;

    private sealed class Frame
    {
        public int index;
        public float time;
        public Vector3 leftRootPosition;
        public Quaternion leftRootRotation;
        public Vector3 rightRootPosition;
        public Quaternion rightRootRotation;
        public readonly Vector3[] leftPositions = new Vector3[JointCount];
        public readonly Quaternion[] leftRotations = new Quaternion[JointCount];
        public readonly Vector3[] rightPositions = new Vector3[JointCount];
        public readonly Quaternion[] rightRotations = new Quaternion[JointCount];
        public readonly bool[] leftPresent = new bool[JointCount];
        public readonly bool[] rightPresent = new bool[JointCount];
        public bool leftRootPresent;
        public bool rightRootPresent;
    }

    [NonSerialized] private Frame[] frames;
    [NonSerialized] private TextAsset loadedAsset;
    [NonSerialized] private bool initialized;
    [NonSerialized] private bool isPlaying;
    [NonSerialized] private bool halfSpeed;
    [NonSerialized] private float currentTime;
    [NonSerialized] private float duration;
    [NonSerialized] private float playbackStart;
    [NonSerialized] private float playbackEnd;
    [NonSerialized] private string lastFailure;
    [NonSerialized] private Quaternion leftRetargetRotation = Quaternion.identity;
    [NonSerialized] private Quaternion rightRetargetRotation = Quaternion.identity;

    public float CurrentTime => currentTime;
    public float Duration => duration;
    public bool IsPlaying => isPlaying;

    private void Awake()
    {
        TryInitialize(out _);
    }

    private void OnEnable()
    {
        if (TryInitialize(out _) && Application.isPlaying && autoPlay) PlayFull();
    }

    private void OnDisable()
    {
        isPlaying = false;
    }

    private void Update()
    {
        if (!Application.isPlaying || !isPlaying || !initialized) return;
        float nextTime = currentTime + Time.deltaTime * (halfSpeed ? .5f : 1f);
        bool ended = nextTime >= playbackEnd;
        if (!ApplySampleAtTime(ended ? playbackEnd : nextTime))
        {
            isPlaying = false;
            return;
        }
        if (ended) isPlaying = false;
    }

    /// <summary>Loads the assigned TextAsset once; later calls reuse the cached frames.</summary>
    public bool TryInitialize(out string error)
    {
        if (!ValidateReferences(out error)) return Fail(error);
        CacheRetargetRotations();
        if (initialized && loadedAsset == motionCsv)
        {
            lastFailure = null;
            return true;
        }

        initialized = false;
        isPlaying = false;
        frames = null;
        try
        {
            frames = ReadFrames(motionCsv.text);
            loadedAsset = motionCsv;
            duration = frames[frames.Length - 1].time;
            currentTime = 0f;
            playbackStart = 0f;
            playbackEnd = duration;
            initialized = true;
            lastFailure = null;
            error = null;
            Debug.Log("[Rib2SceneDemo] Initialized separate video/text draft: " + motionCsv.name +
                "; frames=" + frames.Length + "; duration=" + duration.ToString("F3", CultureInfo.InvariantCulture) +
                "s; joints=L26/R26; motionFrame=" + motionFrame.name + "; scale=" + motionFrame.lossyScale +
                "; leftGripAnchor=" + leftGripAnchor.name + "; no assessment or patient IK calls.", this);
            Frame first = frames[0];
            Debug.Log("[Rib2SceneDemo] Initial transform: left input=" + first.leftRootPosition +
                " -> world=" + motionFrame.TransformPoint(first.leftRootPosition) +
                "; right input=" + first.rightRootPosition + " -> world=" +
                motionFrame.TransformPoint(first.rightRootPosition) +
                "; root rotation=motionFrame.rotation * inputRotation * retargetRotation; retarget Euler L=" +
                leftRetargetEuler + ", R=" + rightRetargetEuler + "; source depth is authored, not measured.", this);
            ApplyFramePair(first, first, 0f);
            return true;
        }
        catch (Exception exception)
        {
            initialized = false;
            frames = null;
            loadedAsset = null;
            currentTime = 0f;
            duration = 0f;
            error = exception.Message;
            return Fail("CSV initialization failed: " + error);
        }
    }

    public bool Seek(float seconds)
    {
        return ApplySampleAtTime(seconds);
    }

    /// <summary>Shared runtime/editor sample path. No file reads or allocations after initialization.</summary>
    public bool ApplySampleAtTime(float seconds)
    {
        if (!Finite(seconds)) return Fail("Requested sample time is not finite.");
        if (!initialized || loadedAsset != motionCsv)
        {
            if (!TryInitialize(out _)) return false;
        }
        if (!ValidateMotionFrame(out string referenceError)) return Fail(referenceError);
        if (leftGuideHand == null || rightGuideHand == null)
            return Fail("A configured guide hand was removed after initialization.");
        // Removed: time outside this authored clip. This never clamps a clinical angle or patient movement.
        currentTime = Mathf.Clamp(seconds, 0f, duration);
        int low = 0;
        int high = frames.Length - 1;
        while (low + 1 < high)
        {
            int middle = (low + high) / 2;
            if (frames[middle].time <= currentTime) low = middle;
            else high = middle;
        }
        Frame a = frames[low];
        Frame b = frames[high];
        float span = b.time - a.time;
        float blend = span > 0f ? (currentTime - a.time) / span : 0f;
        ApplyFramePair(a, b, blend);
        return true;
    }

    public void ShowStartPose()
    {
        isPlaying = false;
        Seek(0f);
    }

    public void PlayFull()
    {
        if (!TryInitialize(out _)) return;
        playbackStart = 0f;
        playbackEnd = duration;
        if (Seek(playbackStart)) isPlaying = duration > 0f;
    }

    public void PlayCycle()
    {
        if (!TryInitialize(out _)) return;
        // Removed: requested cycle start beyond a shorter clip; endpoint observations are never forced to close.
        playbackStart = Mathf.Min(CycleStartSeconds, duration);
        playbackEnd = duration;
        if (Seek(playbackStart)) isPlaying = playbackEnd > playbackStart;
    }

    public void SetHalfSpeed(bool enabled)
    {
        halfSpeed = enabled;
    }

    public void SetPlaying(bool playing)
    {
        if (!playing)
        {
            isPlaying = false;
            return;
        }
        if (!TryInitialize(out _)) return;
        if (currentTime >= playbackEnd && !Seek(playbackStart)) return;
        isPlaying = playbackEnd > currentTime;
    }

    private void ApplyFramePair(Frame a, Frame b, float blend)
    {
        // Dependence: all source XYZ, source rotations, the full motionFrame transform,
        // and all 26 joint poses affect the displayed hands. No axis is flattened here.
        ApplyHand(leftGuideHand, a.leftRootPosition, b.leftRootPosition,
            a.leftRootRotation, b.leftRootRotation, a.leftPositions, b.leftPositions,
            a.leftRotations, b.leftRotations, leftRetargetRotation, blend);
        ApplyHand(rightGuideHand, a.rightRootPosition, b.rightRootPosition,
            a.rightRootRotation, b.rightRootRotation, a.rightPositions, b.rightPositions,
            a.rightRotations, b.rightRotations, rightRetargetRotation, blend);
    }

    private void ApplyHand(HandTransformMapper mapper, Vector3 rootA, Vector3 rootB,
        Quaternion rotationA, Quaternion rotationB, Vector3[] positionsA, Vector3[] positionsB,
        Quaternion[] rotationsA, Quaternion[] rotationsB, Quaternion retargetRotation, float blend)
    {
        mapper.SetRootWorldPose(motionFrame.TransformPoint(Vector3.LerpUnclamped(rootA, rootB, blend)),
            motionFrame.rotation * Quaternion.SlerpUnclamped(rotationA, rotationB, blend) * retargetRotation);
        for (int joint = 0; joint < JointCount; joint++)
            mapper.SetJointLocalPose(joint,
                Vector3.LerpUnclamped(positionsA[joint], positionsB[joint], blend),
                Quaternion.SlerpUnclamped(rotationsA[joint], rotationsB[joint], blend));
    }

    private bool ValidateReferences(out string error)
    {
        if (motionCsv == null) { error = "motionCsv is missing."; return false; }
        if (!Finite(leftRetargetEuler) || !Finite(rightRetargetEuler))
        { error = "A model retarget rotation is not finite."; return false; }
        if (!ValidateMotionFrame(out error)) return false;
        if (!ValidateHand(leftGuideHand, "Left", out error) || !ValidateHand(rightGuideHand, "Right", out error)) return false;
        if (leftGripAnchor == null) { error = "leftGripAnchor is missing."; return false; }
        if (!leftGripAnchor.IsChildOf(leftGuideHand.transform))
        { error = "leftGripAnchor must be a child of the cloned left guide hand."; return false; }
        error = null;
        return true;
    }

    private bool ValidateMotionFrame(out string error)
    {
        if (motionFrame == null) { error = "motionFrame is missing."; return false; }
        if (!Finite(motionFrame.position) || !Finite(motionFrame.rotation) || !Finite(motionFrame.lossyScale))
        { error = "motionFrame contains nonfinite transform values."; return false; }
        Vector3 scale = motionFrame.lossyScale;
        if (scale.x <= .000001f || scale.y <= .000001f || scale.z <= .000001f)
        { error = "motionFrame requires positive scale on all axes; mirrored or zero scale is unsupported."; return false; }
        float tolerance = Mathf.Max(.000001f, scale.x * .0001f);
        // Absolute values compare scale mismatch only; source positions and rotations retain their signs.
        if (Mathf.Abs(scale.x - scale.y) > tolerance || Mathf.Abs(scale.x - scale.z) > tolerance)
        { error = "motionFrame requires a uniform positive world scale; nonuniform scale would skew the hand path."; return false; }
        Matrix4x4 matrix = motionFrame.localToWorldMatrix;
        float determinant = matrix.determinant;
        if (!Finite(determinant) || determinant <= 0f)
        { error = "motionFrame hierarchy must have a finite positive determinant; mirrored ancestry is unsupported."; return false; }
        Vector3 x = matrix.GetColumn(0);
        Vector3 y = matrix.GetColumn(1);
        Vector3 z = matrix.GetColumn(2);
        float squaredScale = scale.x * scale.x;
        if (!Finite(squaredScale))
        { error = "motionFrame scale magnitude is invalid."; return false; }
        // These comparisons detect parent-induced shear; they do not project or flatten the motion itself.
        if (Mathf.Abs(Vector3.Dot(x, y)) > squaredScale * .0001f ||
            Mathf.Abs(Vector3.Dot(x, z)) > squaredScale * .0001f ||
            Mathf.Abs(Vector3.Dot(y, z)) > squaredScale * .0001f)
        { error = "motionFrame hierarchy contains shear; use a positive uniform-scale parent frame."; return false; }
        error = null;
        return true;
    }

    private static bool ValidateHand(HandTransformMapper mapper, string label, out string error)
    {
        if (mapper == null || mapper.Root == null)
        { error = label + " cloned guide hand or its OpenXR root is missing."; return false; }
        if (mapper.GetJointCount() != JointCount)
        { error = label + " guide hand must have exactly 26 mapped joints."; return false; }
        for (int i = 0; i < JointCount; i++)
        {
            if (mapper.GetJoint(i) != null) continue;
            error = label + " guide hand joint " + i + " is missing.";
            return false;
        }
        error = null;
        return true;
    }

    private bool Fail(string error)
    {
        isPlaying = false;
        if (error != lastFailure) Debug.LogWarning("[Rib2SceneDemo] " + error, this);
        lastFailure = error;
        return false;
    }

    private void CacheRetargetRotations()
    {
        leftRetargetRotation = Quaternion.Euler(leftRetargetEuler);
        rightRetargetRotation = Quaternion.Euler(rightRetargetEuler);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Finite(leftRetargetEuler) && Finite(rightRetargetEuler)) CacheRetargetRotations();
    }
#endif

    private static Frame[] ReadFrames(string csv)
    {
        var result = new List<Frame>();
        using (var reader = new StringReader(csv))
        {
            string header = reader.ReadLine();
            if (header == null || !header.TrimStart('\uFEFF').StartsWith("FrameIndex,HandType,JointID,", StringComparison.Ordinal))
                throw new FormatException("Expected the separate 18-column hand draft CSV header.");
            Frame current = null;
            string line;
            int lineNumber = 1;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    string[] columns = line.Split(',');
                    if (columns.Length != 18) throw new FormatException("Expected exactly 18 columns.");
                    int index = int.Parse(columns[0], CultureInfo.InvariantCulture);
                    int joint = int.Parse(columns[2], CultureInfo.InvariantCulture);
                    float time = ReadFloat(columns[10]);
                    if (index < 0 || joint < 0 || joint >= JointCount || time < 0f)
                        throw new FormatException("Invalid frame, joint index, or negative timestamp.");
                    if (current == null || current.index != index)
                    {
                        if (current != null)
                        {
                            ValidateFrame(current);
                            if (index <= current.index || time <= current.time)
                                throw new FormatException("Frames and timestamps must increase strictly.");
                        }
                        current = new Frame { index = index, time = time };
                        result.Add(current);
                    }
                    if (Mathf.Abs(current.time - time) > .00001f)
                        throw new FormatException("Joint timestamps disagree within a frame.");
                    bool left = columns[1] == "Left";
                    if (!left && columns[1] != "Right") throw new FormatException("HandType must be Left or Right.");
                    bool[] present = left ? current.leftPresent : current.rightPresent;
                    if (present[joint]) throw new FormatException("Duplicate hand joint in a frame.");
                    present[joint] = true;
                    (left ? current.leftPositions : current.rightPositions)[joint] = ReadVector(columns, 3);
                    (left ? current.leftRotations : current.rightRotations)[joint] = ReadRotation(columns, 6);
                    if (!string.IsNullOrWhiteSpace(columns[11]))
                    {
                        if (left ? current.leftRootPresent : current.rightRootPresent)
                            throw new FormatException("Duplicate root data for a hand.");
                        Vector3 rootPosition = ReadVector(columns, 11);
                        Quaternion rootRotation = ReadRotation(columns, 14);
                        if (left)
                        {
                            current.leftRootPosition = rootPosition;
                            current.leftRootRotation = rootRotation;
                            current.leftRootPresent = true;
                        }
                        else
                        {
                            current.rightRootPosition = rootPosition;
                            current.rightRootRotation = rootRotation;
                            current.rightRootPresent = true;
                        }
                    }
                    else
                    {
                        for (int column = 12; column < 18; column++)
                            if (!string.IsNullOrWhiteSpace(columns[column]))
                                throw new FormatException("Incomplete root data.");
                    }
                }
                catch (Exception exception)
                {
                    throw new FormatException("CSV line " + lineNumber + ": " + exception.Message, exception);
                }
            }
            if (current != null) ValidateFrame(current);
        }
        if (result.Count < 2) throw new FormatException("At least two complete frames are required.");
        float origin = result[0].time;
        for (int i = 0; i < result.Count; i++) result[i].time -= origin;
        return result.ToArray();
    }

    private static void ValidateFrame(Frame frame)
    {
        if (!frame.leftRootPresent || !frame.rightRootPresent)
            throw new FormatException("Frame " + frame.index + " is missing a hand root.");
        for (int i = 0; i < JointCount; i++)
            if (!frame.leftPresent[i] || !frame.rightPresent[i])
                throw new FormatException("Frame " + frame.index + " is missing joint " + i + ".");
    }

    private static float ReadFloat(string value)
    {
        float number = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!Finite(number)) throw new FormatException("Nonfinite numeric value.");
        return number;
    }

    private static Vector3 ReadVector(string[] values, int start)
    {
        return new Vector3(ReadFloat(values[start]), ReadFloat(values[start + 1]), ReadFloat(values[start + 2]));
    }

    private static Quaternion ReadRotation(string[] values, int start)
    {
        var rotation = new Quaternion(ReadFloat(values[start]), ReadFloat(values[start + 1]),
            ReadFloat(values[start + 2]), ReadFloat(values[start + 3]));
        float lengthSquared = Quaternion.Dot(rotation, rotation);
        if (!Finite(lengthSquared) || lengthSquared < MinimumQuaternionLength)
            throw new FormatException("Invalid quaternion magnitude.");
        // Removed: quaternion magnitude drift from text rounding; its orientation remains unchanged.
        float inverseLength = 1f / Mathf.Sqrt(lengthSquared);
        return new Quaternion(rotation.x * inverseLength, rotation.y * inverseLength,
            rotation.z * inverseLength, rotation.w * inverseLength);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
}
