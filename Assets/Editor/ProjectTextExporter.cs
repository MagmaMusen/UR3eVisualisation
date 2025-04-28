using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;

public class ProjectTextExporter_UserFiles
{
    // --- Configuration: What to Include/Exclude ---

    // Include content ONLY for these extensions. Others will just be listed by name.
    private static readonly HashSet<string> IncludeContentExtensions = new HashSet<string> {
        ".cs", ".shader", // Core code/shaders
        ".json", ".xml", ".txt", ".csv", ".asmdef", ".inputactions" // Common data/config formats & Unity specific
    };

    // Folders to completely ignore (relative to Assets or Project Root)
    private static readonly HashSet<string> IgnoredFolders = new HashSet<string> {
        // Standard Unity ignores (relative to Project Root)
        "Library", "Temp", "obj", "Logs", "UserSettings", "Builds",
        // Common VCS/IDE folders (relative to Project Root)
        ".git", ".vs", ".vscode", ".idea",
        // Common large/imported package folders (relative to Assets)
        "TextMesh Pro", // Exclude default TMP assets
        "Packages", // Exclude Assets/Packages folder often used by importers
        "URDF", // Exclude URDF import folders if present
        "Plugins", // Often contains pre-compiled binaries or large assets
        // Add other specific large package/asset store folders here if needed
        // e.g., "Photon", "Standard Assets"
    };

    // Project Settings files to include the content of (relative to ProjectSettings)
    private static readonly string[] ImportantSettingsFiles = {
            "ProjectVersion.txt", // Useful for version info
            "ProjectSettings.asset", // Core settings like Product Name
            "EditorBuildSettings.asset", // Included scenes
            "TagManager.asset", // Tags and Layers
            "InputManager.asset" // Legacy input axes/buttons (if used)
            // Add others selectively if needed, e.g., Physics settings, specific pipeline assets
    };

    // --- End Configuration ---


    [MenuItem("Tools/Export Project Text Info (User Files Only)")]
    public static void ExportUserFileInfo()
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath); // Folder ABOVE Assets
        string outputFile = Path.Combine(projectRoot, "ProjectUserFilesSummary.txt");
        StringBuilder summary = new StringBuilder();

        summary.AppendLine("========== PROJECT USER FILES SUMMARY ==========");
        summary.AppendLine($"Export Timestamp: {System.DateTime.Now}");
        summary.AppendLine($"Unity Version: {Application.unityVersion}");
        summary.AppendLine($"Project Path: {projectRoot}");
        summary.AppendLine($"Included Content Extensions: {string.Join(", ", IncludeContentExtensions)}");
        summary.AppendLine("==============================================");
        summary.AppendLine();

        // --- List Packages ---
        summary.AppendLine("========== INSTALLED PACKAGES (manifest.json) ==========");
        string manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                summary.AppendLine($"--- Content of Packages/manifest.json ---");
                summary.AppendLine(File.ReadAllText(manifestPath));
                summary.AppendLine("--- End of Content ---");
            }
            catch (System.Exception ex)
            {
                summary.AppendLine($"Error reading {manifestPath}: {ex.Message}");
            }
        }
        else
        {
            summary.AppendLine("Packages/manifest.json not found.");
        }
        summary.AppendLine("========================================================");
        summary.AppendLine();

        // --- Key Project Settings ---
        summary.AppendLine("========== KEY PROJECT SETTINGS ==========");
        string projectSettingsPath = Path.Combine(projectRoot, "ProjectSettings");
        if (Directory.Exists(projectSettingsPath))
        {
            foreach (string filename in ImportantSettingsFiles)
            {
                string filePath = Path.Combine(projectSettingsPath, filename);
                if (File.Exists(filePath))
                {
                    summary.AppendLine($"--- Content of ProjectSettings/{filename} ---");
                    try
                    {
                        summary.AppendLine(File.ReadAllText(filePath));
                        summary.AppendLine("--- End of Content ---");
                    }
                    catch (System.Exception ex)
                    {
                        summary.AppendLine($"Error reading {filePath}: {ex.Message}");
                    }
                    summary.AppendLine();
                }
                else
                {
                    summary.AppendLine($"ProjectSettings/{filename} not found.");
                }
            }
        }
        else
        {
            summary.AppendLine("ProjectSettings folder not found.");
        }
        summary.AppendLine("==========================================");
        summary.AppendLine();


        // --- Traverse Assets Folder ---
        summary.AppendLine("========== ASSETS FOLDER CONTENTS (USER FILES FOCUS) ==========");
        List<string> textFileContents = new List<string>();
        ProcessDirectoryRecursive(Application.dataPath, Application.dataPath, summary, textFileContents);
        summary.AppendLine("================================================================");
        summary.AppendLine();

        // --- Append Text File Contents ---
        summary.AppendLine("========== TEXT FILE CONTENTS (USER SCRIPTS ETC.) ==========");
        if (textFileContents.Count > 0)
        {
            summary.Append(string.Join("\n\n", textFileContents));
        }
        else
        {
            summary.AppendLine($"No text files found matching the specified extensions: {string.Join(", ", IncludeContentExtensions)}");
        }
        summary.AppendLine("\n==========================================================");


        // --- Write to File ---
        try
        {
            File.WriteAllText(outputFile, summary.ToString());
            Debug.Log($"Project user files summary exported successfully to: {outputFile}");
            EditorUtility.DisplayDialog("Export Successful", $"Project user files summary exported successfully to:\n{outputFile}", "OK");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to write project summary file: {ex.Message}");
            EditorUtility.DisplayDialog("Export Failed", $"Failed to write project summary file:\n{ex.Message}", "OK");
        }
    }

    private static void ProcessDirectoryRecursive(string targetDirectory, string rootAssetPath, StringBuilder summary, List<string> textFileContents)
    {
        string relativeDirPath = targetDirectory.Replace(rootAssetPath, "Assets").Replace('\\', '/');
        if (relativeDirPath == targetDirectory) // Handle processing outside Assets (shouldn't happen with current logic)
        {
            relativeDirPath = Path.GetFileName(targetDirectory);
        }

        summary.AppendLine($"\n--- Directory: {relativeDirPath} ---");

        // Process Files
        try
        {
            foreach (string filePath in Directory.GetFiles(targetDirectory))
            {
                string fileName = Path.GetFileName(filePath);
                string fileExt = Path.GetExtension(fileName).ToLowerInvariant();

                // *** Skip .meta files entirely ***
                if (fileExt == ".meta")
                {
                    continue;
                }

                string relativeFilePath = filePath.Replace(rootAssetPath, "Assets").Replace('\\', '/');

                summary.AppendLine($"  - File: {fileName}"); // List the file name

                // If it's a text file we want the content of, schedule it
                if (IncludeContentExtensions.Contains(fileExt))
                {
                    try
                    {
                        string fileContent = File.ReadAllText(filePath);
                        textFileContents.Add(
                            $"----- START FILE: {relativeFilePath} -----\n" +
                            $"{fileContent}\n" +
                            $"----- END FILE: {relativeFilePath} -----"
                        );
                    }
                    catch (System.Exception ex)
                    {
                        textFileContents.Add(
                            $"----- ERROR READING FILE: {relativeFilePath} -----\n" +
                            $"{ex.Message}\n" +
                            $"----- END FILE: {relativeFilePath} -----"
                        );
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            summary.AppendLine($"  * Error listing files in {relativeDirPath}: {ex.Message}");
        }


        // Process Subdirectories Recursively
        try
        {
            foreach (string directoryPath in Directory.GetDirectories(targetDirectory))
            {
                string dirName = Path.GetFileName(directoryPath);
                string relativeSubDirPath = directoryPath.Replace(rootAssetPath, "Assets").Replace('\\', '/');

                // Skip ignored folders (check both root-level and Assets-level ignores)
                if (IgnoredFolders.Contains(dirName) || IgnoredFolders.Contains(relativeSubDirPath))
                {
                    summary.AppendLine($"  (Skipping ignored directory: {dirName})");
                }
                else
                {
                    ProcessDirectoryRecursive(directoryPath, rootAssetPath, summary, textFileContents);
                }
            }
        }
        catch (System.Exception ex)
        {
            summary.AppendLine($"  * Error listing subdirectories in {relativeDirPath}: {ex.Message}");
        }
    }
}