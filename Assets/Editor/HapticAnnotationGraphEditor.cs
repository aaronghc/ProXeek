using UnityEditor;
using UnityEngine;

// This attribute tells Unity to use this editor for HapticAnnotationGraph assets
[CustomEditor(typeof(HapticAnnotationGraph))]
public class HapticAnnotationGraphEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space(10);

        // Add an "Edit Graph" button
        if (GUILayout.Button("Edit Graph", GUILayout.Height(30)))
        {
            OpenGraphEditor();
        }
    }

    private void OpenGraphEditor()
    {
        HapticAnnotationGraph graph = (HapticAnnotationGraph)target;

        // Open the Haptic Annotation Window
        HapticsAnnotationWindow.ShowWindow();

        // Load the graph into the editor
        HapticsAnnotationWindow window = EditorWindow.GetWindow<HapticsAnnotationWindow>();
        window.LoadGraph(graph);
    }
}

// This class handles double-clicking on the asset in the Project window
[InitializeOnLoad]
public class HapticAnnotationGraphAssetHandler
{
    static HapticAnnotationGraphAssetHandler() =>
        // Register for the asset open event
        EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;

    private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
    {
        // Check if the current event is a double-click
        if (Event.current.type == EventType.MouseDown && Event.current.clickCount == 2)
        {
            // Check if the mouse is over the selection rect
            if (selectionRect.Contains(Event.current.mousePosition))
            {
                // Get the asset path from the GUID
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                // Check if the asset is a HapticAnnotationGraph
                HapticAnnotationGraph graph = AssetDatabase.LoadAssetAtPath<HapticAnnotationGraph>(assetPath);

                if (graph != null)
                {
                    // Consume the event to prevent Unity's default behavior
                    Event.current.Use();

                    // Open the graph editor
                    HapticsAnnotationWindow.ShowWindow();
                    HapticsAnnotationWindow window = EditorWindow.GetWindow<HapticsAnnotationWindow>();
                    window.LoadGraph(graph);
                }
            }
        }
    }
}