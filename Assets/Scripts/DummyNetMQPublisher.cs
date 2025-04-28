using UnityEngine;
using NetMQ;
using NetMQ.Sockets;
using System.Globalization; // For consistent float formatting

public class DummyNetMQPublisher : MonoBehaviour
{
    [Header("Network Settings")]
    [Tooltip("Port to publish messages on. Should match the port ClientObject connects to.")]
    public string port = "5556";

    [Header("Topic Settings")]
    [Tooltip("Topic prefix for Physical Twin messages.")]
    public string ptTopicPrefix = "actual_q";
    [Tooltip("Topic prefix for Digital Twin messages.")]
    public string dtTopicPrefix = "desired_q";

    [Header("Trajectory Settings")]
    [Tooltip("Enable automatic trajectory sending on start.")]
    public bool runTrajectory = true;
    [Tooltip("Overall speed multiplier for the trajectory.")]
    public float trajectorySpeed = 0.5f;
    [Tooltip("Amplitude of the sine wave motion (radians). Affects Joints 1 & 2.")]
    public float motionAmplitude = 0.8f; // Radians (approx 45 degrees)
    [Tooltip("Base frequency of the sine wave motion. Higher values = faster oscillation.")]
    public float motionFrequency = 0.5f;

    [Header("PT/DT Differences")]
    [Tooltip("Slight speed difference for the DT trajectory.")]
    public float dtSpeedMultiplier = 1.05f; // Make DT slightly faster/slower
    [Tooltip("Small constant angle offset for the DT trajectory (radians).")]
    public float dtAngleOffset = 0.05f; // Radians (approx 3 degrees)

    private PublisherSocket publisherSocket;
    private bool isInitialized = false;
    private float trajectoryTime = 0f;

    void Start()
    {
        try
        {
            AsyncIO.ForceDotNet.Force();
            Debug.Log("Dummy Trajectory Publisher: AsyncIO Forced.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Dummy Trajectory Publisher: Could not force AsyncIO (might be already forced): {e.Message}");
        }

        publisherSocket = new PublisherSocket();
        string address = $"tcp://*:{port}";
        try
        {
            Debug.Log($"Dummy Trajectory Publisher: Attempting to bind to {address}");
            publisherSocket.Bind(address);
            isInitialized = true;
            if (runTrajectory)
            {
                Debug.Log($"Dummy Trajectory Publisher: Successfully bound to {address}. Running automatic trajectory.");
            }
            else
            {
                Debug.Log($"Dummy Trajectory Publisher: Successfully bound to {address}. Automatic trajectory DISABLED.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Dummy Trajectory Publisher: Failed to bind socket to {address} - {ex.GetType().Name}: {ex.Message}. Is the port already in use?");
            publisherSocket = null; // Prevent further use
        }
    }

    void OnDestroy()
    {
        if (publisherSocket != null && isInitialized)
        {
            Debug.Log("Dummy Trajectory Publisher: Closing publisher socket.");
            try
            {
                publisherSocket.Unbind($"tcp://*:{port}");
                publisherSocket.Close();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Dummy Trajectory Publisher: Exception during socket close/unbind: {ex.Message}");
            }
            finally
            {
                publisherSocket.Dispose();
                publisherSocket = null;
            }
        }
        // NetMQConfig.Cleanup(block: false); // Optional: Global cleanup might be better
    }

    void Update()
    {
        // Only run if initialized and trajectory is enabled
        if (!isInitialized || publisherSocket == null || !runTrajectory)
        {
            return;
        }

        // Increment time based on trajectory speed
        trajectoryTime += Time.deltaTime * trajectorySpeed;

        // --- Calculate Target Angles for each joint ---
        // Example: Make joints 1 (Shoulder Pan) and 2 (Shoulder Lift) move

        // Joint 1: Simple Sine Wave
        float ptAngle1 = motionAmplitude * Mathf.Sin(motionFrequency * trajectoryTime);
        float dtAngle1 = motionAmplitude * Mathf.Sin(motionFrequency * dtSpeedMultiplier * trajectoryTime) + dtAngleOffset;

        // Joint 2: Sine Wave with different phase/frequency for variation
        float ptAngle2 = (motionAmplitude * 0.7f) * Mathf.Sin(motionFrequency * 0.8f * trajectoryTime + Mathf.PI / 2f); // Offset phase
        float dtAngle2 = (motionAmplitude * 0.7f) * Mathf.Sin(motionFrequency * 0.8f * dtSpeedMultiplier * trajectoryTime + Mathf.PI / 2f) + dtAngleOffset;

        // --- Send updates for all joints ---
        for (int i = 0; i < 6; i++) // Assuming 6 joints for UR3e
        {
            float currentPtAngle = 0f;
            float currentDtAngle = 0f;

            // Assign calculated angles or default (0)
            switch (i)
            {
                case 1: // Shoulder Pan
                    currentPtAngle = ptAngle1;
                    currentDtAngle = dtAngle1;
                    break;
                case 2: // Shoulder Lift
                    currentPtAngle = ptAngle2;
                    currentDtAngle = dtAngle2;
                    break;
                // Add cases for other joints if you want them to move
                // case 0: currentPtAngle = ... ; currentDtAngle = ... ; break;
                // case 3: currentPtAngle = ... ; currentDtAngle = ... ; break;
                // ...
                default:
                    // Keep other joints at 0 for simplicity
                    currentPtAngle = 0f;
                    currentDtAngle = 0f;
                    break;
            }

            // Send the PT and DT messages for this joint index
            SendMessage(ptTopicPrefix, i, currentPtAngle);
            SendMessage(dtTopicPrefix, i, currentDtAngle);
        }
    }

    // Helper to send the message in the correct format (now takes jointIndex)
    private void SendMessage(string topicPrefix, int jointIndex, float value)
    {
        if (publisherSocket == null) return;

        string topic = $"{topicPrefix}{jointIndex}";
        // Format value consistently using InvariantCulture
        string message = value.ToString("F4", CultureInfo.InvariantCulture);

        try
        {
            // Using TrySendFrame for non-blocking send attempt
            bool topicSent = publisherSocket.TrySendFrame(System.TimeSpan.Zero, topic, more: true);
            if (topicSent)
            {
                publisherSocket.TrySendFrame(System.TimeSpan.Zero, message);
            }
            // Optional: Add warning if sending fails
            // else { Debug.LogWarning($"Dummy Publisher: Could not send topic frame (socket busy?). Topic: {topic}"); }
        }
        catch (NetMQ.TerminatingException)
        {
            Debug.LogWarning("Dummy Publisher: NetMQ context terminated while trying to send.");
            publisherSocket = null; // Stop trying to use it
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Dummy Publisher: Error sending message - {ex.Message}");
        }
    }
}