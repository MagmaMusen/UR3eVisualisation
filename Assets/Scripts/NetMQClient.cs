// Filename: NetMQClient.cs
using System;
using System.Collections; // Not used, but keep for potential future use
using System.Collections.Concurrent;
using System.Threading;
using NetMQ; // Use this namespace for NetMQ exceptions
using UnityEngine;
using NetMQ.Sockets;
using UnityEngine.Events;
using System.Globalization;

// --- Main MonoBehaviour Class ---
public class ClientObject : MonoBehaviour
{
    [Header("Connection Settings")]
    public string serverAddress = "127.0.0.1";
    public string port = "5556";

    [Header("Topic Settings")]
    public string ptTopicPrefix = "actual_q";
    public string dtTopicPrefix = "desired_q";

    [Header("Events")]
    public UnityEvent<int, float> UpdateJP; // Event for Physical Twin (PT) updates
    public UnityEvent<int, float> UpdateDT_JP; // Event for Digital Twin (DT) updates

    private NetMqListener _netMqListener;
    public bool isQuitting = false; // Made public for listener check

    void Awake()
    {
        LoadConnectionSettings();
        Debug.Log($"[ClientObject {Time.frameCount}] Awake: Configured for tcp://{serverAddress}:{port}");
        _netMqListener = new NetMqListener(HandleMessage, serverAddress, port, ptTopicPrefix, dtTopicPrefix, this); // Pass reference
    }

    IEnumerator Start()
    {
        if (_netMqListener == null)
        {
            Debug.LogError("[ClientObject] Start: Listener is null!");
            yield break;
        }
        Debug.Log($"[ClientObject {Time.frameCount}] Start: Waiting 0.2s before starting listener...");
        yield return new WaitForSeconds(0.2f); // Small delay can sometimes help network init
        Debug.Log($"[ClientObject {Time.frameCount}] Start: Starting NetMQ Listener...");
        _netMqListener.Start();
    }

    private void Update()
    {
        // Process any messages received by the listener thread
        if (_netMqListener != null)
        {
            _netMqListener.Update();
        }
    }

    // Called when the application is quitting (including stopping Play mode in editor)
    private void OnApplicationQuit()
    {
        Debug.Log($"[ClientObject {Time.frameCount}] OnApplicationQuit called. Setting isQuitting flag and signalling listener stop.");
        isQuitting = true; // Signal that we are shutting down
        // Proactively signal stop here as well, in case OnDestroy is delayed
        _netMqListener?.Stop();
    }


    private void OnDestroy()
    {
        Debug.Log($"[ClientObject {Time.frameCount}] OnDestroy Entered. isQuitting={isQuitting}");
        if (_netMqListener != null)
        {
            // Stop() might have already been called by OnApplicationQuit, but calling again is safe
            Debug.Log($"[ClientObject {Time.frameCount}] OnDestroy: Ensuring _netMqListener.Stop() is called...");
            _netMqListener.Stop();
            Debug.Log($"[ClientObject {Time.frameCount}] OnDestroy: _netMqListener.Stop() returned.");
        }
        else
        {
            Debug.Log($"[ClientObject {Time.frameCount}] OnDestroy: _netMqListener was null.");
        }
        Debug.Log($"[ClientObject {Time.frameCount}] OnDestroy Exited.");
    }

    private void HandleMessage(string message)
    {
        // Basic parsing: Assumes "topic value" format separated by space
        string[] msgSplit = message.Trim().Split(' ');
        if (msgSplit.Length != 2) return;

        string fullTopic = msgSplit[0];
        string valueString = msgSplit[1];
        int jointIndex = -1;
        float jointValue = 0f;
        string actualTopicPrefix = "";
        string indexString = "";

        // Improved Topic Parsing Logic
        int lastUnderscore = fullTopic.LastIndexOf('_');
        if (lastUnderscore != -1 && lastUnderscore < fullTopic.Length - 1)
        {
            actualTopicPrefix = fullTopic.Substring(0, lastUnderscore);
            indexString = fullTopic.Substring(lastUnderscore + 1);
        }
        else if (fullTopic.StartsWith(ptTopicPrefix) && fullTopic.Length > ptTopicPrefix.Length && char.IsDigit(fullTopic[ptTopicPrefix.Length]))
        { actualTopicPrefix = ptTopicPrefix; indexString = fullTopic.Substring(ptTopicPrefix.Length); }
        else if (fullTopic.StartsWith(dtTopicPrefix) && fullTopic.Length > dtTopicPrefix.Length && char.IsDigit(fullTopic[dtTopicPrefix.Length]))
        { actualTopicPrefix = dtTopicPrefix; indexString = fullTopic.Substring(dtTopicPrefix.Length); }
        else { return; }

        if (!int.TryParse(indexString, out jointIndex)) return;
        if (!float.TryParse(valueString, NumberStyles.Any, CultureInfo.InvariantCulture, out jointValue)) return;

        // Invoke the correct UnityEvent
        if (actualTopicPrefix == ptTopicPrefix) { UpdateJP.Invoke(jointIndex, jointValue); }
        else if (actualTopicPrefix == dtTopicPrefix) { UpdateDT_JP.Invoke(jointIndex, jointValue); }
    }

    private void LoadConnectionSettings()
    {
        port = PlayerPrefs.GetString(DefaultSettings.Keys.Port, port);
        serverAddress = PlayerPrefs.GetString("ServerAddress", serverAddress);
        Debug.Log($"[ClientObject] LoadConnectionSettings: Using Address={serverAddress}, Port={port}");
    }
}


// --- NetMqListener Class (Handles the background thread) ---
public class NetMqListener
{
    private readonly Thread _listenerWorker;
    private volatile bool _listenerCancelled;
    private readonly string _serverAddress;
    private readonly string _port;
    private readonly string _ptTopicPrefix;
    private readonly string _dtTopicPrefix;
    private readonly string connectionString;
    private readonly ClientObject _parentClientObject; // Reference to parent

    public delegate void MessageDelegate(string message);
    private readonly MessageDelegate _messageDelegate;
    private readonly ConcurrentQueue<string> _messageQueue = new ConcurrentQueue<string>();
    private long loopIterations = 0;
    private long messagesReceived = 0;
    private SubscriberSocket subSocket;

    public NetMqListener(MessageDelegate messageDelegate, string serverAddress, string port, string ptTopicPrefix, string dtTopicPrefix, ClientObject parent)
    {
        _messageDelegate = messageDelegate ?? throw new ArgumentNullException(nameof(messageDelegate));
        _serverAddress = serverAddress;
        _port = port;
        _ptTopicPrefix = ptTopicPrefix ?? "";
        _dtTopicPrefix = dtTopicPrefix ?? "";
        _parentClientObject = parent ?? throw new ArgumentNullException(nameof(parent)); // Store reference
        connectionString = $"tcp://{_serverAddress}:{_port}";
        _listenerWorker = new Thread(ListenerWork);
        _listenerWorker.IsBackground = true;
        Debug.Log("[NetMqListener] Constructor: Thread object created.");
    }

    private void ListenerWork()
    {
        try { AsyncIO.ForceDotNet.Force(); } catch { /* Ignore */ }
        Debug.Log("[NetMQ Thread] Listener thread started execution.");

        subSocket = new SubscriberSocket();
        subSocket.Options.ReceiveHighWatermark = 1000;
        bool socketInitialized = false;

        try
        {
            subSocket.Connect(this.connectionString);
            Debug.Log($"[NetMQ Thread] Connected to {this.connectionString}");

            // Subscription Logic
            bool subscribed = false;
            if (!string.IsNullOrEmpty(_ptTopicPrefix)) { subSocket.Subscribe(_ptTopicPrefix); subscribed = true; Debug.Log($"[NetMQ Thread] Subscribed to '{_ptTopicPrefix}'"); }
            if (!string.IsNullOrEmpty(_dtTopicPrefix)) { if (!subscribed || _dtTopicPrefix != _ptTopicPrefix) { subSocket.Subscribe(_dtTopicPrefix); subscribed = true; Debug.Log($"[NetMQ Thread] Subscribed to '{_dtTopicPrefix}'"); } }
            if (!subscribed) { Debug.LogWarning("[NetMQ Thread] Subscribing to ALL topics..."); subSocket.Subscribe(""); }

            socketInitialized = true;
            Debug.Log("[NetMQ Thread] Entering receive loop...");
            TimeSpan receiveTimeout = TimeSpan.FromMilliseconds(10); // Keep short timeout

            while (true)
            {
                if (_listenerCancelled)
                {
                    Debug.Log($"[NetMQ Thread Iter {loopIterations}] Cancellation detected at loop start. Exiting.");
                    break;
                }

                loopIterations++;
                // Log periodically inside the loop to check if it's stuck *before* receive
                //if (loopIterations % 500 == 0) Debug.Log($"[NetMQ Thread] Loop Iter: {loopIterations}. Still running...");

                string receivedMessage = null;
                bool received = false;

                try
                {
                    received = subSocket.TryReceiveFrameString(receiveTimeout, out receivedMessage);
                }
                catch (NetMQ.TerminatingException te) { Debug.Log($"[NetMQ Thread Iter {loopIterations}] TerminatingException caught. Exiting loop. {te.Message}"); break; }
                catch (ThreadInterruptedException) { Debug.Log($"[NetMQ Thread Iter {loopIterations}] Thread interrupted. Exiting loop."); Thread.CurrentThread.Interrupt(); break; }
                catch (ObjectDisposedException ode) { Debug.Log($"[NetMQ Thread Iter {loopIterations}] ObjectDisposedException during receive. Exiting loop. {ode.Message}"); break; }
                catch (System.Exception sockEx) { Debug.LogError($"[NetMQ Thread Iter {loopIterations}] Socket Error: {sockEx.GetType().Name} - {sockEx.Message}"); break; }

                if (received && receivedMessage != null)
                {
                    messagesReceived++;
                    _messageQueue.Enqueue(receivedMessage);
                }
            }
        }
        catch (System.Exception ex) { Debug.LogError($"[NetMQ Thread] Setup Exception before loop: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}"); }
        finally
        {
            Debug.Log($"[NetMQ Thread] Loop finished or exception occurred (Iter: {loopIterations}, Msgs: {messagesReceived}). Cleaning up socket in finally block.");
            if (subSocket != null)
            {
                try
                {
                    // string connectionString = $"tcp://{_serverAddress}:{_port}"; // Not needed
                    if (!string.IsNullOrEmpty(_ptTopicPrefix)) subSocket.Unsubscribe(_ptTopicPrefix);
                    if (!string.IsNullOrEmpty(_dtTopicPrefix) && (!string.IsNullOrEmpty(_ptTopicPrefix) && _dtTopicPrefix != _ptTopicPrefix || string.IsNullOrEmpty(_ptTopicPrefix))) subSocket.Unsubscribe(_dtTopicPrefix);
                    else if (string.IsNullOrEmpty(_ptTopicPrefix) && string.IsNullOrEmpty(_dtTopicPrefix)) subSocket.Unsubscribe("");

                    subSocket.Disconnect(this.connectionString);
                    subSocket.Close();
                    subSocket.Dispose();
                    Debug.Log("[NetMQ Thread] Socket closed and disposed in finally.");
                }
                catch (Exception cleanupEx) { Debug.LogWarning($"[NetMQ Thread] Exception during socket cleanup in finally: {cleanupEx.Message}"); }
                finally { subSocket = null; }
            }
            else if (!socketInitialized)
            {
                Debug.LogWarning("[NetMQ Thread] Socket was never initialized properly.");
            }
        }
        Debug.Log("[NetMQ Thread] Listener thread finished execution.");
    }

    // Called from the main Unity thread (e.g., in Update)
    public void Update()
    {
        while (!_messageQueue.IsEmpty)
        {
            string message;
            if (_messageQueue.TryDequeue(out message))
            {
                try { _messageDelegate(message); }
                catch (Exception ex) { Debug.LogError($"Error processing message (Main Thread): {ex.Message}\n{ex.StackTrace}"); }
            }
            else { break; }
        }
    }

    // Call this to start the listener thread
    public void Start()
    {
        if (_listenerWorker == null) { Debug.LogError("[NetMqListener] Start: Listener worker thread is null! Cannot start."); return; }
        if (_listenerWorker.IsAlive || _listenerWorker.ThreadState == ThreadState.Running) { Debug.LogWarning($"[NetMqListener] Start: Thread is already alive/running (State: {_listenerWorker.ThreadState}). Ignoring request."); return; }
        try
        {
            _listenerCancelled = false;
            loopIterations = 0;
            messagesReceived = 0;
            Debug.Log($"[NetMqListener] Start: Attempting to start thread (State: {_listenerWorker.ThreadState})");
            _listenerWorker.Start();
            Debug.Log("[NetMqListener] Start: Call to _listenerWorker.Start() completed.");
        }
        catch (ThreadStateException tse) { Debug.LogError($"[NetMqListener] Start: ThreadStateException - Thread cannot be started. Current State: '{_listenerWorker.ThreadState}'. Message: {tse.Message}"); }
        catch (Exception ex) { Debug.LogError($"[NetMqListener] Start: Unexpected error starting thread: {ex.GetType().Name} - {ex.Message}"); }
    }

    // Call this to stop the listener thread cleanly
    public void Stop()
    {
        // Access the isQuitting flag directly from the parent reference
        // Ensure parent object still exists before accessing
        bool isApplicationQuitting = _parentClientObject != null && _parentClientObject.isQuitting;

        long stopCallTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        Debug.Log($"[NetMqListener] Stop START @ {stopCallTimestamp}. isAppQuitting={isApplicationQuitting}. Current thread state: {_listenerWorker?.ThreadState}");

        // 1. Signal Cancellation
        if (!_listenerCancelled)
        {
            Debug.Log($"[NetMqListener] Stop @ {System.Diagnostics.Stopwatch.GetTimestamp()}: Setting _listenerCancelled = true.");
            _listenerCancelled = true;
        }
        else
        {
            Debug.Log($"[NetMqListener] Stop @ {System.Diagnostics.Stopwatch.GetTimestamp()}: _listenerCancelled was already true.");
        }

        // 2. Socket cleanup is handled in ListenerWork's finally block

        // 3. Wait for Thread to Finish (Use VERY short timeout if quitting)
        if (_listenerWorker != null)
        {
            Debug.Log($"[NetMqListener] Stop @ {System.Diagnostics.Stopwatch.GetTimestamp()}: Checking if worker thread is alive (State: {_listenerWorker.ThreadState})...");
            if (_listenerWorker.IsAlive)
            {
                // Use a minimal timeout if the editor/app is quitting to avoid hangs.
                TimeSpan joinTimeout = isApplicationQuitting ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(200); // Adjusted slightly higher just in case

                Debug.Log($"[NetMqListener] Stop @ {System.Diagnostics.Stopwatch.GetTimestamp()}: Calling _listenerWorker.Join({joinTimeout.TotalMilliseconds}ms)...");
                bool finished = _listenerWorker.Join(joinTimeout); // Wait briefly
                Debug.Log($"[NetMqListener] Stop @ {System.Diagnostics.Stopwatch.GetTimestamp()}: _listenerWorker.Join returned {finished}.");

                if (finished)
                {
                    Debug.Log($"[NetMqListener] Stop: _listenerWorker.Join() completed within timeout. Final state: {_listenerWorker.ThreadState}");
                }
                else
                {
                    // Log appropriately based on context
                    Debug.LogWarning($"[NetMqListener] Stop: _listenerWorker DID NOT complete Join() within {joinTimeout.TotalMilliseconds}ms timeout! State: {_listenerWorker.ThreadState}. Letting main thread continue...");
                }
            }
            else
            {
                Debug.Log($"[NetMqListener] Stop @ {System.Diagnostics.Stopwatch.GetTimestamp()}: Worker thread was not alive when Stop was called (State: {_listenerWorker.ThreadState}). Join not needed.");
            }
        }
        else { Debug.Log($"[NetMqListener] Stop @ {System.Diagnostics.Stopwatch.GetTimestamp()}: Worker thread reference was null."); }

        // --- NetMQConfig.Cleanup() remains REMOVED from here ---
        Debug.Log($"[NetMqListener] Stop FINISHED @ {System.Diagnostics.Stopwatch.GetTimestamp()}.");
    }
} // End of NetMqListener class