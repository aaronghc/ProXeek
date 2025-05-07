using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

public class HapticsRelationshipGraphView : GraphView
{
    public void RegisterCallback<T>(EventCallback<T> callback) where T : EventBase<T>, new()
    {

        base.RegisterCallback(callback);
    }

    private readonly List<HapticNode> _nodes = new List<HapticNode>();

    public delegate void GraphChangedEventHandler();
    public static event GraphChangedEventHandler OnGraphChanged;

    // Add a list to track scopes
    private readonly List<HapticScope> _scopes = new List<HapticScope>();

    // Update the HapticsRelationshipGraphView class constructor
    public HapticsRelationshipGraphView()
    {
        style.flexGrow = 1;

        // Setup basic manipulators
        SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        // Create and add the grid background
        var grid = new GridBackground();
        Insert(0, grid);
        grid.StretchToParentSize();

        // Style the grid to match Script Graph
        grid.AddToClassList("grid-background");

        // Register for graph changes to handle connections
        graphViewChanged = OnGraphViewChanged;

        // Add context menu
        this.AddManipulator(CreateContextMenu());
    }

    // Add these methods to handle dragging nodes into scopes
    private void OnDragUpdatedEvent(DragUpdatedEvent evt)
    {
        // Check if we're dragging nodes
        if (selection.OfType<HapticNode>().Any())
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            evt.StopPropagation();
        }
    }

    private void OnDragPerformEvent(DragPerformEvent evt)
    {
        // Get the selected nodes
        var selectedNodes = selection.OfType<HapticNode>().ToList();
        if (selectedNodes.Count == 0)
            return;

        // Find the scope under the mouse
        Vector2 mousePosition = evt.mousePosition;
        var scopes = graphElements.OfType<HapticScope>().ToList();

        foreach (var scope in scopes)
        {
            if (scope.worldBound.Contains(mousePosition))
            {
                // Add selected nodes to this scope
                foreach (var node in selectedNodes)
                {
                    scope.AddElement(node);
                }
                break;
            }
        }
    }

    public void ClearGraph()
    {
        DeleteElements(graphElements);
        _nodes.Clear();
        _scopes.Clear();
    }

    // Update the AddGameObjectNode method in HapticsRelationshipGraphView
    public HapticNode AddGameObjectNode(GameObject obj, Vector2 dropPosition = default(Vector2))
    {
        // Create a new HapticNode (custom node) with a reference to GameObject
        var node = new HapticNode(obj);
        node.SetPosition(new Rect(dropPosition.x, dropPosition.y, 200, 150));
        AddElement(node);
        _nodes.Add(node);

        // Check if the node was dropped inside a scope
        Rect nodeRect = node.GetPosition();
        Vector2 nodeCenter = new Vector2(nodeRect.x + nodeRect.width / 2, nodeRect.y + nodeRect.height / 2);

        // Check all scopes to see if the node is inside any of them
        var scopes = graphElements.OfType<HapticScope>().ToList();
        foreach (var scope in scopes)
        {
            Rect scopeRect = scope.GetPosition();
            if (scopeRect.Contains(nodeCenter))
            {
                scope.AddElement(node);
                break;
            }
        }

        // Return the created node
        return node;
    }

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
    {
        // Gather all ports by examining every node in this GraphView
        var compatiblePorts = new List<Port>();

        // 'nodes' is a built-in property that returns all Node elements in this GraphView
        var allNodes = nodes.ToList();
        foreach (var node in allNodes)
        {
            // Each node has inputContainer and outputContainer,
            // but you can query all Port elements if you prefer
            var nodePorts = node.Query<Port>().ToList();
            foreach (var port in nodePorts)
            {
                // 1. Skip the same port
                if (port == startPort)
                    continue;

                // 2. Skip ports on the same node
                if (port.node == startPort.node)
                    continue;

                // 3. Skip if the direction is the same (both Output or both Input)
                if (port.direction == startPort.direction)
                {
                    // We only connect Output → Input or vice versa
                    // so we require opposite directions
                    continue;
                }

                // 4. Check data type matching
                // GraphView compares 'portType' to control whether the two ports share a data type
                if (port.portType == startPort.portType)
                {
                    compatiblePorts.Add(port);
                }
            }
        }

        return compatiblePorts;
    }

    // Update the OnGraphViewChanged method in HapticsRelationshipGraphView
    // Update the OnGraphViewChanged method in HapticsRelationshipGraphView
    private GraphViewChange OnGraphViewChanged(GraphViewChange change)
    {
        bool graphChanged = false;

        // Handle new edges (connections)
        if (change.edgesToCreate != null && change.edgesToCreate.Count > 0)
        {
            graphChanged = true;

            foreach (var edge in change.edgesToCreate)
            {
                // Notify the input port's node about the connection
                if (edge.input.node is HapticNode inputNode)
                {
                    inputNode.OnPortConnected(edge.input);
                }

                // Add a new direct port to the output node if needed
                if (edge.output.node is HapticNode outputNode)
                {
                    outputNode.AddDirectPort();
                }
            }
        }

        // Handle element removals (nodes, edges, and scopes)
        if (change.elementsToRemove != null && change.elementsToRemove.Count > 0)
        {
            graphChanged = true;
            // Create a list to store additional elements that need to be removed
            List<GraphElement> additionalElementsToRemove = new List<GraphElement>();

            foreach (var element in change.elementsToRemove)
            {
                if (element is HapticScope scope)
                {
                    // Remove the scope from our tracking list
                    _scopes.Remove(scope);
                }
                else if (element is HapticNode node)
                {
                    // Find all edges connected to this node
                    var connectedEdges = edges.ToList().Where(edge =>
                        edge.input.node == node || edge.output.node == node).ToList();

                    // For each connected edge, notify the other node about the disconnection
                    foreach (var edge in connectedEdges)
                    {
                        // If this node is the input node, notify the output node
                        if (edge.input.node == node && edge.output.node is HapticNode outputNode)
                        {
                            outputNode.OnPortDisconnected(edge.output);
                        }
                        // If this node is the output node, notify the input node
                        else if (edge.output.node == node && edge.input.node is HapticNode inputNode)
                        {
                            inputNode.OnPortDisconnected(edge.input);
                        }
                    }

                    // Add connected edges to removal list
                    additionalElementsToRemove.AddRange(connectedEdges);

                    // Call PrepareForDeletion to disconnect all ports
                    node.PrepareForDeletion();

                    // Remove the node from our tracking list
                    _nodes.Remove(node);
                }
                else if (element is Edge edge)
                {
                    Debug.Log($"Removing edge: {edge.output?.node?.title} -> {edge.input?.node?.title}");

                    // Make sure to disconnect the edge from both ports
                    edge.output?.Disconnect(edge);
                    edge.input?.Disconnect(edge);

                    // Notify the nodes about the disconnection
                    if (edge.input?.node is HapticNode inputNode)
                    {
                        inputNode.OnPortDisconnected(edge.input);
                    }

                    if (edge.output?.node is HapticNode outputNode)
                    {
                        outputNode.OnPortDisconnected(edge.output);
                    }
                }
            }

            // Add the additional elements to the removal list
            if (additionalElementsToRemove.Count > 0)
            {
                change.elementsToRemove.AddRange(additionalElementsToRemove);
            }
        }

        if (graphChanged)
        {
            OnGraphChanged?.Invoke();
        }

        return change;
    }

    public HapticAnnotationData CollectAnnotationData()
    {
        // Example of collecting annotation data from each node
        var data = new HapticAnnotationData();
        data.nodeAnnotations = new List<HapticObjectRecord>();
        data.relationshipAnnotations = new List<HapticConnectionRecord>();
        data.groups = new List<GroupRecord>(); 

        foreach (var hNode in _nodes)
        {
            data.nodeAnnotations.Add(new HapticObjectRecord
            {
                objectName = hNode.AssociatedObject.name,
                inertia = hNode.Inertia,
                interactivity = hNode.Interactivity,
                outline = hNode.Outline,
                texture = hNode.Texture,
                hardness = hNode.Hardness,
                temperature = hNode.Temperature,
                engagementLevel = hNode.EngagementLevel,
                isDirectContacted = hNode.IsDirectContacted,
                description = hNode.Description,

                // Add slider values
                inertiaValue = hNode.InertiaValue,
                interactivityValue = hNode.InteractivityValue,
                outlineValue = hNode.OutlineValue,
                textureValue = hNode.TextureValue,
                hardnessValue = hNode.HardnessValue,
                temperatureValue = hNode.TemperatureValue
            });

            // Collect tool-mediated annotations
            var toolMediatedAnnotations = hNode.GetToolMediatedAnnotations();
            foreach (var kvp in toolMediatedAnnotations)
            {
                data.relationshipAnnotations.Add(new HapticConnectionRecord
                {
                    contactObject = kvp.Key,
                    substrateObject = hNode.AssociatedObject.name,
                    annotationText = kvp.Value
                });
            }
        }

        // Process all edges in the graph to ensure we capture all connections
        var allEdges = edges.ToList();
        foreach (var edge in allEdges)
        {
            var outputNode = edge.output?.node as HapticNode;
            var inputNode = edge.input?.node as HapticNode;

            if (outputNode != null && inputNode != null)
            {
                // Check if this relationship is already in the list
                bool alreadyExists = data.relationshipAnnotations.Any(r =>
                    r.contactObject == outputNode.AssociatedObject.name &&
                    r.substrateObject == inputNode.AssociatedObject.name);

                if (!alreadyExists)
                {
                    // Get the annotation text from the input node for this specific port
                    string annotationText = inputNode.GetAnnotationTextForPort(edge.input);

                    // Add the relationship
                    data.relationshipAnnotations.Add(new HapticConnectionRecord
                    {
                        contactObject = outputNode.AssociatedObject.name,
                        substrateObject = inputNode.AssociatedObject.name,
                        annotationText = annotationText
                    });
                }
            }
        }

        // Process all groups (scopes) in the graph
        var scopes = GetScopes();
        foreach (var scope in scopes)
        {
            // Create a new group record
            var groupRecord = new GroupRecord
            {
                title = scope.title,
                objectNames = new List<string>(),
                objectVectors = new List<ObjectVectorRecord>()
            };

            // Get all HapticNodes in this scope
            var nodesInScope = scope.containedElements.OfType<HapticNode>().ToList();

            // Add all object names to the group
            foreach (var node in nodesInScope)
            {
                if (node.AssociatedObject != null)
                {
                    groupRecord.objectNames.Add(node.AssociatedObject.name);
                }
            }

            // Calculate vectors between each pair of objects in the group
            for (int i = 0; i < nodesInScope.Count; i++)
            {
                for (int j = i + 1; j < nodesInScope.Count; j++)
                {
                    var nodeA = nodesInScope[i];
                    var nodeB = nodesInScope[j];

                    if (nodeA.AssociatedObject != null && nodeB.AssociatedObject != null)
                    {
                        // Calculate the vector between the two objects
                        Vector3 vectorAB = nodeB.AssociatedObject.transform.position - nodeA.AssociatedObject.transform.position;

                        // Add the vector record
                        groupRecord.objectVectors.Add(new ObjectVectorRecord
                        {
                            objectA = nodeA.AssociatedObject.name,
                            objectB = nodeB.AssociatedObject.name,
                            vector = new SerializableVector3
                            {
                                x = vectorAB.x,
                                y = vectorAB.y,
                                z = vectorAB.z
                            },
                            distance = vectorAB.magnitude
                        });
                    }
                }
            }

            // Add the group record to the data
            data.groups.Add(groupRecord);
        }

        return data;
    }

    public List<HapticNode> GetNodes()
    {
        return _nodes;
    }

    public void FrameAndFocusNode(HapticNode node, bool select = false)
    {
        if (node == null) return;

        // Store the current selection
        var currentSelection = selection.ToList();

        // Temporarily select the node we want to frame
        ClearSelection();
        AddToSelection(node);

        // Use the built-in method to frame the selection
        FrameSelection();

        // If we don't want to keep the node selected, restore the previous selection
        if (!select)
        {
            ClearSelection();
            foreach (var item in currentSelection)
            {
                AddToSelection(item);
            }
        }
    }

    public void ConnectNodes(HapticNode sourceNode, HapticNode targetNode, string annotationText)
    {
        // Find an output port on the source node
        var outputPort = sourceNode.outputContainer.Q<Port>();

        // Find an input port on the target node
        var inputPort = targetNode.inputContainer.Q<Port>();

        if (outputPort != null && inputPort != null)
        {
            // Create an edge between the ports
            var edge = new Edge
            {
                output = outputPort,
                input = inputPort
            };

            // Connect the ports
            outputPort.Connect(edge);
            inputPort.Connect(edge);

            // Add the edge to the graph
            AddElement(edge);

            // Set the annotation text
            targetNode.SetAnnotationTextForPort(inputPort, annotationText);
        }
    }

    // Update the CreateScope method in HapticsRelationshipGraphView
    public HapticScope CreateScope(Rect position, string title = "Group")
    {
        var scope = new HapticScope();
        scope.SetPosition(position);
        scope.title = title; // Use title instead of ScopeTitle

        // Add the scope to the graph
        AddElement(scope);
        _scopes.Add(scope);

        // Return the scope for further customization
        return scope;
    }

    // Update the CreateScopeFromSelection method in HapticsRelationshipGraphView
    public HapticScope CreateScopeFromSelection()
    {
        var selectedNodes = selection.OfType<HapticNode>().ToList();
        if (selectedNodes.Count == 0)
            return null;

        // Calculate the bounds of the selected nodes
        Rect bounds = CalculateBounds(selectedNodes);

        // Add some padding
        bounds.x -= 20;
        bounds.y -= 40; // Extra space for the header
        bounds.width += 40;
        bounds.height += 60;

        // Create the scope
        var scope = CreateScope(bounds);

        // Add the selected nodes to the scope
        foreach (var node in selectedNodes)
        {
            scope.AddElement(node);
        }

        return scope;
    }

    // Helper method to calculate the bounds of a set of nodes
    private Rect CalculateBounds(List<HapticNode> nodes)
    {
        if (nodes.Count == 0)
            return new Rect();

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var node in nodes)
        {
            Rect position = node.GetPosition();
            minX = Mathf.Min(minX, position.x);
            minY = Mathf.Min(minY, position.y);
            maxX = Mathf.Max(maxX, position.x + position.width);
            maxY = Mathf.Max(maxY, position.y + position.height);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    // Get all scopes in the graph
    public List<HapticScope> GetScopes()
    {
        return _scopes;
    }

    // This handles right-clicks on empty space
    private ContextualMenuManipulator CreateContextMenu()
    {
        return new ContextualMenuManipulator(
            menuEvent => {
            // Get the mouse position
            Vector2 localMousePosition = contentViewContainer.WorldToLocal(menuEvent.mousePosition);

            // Add menu items for creating groups
            menuEvent.menu.AppendAction("Create Empty Group",
                    action => CreateScope(new Rect(localMousePosition.x, localMousePosition.y, 200, 150)),
                    DropdownMenuAction.AlwaysEnabled);

            // Only show "Create Group From Selection" if nodes are selected
            if (selection.OfType<HapticNode>().Any())
                {
                    menuEvent.menu.AppendAction("Create Group From Selection",
                        action => CreateScopeFromSelection(),
                        DropdownMenuAction.Status.Normal);

                // Add "Remove From Group" option if any selected node is in a group
                bool anyNodeInGroup = selection.OfType<HapticNode>().Any(node =>
                        graphElements.OfType<HapticScope>().Any(s =>
                            s.containedElements.Contains(node)));

                    if (anyNodeInGroup)
                    {
                        menuEvent.menu.AppendAction("Remove From Group",
                            action => {
                                var selectedNodes = selection.OfType<HapticNode>().ToList();
                                var availableScopes = graphElements.OfType<HapticScope>().ToList();

                                foreach (var node in selectedNodes)
                                {
                                    foreach (var s in availableScopes)
                                    {
                                        if (s.containedElements.Contains(node))
                                        {
                                            s.RemoveElement(node);
                                        }
                                    }
                                }
                            },
                            DropdownMenuAction.Status.Normal);
                    }

                // Add options to add to existing groups
                var availableGroups = graphElements.OfType<HapticScope>().ToList();
                    if (availableGroups.Count > 0)
                    {
                    // Add each scope as a separate menu item
                    foreach (var existingScope in availableGroups)
                        {
                            menuEvent.menu.AppendAction($"Add to Group: {existingScope.title}",
                                action => {
                                    foreach (var element in selection)
                                    {
                                        if (element is HapticNode node)
                                        {
                                            existingScope.AddElement(node);
                                        }
                                    }
                                },
                                DropdownMenuAction.Status.Normal);
                        }
                    }
                }
            }
        );
    }

    // This is now simpler since groups handle their own context menus
    public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        base.BuildContextualMenu(evt);

    }

    public void NotifyGraphChanged()
    {
        // This method can be called from outside to trigger the OnGraphChanged event
        OnGraphChanged?.Invoke();
    }
}

// Minimal data structure to hold annotation data
[System.Serializable]
public class HapticAnnotationData
{
    public string summary;
    public List<HapticObjectRecord> nodeAnnotations;
    public List<HapticConnectionRecord> relationshipAnnotations;
    public List<GroupRecord> groups;
}

[System.Serializable]
public class HapticObjectRecord
{
    public string objectName;
    public bool isDirectContacted;
    public string description;
    public int engagementLevel;
    public string snapshotPath;

    public string inertia;
    public string interactivity;
    public string outline;
    public string texture;
    public string hardness;
    public string temperature;

    // Add slider values
    public float inertiaValue;
    public float interactivityValue;
    public float outlineValue;
    public float textureValue;
    public float hardnessValue;
    public float temperatureValue;
}

// Record for each connection
[System.Serializable]
public class HapticConnectionRecord
{
    public string contactObject;
    public string substrateObject;
    public string annotationText;
}

[System.Serializable]
public class GroupRecord
{
    public string title;
    public List<string> objectNames;
    public List<ObjectVectorRecord> objectVectors;
    public string arrangementSnapshotPath; // Path to the main group arrangement snapshot
    public List<string> additionalViewAngles; // Paths to additional view angles
}

[System.Serializable]
public class ObjectVectorRecord
{
    public string objectA;
    public string objectB;
    public SerializableVector3 vector;
    public float distance;
}

[System.Serializable]
public class SerializableVector3
{
    public float x;
    public float y;
    public float z;
}

// Example node class: represents one VR object in the graph
public class HapticNode : Node
{
    // Add a delegate and event for engagement level changes
    public delegate void EngagementLevelChangedEventHandler(HapticNode node, int newLevel);
    public static event EngagementLevelChangedEventHandler OnEngagementLevelChanged;

    public bool IsDirectContacted { get; set; } = false;
    public string Description { get; set; } = "";

    public string Inertia { get; set; } = "";
    public string Interactivity { get; set; } = "";
    public string Outline { get; set; } = "";
    public string Texture { get; set; } = "";
    public string Hardness { get; set; } = "";
    public string Temperature { get; set; } = "";

    // Add float values for sliders (0-1 range)
    public float InertiaValue { get; set; } = 0f;
    public float InteractivityValue { get; set; } = 0f;
    public float OutlineValue { get; set; } = 0f;
    public float TextureValue { get; set; } = 0f;
    public float HardnessValue { get; set; } = 0f;
    public float TemperatureValue { get; set; } = 0f;

    public Dictionary<string, bool> PropertyFoldoutStates { get; private set; } = new Dictionary<string, bool>()
    {
        { "Inertia", false },
        { "Interactivity", false },
        { "Outline", false },
        { "Texture", false },
        { "Hardness", false },
        { "Temperature", false }
    };

    private int _engagementLevel = 1; // Default to Medium Engagement (index 1)

    public int EngagementLevel
    {
        get => _engagementLevel;
        set
        {
            if (_engagementLevel != value)
            {
                _engagementLevel = value;
                // Trigger the event when engagement level changes
                OnEngagementLevelChanged?.Invoke(this, value);
            }
        }
    }

    // Add this method to the HapticNode class
    public Texture2D CaptureNodeSnapshot()
    {
        // Create a render texture to capture the preview
        RenderTexture renderTexture = new RenderTexture(256, 256, 24);
        RenderTexture.active = renderTexture;

        // Create a texture to store the snapshot
        Texture2D snapshot = new Texture2D(256, 256, TextureFormat.RGBA32, false);

        // If we have a valid GameObject and editor
        if (AssociatedObject != null && _gameObjectEditor != null)
        {
            // Draw the preview to the render texture
            _gameObjectEditor.OnPreviewGUI(new Rect(0, 0, 256, 256), GUIStyle.none);

            // Read the pixels from the render texture
            snapshot.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            snapshot.Apply();
        }
        else
        {
            // Fill with a default color if no valid preview
            Color[] pixels = new Color[256 * 256];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(0.3f, 0.3f, 0.3f, 1.0f);
            snapshot.SetPixels(pixels);
            snapshot.Apply();
        }

        // Clean up
        RenderTexture.active = null;

        return snapshot;
    }

    public GameObject AssociatedObject { get; private set; }

    private List<Port> _outputPorts = new List<Port>();
    private List<ToolMediatedPortData> _inputPorts = new List<ToolMediatedPortData>();
    private IMGUIContainer _previewContainer;
    private Editor _gameObjectEditor;
    private bool _needsEditorUpdate = true;

    // Class to hold port and its associated text field
    private class ToolMediatedPortData
    {
        public Port Port;
        public TextField AnnotationField;
        public VisualElement Container;
    }

    public string GetAnnotationTextForPort(Port port)
    {
        var portData = _inputPorts.Find(p => p.Port == port);
        return portData?.AnnotationField?.value ?? "";
    }

    public HapticNode(GameObject go)
    {
        AssociatedObject = go;

        // Truncate long names for display purposes
        title = TruncateNodeTitle(go.name);

        // Set tooltip to show full name on hover
        tooltip = go.name;

        // Create a container for the preview and radio buttons with proper layout
        var previewAndControlsContainer = new VisualElement();
        previewAndControlsContainer.AddToClassList("preview-controls-container");


        // Create a preview container using IMGUI
        _previewContainer = new IMGUIContainer(() => {
            // Check if we need to update the editor
            if (_needsEditorUpdate || _gameObjectEditor == null)
            {
                if (_gameObjectEditor != null)
                {
                    Object.DestroyImmediate(_gameObjectEditor);
                }

                if (AssociatedObject != null)
                {
                    _gameObjectEditor = Editor.CreateEditor(AssociatedObject);
                }

                _needsEditorUpdate = false;
            }

            // Draw the preview
            if (AssociatedObject != null && _gameObjectEditor != null)
            {
                // Calculate the preview rect
                Rect previewRect = GUILayoutUtility.GetRect(150, 150);

                // Draw the preview
                _gameObjectEditor.OnInteractivePreviewGUI(previewRect, GUIStyle.none);
            }
        });

        _previewContainer.AddToClassList("preview-container");

        // Add the preview to the container
        previewAndControlsContainer.Add(_previewContainer);

        // Create the radio button group container
        var radioGroupContainer = new VisualElement();
        radioGroupContainer.AddToClassList("radio-group-container");

        // Add a title for the radio group
        var radioGroupTitle = new Label("Levels of Participation");
        radioGroupTitle.AddToClassList("radio-group-title");
        radioGroupContainer.Add(radioGroupTitle);

        // Create the radio buttons
        var highEngagementRadio = CreateRadioButton("High Engagement", 2, EngagementLevel == 2);
        var mediumEngagementRadio = CreateRadioButton("Medium Engagement", 1, EngagementLevel == 1);
        var lowEngagementRadio = CreateRadioButton("Low Engagement", 0, EngagementLevel == 0);

        // Add the radio buttons to the container
        radioGroupContainer.Add(highEngagementRadio);
        radioGroupContainer.Add(mediumEngagementRadio);
        radioGroupContainer.Add(lowEngagementRadio);

        // Add both containers to the main container
        previewAndControlsContainer.Add(_previewContainer);
        previewAndControlsContainer.Add(radioGroupContainer);

        // Add the container to the node
        mainContainer.Add(previewAndControlsContainer);

        // Create the initial direct port
        AddDirectPort();

        // Create the initial tool-mediated port with its text field
        AddToolMediatedPort();

        // Register for scene changes to update the preview
        EditorApplication.update += OnEditorUpdate;

        // Register for cleanup when the node is removed
        RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

        RefreshExpandedState();
        RefreshPorts();
    }

    private string TruncateNodeTitle(string originalName)
    {
        const int maxLength = 25; // Maximum characters to display

        if (originalName.Length <= maxLength)
            return originalName;

        // Truncate and add ellipsis
        return originalName.Substring(0, maxLength - 3) + "...";
    }

    private VisualElement CreateRadioButton(string label, int value, bool isSelected)
    {
        var container = new VisualElement();
        container.AddToClassList("radio-button-container");

        var radioButton = new Toggle();
        radioButton.value = isSelected;
        radioButton.AddToClassList("radio-button");

        var radioLabel = new Label(label);
        radioLabel.AddToClassList("radio-label");

        container.Add(radioButton);
        container.Add(radioLabel);

        // Add click handler
        radioButton.RegisterValueChangedCallback(evt => {
            if (evt.newValue)
            {
                // Deselect all other radio buttons in the group
                VisualElement parent = container.parent;
                if (parent != null)
                {
                    var allRadioButtons = parent.Query<Toggle>().ToList();
                    foreach (var rb in allRadioButtons)
                    {
                        if (rb != radioButton)
                        {
                            rb.SetValueWithoutNotify(false);
                        }
                    }
                }

                // Set the engagement level
                EngagementLevel = value;
            }
            else
            {
                // Don't allow deselecting without selecting another option
                radioButton.SetValueWithoutNotify(true);
            }
        });

        return container;
    }

    private void OnDetachFromPanel(DetachFromPanelEvent evt)
    {
        // Clean up resources
        EditorApplication.update -= OnEditorUpdate;

        // Use a safer approach to clean up the editor
        if (_gameObjectEditor != null)
        {
            try
            {
                // Check if the editor is still valid before destroying it
                if (_gameObjectEditor.target != null)
                {
                    Object.DestroyImmediate(_gameObjectEditor);
                }
            }
            catch (System.Exception e)
            {
                // Log the error but don't let it crash the application
                Debug.LogWarning($"Error cleaning up editor: {e.Message}");
            }
            finally
            {
                _gameObjectEditor = null;
            }
        }
    }

    private void OnEditorUpdate()
    {
        // Mark that we need to update the editor on the next IMGUI pass
        if (AssociatedObject != null)
        {
            // Force a repaint to update the preview
            _previewContainer.MarkDirtyRepaint();
        }
    }

    public void AddDirectPort()
    {
        // Create a new direct port
        var newOutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
        newOutputPort.portName = "Contact →";

        // Add to containers
        outputContainer.Add(newOutputPort);
        _outputPorts.Add(newOutputPort);

        Debug.Log($"Added new direct port. Total ports: {_outputPorts.Count}");

        RefreshExpandedState();
        RefreshPorts();

    }

    public void AddToolMediatedPort()
    {
        // Create a container for the port and its text field
        var portContainer = new VisualElement();
        portContainer.style.flexDirection = FlexDirection.Row;
        portContainer.style.alignItems = Align.Center;

        // Create the port
        var inputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(bool));
        inputPort.portName = "→ Substrate";

        // Create the text field (disabled by default) without a label
        var textField = new TextField();
        textField.SetEnabled(false);

        // Remove the label to save space
        textField.label = "";

        // Expand the width to use available space
        textField.style.width = 150;
        textField.style.height = 18;
        textField.style.marginLeft = 0;
        textField.style.marginRight = 5;

        // Add tooltip for better UX
        textField.tooltip = "Expected haptic feedback derived from object contact";

        // Add port to container
        portContainer.Add(inputPort);
        portContainer.Add(textField);

        // Add container to the node's input container
        inputContainer.Add(portContainer);

        // Store the port data
        _inputPorts.Add(new ToolMediatedPortData
        {
            Port = inputPort,
            AnnotationField = textField,
            Container = portContainer
        });

        RefreshExpandedState();
        RefreshPorts();
    }

    // Called by the GraphView when a connection is made to this port
    public void OnPortConnected(Port port)
    {
        Debug.Log($"Port connected on {title}: {port.portName}");

        // Find the corresponding data
        var portData = _inputPorts.Find(p => p.Port == port);
        if (portData != null)
        {
            Debug.Log($"Found port data, enabling text field");
            // Enable the text field
            portData.AnnotationField.SetEnabled(true);

            // Add a new tool-mediated port if this was the last one
            if (_inputPorts.IndexOf(portData) == _inputPorts.Count - 1)
            {
                AddToolMediatedPort();
            }
        }
        else
        {
            Debug.LogWarning($"Could not find port data for {port.portName} on {title}");
        }
    }

    // Called by the GraphView when a port is disconnected
    public void OnPortDisconnected(Port port)
    {
        Debug.Log($"Port disconnected on {title}: {port.portName}");

        // Handle tool-mediated port disconnection
        if (port.direction == Direction.Input)
        {
            var portData = _inputPorts.Find(p => p.Port == port);
            if (portData != null)
            {
                // Disable the text field and clear its value
                portData.AnnotationField.SetEnabled(false);
                portData.AnnotationField.value = "";

                // If this is not the last port, remove it
                int index = _inputPorts.IndexOf(portData);
                if (index < _inputPorts.Count - 1)
                {
                    inputContainer.Remove(portData.Container);
                    _inputPorts.Remove(portData);
                    RefreshPorts();
                }
            }
        }
        // Handle direct port disconnection
        else if (port.direction == Direction.Output)
        {
            Debug.Log($"Direct port disconnected: connected={port.connected}, connections={port.connections.Count()}");

            // Only remove if it has no connections AND it's not the last port
            if (_outputPorts.Count > 1)
            {
                // Check if the port has any remaining connections
                bool hasConnections = port.connections.Count() > 0;

                if (!hasConnections)
                {
                    Debug.Log($"Removing direct port");
                    outputContainer.Remove(port);
                    _outputPorts.Remove(port);
                    RefreshPorts();
                }
            }
        }
    }

    public void PrepareForDeletion()
    {
        Debug.Log($"Preparing node {title} for deletion");

        // Disconnect all input ports
        foreach (var portData in _inputPorts)
        {
            if (portData.Port.connected)
            {
                // Create a copy of connections to avoid modification during enumeration
                var connections = portData.Port.connections.ToList();
                foreach (var connection in connections)
                {
                    // Get the source node before disconnecting
                    var sourceNode = connection.output.node as HapticNode;
                    var sourcePort = connection.output;

                    // Disconnect the connection
                    connection.output.Disconnect(connection);
                    connection.input.Disconnect(connection);

                    // Notify the source node about the disconnection
                    if (sourceNode != null)
                    {
                        // Explicitly call OnPortDisconnected on the source node
                        sourceNode.OnPortDisconnected(sourcePort);
                    }
                }
            }
        }

        // Disconnect all output ports
        foreach (var port in _outputPorts)
        {
            if (port.connected)
            {
                // Create a copy of connections to avoid modification during enumeration
                var connections = port.connections.ToList();
                foreach (var connection in connections)
                {
                    // Get the target node before disconnecting
                    var targetNode = connection.input.node as HapticNode;
                    var targetPort = connection.input;

                    // Disconnect the connection
                    connection.output.Disconnect(connection);
                    connection.input.Disconnect(connection);

                    // Notify the target node about the disconnection
                    if (targetNode != null)
                    {
                        // Explicitly call OnPortDisconnected on the target node
                        targetNode.OnPortDisconnected(targetPort);
                    }
                }
            }
        }
    }

    // Method to collect all tool-mediated annotations
    public Dictionary<string, string> GetToolMediatedAnnotations()
    {
        var annotations = new Dictionary<string, string>();

        foreach (var portData in _inputPorts)
        {
            if (portData.Port.connected && !string.IsNullOrEmpty(portData.AnnotationField.value))
            {
                // Use the connected node's name as the key
                foreach (var connection in portData.Port.connections)
                {
                    var sourceNode = connection.output.node as HapticNode;
                    if (sourceNode != null)
                    {
                        annotations[sourceNode.AssociatedObject.name] = portData.AnnotationField.value;
                    }
                }
            }
        }

        return annotations;
    }

    public void SetAnnotationTextForPort(Port port, string text)
    {
        var portData = _inputPorts.Find(p => p.Port == port);
        if (portData != null)
        {
            portData.AnnotationField.value = text;
            portData.AnnotationField.SetEnabled(true);
        }
    }

    // Add this method to the HapticNode class
    public void SetEngagementLevel(int level)
    {
        // Validate the level (0-2)
        if (level < 0 || level > 2)
            return;

        // Find the radio buttons in the radio group container
        // We need to use the correct path to find the radio buttons
        var radioContainers = this.Query<VisualElement>(className: "radio-button-container").ToList();

        // If we have the expected radio containers
        if (radioContainers.Count >= 3)
        {
            // Get the toggle from each container
            var radioButtons = new List<Toggle>();
            foreach (var container in radioContainers)
            {
                var toggle = container.Q<Toggle>();
                if (toggle != null)
                {
                    radioButtons.Add(toggle);
                }
            }

            // If we found the radio buttons
            if (radioButtons.Count >= 3)
            {
                // Set the appropriate radio button based on level
                // The radio buttons are in order: High (2), Medium (1), Low (0)
                // So we need to convert the level to the correct index
                int buttonIndex = 2 - level; // Convert level to button index

                // Trigger the radio button click
                radioButtons[buttonIndex].SetValueWithoutNotify(true);

                // Deselect other radio buttons
                for (int i = 0; i < radioButtons.Count; i++)
                {
                    if (i != buttonIndex)
                    {
                        radioButtons[i].SetValueWithoutNotify(false);
                    }
                }

                // Set the engagement level field
                EngagementLevel = level;
            }
        }
        else
        {
            // If we can't find the radio buttons, set the property directly
            // This is a fallback
            EngagementLevel = level;
        }
    }

    // Add this method to the HapticNode class
    public int GetPortIndex(Port port)
    {
        if (port.direction == Direction.Output)
        {
            // Find the index in output ports
            var outputPorts = outputContainer.Query<Port>().ToList();
            return outputPorts.IndexOf(port);
        }
        else
        {
            // Find the index in input ports
            var inputPorts = inputContainer.Query<Port>().ToList();
            return inputPorts.IndexOf(port);
        }
    }

}

public class HapticRelationshipEdge : Edge
{
    private TextField _annotationField; // Inline text field on the edge
    public string Annotation
    {
        get => _annotationField?.value ?? string.Empty;
        set
        {
            if (_annotationField != null)
                _annotationField.value = value;
        }
    }

    public HapticRelationshipEdge() : base()
    {
        // A small container to hold the TextField
        VisualElement annotationContainer = new VisualElement
        {
            style =
        {
            flexDirection = FlexDirection.Row,
            alignItems = Align.Center
        }
        };

        _annotationField = new TextField("Annotation:")
        {
            value = ""
        };
        annotationContainer.Add(_annotationField);

        // Place the annotation container visually in the middle of the edge
        // GraphView doesn't do this automatically, so you can experiment with styling
        Add(annotationContainer);
    }

}

public class HapticScope : Group
{
    private Color _scopeColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);

    public Color ScopeColor
    {
        get => _scopeColor;
        set
        {
            _scopeColor = value;
            ApplyColorToStyle();
        }
    }

    public HapticScope()
    {
        // Add a context menu manipulator specifically for this group
        this.AddManipulator(new ContextualMenuManipulator(BuildContextualMenu));

        // Apply the initial color
        ApplyColorToStyle();

        // Register for when the element is attached to a panel (becomes visible)
        RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
    }

    private void OnAttachToPanel(AttachToPanelEvent evt)
    {
        // Ensure the color is applied when the element is added to the UI
        ApplyColorToStyle();
    }

    private void ApplyColorToStyle()
    {
        // Apply the color to the style
        if (style != null)
        {
            style.backgroundColor = _scopeColor;
        }
    }

    private void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        // Add "Ungroup All" option
        evt.menu.AppendAction("Ungroup All", (a) => {
            // Get all elements in the scope
            var elementsToRemove = containedElements.ToList();

            // Remove all elements from the scope
            foreach (var element in elementsToRemove)
            {
                RemoveElement(element);
            }

            // Get a reference to the parent GraphView
            var graphView = GetFirstAncestorOfType<GraphView>();
            if (graphView != null)
            {
                // Remove the group itself from the graph
                graphView.RemoveElement(this);
            }
        });

        evt.menu.AppendSeparator();

        // Color options
        evt.menu.AppendAction("Set Color/Blue", (a) => {
            ScopeColor = new Color(0.2f, 0.3f, 0.4f, 0.3f);
            NotifyGraphChanged();
        });

        evt.menu.AppendAction("Set Color/Green", (a) => {
            ScopeColor = new Color(0.2f, 0.4f, 0.2f, 0.3f);
            NotifyGraphChanged();
        });

        evt.menu.AppendAction("Set Color/Red", (a) => {
            ScopeColor = new Color(0.4f, 0.2f, 0.2f, 0.3f);
            NotifyGraphChanged();
        });

        evt.menu.AppendAction("Set Color/Purple", (a) => {
            ScopeColor = new Color(0.4f, 0.2f, 0.4f, 0.3f);
            NotifyGraphChanged();
        });

        evt.menu.AppendAction("Set Color/Orange", (a) => {
            ScopeColor = new Color(0.5f, 0.3f, 0.1f, 0.3f);
            NotifyGraphChanged();
        });
    }

    // Helper method to notify the graph that a change has occurred
    private void NotifyGraphChanged()
    {
        // Find the parent graph view
        var graphView = GetFirstAncestorOfType<HapticsRelationshipGraphView>();
        if (graphView != null)
        {
            // Call a public method on the graph view to notify of changes
            graphView.NotifyGraphChanged();
        }
    }
}