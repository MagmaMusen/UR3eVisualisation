using UnityEngine;
using System.IO; // Required for StringReader
using System.Collections.Generic; // Required for List
using System.Globalization; // Required for CultureInfo
using System.Linq; // Required for LINQ methods like Where

public class CsvTester : MonoBehaviour
{
    // Assign your CSV file here in the Unity Inspector
    public TextAsset csvFile;

    // Expected number of columns based on your data
    private const int EXPECTED_COLUMN_COUNT = 16; // timestamp + 6x q + 6x qd + safety + 2x bits

    void Start()
    {
        if (csvFile == null)
        {
            Debug.LogError("CSV Tester: Please assign the CSV file to the 'Csv File' field in the Inspector!");
            return;
        }

        Debug.Log($"--- Starting CSV Test for: {csvFile.name} ---");
        ParseAndTestCsv(csvFile.text);
        Debug.Log($"--- CSV Test Finished for: {csvFile.name} ---");
    }

    void ParseAndTestCsv(string csvText)
    {
        int lineCount = 0;
        int headerColumnCount = 0;
        int dataRowsProcessed = 0;
        int errorCount = 0;
        List<string> headers = new List<string>();

        // Use StringReader for efficient line-by-line reading
        using (StringReader reader = new StringReader(csvText))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lineCount++;
                line = line.Trim(); // Remove leading/trailing whitespace

                if (string.IsNullOrWhiteSpace(line)) // Skip empty lines
                {
                    if (lineCount > 1) // Don't warn for initial blank lines if any
                        Debug.LogWarning($"Line {lineCount}: Skipped empty line.");
                    continue;
                }

                // Split by space, removing empty entries caused by multiple spaces
                string[] parts = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

                if (lineCount == 1) // Header Row
                {
                    headers = parts.ToList();
                    headerColumnCount = headers.Count;
                    Debug.Log($"Header Found ({headerColumnCount} columns): {string.Join(" | ", headers)}");

                    if (headerColumnCount != EXPECTED_COLUMN_COUNT)
                    {
                        Debug.LogWarning($"Header column count ({headerColumnCount}) does not match expected ({EXPECTED_COLUMN_COUNT}). Parsing will continue based on header count.");
                        // Reset expected count based on header if desired, or keep warning
                        // headerColumnCount = EXPECTED_COLUMN_COUNT; // Or adjust logic based on header
                    }
                }
                else // Data Row
                {
                    dataRowsProcessed++;
                    if (parts.Length != headerColumnCount)
                    {
                        Debug.LogError($"Line {lineCount}: Column count mismatch! Expected {headerColumnCount}, got {parts.Length}. Line content: '{line}'");
                        errorCount++;
                        continue; // Skip processing this malformed line
                    }

                    // --- Attempt to parse each part ---
                    bool parseSuccess = true; // Assume success for the line initially
                    string currentParseInfo = $"Line {lineCount}: ";

                    // Variables to store parsed values for logging
                    float timestamp = float.NaN;
                    float actual_q_0 = float.NaN;
                    // Add placeholders for other q and qd values if you want to log them specifically
                    int safetyStatus = -1;
                    bool bit65 = false; // Default value
                    bool bit66 = false; // Default value

                    // Track if specific columns were successfully parsed
                    bool timestampParsed = false;
                    bool actualQ0Parsed = false;
                    bool safetyParsed = false;
                    bool bit65Parsed = false;
                    bool bit66Parsed = false;


                    try
                    {
                        // --- Column 0: timestamp ---
                        if (parts.Length > 0 && float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out timestamp))
                        {
                            timestampParsed = true;
                        }
                        else if (parts.Length > 0)
                        {
                            currentParseInfo += $" [Col 0 '{headers[0]}' failed float parse ('{parts[0]}')]";
                            parseSuccess = false;
                        }
                        else
                        {
                            currentParseInfo += $" [Col 0 '{headers[0]}' missing!]";
                            parseSuccess = false;
                        }


                        // --- Column 1: actual_q_0 ---
                        if (parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out actual_q_0))
                        {
                            actualQ0Parsed = true;
                        }
                        else if (parts.Length > 1)
                        {
                            currentParseInfo += $" [Col 1 '{headers[1]}' failed float parse ('{parts[1]}')]";
                            parseSuccess = false;
                        }
                        else
                        {
                            currentParseInfo += $" [Col 1 '{headers[1]}' missing!]";
                            parseSuccess = false;
                        }

                        // --- Add loops or individual checks for columns 2 through 12 (actual_q_1 to actual_qd_5) if needed ---
                        // Example: Check actual_q_1 (index 2)
                        // if (parts.Length > 2 && !float.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out float q1)) { parseSuccess = false; /* Add error info */ }
                        // ...


                        // --- Column 13: safety_status ---
                        if (13 < parts.Length && int.TryParse(parts[13], out safetyStatus))
                        {
                            safetyParsed = true;
                        }
                        else if (13 < parts.Length)
                        {
                            currentParseInfo += $" [Col 13 '{headers[13]}' failed int parse ('{parts[13]}')]";
                            parseSuccess = false;
                        }
                        else
                        { // Column missing
                            currentParseInfo += $" [Col 13 '{headers[13]}' missing!]";
                            parseSuccess = false;
                        }

                        // --- Column 14: output_bit_register_65 ---
                        if (14 < parts.Length)
                        {
                            if (bool.TryParse(parts[14], out bit65)) { bit65Parsed = true; }
                            else if (parts[14] == "0") { bit65 = false; bit65Parsed = true; }
                            else if (parts[14] == "1") { bit65 = true; bit65Parsed = true; }
                            else
                            {
                                currentParseInfo += $" [Col 14 '{headers[14]}' failed bool parse ('{parts[14]}')]";
                                parseSuccess = false;
                            }
                        }
                        else
                        { // Column missing
                            currentParseInfo += $" [Col 14 '{headers[14]}' missing!]";
                            parseSuccess = false;
                        }

                        // --- Column 15: output_bit_register_66 ---
                        if (15 < parts.Length)
                        {
                            if (bool.TryParse(parts[15], out bit66)) { bit66Parsed = true; }
                            else if (parts[15] == "0") { bit66 = false; bit66Parsed = true; }
                            else if (parts[15] == "1") { bit66 = true; bit66Parsed = true; }
                            else
                            {
                                currentParseInfo += $" [Col 15 '{headers[15]}' failed bool parse ('{parts[15]}')]";
                                parseSuccess = false;
                            }
                        }
                        else
                        { // Column missing
                            currentParseInfo += $" [Col 15 '{headers[15]}' missing!]";
                            parseSuccess = false;
                        }

                        // --- Logging ---
                        if (!parseSuccess)
                        {
                            // Log error with accumulated parse info for the line
                            Debug.LogError(currentParseInfo);
                            errorCount++;
                        }
                        else if (dataRowsProcessed <= 3) // Log details for the first few successful rows
                        {
                            // Construct log message using variables that are now guaranteed to be assigned
                            if (timestampParsed) currentParseInfo += $" [Col 0:'{headers[0]}' OK ({timestamp:F3})]";
                            if (actualQ0Parsed) currentParseInfo += $" [Col 1:'{headers[1]}' OK ({actual_q_0:F4})]";
                            // Add other successful parses...
                            if (safetyParsed) currentParseInfo += $" [Col 13:'{headers[13]}' OK ({safetyStatus})]";
                            if (bit65Parsed) currentParseInfo += $" [Col 14:'{headers[14]}' OK ({bit65})]";
                            if (bit66Parsed) currentParseInfo += $" [Col 15:'{headers[15]}' OK ({bit66})]";
                            currentParseInfo += " [All Checked Parsed OK]";
                            Debug.Log(currentParseInfo);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"Line {lineCount}: Exception during parsing! Error: {ex.Message}. Line content: '{line}'");
                        errorCount++;
                    }
                }
            }
        } // End using StringReader

        // --- Summary ---
        Debug.Log($"--- Summary ---");
        Debug.Log($"Total Lines Read (incl. header/empty): {lineCount}");
        Debug.Log($"Header Columns Found: {headerColumnCount}");
        Debug.Log($"Data Rows Attempted: {dataRowsProcessed}");
        Debug.Log($"Parsing/Format Errors Encountered: {errorCount}");

        if (errorCount == 0 && dataRowsProcessed > 0)
        {
            Debug.Log("CSV file appears to be parsed successfully according to basic type checks.");
        }
        else if (dataRowsProcessed == 0 && lineCount > 0) // Header might exist but no data
        {
            Debug.LogWarning("No data rows were processed or found. Check file content after the header.");
        }
        else if (lineCount == 0)
        {
            Debug.LogError("File appears to be empty.");
        }
        else
        {
            Debug.LogError("Errors were encountered during parsing. Please review the console logs above.");
        }
    }
}