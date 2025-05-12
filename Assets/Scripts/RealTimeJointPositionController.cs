using UnityEngine;

public class JointPositionController : MonoBehaviour
{
    public ArticulationBody[] joints; // Assign joints in Inspector

    // --- Internal State for Control & Debugging ---
    private float[] targetPositionsRad = { 0f, 0f, 0f, 0f, 0f, 0f }; // Store target in radians internally
    private float[] previousTargetPositionsDeg = { 0f, 0f, 0f, 0f, 0f, 0f }; // Store previous target (in degrees) for velocity calc
    private float Speed = 20f;        // Speed in degrees per second (loaded below)
    private float Stiffness = 100000f;
    private float Damping = 10000f;
    private float ForceLimit = 10000f;

    // --- Debugging ---
    [Header("Debug Settings")]
    [Tooltip("Log detailed joint info only for this specific index (-1 to disable detailed logging).")]
    public int debugLogJointIndex = 0; // Set to 0, 1, etc. to focus logging, or -1 to disable


    public void GetRecentParameters()
    {
        Debug.Log("Applying recent settings (or defaults if PlayerPrefs not used/found)");
        // Using try-catch and default lookup values for robustness
        try { Speed = float.Parse(PlayerPrefs.GetString(DefaultSettings.Keys.Speed, DefaultSettings.Lookup[DefaultSettings.Keys.Speed])); }
        catch (System.Exception e) { Debug.LogError($"Failed to parse Speed setting: {e.Message}. Using default: {Speed}"); }

        try { Stiffness = float.Parse(PlayerPrefs.GetString(DefaultSettings.Keys.Stiffness, DefaultSettings.Lookup[DefaultSettings.Keys.Stiffness])); }
        catch (System.Exception e) { Debug.LogError($"Failed to parse Stiffness setting: {e.Message}. Using default: {Stiffness}"); }

        try { Damping = float.Parse(PlayerPrefs.GetString(DefaultSettings.Keys.Damping, DefaultSettings.Lookup[DefaultSettings.Keys.Damping])); }
        catch (System.Exception e) { Debug.LogError($"Failed to parse Damping setting: {e.Message}. Using default: {Damping}"); }

        try { ForceLimit = float.Parse(PlayerPrefs.GetString(DefaultSettings.Keys.ForceLimit, DefaultSettings.Lookup[DefaultSettings.Keys.ForceLimit])); }
        catch (System.Exception e) { Debug.LogError($"Failed to parse ForceLimit setting: {e.Message}. Using default: {ForceLimit}"); }

        Debug.Log($"Controller Params Loaded: Speed={Speed}, Stiffness={Stiffness}, Damping={Damping}, ForceLimit={ForceLimit}");
    }


    void Start()
    {
        if (joints == null || joints.Length == 0)
        {
            Debug.LogError("JointPositionController: No joints assigned!", this);
            enabled = false; return;
        }

        if (joints.Length != targetPositionsRad.Length)
        {
            Debug.LogError($"Joints array length ({joints.Length}) does not match internal target array length ({targetPositionsRad.Length})!", this);
            // Optionally resize internal arrays if needed, or disable script
            System.Array.Resize(ref targetPositionsRad, joints.Length);
            System.Array.Resize(ref previousTargetPositionsDeg, joints.Length);
            Debug.LogWarning("Resized internal arrays to match joints array.");
            // enabled = false; return;
        }

        GetRecentParameters();

        // Initialize previous target positions based on starting state
        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] != null)
            {
                // Initialize with the drive's current target
                previousTargetPositionsDeg[i] = joints[i].xDrive.target;
            }
        }
        Debug.Log("JointPositionController Initialized.");
    }

    void FixedUpdate() // Switched to FixedUpdate for physics consistency
    {
        if (joints == null) return;

        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] == null) continue; // Skip if a joint wasn't assigned

            ArticulationDrive drive = joints[i].xDrive;

            // --- Calculate Target in Degrees (needed for drive.target) ---
            float targetDegrees = targetPositionsRad[i] * Mathf.Rad2Deg;

            // --- Debug Logging (Conditional) ---
            if (i == debugLogJointIndex) // Only log for the selected joint index
            {
                float currentPositionRad = joints[i].jointPosition[0]; // Current position (radians)
                float currentPositionDeg = currentPositionRad * Mathf.Rad2Deg;
                float deltaAngleDeg = targetDegrees - previousTargetPositionsDeg[i];
                float commandedAngularVelocityDegS = 0;
                if (Time.fixedDeltaTime > Mathf.Epsilon) // Avoid division by zero
                {
                    commandedAngularVelocityDegS = deltaAngleDeg / Time.fixedDeltaTime;
                }

                Debug.Log($"Joint[{i}]: Target={targetDegrees:F2} deg | Current={currentPositionDeg:F2} deg | CmdVel={commandedAngularVelocityDegS:F1} deg/s");
            }
            // --- End Debug Logging ---


            // --- Apply Drive Settings ---
            drive.target = targetDegrees; // Set target in degrees

            // Optional: Calculate target velocity (less critical if stiffness/damping handle it)
            // float currentPosRad = joints[i].jointPosition[0];
            // float direction = Mathf.Sign(targetPositionsRad[i] - currentPosRad);
            // drive.targetVelocity = direction * Speed * Mathf.Deg2Rad; // Velocity in rad/s

            drive.stiffness = Stiffness;
            drive.damping = Damping;
            drive.forceLimit = ForceLimit;

            joints[i].xDrive = drive;

            // Update previous target for next frame's velocity calculation
            previousTargetPositionsDeg[i] = targetDegrees;
        }
    }

    // This method receives the target angle in RADIANS from external sources (like NetMQClient events)
    public void SetTarget(int index, float valueRadians)
    {
        if (index >= 0 && index < targetPositionsRad.Length)
        {
            // --- Debug Logging for Input ---
            if (index == debugLogJointIndex)
            {
                Debug.Log($"Joint[{index}]: SetTarget RECEIVED {valueRadians:F4} rad");
            }
            // --- End Debug Logging ---

            targetPositionsRad[index] = valueRadians; // Store the target in radians
        }
        // else { Debug.LogWarning($"SetTarget: Invalid index {index}"); } // Reduce log spam
    }
}