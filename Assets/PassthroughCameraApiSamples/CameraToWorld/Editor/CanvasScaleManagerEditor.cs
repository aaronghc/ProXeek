using UnityEditor;
using UnityEngine;

namespace PassthroughCameraSamples.CameraToWorld.Editor
{
    [CustomEditor(typeof(CanvasScaleManager))]
    public class CanvasScaleManagerEditor : UnityEditor.Editor
    {
        private SerializedProperty m_minScaleProp;
        private SerializedProperty m_maxScaleProp;
        private SerializedProperty m_scaleSpeedProp;
        private SerializedProperty m_initialLiveCanvasScaleProp;
        private SerializedProperty m_initialSnapshotCanvasScaleProp;
        private SerializedProperty m_affectSnapshotCanvasesProp;
        private SerializedProperty m_affectLiveCanvasProp;

        private void OnEnable()
        {
            m_minScaleProp = serializedObject.FindProperty("m_minScale");
            m_maxScaleProp = serializedObject.FindProperty("m_maxScale");
            m_scaleSpeedProp = serializedObject.FindProperty("m_scaleSpeed");
            m_initialLiveCanvasScaleProp = serializedObject.FindProperty("m_initialLiveCanvasScale");
            m_initialSnapshotCanvasScaleProp = serializedObject.FindProperty("m_initialSnapshotCanvasScale");
            m_affectSnapshotCanvasesProp = serializedObject.FindProperty("m_affectSnapshotCanvases");
            m_affectLiveCanvasProp = serializedObject.FindProperty("m_affectLiveCanvas");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Initial Scale Settings", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(m_initialLiveCanvasScaleProp, new GUIContent("Live Canvas Initial Scale",
                "Initial scale for the live camera canvas. All canvases start at this scale regardless of whether they're affected by scaling controls."));

            EditorGUILayout.PropertyField(m_initialSnapshotCanvasScaleProp, new GUIContent("Snapshot Canvas Initial Scale",
                "Initial scale for snapshot canvases. All canvases start at this scale regardless of whether they're affected by scaling controls."));

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Scale Limits (Relative to Initial Scale)", EditorStyles.boldLabel);

            float minScale = m_minScaleProp.floatValue;
            float maxScale = m_maxScaleProp.floatValue;

            // Display min scale as percentage of initial scale
            EditorGUILayout.Slider(m_minScaleProp, 0.1f, 1.0f, new GUIContent("Minimum Scale",
                "The smallest scale factor relative to initial scale (0.5 = 50% of initial size)"));

            // Ensure max scale is at least equal to min scale
            if (maxScale < minScale)
            {
                maxScale = minScale;
                m_maxScaleProp.floatValue = maxScale;
            }

            // Display max scale as percentage of initial scale
            EditorGUILayout.Slider(m_maxScaleProp, minScale, 2.0f, new GUIContent("Maximum Scale",
                "The largest scale factor relative to initial scale (1.5 = 150% of initial size)"));

            EditorGUILayout.PropertyField(m_scaleSpeedProp, new GUIContent("Scale Speed",
                "How quickly the canvas scales with thumbstick movement"));

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Canvas Types to Affect", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(m_affectLiveCanvasProp, new GUIContent("Affect Live Canvas",
                "Allow scaling of the live camera canvas with thumbstick"));

            EditorGUILayout.PropertyField(m_affectSnapshotCanvasesProp, new GUIContent("Affect Snapshot Canvases",
                "Allow scaling of snapshot canvases with thumbstick"));

            EditorGUILayout.Space(10);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Reset All Canvases to Initial Scale"))
                {
                    if (Application.isPlaying)
                    {
                        CanvasScaleManager manager = (CanvasScaleManager)target;
                        manager.ResetAllCanvasesToInitialScale();
                    }
                }
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("Use the right thumbstick to scale canvases:\n• Up: Expand canvases\n• Down: Shrink canvases", MessageType.Info);

            // Show a warning if neither canvas type is selected for scaling
            if (!m_affectLiveCanvasProp.boolValue && !m_affectSnapshotCanvasesProp.boolValue)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("No canvas types selected for scaling. Thumbstick controls will have no effect.", MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}