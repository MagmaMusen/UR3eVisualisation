// Filename: CsvToNetMQPublisher.cs
using UnityEngine;
using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading; // Not needed for this version

public class CsvToNetMQPublisher : MonoBehaviour
{
    [Header("CSV Data Source")]
    [Tooltip("Assign the CSV file containing the trajectory data.")]
    public TextAsset trajectoryCsvFile;

    [Header("Network Settings")]
    [Tooltip("Port to publish on (must match NetMQClient's port).")]
    public string port = "5556"; // Make sure this matches your ClientObject script

    [Header("Topic Settings")]
    [Tooltip("Topic prefix for Physical Twin joint angles (e.g., 'actual_q').")]
    public string ptTopicPrefix = "actual_q";
    [Tooltip("Topic prefix for Digital Twin joint angles (e.g., 'desired_q').")]
    public string dtTopicPrefix = "desired_q";

    [Header("Playback Settings")]
    [Tooltip("Multiplier for playback speed. 1 = real-time based on CSV timestamps.")]
    [Range(0.1f, 10f)]
    public float playbackSpeed = 1.0f;
    [Tooltip("Should the trajectory loop when it reaches the end?")]
    public bool loopTrajectory = true;

    // --- Internal Data Structure ---
    private struct TrajectoryPoint
    {
        public float Timestamp;
        public float[] JointAnglesRad; // Store all 6 angles from actual_q
    }

    // --- Internal State ---
    private List<TrajectoryPoint> trajectoryPoints = new List<TrajectoryPoint>();
    private int currentTrajectoryIndex = 0;
    private float elapsedPlaybackTime = 0f;
    private float trajectoryStartTimeOffset = 0f;
    private float trajectoryDuration = 0f;
    private bool isDataLoaded = false;
    private bool isPlaying = false;
    private bool isNetMqInitialized = false;

    private PublisherSocket publisherSocket;
    private string bindAddress = "";

    private const int EXPECTED_COLUMNS = 16;
    private const int TIMESTAMP_COL_INDEX = 0;
    private const int FIRST_Q_COL_INDEX = 1; // Index of actual_q_0
    private const int NUM_JOINTS = 6;

    void Start()
    {
        if (trajectoryCsvFile == null)
        {
            Debug.LogError("CsvToNetMQPublisher: No trajectory CSV file assigned!", this);
            enabled = false; return;
        }

        // Load port from PlayerPrefs if available, otherwise use Inspector default
        port = PlayerPrefs.GetString(DefaultSettings.Keys.Port, port);
        bindAddress = $"tcp://127.0.0.1:{port}"; // Bind locally for this test

        isDataLoaded = ParseCsvData(trajectoryCsvFile.text);

        if (isDataLoaded && trajectoryPoints.Count > 0)
        {
            InitializeNetMQ(); // Initialize the publisher socket

            if (isNetMqInitialized)
            {
                isPlaying = true;
                elapsedPlaybackTime = trajectoryPoints[0].Timestamp; // Start timing from the first point's timestamp
                trajectoryStartTimeOffset = trajectoryPoints[0].Timestamp;
                // Publish the first point immediately
                PublishPose(trajectoryPoints[0]);
                Debug.Log($"CsvToNetMQPublisher: Loaded {trajectoryPoints.Count} points. Publishing on {bindAddress}. Playback started.");
            }
            else
            {
                Debug.LogError("CsvToNetMQPublisher: Failed to initialize NetMQ. Playback aborted.", this);
                enabled = false;
            }
        }
        else
        {
            Debug.LogError("CsvToNetMQPublisher: Failed to load or parse trajectory data, or file is empty.", this);
            enabled = false;
        }
    }

    void InitializeNetMQ()
    {
        try { AsyncIO.ForceDotNet.Force(); }
        catch (Exception e) { Debug.LogWarning($"CsvToNetMQPublisher Warning: Could not force AsyncIO: {e.Message}"); }

        publisherSocket = new PublisherSocket();
        publisherSocket.Options.Linger = TimeSpan.Zero; // Prevent blocking on close
        publisherSocket.Options.SendHighWatermark = 1000; // Default HWM

        try
        {
            Debug.Log($"CsvToNetMQPublisher: Attempting to bind to {bindAddress}");
            publisherSocket.Bind(bindAddress);
            isNetMqInitialized = true;
            Debug.Log($"CsvToNetMQPublisher: Bind successful.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"CsvToNetMQPublisher Error: Failed to bind socket {bindAddress} - {ex.GetType().Name}: {ex.Message}");
            publisherSocket?.Dispose(); // Use null-conditional operator
            publisherSocket = null;
            isNetMqInitialized = false;
            enabled = false; // Stop script if binding fails
        }
    }

    void Update() // Using Update for smoother timing relative to Time.deltaTime
    {
        if (!isPlaying || !isNetMqInitialized || !isDataLoaded || trajectoryPoints.Count == 0)
            return;

        elapsedPlaybackTime += Time.deltaTime * playbackSpeed;
        float currentRelativeTime = elapsedPlaybackTime - trajectoryStartTimeOffset;

        // Find the next point in time
        while (currentTrajectoryIndex < trajectoryPoints.Count)
        {
            float nextPointRelativeTime = trajectoryPoints[currentTrajectoryIndex].Timestamp - trajectoryStartTimeOffset;

            if (currentRelativeTime >= nextPointRelativeTime)
            {
                // It's time to publish this pose
                PublishPose(trajectoryPoints[currentTrajectoryIndex]);
                currentTrajectoryIndex++;

                // Handle end of trajectory
                if (currentTrajectoryIndex >= trajectoryPoints.Count)
                {
                    if (loopTrajectory)
                    {
                        HandleLooping();
                        // After looping, immediately publish the first pose again
                        if (trajectoryPoints.Count > 0)
                        {
                            PublishPose(trajectoryPoints[0]);
                        }
                    }
                    else
                    {
                        Debug.Log("CsvToNetMQPublisher: End of trajectory reached. Stopping playback.");
                        isPlaying = false;
                        // Consider closing the socket here or leave it to OnDestroy
                        // enabled = false; // Optional: Disable component
                        return; // Exit Update for this frame
                    }
                }
            }
            else
            {
                // Haven't reached the time for the next point yet
                break; // Exit the while loop, wait for next Update
            }
        }
    }

    void PublishPose(TrajectoryPoint point)
    {
        if (publisherSocket == null || !isNetMqInitialized) return;

        if (point.JointAnglesRad == null || point.JointAnglesRad.Length != NUM_JOINTS)
        {
            Debug.LogWarning($"Skipping PublishPose due to invalid JointAnglesRad for timestamp {point.Timestamp}");
            return;
        }

        // Publish each joint angle for both PT and DT topics
        for (int i = 0; i < NUM_JOINTS; i++)
        {
            // --- Physical Twin Topic ---
            string ptTopic = $"{ptTopicPrefix}{i}";
            string ptValue = point.JointAnglesRad[i].ToString("F6", CultureInfo.InvariantCulture); // Use invariant culture for '.' decimal
            SendMessageInternal(ptTopic, ptValue);

            // --- Digital Twin Topic ---
            // For this test, we send the same 'actual_q' value to the desired topic
            string dtTopic = $"{dtTopicPrefix}{i}";
            string dtValue = point.JointAnglesRad[i].ToString("F6", CultureInfo.InvariantCulture);
            SendMessageInternal(dtTopic, dtValue);

            // Log only joint 0 for less spam
            // if (i == 0)
            // {
            //      Debug.Log($"--> Publishing: {ptTopic} {ptValue} | {dtTopic} {dtValue}");
            // }
        }
    }

    private void SendMessageInternal(string topic, string message)
    {
        if (publisherSocket == null || !isNetMqInitialized) return;

        try
        {
            // Send topic frame first, with the 'more' flag set to true
            bool topicSent = publisherSocket.TrySendFrame(TimeSpan.Zero, topic, more: true); // Non-blocking attempt

            if (topicSent)
            {
                // Send message frame second, with the 'more' flag set to false (or omitted)
                bool messageSent = publisherSocket.TrySendFrame(TimeSpan.Zero, message); // Non-blocking attempt
                // if (!messageSent) Debug.LogWarning($"CsvToNetMQPublisher: Failed to send message frame for topic {topic}");
            }
            // else { Debug.LogWarning($"CsvToNetMQPublisher: Failed to send topic frame {topic}"); } // Can be spammy
        }
        catch (ObjectDisposedException) { isNetMqInitialized = false; Debug.LogWarning("CsvToNetMQPublisher: Socket disposed."); }
        catch (NetMQ.TerminatingException) { isNetMqInitialized = false; Debug.LogWarning("CsvToNetMQPublisher: Context terminated."); }
        catch (System.Exception ex) { Debug.LogError($"CsvToNetMQPublisher Send Error: {ex.GetType().Name} on topic {topic} - {ex.Message}"); }
    }

    void HandleLooping()
    {
        Debug.Log("CsvToNetMQPublisher: Looping trajectory.");
        float overshoot = elapsedPlaybackTime - (trajectoryPoints.Last().Timestamp);
        currentTrajectoryIndex = 0;
        // Reset elapsed time, preserving overshoot relative to the new start
        elapsedPlaybackTime = trajectoryPoints[0].Timestamp + overshoot;
    }

    // --- CSV Parsing (same as TrajectoryPlaybackManager) ---
    private bool ParseCsvData(string csvText)
    {
        trajectoryPoints.Clear();
        int lineNum = 0;
        List<string> headers = new List<string>();

        using (StringReader reader = new StringReader(csvText))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNum++;
                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue; // Skip empty lines

                string[] parts = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

                if (lineNum == 1) // Header
                {
                    headers = parts.ToList();
                    if (headers.Count < FIRST_Q_COL_INDEX + NUM_JOINTS)
                    {
                        Debug.LogError($"CSV Parse Error: Header has only {headers.Count} columns, but needs at least {FIRST_Q_COL_INDEX + NUM_JOINTS} for timestamp and {NUM_JOINTS} q values.");
                        return false;
                    }
                    continue;
                }

                // Data Row Parsing
                if (parts.Length < FIRST_Q_COL_INDEX + NUM_JOINTS)
                {
                    Debug.LogWarning($"Line {lineNum}: Skipped row due to insufficient columns ({parts.Length}). Expected at least {FIRST_Q_COL_INDEX + NUM_JOINTS}. Content: '{line}'");
                    continue;
                }

                TrajectoryPoint point = new TrajectoryPoint();
                point.JointAnglesRad = new float[NUM_JOINTS];
                bool rowParseSuccess = true;

                if (!float.TryParse(parts[TIMESTAMP_COL_INDEX], NumberStyles.Any, CultureInfo.InvariantCulture, out point.Timestamp))
                {
                    Debug.LogWarning($"Line {lineNum}: Failed to parse timestamp '{parts[TIMESTAMP_COL_INDEX]}'. Skipping row.");
                    rowParseSuccess = false;
                    continue;
                }

                for (int i = 0; i < NUM_JOINTS; i++)
                {
                    int columnIndex = FIRST_Q_COL_INDEX + i;
                    // Added safety check for parts length inside loop just in case header was wrong
                    if (columnIndex >= parts.Length)
                    {
                        Debug.LogWarning($"Line {lineNum}: Column index {columnIndex} is out of bounds (only {parts.Length} parts). Skipping row.");
                        rowParseSuccess = false;
                        break;
                    }
                    if (!float.TryParse(parts[columnIndex], NumberStyles.Any, CultureInfo.InvariantCulture, out point.JointAnglesRad[i]))
                    {
                        // Check if header list is valid before trying to access it
                        string headerName = (columnIndex < headers.Count) ? headers[columnIndex] : $"Column_{columnIndex}";
                        Debug.LogWarning($"Line {lineNum}: Failed to parse '{headerName}' ('{parts[columnIndex]}'). Skipping row.");
                        rowParseSuccess = false;
                        break;
                    }
                }

                if (rowParseSuccess)
                {
                    trajectoryPoints.Add(point);
                }
            }
        }

        if (trajectoryPoints.Count > 0)
        {
            trajectoryDuration = trajectoryPoints.Last().Timestamp - trajectoryPoints.First().Timestamp;
            return true;
        }
        else
        {
            Debug.LogError("CSV Parse Error: No valid data points were parsed from the file.");
            return false;
        }
    }

    void OnDestroy()
    {
        Debug.Log("[CsvToNetMQPublisher] OnDestroy called.");
        isPlaying = false; // Stop playback loop if active
        isNetMqInitialized = false;

        if (publisherSocket != null)
        {
            Debug.Log("[CsvToNetMQPublisher] Closing and disposing socket.");
            try
            {
                // publisherSocket.Disconnect(bindAddress); // Not strictly needed for Bind
                publisherSocket.Close();
            }
            catch (Exception ex) { Debug.LogWarning($"[CsvToNetMQPublisher] Exception during socket Close: {ex.Message}"); }
            finally
            {
                // Important: Dispose the socket after closing
                publisherSocket.Dispose();
                publisherSocket = null;
                Debug.Log("[CsvToNetMQPublisher] Socket disposed.");
            }
        }
        else { Debug.Log("[CsvToNetMQPublisher] Socket was already null in OnDestroy."); }

        // --- NetMQConfig.Cleanup() is REMOVED from here ---

        Debug.Log("[CsvToNetMQPublisher] OnDestroy finished (socket cleanup only).");
    }
}