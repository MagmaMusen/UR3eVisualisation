/*
 This is a modified version of: https://github.com/valkjsaaa/Unity-ZeroMQ-Example/blob/master/Assets/ClientObject.cs
 Modified to handle separate topics for Physical Twin (PT) and Digital Twin (DT) data streams.
 Includes fix for multipart message handling in the listener.
 */

using System.Collections.Concurrent;
using System.Threading;
using NetMQ;
using UnityEngine;
using NetMQ.Sockets;
using UnityEngine.Events;
using System.Text.RegularExpressions; // For more robust index parsing if needed
using System; // For TimeSpan
using System.Collections.Generic; // For List used in ListenerWork


public class ClientObject : MonoBehaviour
{
    [Header("Connection Settings")]
    public string serverAddress = "localhost"; // Address of the NetMQ publisher
    public string port = "5556";               // Port the publisher is using

    [Header("Topic Settings")]
    [Tooltip("The topic prefix for Physical Twin (actual) joint positions (e.g., 'actual_q')")]
    public string ptTopicPrefix = "actual_q";

    [Tooltip("The topic prefix for Digital Twin (desired/planned) joint positions (e.g., 'desired_q')")]
    public string dtTopicPrefix = "desired_q";

    [Header("Events")]
    [Tooltip("Event triggered when a Physical Twin joint position update is received.")]
    public UnityEvent<int, float> UpdateJP; // Renamed for clarity (Joint Position - Physical)

    [Tooltip("Event triggered when a Digital Twin joint position update is received.")]
    public UnityEvent<int, float> UpdateDT_JP; // Event for Digital Twin Joint Position

    private NetMqListener _netMqListener;

    // Standard Unity Message: Called once when the script instance is first enabled.
    private void Start()
    {
        LoadConnectionSettings(); // Load settings from PlayerPrefs if needed

        Debug.Log($"Starting NetMQ Listener for PT Topic '{ptTopicPrefix}' and DT Topic '{dtTopicPrefix}' on tcp://{serverAddress}:{port}");
        _netMqListener = new NetMqListener(HandleMessage, serverAddress, port, ptTopicPrefix, dtTopicPrefix);
        _netMqListener.Start();
    }

    // Standard Unity Message: Called every frame.
    private void Update()
    {
        // Process any messages received by the listener thread
        if (_netMqListener != null)
        {
            _netMqListener.Update();
        }
    }

    // Standard Unity Message: Called when the script instance is being destroyed.
    private void OnDestroy()
    {
        Debug.Log("Stopping NetMQ Listener.");
        _netMqListener?.Stop(); // Safely stop the listener thread
    }

    // Callback method passed to the NetMqListener to process received messages
    // This function now correctly receives the combined "topic value" string
    private void HandleMessage(string message)
    {
        // Expected format: "topic_prefix[index] value" (e.g., "actual_q0 1.5708")
        string[] msgSplit = message.Trim().Split(' '); // Trim whitespace just in case

        if (msgSplit.Length != 2)
        {
            // This warning should no longer appear frequently if the ListenerWork fix works
            Debug.LogWarning($"Received message with unexpected format: '{message}'. Expected 'topic_prefix[index] value'.");
            return;
        }

        string fullTopic = msgSplit[0]; // e.g., "actual_q0" or "desired_q1"
        string valueString = msgSplit[1];

        int jointIndex = -1;
        float jointValue = 0f;

        // --- Basic Index Parsing (assumes single digit at the end) ---
        if (fullTopic.Length > 0 && char.IsDigit(fullTopic[fullTopic.Length - 1]))
        {
            jointIndex = int.Parse(fullTopic[fullTopic.Length - 1].ToString());
        }
        // --- More Robust Index Parsing (Example using Regex for digits at the end) ---
        // Match match = Regex.Match(fullTopic, @"(\d+)$");
        // if (match.Success)
        // {
        //     jointIndex = int.Parse(match.Groups[1].Value);
        // }
        // ---

        if (jointIndex == -1)
        {
            Debug.LogWarning($"Could not parse joint index from topic: '{fullTopic}'");
            return;
        }

        if (!float.TryParse(valueString, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out jointValue))
        {
            Debug.LogWarning($"Could not parse float value from message part: '{valueString}' in full message '{message}'");
            return;
        }


        // Check which topic prefix the message matches and invoke the correct event
        if (fullTopic.StartsWith(ptTopicPrefix))
        {
            //Debug.Log($"Received PT Update: Joint {jointIndex}, Value {jointValue}"); // Log if needed
            UpdateJP.Invoke(jointIndex, jointValue);
        }
        else if (fullTopic.StartsWith(dtTopicPrefix))
        {
            //Debug.Log($"Received DT Update: Joint {jointIndex}, Value {jointValue}"); // Log if needed
            UpdateDT_JP.Invoke(jointIndex, jointValue);
        }
        else
        {
            // Optional: Log if message topic doesn't match expected prefixes
            // Debug.LogWarning($"Received message with unrecognized topic prefix: '{fullTopic}'");
        }
    }

    // Optional: Load settings from PlayerPrefs if you use the Settings scene
    private void LoadConnectionSettings()
    {
        // Example: Load port from PlayerPrefs, fallback to default if not set
        port = PlayerPrefs.GetString(DefaultSettings.Keys.Port, port);
        // Add similar lines if PT/DT topics or server address need to be configurable via settings
        // ptTopicPrefix = PlayerPrefs.GetString("PT_Topic", ptTopicPrefix);
        // dtTopicPrefix = PlayerPrefs.GetString("DT_Topic", dtTopicPrefix);
        // serverAddress = PlayerPrefs.GetString("ServerAddress", serverAddress);
    }
}


// --- NetMqListener Class (Handles the background thread work) ---
public class NetMqListener
{
    private readonly Thread _listenerWorker;
    private volatile bool _listenerCancelled; // Use volatile for thread safety signal

    // Connection details passed from ClientObject
    private readonly string _serverAddress;
    private readonly string _port;
    private readonly string _ptTopicPrefix;
    private readonly string _dtTopicPrefix;

    // Delegate and queue for message handling on the main thread
    public delegate void MessageDelegate(string message);
    private readonly MessageDelegate _messageDelegate;
    private readonly ConcurrentQueue<string> _messageQueue = new ConcurrentQueue<string>();

    // Constructor now takes both topic prefixes
    public NetMqListener(MessageDelegate messageDelegate, string serverAddress, string port, string ptTopicPrefix, string dtTopicPrefix)
    {
        _messageDelegate = messageDelegate;
        _serverAddress = serverAddress;
        _port = port;
        _ptTopicPrefix = ptTopicPrefix ?? ""; // Ensure not null
        _dtTopicPrefix = dtTopicPrefix ?? ""; // Ensure not null
        _listenerWorker = new Thread(ListenerWork);
    }

    // *** MODIFIED ListenerWork to handle multipart messages ***
    private void ListenerWork()
    {
        AsyncIO.ForceDotNet.Force(); // Recommended for NetMQ in Unity
        using (var subSocket = new SubscriberSocket())
        {
            subSocket.Options.ReceiveHighWatermark = 1000; // Or adjust as needed
            string connectionString = $"tcp://{_serverAddress}:{_port}";
            Debug.Log($"NetMQ Thread: Connecting to {connectionString}");
            try
            {
                subSocket.Connect(connectionString);

                // Subscribe to both PT and DT topics using non-empty prefixes
                if (!string.IsNullOrEmpty(_ptTopicPrefix))
                {
                    Debug.Log($"NetMQ Thread: Subscribing to PT topic prefix: '{_ptTopicPrefix}'");
                    subSocket.Subscribe(_ptTopicPrefix);
                }
                if (!string.IsNullOrEmpty(_dtTopicPrefix))
                {
                    Debug.Log($"NetMQ Thread: Subscribing to DT topic prefix: '{_dtTopicPrefix}'");
                    subSocket.Subscribe(_dtTopicPrefix);
                }

                Debug.Log("NetMQ Thread: Subscription complete. Waiting for messages...");

                while (!_listenerCancelled)
                {
                    string topicFrame;
                    // Try to receive the first frame (topic) with a timeout
                    if (subSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(100), out topicFrame))
                    {
                        // If the first frame was received, check if there's more to come (the value frame)
                        if (subSocket.Options.ReceiveMore)
                        {
                            string valueFrame;
                            // Try to receive the second frame (value)
                            if (subSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(50), out valueFrame)) // Shorter timeout ok
                            {
                                // Successfully received both parts!
                                // Combine them into the single string format expected by HandleMessage
                                string combinedMessage = $"{topicFrame} {valueFrame}";
                                _messageQueue.Enqueue(combinedMessage);
                            }
                            else
                            {
                                // Received topic but timed out waiting for value - likely an error from publisher or network
                                Debug.LogWarning($"NetMQ Thread: Received topic '{topicFrame}' but timed out waiting for value frame.");
                                // Discard the incomplete message
                            }
                        }
                        else
                        {
                            // Received only one frame when expecting two (topic + value) - error from publisher
                            Debug.LogWarning($"NetMQ Thread: Received single-frame message: '{topicFrame}'. Expected multipart message (topic + value).");
                            // Discard this frame
                        }
                    }
                    // If TryReceiveFrameString for the topic times out or fails, the loop simply continues and tries again
                }

                Debug.Log("NetMQ Thread: Listener cancelled. Closing socket.");
                subSocket.Close();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"NetMQ Thread Exception: {ex.Message}\n{ex.StackTrace}");
            }
        }
        NetMQConfig.Cleanup(); // Recommended cleanup
        Debug.Log("NetMQ Thread: Listener thread finished.");
    }


    // Called from the main thread (in ClientObject.Update)
    public void Update()
    {
        while (!_messageQueue.IsEmpty)
        {
            string message;
            if (_messageQueue.TryDequeue(out message))
            {
                _messageDelegate(message); // Process message on main thread
            }
            else
            {
                break; // Queue is empty or couldn't dequeue
            }
        }
    }

    // Start the background listener thread
    public void Start()
    {
        _listenerCancelled = false;
        _listenerWorker.Start();
    }

    // Signal the background thread to stop and wait for it to finish
    public void Stop()
    {
        _listenerCancelled = true;
        // Wait for the thread to finish before continuing (important for clean shutdown)
        if (_listenerWorker != null && _listenerWorker.IsAlive)
        {
            _listenerWorker.Join(); // Blocks until the thread terminates
        }
    }
}