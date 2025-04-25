using UnityEditor;
using UnityEngine;
using UnityEditor.Scripting.Python;

public class MenuItem_NewPythonScript_Class
{
    [MenuItem("Python Scripts/New Python Script")]
    public static void NewPythonScript()
    {

        Debug.Log("Running Python script...");

        try
        {
            // Ensure Python is initialized
            PythonRunner.EnsureInitialized();

            // Run the Python script
            PythonRunner.RunFile("Assets/Editor/Script_py/new_python_script.py");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error running Python script: {e.Message}");
        }
    }
}