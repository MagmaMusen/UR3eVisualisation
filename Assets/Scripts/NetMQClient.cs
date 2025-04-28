/*
 This is a modified version of: https://github.com/valkjsaaa/Unity-ZeroMQ-Example/blob/master/Assets/ClientObject.cs
 Modified to handle separate topics for Physical Twin (PT) and Digital Twin (DT) data streams.
 Listener configured to expect SINGLE-FRAME messages containing "topic value".
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
    // This function receives the combined "topic value" string
    private void HandleMessage(string message)
    {
        // Expected format: "topic_prefix[index] value" (e.g., "actual_q0 1.5708")
        string[] msgSplit = message.Trim().Split(' '); // Trim whitespace just in case

        if (msgSplit.Length != 2)
        {
            Debug.LogWarning($"[HandleMessage] Received message with unexpected format: '{message}'. Expected 'topic_prefix[index] value'.");
            return;
        }

        string fullTopic = msgSplit[0]; // e.g., "actual_q0" or "desired_q1"
        string valueString = msgSplit[1];

        int jointIndex = -1;
        float jointValue = 0f;

        // --- Topic/Index Parsing ---
        // Find the last underscore if present (e.g., actual_q_2)
        int lastUnderscore = fullTopic.LastIndexOf('_');
        string actualTopicPrefix;
        string indexString;

        if (lastUnderscore != -1 && lastUnderscore < fullTopic.Length - 1)
        {
            // Assume index is after the last underscore
            actualTopicPrefix = fullTopic.Substring(0, lastUnderscore);
            indexString = fullTopic.Substring(lastUnderscore + 1);
        }
        else if (fullTopic.Length > 0 && char.IsDigit(fullTopic[fullTopic.Length - 1]))
        {
            // Fallback: Assume index is the last character if no underscore found before digits
            // and the prefix matches the expected ones
            if (fullTopic.StartsWith(ptTopicPrefix) && fullTopic.Length > ptTopicPrefix.Length)
            {
                actualTopicPrefix = ptTopicPrefix;
                indexString = fullTopic.Substring(ptTopicPrefix.Length);
            }
            else if (fullTopic.StartsWith(dtTopicPrefix) && fullTopic.Length > dtTopicPrefix.Length)
            {
                actualTopicPrefix = dtTopicPrefix;
                indexString = fullTopic.Substring(dtTopicPrefix.Length);
            }
            else
            {
                Debug.LogWarning($"[HandleMessage] Could not determine index/prefix from topic: '{fullTopic}'");
                return;
            }
        }
        else
        {
            Debug.LogWarning($"[HandleMessage] Could not determine index/prefix from topic: '{fullTopic}'");
            return;
        }

        if (!int.TryParse(indexString, out jointIndex))
        {
            Debug.LogWarning($"[HandleMessage] Could not parse joint index from extracted string: '{indexString}' in topic '{fullTopic}'");
            return;
        }
        // --- End Topic/Index Parsing ---


        if (!float.TryParse(valueString, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out jointValue))
        {
            Debug.LogWarning($"[HandleMessage] Could not parse float value from message part: '{valueString}' in full message '{message}'");
            return;
        }

        // Check which topic prefix the message *actually* started with and invoke the correct event
        // Use StartsWith on the original fullTopic or the extracted actualTopicPrefix
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
            Debug.LogWarning($"[HandleMessage] Received message topic '{fullTopic}' does not match configured PT ('{ptTopicPrefix}') or DT ('{dtTopicPrefix}') prefixes.");
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

    // *** ListenerWork configured for SINGLE-FRAME messages ***
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

                // Subscribe to topics using non-empty prefixes
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
                    string receivedMessage;
                    // Try to receive a single frame with a timeout
                    if (subSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(100), out receivedMessage))
                    {
                        // Successfully received the single frame containing "topic value"
                        _messageQueue.Enqueue(receivedMessage);
                        // Debug.Log($"[ListenerWork] Received: {receivedMessage}"); // Uncomment for deep debug
                    }
                    // If TryReceiveFrameString times out or fails, the loop simply continues
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