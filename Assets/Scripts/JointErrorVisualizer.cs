using UnityEngine;
using System.Collections.Generic; // Needed for Lists
#if UNITY_EDITOR
using UnityEditor; // Required for ContextMenu, SetDirty
using UnityEditor.SceneManagement; // Required for MarkSceneDirty
#endif

// Helper class to make the Inspector cleaner for grouping renderers per joint
[System.Serializable]
public class RendererGroup
{
    [Tooltip("Optional name for identification in Inspector.")]
    public string jointIdentifier; // e.g., "Shoulder", "Upper Arm"
    public List<Renderer> renderers = new List<Renderer>(); // List of renderers for THIS joint
}

public class JointErrorVisualizer : MonoBehaviour
{
    // --- ADDED FOR AUTO-SETUP ---
    [Header("Root References (Assign These)")]
    [Tooltip("Assign the top-level GameObject of the Physical Twin robot.")]
    public Transform physicalTwinRoot;

    [Tooltip("Assign the top-level GameObject of the Digital Twin robot.")]
    public Transform digitalTwinRoot;
    // --- END ADDED ---

    [Header("Joint References (Auto-Populated or Manual)")]
    [Tooltip("Articulation Bodies for the Physical Twin joints.")]
    public List<ArticulationBody> physicalTwinJoints = new List<ArticulationBody>();

    [Tooltip("Articulation Bodies for the Digital Twin joints.")]
    public List<ArticulationBody> digitalTwinJoints = new List<ArticulationBody>();

    [Header("Visual Groups (Auto-Populated or Manual)")]
    [Tooltip("List containing groups of renderers. Each group corresponds to one joint in the 'Physical Twin Joints' list.")]
    public List<RendererGroup> physicalTwinVisualGroups = new List<RendererGroup>();

    [Header("Error Visualization Settings")]
    [Tooltip("Color gradient mapping error. Left=Zero Error, Right=Max Error.")]
    public Gradient errorGradient;

    [Tooltip("The absolute error (in degrees) at which the gradient reaches its maximum color (right side).")]
    public float maxErrorThresholdDegrees = 15.0f;

    [Tooltip("Material property name for color (usually '_Color' or '_BaseColor').")]
    public string materialColorPropertyName = "_Color";

    // Internal State
    private float[] currentPtAnglesRad;
    private float[] currentDtAnglesRad;
    private List<List<Material>> materialInstancesPerJoint = new List<List<Material>>();
    private int jointCount;

    // Define the expected joint link names IN ORDER
    private readonly string[] jointLinkNames = {
        "shoulder_link",
        "upper_arm_link",
        "forearm_link",
        "wrist_1_link",
        "wrist_2_link",
        "wrist_3_link"
    };

    void Start()
    {
        // --- Validation (Moved null checks here, simplified logic below relies on this) ---
        if (physicalTwinJoints == null || digitalTwinJoints == null || physicalTwinVisualGroups == null ||
            physicalTwinJoints.Count != jointLinkNames.Length ||
            digitalTwinJoints.Count != jointLinkNames.Length ||
            physicalTwinVisualGroups.Count != jointLinkNames.Length)
        {
            Debug.LogError($"JointErrorVisualizer: Reference lists are null, empty, or counts don't match expected ({jointLinkNames.Length}). Did you run 'Auto-Assign References'?", this);
            enabled = false; return;
        }

        jointCount = physicalTwinJoints.Count; // Should be 6

        // --- Initialization ---
        currentPtAnglesRad = new float[jointCount];
        currentDtAnglesRad = new float[jointCount];
        materialInstancesPerJoint = new List<List<Material>>(jointCount);

        for (int i = 0; i < jointCount; i++)
        {
            List<Material> materialsForThisJoint = new List<Material>();
            materialInstancesPerJoint.Add(materialsForThisJoint);

            if (physicalTwinVisualGroups[i] == null || physicalTwinVisualGroups[i].renderers == null)
            {
                Debug.LogError($"JointErrorVisualizer: Visual Group or its renderer list at index {i} is invalid!", this);
                // Don't disable here, maybe some joints work
                continue;
            }

            foreach (Renderer rend in physicalTwinVisualGroups[i].renderers)
            {
                if (rend == null) continue; // Skip null renderers added during setup

                Material matInstance = rend.material;
                if (matInstance == null) continue;

                materialsForThisJoint.Add(matInstance);
                matInstance.SetColor(materialColorPropertyName, errorGradient.Evaluate(0f));
            }
        }
        Debug.Log($"JointErrorVisualizer: Initialized for {jointCount} joints.");
    }

    // --- Public Methods to Receive Updates ---
    public void UpdatePhysicalTwinAngle(int jointIndex, float angleRadians)
    {
        if (jointIndex >= 0 && jointIndex < jointCount)
        {
            currentPtAnglesRad[jointIndex] = angleRadians;
            UpdateJointColor(jointIndex);
        }
        // Removed warning spam for performance if index is slightly off
    }

    public void UpdateDigitalTwinAngle(int jointIndex, float angleRadians)
    {
        if (jointIndex >= 0 && jointIndex < jointCount)
        {
            currentDtAnglesRad[jointIndex] = angleRadians;
            UpdateJointColor(jointIndex);
        }
        // Removed warning spam
    }

    // --- Core Logic ---
    void UpdateJointColor(int jointIndex)
    {
        if (jointIndex < 0 || jointIndex >= jointCount || materialInstancesPerJoint.Count <= jointIndex || materialInstancesPerJoint[jointIndex] == null)
        {
            return; // Safety checks
        }

        float errorRadians = Mathf.Abs(currentPtAnglesRad[jointIndex] - currentDtAnglesRad[jointIndex]);
        float errorDegrees = errorRadians * Mathf.Rad2Deg;
        float t = Mathf.Clamp01(errorDegrees / maxErrorThresholdDegrees);
        Color targetColor = errorGradient.Evaluate(t);

        List<Material> materialsForThisJoint = materialInstancesPerJoint[jointIndex];
        foreach (Material matInstance in materialsForThisJoint)
        {
            if (matInstance != null)
            {
                // Check if property exists before setting - avoids errors if name is wrong
                if (matInstance.HasProperty(materialColorPropertyName))
                {
                    matInstance.SetColor(materialColorPropertyName, targetColor);
                }
                // else { Debug.LogWarningOnce($"Material {matInstance.name} does not have property {materialColorPropertyName}"); } // Optional warning
            }
        }
    }


    // --- AUTO-ASSIGNMENT FUNCTIONALITY (EDITOR ONLY) ---
#if UNITY_EDITOR
    [ContextMenu("Auto-Assign Joint References")]
    void AutoAssignReferences()
    {
        Debug.Log("Attempting to auto-assign references...");

        if (physicalTwinRoot == null || digitalTwinRoot == null)
        {
            Debug.LogError("Auto-Assign failed: Physical Twin Root and Digital Twin Root must be assigned first!", this);
            return;
        }

        // Clear existing lists before populating
        physicalTwinJoints.Clear();
        digitalTwinJoints.Clear();
        physicalTwinVisualGroups.Clear();

        bool assignmentSuccessful = true;

        // Iterate through the defined joint link names
        for (int i = 0; i < jointLinkNames.Length; i++)
        {
            string linkName = jointLinkNames[i];
            Debug.Log($"Processing link: {linkName} (Index {i})");

            // --- Find GameObjects ---
            Transform ptLinkTransform = FindDeepChild(physicalTwinRoot, linkName);
            Transform dtLinkTransform = FindDeepChild(digitalTwinRoot, linkName);

            if (ptLinkTransform == null)
            {
                Debug.LogError($" - Could not find '{linkName}' under Physical Twin Root '{physicalTwinRoot.name}'.", this);
                assignmentSuccessful = false;
                // Add placeholders to keep list counts aligned
                physicalTwinJoints.Add(null);
                digitalTwinJoints.Add(null);
                physicalTwinVisualGroups.Add(new RendererGroup { jointIdentifier = linkName + " (Error)", renderers = new List<Renderer>() });
                continue; // Skip to next link
            }
            if (dtLinkTransform == null)
            {
                Debug.LogError($" - Could not find '{linkName}' under Digital Twin Root '{digitalTwinRoot.name}'.", this);
                assignmentSuccessful = false;
                // Add placeholders
                physicalTwinJoints.Add(null); // Need to keep counts aligned
                digitalTwinJoints.Add(null);
                physicalTwinVisualGroups.Add(new RendererGroup { jointIdentifier = linkName + " (Error)", renderers = new List<Renderer>() });
                continue;
            }

            // --- Get Articulation Bodies ---
            ArticulationBody ptBody = ptLinkTransform.GetComponent<ArticulationBody>();
            ArticulationBody dtBody = dtLinkTransform.GetComponent<ArticulationBody>();

            if (ptBody == null)
            {
                Debug.LogError($" - Found '{linkName}' for PT, but it has no ArticulationBody component!", ptLinkTransform);
                physicalTwinJoints.Add(null); // Add placeholder
                assignmentSuccessful = false;
            }
            else
            {
                physicalTwinJoints.Add(ptBody);
                Debug.Log($"   - Added PT ArticulationBody: {ptBody.name}");
            }

            if (dtBody == null)
            {
                Debug.LogError($" - Found '{linkName}' for DT, but it has no ArticulationBody component!", dtLinkTransform);
                digitalTwinJoints.Add(null); // Add placeholder
                assignmentSuccessful = false;
            }
            else
            {
                digitalTwinJoints.Add(dtBody);
                Debug.Log($"   - Added DT ArticulationBody: {dtBody.name}");
            }

            // --- Find Visuals (Only for Physical Twin) ---
            Transform visualsContainer = ptLinkTransform.Find("Visuals"); // Assume "Visuals" is direct child
            if (visualsContainer == null)
            {
                Debug.LogWarning($" - Could not find 'Visuals' child under PT '{linkName}'. Cannot assign renderers.", ptLinkTransform);
                // Add empty group to keep list aligned
                physicalTwinVisualGroups.Add(new RendererGroup { jointIdentifier = linkName + " (No Visuals)", renderers = new List<Renderer>() });
            }
            else
            {
                // Find *all* MeshRenderers within the Visuals container and its children
                MeshRenderer[] renderersInChildren = visualsContainer.GetComponentsInChildren<MeshRenderer>(true); // Include inactive

                RendererGroup currentGroup = new RendererGroup();
                currentGroup.jointIdentifier = linkName; // Set identifier

                if (renderersInChildren.Length == 0)
                {
                    Debug.LogWarning($" - Found 'Visuals' under PT '{linkName}', but no MeshRenderers were found within it.", visualsContainer);
                }
                else
                {
                    Debug.Log($"   - Found {renderersInChildren.Length} renderers under 'Visuals' for PT '{linkName}'.");
                    // Add only MeshRenderers (Renderer is base class, could catch ParticleSystemRenderer etc.)
                    foreach (MeshRenderer rend in renderersInChildren)
                    {
                        currentGroup.renderers.Add(rend);
                        Debug.Log($"     - Added Renderer: {rend.gameObject.name}");
                    }
                }
                physicalTwinVisualGroups.Add(currentGroup); // Add the group (even if empty)
            }
        } // End loop through joint names

        if (assignmentSuccessful)
        {
            Debug.Log("Auto-assignment completed successfully! Check the Inspector lists.");
        }
        else
        {
            Debug.LogError("Auto-assignment finished with ERRORS. Check console and Inspector lists for null entries or missing items.", this);
        }

        // Mark the component and the scene as dirty to ensure changes are saved
        EditorUtility.SetDirty(this);
        if (!Application.isPlaying) // Only mark scene dirty if not in play mode
        {
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
    }

    // Helper function to find a child recursively by name
    private Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null) return null;
        foreach (Transform child in parent)
        {
            if (child.name == childName)
                return child;
            Transform result = FindDeepChild(child, childName);
            if (result != null)
                return result;
        }
        return null;
    }
#endif // UNITY_EDITOR

    // OnDestroy cleanup usually handled by Unity for material instances
    // void OnDestroy() { ... }
}