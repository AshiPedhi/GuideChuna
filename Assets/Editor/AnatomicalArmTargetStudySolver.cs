using System;
using UnityEngine;

/// <summary>
/// Editor experiment: fits a hand target by evaluating this rig's forward
/// kinematics. The bounds below are an engineering search envelope, not clinical
/// ROM limits. This solver supplies no collision or anatomical-validity test.
/// </summary>
internal sealed class AnatomicalArmTargetStudySolver
{
    private const int ParameterCount = 7;
    private const int ResidualCount = 6;
    private const double RotationScale = .15; // metres per radian, objective weighting only
    private const float DifferenceDegrees = .35f;
    private const double MaximumStepDegrees = 8.0;
    private const double MinimumDamping = .0003;
    private const double MaximumDamping = .05;

    // elevation, elevation plane, humeral rotation, elbow flexion, forearm
    // rotation, wrist flexion, wrist deviation. These do not certify safety.
    private static readonly float[] Minimum = { 2f, -25f, -65f, 0f, -80f, -55f, -25f };
    private static readonly float[] Maximum = { 165f, 155f, 65f, 145f, 80f, 55f, 25f };

    private readonly AnatomicalPatientRig rig;
    private readonly Transform hand;
    private readonly bool left;
    private readonly float[] parameters = new float[ParameterCount];
    private readonly float[] candidate = new float[ParameterCount];
    private readonly double[] residual = new double[ResidualCount];
    private readonly double[] trialResidual = new double[ResidualCount];
    private readonly double[,] jacobian = new double[ResidualCount, ParameterCount];
    private readonly double[,] augmented = new double[ResidualCount, ResidualCount + 1];
    private readonly double[] dualStep = new double[ResidualCount];
    private readonly double[] parameterStep = new double[ParameterCount];

    public int LastIterations { get; private set; }
    public bool LastConstrained { get; private set; }
    public bool LastAtEnvelope { get; private set; }
    public bool LastEvaluationSucceeded { get; private set; }
    public float LastPositionErrorMetres { get; private set; }
    public float LastRotationErrorDegrees { get; private set; }
    public float LastWeightedResidual { get; private set; }

    public AnatomicalArmTargetStudySolver(AnatomicalPatientRig rig, Transform hand, bool left)
    {
        this.rig = rig != null ? rig : throw new ArgumentNullException(nameof(rig));
        this.hand = hand != null ? hand : throw new ArgumentNullException(nameof(hand));
        this.left = left;
    }

    /// <summary>
    /// Returns the best accepted constrained pose and leaves the FK rig in that
    /// pose. Other-arm, neck, shrug and protraction values are retained from seed.
    /// Unreachable targets retain their actual nonzero hand residual.
    /// </summary>
    public AnatomicalPatientRig.Pose Solve(AnatomicalPatientRig.Pose seed,
        Vector3 targetPosition, Quaternion targetRotation, int iterations = 18)
    {
        ValidateTarget(targetPosition, targetRotation);
        targetRotation = Normalize(targetRotation);
        LastIterations = 0;
        LastConstrained = false;
        LastAtEnvelope = false;
        LastEvaluationSucceeded = false;
        LastPositionErrorMetres = float.PositiveInfinity;
        LastRotationErrorDegrees = float.PositiveInfinity;
        LastWeightedResidual = float.PositiveInfinity;

        ReadParameters(left ? seed.leftArm : seed.rightArm, parameters);
        for (int i = 0; i < ParameterCount; i++)
        {
            if (!IsFinite(parameters[i]))
                throw new ArgumentException("The selected arm seed contains a nonfinite parameter.", nameof(seed));
            float bounded = Mathf.Clamp(parameters[i], Minimum[i], Maximum[i]);
            LastConstrained |= bounded != parameters[i];
            parameters[i] = bounded;
        }

        AnatomicalPatientRig.Pose best = WithParameters(seed, parameters);
        if (!EvaluateResidual(best, targetPosition, targetRotation, residual)) return best;
        LastEvaluationSucceeded = true;
        double cost = SquaredNorm(residual);
        double damping = .002;
        int budget = Mathf.Clamp(iterations, 0, 256);

        for (int iteration = 0; iteration < budget; iteration++)
        {
            // Both tasks must be close; a low weighted orientation objective
            // alone must not conceal a position error.
            if (PositionNorm(residual) < .0002 && RotationNorm(residual) < .002) break;
            LastIterations = iteration + 1;
            if (!BuildJacobian(seed, targetPosition, targetRotation)) break;

            BuildDampedSystem(damping);
            if (!SolveDampedSystem())
            {
                damping = Math.Min(MaximumDamping, damping * 4.0);
                continue;
            }

            double largest = 0.0;
            for (int column = 0; column < ParameterCount; column++)
            {
                double value = 0.0;
                for (int row = 0; row < ResidualCount; row++)
                    value += jacobian[row, column] * dualStep[row];
                parameterStep[column] = value;
                largest = Math.Max(largest, Math.Abs(value));
            }
            if (largest < .00001) break;

            // Scale the whole step together, preserving its minimum-norm
            // direction rather than clipping each coordinate independently.
            double stepScale = largest > MaximumStepDegrees ? MaximumStepDegrees / largest : 1.0;
            bool accepted = false;
            for (int search = 0; search < 6; search++)
            {
                double alpha = stepScale / (1 << search);
                bool constrained = false;
                for (int i = 0; i < ParameterCount; i++)
                {
                    float raw = parameters[i] + (float)(parameterStep[i] * alpha);
                    candidate[i] = Mathf.Clamp(raw, Minimum[i], Maximum[i]);
                    constrained |= candidate[i] != raw;
                }

                AnatomicalPatientRig.Pose trial = WithParameters(seed, candidate);
                if (!EvaluateResidual(trial, targetPosition, targetRotation, trialResidual)) break;
                double trialCost = SquaredNorm(trialResidual);
                if (trialCost + 1e-14 >= cost) continue;

                Array.Copy(candidate, parameters, ParameterCount);
                Array.Copy(trialResidual, residual, ResidualCount);
                best = trial;
                cost = trialCost;
                LastConstrained |= constrained;
                damping = Math.Max(MinimumDamping, damping * .65);
                accepted = true;
                break;
            }
            if (!accepted)
            {
                damping = Math.Min(MaximumDamping, damping * 3.0);
                if (damping >= MaximumDamping) break;
            }
        }

        // Jacobian probes and rejected line-search candidates also evaluate FK.
        // Restore exactly the returned pose before any caller reads the bones.
        LastEvaluationSucceeded = EvaluateResidual(best, targetPosition, targetRotation, residual);
        if (LastEvaluationSucceeded)
        {
            LastPositionErrorMetres = (float)PositionNorm(residual);
            LastRotationErrorDegrees = (float)(RotationNorm(residual) * Mathf.Rad2Deg);
            LastWeightedResidual = (float)Math.Sqrt(SquaredNorm(residual));
        }
        for (int i = 0; i < ParameterCount; i++)
            LastAtEnvelope |= parameters[i] <= Minimum[i] + .01f || parameters[i] >= Maximum[i] - .01f;
        return best;
    }

    private bool BuildJacobian(AnatomicalPatientRig.Pose seed, Vector3 targetPosition, Quaternion targetRotation)
    {
        if (!EvaluateResidual(WithParameters(seed, parameters), targetPosition, targetRotation, trialResidual)) return false;
        Vector3 basePosition = hand.position;
        Quaternion baseRotationInverse = Quaternion.Inverse(hand.rotation);
        for (int column = 0; column < ParameterCount; column++)
        {
            Array.Copy(parameters, candidate, ParameterCount);
            // One-sided finite difference reverses at an envelope boundary;
            // the actual denominator is retained instead of assuming .35.
            float step = parameters[column] + DifferenceDegrees <= Maximum[column]
                ? DifferenceDegrees : -DifferenceDegrees;
            candidate[column] = Mathf.Clamp(parameters[column] + step, Minimum[column], Maximum[column]);
            double actualStep = candidate[column] - parameters[column];
            if (Math.Abs(actualStep) < 1e-8) return false;
            if (!EvaluateResidual(WithParameters(seed, candidate), targetPosition, targetRotation, trialResidual)) return false;
            Vector3 positionChange = hand.position - basePosition;
            // Differentiate the actual hand motion, not two target-error logs.
            // The latter can jump from +pi to -pi when the target is opposite
            // the hand. This small observed rotation has no such branch jump.
            Vector3 angularChange = RotationVector(hand.rotation * baseRotationInverse);
            jacobian[0, column] = positionChange.x / actualStep;
            jacobian[1, column] = positionChange.y / actualStep;
            jacobian[2, column] = positionChange.z / actualStep;
            jacobian[3, column] = angularChange.x * RotationScale / actualStep;
            jacobian[4, column] = angularChange.y * RotationScale / actualStep;
            jacobian[5, column] = angularChange.z * RotationScale / actualStep;
        }
        return true;
    }

    private void BuildDampedSystem(double damping)
    {
        // J maps a degree-valued parameter step to a metre-valued reduction of
        // the six residuals. J^T (J J^T + lambda^2 I)^-1 e is the damped
        // minimum-norm update; no inverse of a near-singular J is required.
        for (int row = 0; row < ResidualCount; row++)
        {
            for (int column = 0; column < ResidualCount; column++)
            {
                double value = row == column ? damping * damping : 0.0;
                for (int k = 0; k < ParameterCount; k++)
                    value += jacobian[row, k] * jacobian[column, k];
                augmented[row, column] = value;
            }
            augmented[row, ResidualCount] = residual[row];
        }
    }

    private bool SolveDampedSystem()
    {
        for (int pivot = 0; pivot < ResidualCount; pivot++)
        {
            int strongest = pivot;
            for (int row = pivot + 1; row < ResidualCount; row++)
                if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[strongest, pivot])) strongest = row;
            if (Math.Abs(augmented[strongest, pivot]) < 1e-16) return false;
            if (strongest != pivot)
            {
                for (int column = pivot; column <= ResidualCount; column++)
                {
                    double temporary = augmented[pivot, column];
                    augmented[pivot, column] = augmented[strongest, column];
                    augmented[strongest, column] = temporary;
                }
            }
            for (int row = pivot + 1; row < ResidualCount; row++)
            {
                double factor = augmented[row, pivot] / augmented[pivot, pivot];
                for (int column = pivot; column <= ResidualCount; column++)
                    augmented[row, column] -= factor * augmented[pivot, column];
            }
        }
        for (int row = ResidualCount - 1; row >= 0; row--)
        {
            double value = augmented[row, ResidualCount];
            for (int column = row + 1; column < ResidualCount; column++)
                value -= augmented[row, column] * dualStep[column];
            dualStep[row] = value / augmented[row, row];
            if (double.IsNaN(dualStep[row]) || double.IsInfinity(dualStep[row])) return false;
        }
        return true;
    }

    private bool EvaluateResidual(AnatomicalPatientRig.Pose pose, Vector3 targetPosition,
        Quaternion targetRotation, double[] result)
    {
        if (!rig.Evaluate(pose)) return false;
        Vector3 delta = targetPosition - hand.position;
        Vector3 rotation = RotationVector(targetRotation * Quaternion.Inverse(hand.rotation));
        result[0] = delta.x;
        result[1] = delta.y;
        result[2] = delta.z;
        result[3] = rotation.x * RotationScale;
        result[4] = rotation.y * RotationScale;
        result[5] = rotation.z * RotationScale;
        for (int i = 0; i < ResidualCount; i++)
            if (double.IsNaN(result[i]) || double.IsInfinity(result[i])) return false;
        return true;
    }

    private static Vector3 RotationVector(Quaternion delta)
    {
        delta = Normalize(delta);
        // q and -q describe the same orientation. Positive w selects the
        // shortest arc, preventing an artificial 360-degree residual.
        if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
        Vector3 vector = new Vector3(delta.x, delta.y, delta.z);
        double sine = vector.magnitude;
        if (sine < 1e-8) return vector * 2f;
        double angle = 2.0 * Math.Atan2(sine, Math.Max(0.0, delta.w));
        return vector * (float)(angle / sine);
    }

    private static Quaternion Normalize(Quaternion value)
    {
        double length = Math.Sqrt((double)value.x * value.x + (double)value.y * value.y +
                                  (double)value.z * value.z + (double)value.w * value.w);
        return new Quaternion((float)(value.x / length), (float)(value.y / length),
            (float)(value.z / length), (float)(value.w / length));
    }

    private static void ValidateTarget(Vector3 position, Quaternion rotation)
    {
        if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z))
            throw new ArgumentException("The hand target position must be finite.", nameof(position));
        double norm = (double)rotation.x * rotation.x + (double)rotation.y * rotation.y +
                      (double)rotation.z * rotation.z + (double)rotation.w * rotation.w;
        if (!IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) ||
            !IsFinite(rotation.w) || norm < 1e-16)
            throw new ArgumentException("The hand target rotation must be finite and nonzero.", nameof(rotation));
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static double SquaredNorm(double[] values)
    {
        double value = 0.0;
        for (int i = 0; i < ResidualCount; i++) value += values[i] * values[i];
        return value;
    }

    private static double PositionNorm(double[] values) => Math.Sqrt(values[0] * values[0] + values[1] * values[1] + values[2] * values[2]);
    private static double RotationNorm(double[] values) => Math.Sqrt(values[3] * values[3] + values[4] * values[4] + values[5] * values[5]) / RotationScale;

    private static void ReadParameters(AnatomicalPatientRig.ArmPose arm, float[] values)
    {
        values[0] = arm.shoulderElevation;
        values[1] = arm.elevationPlane;
        values[2] = arm.humeralRotation;
        values[3] = arm.elbowFlexion;
        values[4] = arm.forearmRotation;
        values[5] = arm.wristFlexion;
        values[6] = arm.wristDeviation;
    }

    private AnatomicalPatientRig.Pose WithParameters(AnatomicalPatientRig.Pose seed, float[] values)
    {
        AnatomicalPatientRig.ArmPose arm = left ? seed.leftArm : seed.rightArm;
        arm.shoulderElevation = values[0];
        arm.elevationPlane = values[1];
        arm.humeralRotation = values[2];
        arm.elbowFlexion = values[3];
        arm.forearmRotation = values[4];
        arm.wristFlexion = values[5];
        arm.wristDeviation = values[6];
        if (left) seed.leftArm = arm;
        else seed.rightArm = arm;
        return seed;
    }
}
