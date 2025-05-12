// Filename: NetMQManager.cs
using UnityEngine;
using NetMQ;
using System;

public class NetMQManager : MonoBehaviour
{
    private static bool cleanupCalled = false; // Ensure cleanup happens only once
    private static bool quitSignalled = false; // Track if OnApplicationQuit has been called

    void Awake()
    {
        // Optional: Make it a persistent singleton if needed across scenes
        // DontDestroyOnLoad(gameObject);
        Debug.Log($"[NetMQManager {Time.frameCount}] Awake.");
    }

    // This method is called when the application quits (in editor or build)
    private void OnApplicationQuit()
    {
        long quitTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(); // Use high-precision timer if available
        Debug.Log($"[NetMQManager {Time.frameCount}] OnApplicationQuit START @ {quitTimestamp}. cleanupCalled={cleanupCalled}, quitSignalled={quitSignalled}");
        quitSignalled = true;

        if (!cleanupCalled)
        {
            try
            {
                Debug.Log($"[NetMQManager {Time.frameCount}] OnApplicationQuit: Calling NetMQConfig.Cleanup(false)...");
                NetMQConfig.Cleanup(false); // Attempt non-blocking cleanup
                cleanupCalled = true;
                Debug.Log($"[NetMQManager {Time.frameCount}] OnApplicationQuit: NetMQConfig.Cleanup() finished @ {System.Diagnostics.Stopwatch.GetTimestamp()}.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetMQManager {Time.frameCount}] OnApplicationQuit: Exception during NetMQConfig.Cleanup: {ex.Message}");
                // Even if cleanup fails, mark it as attempted to avoid loops if OnDestroy calls again
                cleanupCalled = true;
            }
        }
        else
        {
            Debug.Log($"[NetMQManager {Time.frameCount}] OnApplicationQuit: Cleanup already called, skipping.");
        }
        Debug.Log($"[NetMQManager {Time.frameCount}] OnApplicationQuit FINISHED @ {System.Diagnostics.Stopwatch.GetTimestamp()}.");
    }

    void OnDestroy()
    {
        Debug.Log($"[NetMQManager {Time.frameCount}] OnDestroy called. quitSignalled={quitSignalled}, cleanupCalled={cleanupCalled}");
        // It's possible OnDestroy gets called *after* OnApplicationQuit when stopping editor
        // Ensure cleanup is called if OnApplicationQuit somehow didn't trigger or complete
        // Only call if quit hasn't already been signalled and cleanup hasn't been done.
        if (!quitSignalled && !cleanupCalled)
        {
            Debug.LogWarning($"[NetMQManager {Time.frameCount}] OnDestroy: Cleanup wasn't called during OnApplicationQuit and quit not signalled. Attempting cleanup now (might indicate unusual shutdown)...");
            OnApplicationQuit(); // Call the same logic just in case
        }
        else if (quitSignalled && !cleanupCalled)
        {
            Debug.LogWarning($"[NetMQManager {Time.frameCount}] OnDestroy: Quit was signalled but cleanup wasn't marked complete. Retrying cleanup...");
            OnApplicationQuit(); // Retry
        }
    }
}