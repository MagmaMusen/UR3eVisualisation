using UnityEngine;

public class JointPositionController : MonoBehaviour
{
    public ArticulationBody[] joints; // Assign joints in Inspector
    private float[] targetPositions = { 0f, 0f, 0f, 0f, 0f, 0f };   // Target positions (in degrees)
    private float Speed = 20f;        // Speed in degrees per second
    private float Stiffness = 100000f;
    private float Damping = 10000f;
    private float ForceLimit = 10000f;


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
