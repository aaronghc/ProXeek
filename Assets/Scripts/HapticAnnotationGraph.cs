using UnityEngine;
using System.Collections.Generic;

// Make the class properly serializable by Unity
[CreateAssetMenu(fileName = "New Haptic Annotation Graph", menuName = "Haptics/Haptic Annotation Graph")]
public class HapticAnnotationGraph : ScriptableObject
{
    // Store the graph data as JSON
    [SerializeField] private string m_graphData;

    // Store references to GameObjects in the graph
    [SerializeField] private List<GameObject> referencedObjects = new List<GameObject>();

    // Store the graph summary
    [SerializeField] private string summary;

    // Properties to access the data
    public string GraphData
    {
        get => m_graphData;
        set => m_graphData = value;
    }

    public List<GameObject> ReferencedObjects => referencedObjects;

    public string Summary
    {
        get => summary;
        set => summary = value;
    }

    // Method to add a referenced object
    public void AddReferencedObject(GameObject obj)
    {
        if (!referencedObjects.Contains(obj))
        {
            referencedObjects.Add(obj);
        }
    }

    // Method to remove a referenced object
    public void RemoveReferencedObject(GameObject obj)
    {
        _ = referencedObjects.Remove(obj);
    }
}