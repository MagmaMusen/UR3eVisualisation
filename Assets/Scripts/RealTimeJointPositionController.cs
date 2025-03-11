using UnityEngine;

public class JointPositionController : MonoBehaviour
{
    public ArticulationBody[] joints; // Assign joints in Inspector
    public float[] targetPositions;   // Target positions (in degrees)
    public float Speed = 10f;        // Speed in degrees per second
    public float Stiffness = 100000f;
    public float Damping = 10000f;
    public float ForceLimit = 100f;


    void Start()
    {
        if (joints.Length != targetPositions.Length)
        {
            Debug.LogError("Joints and target positions must have the same length!");
            return;
        }
    }

    void Update()
    {
        for (int i = 0; i < joints.Length; i++)
        {
            ArticulationDrive drive = joints[i].xDrive;
            float currentPos = joints[i].jointPosition[0]; // Current position (radians)
            float targetPos = targetPositions[i] * Mathf.Deg2Rad; // Convert to radians

            float direction = Mathf.Sign(targetPos - currentPos); // -1 or 1
            float velocity = direction * Speed * Mathf.Deg2Rad; // Convert speed to rad/s

            drive.target = targetPositions[i]; // Keep target in degrees
            drive.targetVelocity = velocity;
            drive.stiffness = Stiffness;  // Strong movement
            drive.damping = Damping;      // Smooth movement
            drive.forceLimit = ForceLimit;   // Allow movement

            joints[i].xDrive = drive;
        }
    }

    public void SetTarget(int index, float value)
    {
        targetPositions[index] = value * Mathf.Rad2Deg;
    }
}
