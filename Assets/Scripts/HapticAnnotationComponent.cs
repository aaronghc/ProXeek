using UnityEngine;

// Make the component appear in the Add Component menu under "Haptics"
[AddComponentMenu("Haptics/Haptic Annotation")]
public class HapticAnnotationComponent : MonoBehaviour
{
    // Reference to the haptic annotation graph asset
    [SerializeField] private HapticAnnotationGraph m_graph;

    // Property to access the graph from code
    public HapticAnnotationGraph Graph
    {
        get => m_graph;
        set => m_graph = value;
    }

    // Optional: Add methods to interact with the graph at runtime if needed
}