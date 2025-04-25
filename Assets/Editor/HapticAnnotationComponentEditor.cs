using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HapticAnnotationComponent))]
public class HapticAnnotationComponentEditor : Editor
{
    private SerializedProperty m_graphProperty;

    private void OnEnable()
    {
        // Make sure to use the exact field name from HapticAnnotationComponent
        m_graphProperty = serializedObject.FindProperty("m_graph");

        // Verify that the property was found
        if (m_graphProperty == null)
        {
            Debug.LogError("Failed to find 'm_graph' property in HapticAnnotationComponent. Check the field name.");
        }
    }

    public override void OnInspectorGUI()
    {
        // Update the serialized object
        serializedObject.Update();

        EditorGUILayout.Space(10);

        // Check if the property exists before drawing it
        if (m_graphProperty != null)
        {
            // Draw the graph field with an object field
            EditorGUILayout.PropertyField(m_graphProperty, new GUIContent("Haptic Annotation Graph"));
        }
        else
        {
            EditorGUILayout.HelpBox("Could not find the graph property. Please check the field name in the component.", MessageType.Error);
        }

        EditorGUILayout.Space(5);

        // Create a horizontal layout for the buttons
        EditorGUILayout.BeginHorizontal();

        // Add a "New" button to create a new graph
        if (GUILayout.Button("＋ New Graph", GUILayout.Height(20)))
        {
            CreateNewGraph();
        }

        // Add an "Edit" button to open the graph in the editor
        if (GUILayout.Button("Edit Graph", GUILayout.Height(20)))
        {
            OpenGraphEditor();
        }

        EditorGUILayout.EndHorizontal();

        // Display a help box if no graph is assigned
        HapticAnnotationComponent component = (HapticAnnotationComponent)target;
        if (component.Graph == null)
        {
            EditorGUILayout.HelpBox("Create a new graph or assign an existing one.", MessageType.Info);
        }

        // Apply modified properties
        serializedObject.ApplyModifiedProperties();
    }

    private void CreateNewGraph()
    {
        // Create a save file dialog to choose where to save the new graph
        string path = EditorUtility.SaveFilePanelInProject(
            "Create New Haptic Annotation Graph",
            "New Haptic Annotation Graph",
            "asset",
            "Choose a location to save the new graph"
        );

        if (!string.IsNullOrEmpty(path))
        {
            // Create a new graph asset
            HapticAnnotationGraph newGraph = ScriptableObject.CreateInstance<HapticAnnotationGraph>();

            // Save the asset to the selected path
            AssetDatabase.CreateAsset(newGraph, path);
            AssetDatabase.SaveAssets();

            // Assign the new graph to the component
            HapticAnnotationComponent component = (HapticAnnotationComponent)target;
            component.Graph = newGraph;

            // Mark the object as dirty to ensure the change is saved
            EditorUtility.SetDirty(target);

            // Open the graph editor
            OpenGraphEditor();
        }
    }

    private void OpenGraphEditor()
    {
        HapticAnnotationComponent component = (HapticAnnotationComponent)target;

        if (component.Graph != null)
        {
            // Open the Haptic Annotation Window
            HapticsAnnotationWindow.ShowWindow();

            // Load the graph into the editor
            HapticsAnnotationWindow window = EditorWindow.GetWindow<HapticsAnnotationWindow>();
            window.LoadGraph(component.Graph);
        }
        else
        {
            EditorUtility.DisplayDialog("No Graph Assigned",
                "Please create a new graph or assign an existing one first.", "OK");
        }
    }
}