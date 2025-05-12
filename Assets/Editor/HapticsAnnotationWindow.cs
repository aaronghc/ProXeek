using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;
using System;
using System.Linq;
using System.Collections.Generic;

public class HapticsAnnotationWindow : EditorWindow
{
    private HapticsRelationshipGraphView _graphView;
    private VisualElement _inspectorContainer;
    private VisualElement _inspectorContent;
    private bool _showInspector = true;
    private List<ISelectable> _lastSelection = new List<ISelectable>();

    // Fields to store graph metadata
    private string _graphTitle = ""; //?Haptic Annotation?
    private string _graphSummary = ""; //"Describe the haptic relationships in this scene."

    // Add these fields to HapticsAnnotationWindow.cs
    private List<HapticNode> _orderedHighEngagementNodes = new List<HapticNode>();
    private List<HapticNode> _orderedMediumEngagementNodes = new List<HapticNode>();
    private List<HapticNode> _orderedLowEngagementNodes = new List<HapticNode>();

    // Add these properties to the HapticNode class
    public bool IsDirectContacted { get; set; } = false;
    public string Description { get; set; } = "";

    private HapticAnnotationGraph _currentGraph;

    private bool _hasUnsavedChanges = false;

    [MenuItem("HapticsAnnotationWindow/Open _%#T")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<HapticsAnnotationWindow>();

        // Try to load the pre-resized icon
        var iconPath = "Assets/Editor/icon16.png"; // Use a pre-resized 16x16 version
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);

        if (icon != null)
        {
            wnd.titleContent = new GUIContent("Haptic Annotation", icon);
        }
        else
        {
            wnd.titleContent = new GUIContent("Haptic Annotation");
            Debug.LogWarning("Could not load icon at path: " + iconPath);
        }

        wnd.Show();
    }

    private void OnEnable()
    {
        // Initialize ordered lists
        _orderedHighEngagementNodes = new List<HapticNode>();
        _orderedMediumEngagementNodes = new List<HapticNode>();
        _orderedLowEngagementNodes = new List<HapticNode>();

        // Load the UXML and USS
        var uxmlPath = "Assets/Editor/VRHapticEditor.uxml";
        var uxmlAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);

        if (uxmlAsset == null)
        {
            Debug.LogError("UXML file not found. Please verify the path.");
            return;
        }

        var ussPath = "Assets/Editor/VRHapticEditor.uss";
        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);

        // Set the rootVisualElement directly
        rootVisualElement.Clear();
        uxmlAsset.CloneTree(rootVisualElement);

        // Apply styling
        if (styleSheet != null)
        {
            rootVisualElement.styleSheets.Add(styleSheet);
        }

        // Set up the graph view
        var graphContainer = rootVisualElement.Q<VisualElement>("graphViewContainer");
        _graphView = new HapticsRelationshipGraphView();
        _graphView.style.flexGrow = 1;
        graphContainer.Add(_graphView);

        // Set up the inspector
        _inspectorContainer = rootVisualElement.Q<VisualElement>("inspectorContainer");
        _inspectorContent = rootVisualElement.Q<VisualElement>("inspectorContent");

        var existingLabels = _inspectorContainer.Query<Label>().ToList();
        foreach (var label in existingLabels)
        {
            if (label.text == "Inspector")
            {
                _inspectorContainer.Remove(label);
            }
        }

        // Set up button handlers
        //var scanButton = rootVisualElement.Q<Button>("scanSceneButton");
        var exportButton = rootVisualElement.Q<Button>("exportDataButton");
        var inspectorToggleButton = rootVisualElement.Q<Button>("inspectorToggleButton");

        // Add a save button to the toolbar
        var saveButton = new Button(SaveGraph);
        saveButton.text = "Save";
        saveButton.name = "saveGraphButton";
        saveButton.AddToClassList("toolbar-button");

        // Insert the save button before the export button
        var toolbar = rootVisualElement.Q<VisualElement>("toolbar");
        toolbar.Insert(toolbar.IndexOf(exportButton), saveButton);

        //scanButton.clicked += OnScanSceneClicked;
        exportButton.clicked += OnExportClicked;
        inspectorToggleButton.clicked += ToggleInspector;

        // Set up drag and drop
        _graphView.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
        _graphView.RegisterCallback<DragPerformEvent>(OnDragPerform);

        // Initialize the inspector
        UpdateInspector(null);

        // Set initial inspector visibility
        SetInspectorVisibility(_showInspector);

        // Start checking for selection changes
        EditorApplication.update += CheckSelectionChange;

        // Register for engagement level changes
        HapticNode.OnEngagementLevelChanged += OnNodeEngagementLevelChanged;

        // Register for graph changes
        HapticsRelationshipGraphView.OnGraphChanged += OnGraphChanged;

        // Register for keyboard events
        rootVisualElement.RegisterCallback<KeyDownEvent>(OnKeyDown);
    }

    private void OnDisable()
    {
        if (_graphView != null)
        {
            _graphView.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated);
            _graphView.UnregisterCallback<DragPerformEvent>(OnDragPerform);
        }

        EditorApplication.update -= CheckSelectionChange;

        // Unregister from engagement level changes
        HapticNode.OnEngagementLevelChanged -= OnNodeEngagementLevelChanged;

        // Unregister from graph changes
        HapticsRelationshipGraphView.OnGraphChanged -= OnGraphChanged;

        // Unregister from keyboard events
        rootVisualElement.UnregisterCallback<KeyDownEvent>(OnKeyDown);

        // Auto-save when closing the window if there are unsaved changes
        if (_hasUnsavedChanges && _currentGraph != null)
        {
            SaveGraph();
        }
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        // Check for Ctrl+S or Cmd+S
        if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.S)
        {
            SaveGraph();
            evt.StopPropagation();
        }
    }

    private void OnGraphChanged()
    {
        // Mark that we have unsaved changes
        _hasUnsavedChanges = true;

        // Update the window title to show unsaved changes
        if (_currentGraph != null)
        {
            titleContent = new GUIContent($"Haptic Annotation* ({_currentGraph.name})", titleContent.image);
        }

        // Update the inspector to reflect the changes in the graph
        UpdateInspector(null);
    }

    private void OnNodeEngagementLevelChanged(HapticNode node, int newLevel)
    {
        // Always update the inspector to reflect the new engagement levels
        // This ensures the scroll containers are properly updated
        UpdateInspector(null);
    }

    private void CheckSelectionChange()
    {
        if (_graphView == null) return;

        // Check if selection has changed
        var currentSelection = _graphView.selection.ToList();
        bool selectionChanged = false;

        if (currentSelection.Count != _lastSelection.Count)
        {
            selectionChanged = true;
        }
        else
        {
            // Check if any elements are different
            for (int i = 0; i < currentSelection.Count; i++)
            {
                if (!_lastSelection.Contains(currentSelection[i]))
                {
                    selectionChanged = true;
                    break;
                }
            }
        }

        if (selectionChanged)
        {
            _lastSelection = currentSelection;
            UpdateInspectorBasedOnSelection();
        }
    }

    private void UpdateInspectorBasedOnSelection()
    {
        var selectedNodes = _graphView.selection.OfType<HapticNode>().ToList();

        if (selectedNodes.Count == 1)
        {
            // Show node inspector
            UpdateInspector(selectedNodes[0]);
        }
        else
        {
            // Show graph inspector
            UpdateInspector(null);
        }
    }

    private void UpdateInspector(HapticNode selectedNode)
    {
        // Clear the existing content
        _inspectorContent.Clear();

        // Create a ScrollView to make the entire inspector content scrollable
        var scrollView = new ScrollView();
        scrollView.style.flexGrow = 1;
        scrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;

        // Add the ScrollView to the inspector content
        _inspectorContent.Add(scrollView);

        // Create a container for all the inspector content
        var contentContainer = new VisualElement();
        contentContainer.style.paddingRight = 10; // Add some padding for the scrollbar
        contentContainer.style.paddingLeft = 15; // Add left padding for better spacing
        contentContainer.style.paddingTop = 10; // Add top padding for better spacing

        // Add the container to the ScrollView
        scrollView.Add(contentContainer);

        if (selectedNode == null)
        {
            // Create graph-level inspector
            // Get the current scene name
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(sceneName))
                sceneName = "Untitled Scene";

            // Create a label for the scene name, styled like the node title
            var sceneNameLabel = new Label(sceneName);
            sceneNameLabel.AddToClassList("inspector-section-title");
            sceneNameLabel.style.fontSize = 20;
            sceneNameLabel.style.marginBottom = 25;
            contentContainer.Add(sceneNameLabel);

            var summaryLabel = new Label("Description");
            summaryLabel.AddToClassList("inspector-field-label");

            var summaryField = new TextField
            {
                multiline = true,
                value = _graphSummary
            };
            //summaryField.style.height = 80;
            summaryField.AddToClassList("inspector-field");
            _ = summaryField.RegisterValueChangedCallback(evt =>
            {
                _graphSummary = evt.newValue;
            });

            // Add elements to the content container instead of directly to _inspectorContent
            contentContainer.Add(summaryLabel);
            contentContainer.Add(summaryField);

            // Add engagement level lists to the content container
            AddEngagementLevelLists(contentContainer);
        }
        else
        {
            // Truncate the title for display in the inspector
            string displayTitle = TruncateInspectorTitle(selectedNode.title);

            var nodeNameLabel = new Label(displayTitle);
            nodeNameLabel.AddToClassList("inspector-section-title");
            nodeNameLabel.style.fontSize = 20;
            nodeNameLabel.style.marginBottom = 25;

            // Add tooltip to show the full name on hover
            nodeNameLabel.tooltip = selectedNode.AssociatedObject.name;

            // Add to the content container
            contentContainer.Add(nodeNameLabel);

            // Create a container for the "Is Direct Contacted" checkbox and label
            var directContactContainer = new VisualElement();
            directContactContainer.style.flexDirection = FlexDirection.Row;
            directContactContainer.style.alignItems = Align.Center;
            directContactContainer.style.marginBottom = 10;

            // Add "Is Direct Contacted" label
            var directContactLabel = new Label("Is Direct Contacted");
            directContactLabel.AddToClassList("inspector-field-label");
            directContactLabel.style.marginRight = 10;
            directContactLabel.style.marginBottom = 0; // Override default margin
            directContactLabel.style.flexGrow = 1;

            // Add checkbox
            var directContactToggle = new Toggle();
            directContactToggle.value = selectedNode.IsDirectContacted;
            directContactToggle.RegisterValueChangedCallback(evt =>
            {
                selectedNode.IsDirectContacted = evt.newValue;
            });

            // Add label and toggle to the container
            directContactContainer.Add(directContactLabel);
            directContactContainer.Add(directContactToggle);

            // Add the container to the main content
            contentContainer.Add(directContactContainer);

            // Add "Description" text field
            var descriptionLabel = new Label("Description");
            descriptionLabel.AddToClassList("inspector-field-label");
            contentContainer.Add(descriptionLabel);

            var descriptionField = new TextField();
            descriptionField.multiline = true;
            descriptionField.value = selectedNode.Description;
            //descriptionField.style.height = 60;
            descriptionField.style.marginBottom = 15;
            descriptionField.AddToClassList("inspector-field");
            descriptionField.RegisterValueChangedCallback(evt =>
            {
                selectedNode.Description = evt.newValue;
            });
            contentContainer.Add(descriptionField);

            // Add a title for the property TreeViews
            var propertyHighlightTitle = new Label("Property Highlight");
            propertyHighlightTitle.AddToClassList("inspector-section-title");
            propertyHighlightTitle.style.marginBottom = 10;
            contentContainer.Add(propertyHighlightTitle);

            // Add the six TreeViews with text fields and sliders
            AddHapticPropertyTreeView(contentContainer, "Inertia", selectedNode,
                value => selectedNode.Inertia = value, () => selectedNode.Inertia,
                value => selectedNode.InertiaValue = value, () => selectedNode.InertiaValue);

            AddHapticPropertyTreeView(contentContainer, "Interactivity", selectedNode,
                value => selectedNode.Interactivity = value, () => selectedNode.Interactivity,
                value => selectedNode.InteractivityValue = value, () => selectedNode.InteractivityValue);

            AddHapticPropertyTreeView(contentContainer, "Outline", selectedNode,
                value => selectedNode.Outline = value, () => selectedNode.Outline,
                value => selectedNode.OutlineValue = value, () => selectedNode.OutlineValue);

            AddHapticPropertyTreeView(contentContainer, "Texture", selectedNode,
                value => selectedNode.Texture = value, () => selectedNode.Texture,
                value => selectedNode.TextureValue = value, () => selectedNode.TextureValue);

            AddHapticPropertyTreeView(contentContainer, "Hardness", selectedNode,
                value => selectedNode.Hardness = value, () => selectedNode.Hardness,
                value => selectedNode.HardnessValue = value, () => selectedNode.HardnessValue);

            AddHapticPropertyTreeView(contentContainer, "Temperature", selectedNode,
                value => selectedNode.Temperature = value, () => selectedNode.Temperature,
                value => selectedNode.TemperatureValue = value, () => selectedNode.TemperatureValue);
        }
    }

    // Helper method to truncate long titles in the inspector
    private string TruncateInspectorTitle(string originalTitle)
    {
        const int maxLength = 20; // Maximum characters to display in inspector

        if (originalTitle.Length <= maxLength)
            return originalTitle;

        // Truncate and add ellipsis
        return originalTitle.Substring(0, maxLength - 3) + "...";
    }

    private void AddHapticPropertyTreeView(VisualElement container, string propertyName,
        HapticNode node, Action<string> setter, Func<string> getter,
        Action<float> sliderSetter, Func<float> sliderGetter)
    {
        // Create a container with relative positioning to hold everything
        var propertyContainer = new VisualElement();
        propertyContainer.style.position = Position.Relative;

        // Create a Foldout (acts like a TreeView item)
        var foldout = new Foldout();
        foldout.text = propertyName;

        // Use the saved state if available, otherwise default to closed
        bool isOpen = node.PropertyFoldoutStates.ContainsKey(propertyName) ?
            node.PropertyFoldoutStates[propertyName] : false;
        foldout.value = isOpen;

        foldout.AddToClassList("haptic-property-foldout");

        // Register callback to save the foldout state when it changes
        foldout.RegisterValueChangedCallback(evt =>
        {
            node.PropertyFoldoutStates[propertyName] = evt.newValue;
        });

        // Create the text field
        var textField = new TextField();
        textField.multiline = true;
        textField.value = getter();
        //textField.style.height = 60;
        textField.AddToClassList("haptic-property-field");

        // Register callback to update the node property when the text changes
        textField.RegisterValueChangedCallback(evt =>
        {
            setter(evt.newValue);
        });

        // Add the text field to the foldout
        foldout.Add(textField);

        // Add the foldout to the property container
        propertyContainer.Add(foldout);

        // Create the slider with absolute positioning
        var slider = new Slider(0, 1);
        slider.value = sliderGetter();
        slider.AddToClassList("haptic-property-slider");

        // Style the slider for absolute positioning
        slider.style.position = Position.Absolute;
        slider.style.width = 80;
        slider.style.right = 40; // Adjusted to make room for value field
        slider.style.top = 10; // Position it vertically centered in the header

        // Create a value field to display and input the slider value
        var valueField = new FloatField();
        valueField.value = Mathf.Round(slider.value * 10) / 10f; // Round to nearest 0.1
        valueField.AddToClassList("slider-value-field");
        valueField.style.position = Position.Absolute;
        valueField.style.right = 5;
        valueField.style.top = 8; // Slightly adjusted to align with slider
        valueField.style.width = 30;

        // Remove the label from the float field
        var labelElement = valueField.Q<Label>();
        if (labelElement != null)
        {
            labelElement.style.display = DisplayStyle.None;
        }

        // Register callback for slider value changes
        slider.RegisterValueChangedCallback(evt =>
        {
            // Round to nearest 0.1
            float roundedValue = Mathf.Round(evt.newValue * 10) / 10f;

            // Update the slider value if it's different from the rounded value
            if (Mathf.Abs(evt.newValue - roundedValue) > 0.001f)
            {
                slider.SetValueWithoutNotify(roundedValue);
            }

            // Update the value field without triggering its change event
            valueField.SetValueWithoutNotify(roundedValue);

            // Update the node property
            sliderSetter(roundedValue);
        });

        // Register callback for value field changes
        valueField.RegisterValueChangedCallback(evt =>
        {
            // Clamp the value between 0 and 1
            float clampedValue = Mathf.Clamp(evt.newValue, 0f, 1f);

            // Round to nearest 0.1
            float roundedValue = Mathf.Round(clampedValue * 10) / 10f;

            // Update the field if the value was clamped or rounded
            if (Mathf.Abs(evt.newValue - roundedValue) > 0.001f)
            {
                valueField.SetValueWithoutNotify(roundedValue);
            }

            // Update the slider without triggering its change event
            slider.SetValueWithoutNotify(roundedValue);

            // Update the node property
            sliderSetter(roundedValue);
        });

        // Ensure the slider and field are on top layer to prevent foldout interference
        slider.pickingMode = PickingMode.Position;
        valueField.pickingMode = PickingMode.Position;

        // Add the slider and value field to the property container
        propertyContainer.Add(slider);
        propertyContainer.Add(valueField);

        // Add the property container to the main container
        container.Add(propertyContainer);
    }

    // Update the AddEngagementLevelLists method to accept a parent container
    private void AddEngagementLevelLists(VisualElement container)
    {
        // Get all nodes from the graph view
        var allNodes = _graphView.GetNodes();

        // Group nodes by engagement level
        var highEngagementNodes = allNodes.Where(n => n.EngagementLevel == 2).ToList();
        var mediumEngagementNodes = allNodes.Where(n => n.EngagementLevel == 1).ToList();
        var lowEngagementNodes = allNodes.Where(n => n.EngagementLevel == 0).ToList();

        // Update our ordered lists, keeping existing order for nodes that are still present
        UpdateOrderedList(_orderedHighEngagementNodes, highEngagementNodes);
        UpdateOrderedList(_orderedMediumEngagementNodes, mediumEngagementNodes);
        UpdateOrderedList(_orderedLowEngagementNodes, lowEngagementNodes);

        // Create a container for all lists
        var listsContainer = new VisualElement();
        listsContainer.style.marginTop = 1;

        // Add a title for the engagement lists section
        var proxyPriorityTitle = new Label("Proxy Priority");
        proxyPriorityTitle.AddToClassList("inspector-section-title");
        proxyPriorityTitle.style.marginBottom = 1;
        listsContainer.Add(proxyPriorityTitle);

        // Add High Engagement list
        AddReorderableList(listsContainer, "High Engagement", _orderedHighEngagementNodes);

        // Add Medium Engagement list
        AddReorderableList(listsContainer, "Medium Engagement", _orderedMediumEngagementNodes);

        // Add Low Engagement list
        AddReorderableList(listsContainer, "Low Engagement", _orderedLowEngagementNodes);

        // Add the lists container to the provided parent container
        container.Add(listsContainer);
    }

    private void UpdateOrderedList(List<HapticNode> orderedList, List<HapticNode> currentNodes)
    {
        // Create a new list to hold the updated order
        var updatedList = new List<HapticNode>();

        // First, add all nodes that are already in the ordered list, in their current order
        foreach (var node in orderedList)
        {
            if (currentNodes.Contains(node))
            {
                updatedList.Add(node);
            }
        }

        // Then add any new nodes that aren't already in the ordered list
        foreach (var node in currentNodes)
        {
            if (!updatedList.Contains(node))
            {
                updatedList.Add(node);
            }
        }

        // Clear and repopulate the original list to maintain the reference
        orderedList.Clear();
        orderedList.AddRange(updatedList);
    }

    private void AddReorderableList(VisualElement container, string title, List<HapticNode> nodes)
    {
        // Create a foldout for the list
        var foldout = new Foldout();
        foldout.text = title;
        foldout.value = true; // Expanded by default
        foldout.AddToClassList("engagement-foldout");

        // Create a scrollable container if needed
        var scrollContainer = new ScrollView();
        scrollContainer.mode = ScrollViewMode.Vertical;
        scrollContainer.verticalScrollerVisibility = ScrollerVisibility.Auto;

        // Add the appropriate class
        if (nodes.Count > 5)
        {
            scrollContainer.AddToClassList("scrollable-list-container");
        }
        else
        {
            // Still add some styling for consistency
            scrollContainer.AddToClassList("reorderable-list-container");
        }

        // Add each node to the list in the specified order
        foreach (var node in nodes)
        {
            var itemContainer = CreateReorderableListItem(node);
            scrollContainer.Add(itemContainer);
        }

        foldout.Add(scrollContainer);
        container.Add(foldout);
    }

    private VisualElement CreateReorderableListItem(HapticNode node)
    {
        // Create the container for the list item
        var itemContainer = new VisualElement();
        itemContainer.AddToClassList("reorderable-list-item");

        // Create the equals sign
        var equalsSign = new Label("=");
        equalsSign.AddToClassList("equals-sign");

        // Create the label for the node name
        var nodeLabel = new Label(node.title);
        nodeLabel.AddToClassList("node-label");
        nodeLabel.tooltip = node.tooltip; // Add tooltip for long names

        // Add elements to the item container
        itemContainer.Add(equalsSign);
        itemContainer.Add(nodeLabel);

        // Make the item draggable
        itemContainer.userData = node; // Store the node reference for drag operations

        // Add drag functionality
        SetupDragAndDrop(itemContainer);

        // Add click handler to focus on the node in the graph without selecting it
        itemContainer.RegisterCallback<ClickEvent>(evt =>
        {
            // Focus on the node in the graph view without selecting it
            _graphView.FrameAndFocusNode(node, false);

            // Prevent event propagation to avoid any default selection behavior
            evt.StopPropagation();
        });

        return itemContainer;
    }

    private void SetupDragAndDrop(VisualElement itemContainer)
    {
        // Store original data for drag operation
        Vector2 mouseStartPosition = Vector2.zero;
        VisualElement placeholder = null;
        VisualElement dragGhost = null;
        HapticNode draggedNode = itemContainer.userData as HapticNode;
        VisualElement parent = null;
        bool isDragging = false;

        // Make the item draggable
        itemContainer.RegisterCallback<MouseDownEvent>(evt =>
        {
            // Start drag operation
            itemContainer.CaptureMouse();
            mouseStartPosition = evt.mousePosition;

            // We'll wait for mouse move to actually start dragging
            evt.StopPropagation();
        });

        itemContainer.RegisterCallback<MouseMoveEvent>(evt =>
        {
            if (itemContainer.HasMouseCapture())
            {
                // Check if we've moved enough to start dragging
                float dragThreshold = 5f; // pixels
                if (!isDragging && Vector2.Distance(mouseStartPosition, evt.mousePosition) > dragThreshold)
                {
                    // Start the actual drag operation
                    isDragging = true;

                    // Get the parent
                    parent = itemContainer.parent;
                    if (parent == null) return;

                    // Create a placeholder with the same size
                    placeholder = new VisualElement();
                    placeholder.AddToClassList("reorderable-list-placeholder");
                    placeholder.style.height = itemContainer.layout.height;

                    // Hide the original item
                    itemContainer.style.visibility = Visibility.Hidden;

                    // Create a visual clone (ghost) for dragging
                    dragGhost = new VisualElement();
                    dragGhost.AddToClassList("reorderable-list-item");
                    dragGhost.AddToClassList("dragging");

                    // Set the width to match the original item
                    dragGhost.style.width = itemContainer.layout.width;
                    dragGhost.style.height = itemContainer.layout.height;
                    dragGhost.style.position = Position.Absolute;

                    // Copy the content from the original item
                    var equalsSign = new Label("=");
                    equalsSign.AddToClassList("equals-sign");

                    var nodeLabel = new Label(((HapticNode)itemContainer.userData).title);
                    nodeLabel.AddToClassList("node-label");

                    // Add the elements to the ghost in the same order
                    dragGhost.Add(equalsSign);
                    dragGhost.Add(nodeLabel);

                    // Add the ghost to the root visual element
                    var window = EditorWindow.focusedWindow;
                    if (window != null)
                    {
                        window.rootVisualElement.Add(dragGhost);
                    }
                    else
                    {
                        // Fallback to the panel's root
                        itemContainer.panel.visualTree.Add(dragGhost);
                    }

                    // Position the ghost initially
                    Vector2 mousePos = evt.mousePosition;
                    dragGhost.style.left = mousePos.x - 15;
                    dragGhost.style.top = mousePos.y - (itemContainer.layout.height / 2);
                }

                if (isDragging && dragGhost != null && parent != null)
                {
                    // Position the ghost at the mouse position
                    Vector2 mousePos = evt.mousePosition;

                    // Position the ghost directly under the cursor
                    dragGhost.style.left = mousePos.x - 15; // Offset to align with cursor
                    dragGhost.style.top = mousePos.y - (dragGhost.layout.height / 2);

                    // Find all siblings (excluding the dragged item)
                    var siblings = parent.Children().Where(c => c != itemContainer && c != placeholder).ToList();

                    // Find the closest sibling to insert the placeholder
                    int targetIndex = -1;
                    float minDistance = float.MaxValue;

                    for (int i = 0; i < siblings.Count; i++)
                    {
                        var sibling = siblings[i];
                        var siblingRect = sibling.worldBound;
                        var siblingCenter = siblingRect.center.y;

                        // Calculate distance to the center of this sibling
                        float distance = Mathf.Abs(mousePos.y - siblingCenter);

                        if (distance < minDistance)
                        {
                            minDistance = distance;

                            // Determine if we should insert before or after this sibling
                            if (mousePos.y < siblingCenter)
                                targetIndex = parent.IndexOf(sibling);
                            else
                                targetIndex = parent.IndexOf(sibling) + 1;
                        }
                    }

                    // If we have no siblings, just add at the beginning
                    if (siblings.Count == 0)
                    {
                        targetIndex = 0;
                    }

                    // If the placeholder doesn't exist yet, add it
                    if (placeholder.parent == null)
                    {
                        if (targetIndex >= 0)
                        {
                            if (targetIndex >= parent.childCount)
                                parent.Add(placeholder);
                            else
                                parent.Insert(targetIndex, placeholder);
                        }
                    }
                    // If the placeholder exists and needs to move
                    else if (targetIndex >= 0 && parent.IndexOf(placeholder) != targetIndex)
                    {
                        // Move the placeholder to the new position
                        parent.Remove(placeholder);

                        if (targetIndex >= parent.childCount)
                            parent.Add(placeholder);
                        else
                            parent.Insert(targetIndex, placeholder);
                    }
                }
            }
        });

        itemContainer.RegisterCallback<MouseUpEvent>(evt =>
        {
            if (itemContainer.HasMouseCapture())
            {
                // End drag operation
                itemContainer.ReleaseMouse();

                if (isDragging && parent != null && placeholder != null && placeholder.parent != null)
                {
                    // Remove the drag ghost
                    if (dragGhost != null && dragGhost.parent != null)
                    {
                        dragGhost.parent.Remove(dragGhost);
                        dragGhost = null;
                    }

                    // Get the placeholder position
                    int placeholderIndex = parent.IndexOf(placeholder);

                    // Remove the placeholder
                    parent.Remove(placeholder);
                    placeholder = null;

                    // Make the original item visible again
                    itemContainer.style.visibility = Visibility.Visible;

                    // Get the current index of the item
                    int currentIndex = parent.IndexOf(itemContainer);

                    // Only move if the position has changed
                    if (currentIndex != placeholderIndex)
                    {
                        // Remove the item from its current position
                        parent.Remove(itemContainer);

                        // Adjust the target index if needed
                        // This is the key fix for the ordering issue
                        int targetIndex = placeholderIndex;
                        if (currentIndex < placeholderIndex)
                        {
                            targetIndex--;
                        }

                        // Insert at the adjusted target position
                        if (targetIndex >= parent.childCount)
                            parent.Add(itemContainer);
                        else
                            parent.Insert(targetIndex, itemContainer);

                        // Update the ordered lists based on the new order
                        UpdateOrderedListsFromUI();
                    }
                }

                // Reset state
                isDragging = false;
                parent = null;
            }
        });
    }

    // Method to update our ordered lists based on the current UI order
    private void UpdateOrderedListsFromUI()
    {
        // Find all ScrollView containers in the inspector
        var scrollContainers = _inspectorContent.Query<ScrollView>().ToList();

        // We need at least 4 ScrollView containers (main + 3 engagement levels)
        if (scrollContainers.Count >= 4)
        {
            // The first ScrollView is the main container, so we skip it
            var highEngagementContainer = scrollContainers[1];
            var mediumEngagementContainer = scrollContainers[2];
            var lowEngagementContainer = scrollContainers[3];

            // Temporary lists to hold the new order
            var newHighEngagementOrder = new List<HapticNode>();
            var newMediumEngagementOrder = new List<HapticNode>();
            var newLowEngagementOrder = new List<HapticNode>();

            // Update high engagement nodes
            foreach (var child in highEngagementContainer.Children())
            {
                if (child.userData is HapticNode node)
                {
                    newHighEngagementOrder.Add(node);
                }
            }

            // Update medium engagement nodes
            foreach (var child in mediumEngagementContainer.Children())
            {
                if (child.userData is HapticNode node)
                {
                    newMediumEngagementOrder.Add(node);
                }
            }

            // Update low engagement nodes
            foreach (var child in lowEngagementContainer.Children())
            {
                if (child.userData is HapticNode node)
                {
                    newLowEngagementOrder.Add(node);
                }
            }

            // Only update if we found nodes
            if (newHighEngagementOrder.Count > 0)
            {
                _orderedHighEngagementNodes.Clear();
                _orderedHighEngagementNodes.AddRange(newHighEngagementOrder);
            }

            if (newMediumEngagementOrder.Count > 0)
            {
                _orderedMediumEngagementNodes.Clear();
                _orderedMediumEngagementNodes.AddRange(newMediumEngagementOrder);
            }

            if (newLowEngagementOrder.Count > 0)
            {
                _orderedLowEngagementNodes.Clear();
                _orderedLowEngagementNodes.AddRange(newLowEngagementOrder);
            }
        }
    }

    private void ToggleInspector()
    {
        _showInspector = !_showInspector;
        SetInspectorVisibility(_showInspector);
    }

    private void SetInspectorVisibility(bool visible)
    {
        _inspectorContainer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnDragUpdated(DragUpdatedEvent evt)
    {
        // We only set the visual mode if we detect at least one GameObject
        bool containsGameObject = false;
        foreach (var obj in DragAndDrop.objectReferences)
        {
            if (obj is GameObject)
            {
                containsGameObject = true;
                break;
            }
        }

        if (containsGameObject)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }
    }

    private void OnDragPerform(DragPerformEvent evt)
    {
        DragAndDrop.AcceptDrag();

        // Convert mouse position to GraphView coordinates
        Vector2 mousePosition = _graphView.ChangeCoordinatesTo(
            _graphView.contentViewContainer, evt.localMousePosition);

        foreach (var obj in DragAndDrop.objectReferences)
        {
            GameObject go = obj as GameObject;
            if (go != null)
            {
                // Create a node at the drop position
                _graphView.AddGameObjectNode(go, mousePosition);
                // Offset subsequent nodes slightly to avoid overlap
                mousePosition += new Vector2(30, 30);
            }
        }

        // Update the inspector after adding nodes
        UpdateInspectorBasedOnSelection();
    }

    //private void OnScanSceneClicked()
    //{
    //    // Example scanning of scene objects
    //    GameObject[] sceneObjects = GameObject.FindObjectsOfType<GameObject>();

    //    // Clear current graph
    //    _graphView.ClearGraph();

    //    // Add nodes in a basic layout
    //    Vector2 position = new Vector2(100, 100);
    //    foreach (var go in sceneObjects)
    //    {
    //        if (!go.activeInHierarchy) continue;
    //        // You could filter further, e.g., only "VR Relevance" objects

    //        _graphView.AddGameObjectNode(go, position);
    //        position += new Vector2(250, 0);

    //        // Start a new row if we've gone too far right
    //        if (position.x > 1000)
    //        {
    //            position = new Vector2(100, position.y + 300);
    //        }
    //    }

    //    // Update the inspector after scanning
    //    UpdateInspectorBasedOnSelection();
    //}

    private void OnExportClicked()
    {
        // Create the Snapshot directory if it doesn't exist
        string snapshotDir = "Assets/StreamingAssets/Export";
        if (!System.IO.Directory.Exists(snapshotDir))
        {
            System.IO.Directory.CreateDirectory(snapshotDir);
        }
        else
        {
            string[] files = System.IO.Directory.GetFiles(snapshotDir);
            foreach (string file in files)
            {
                System.IO.File.Delete(file);
            }

            // Also delete any subdirectories
            string[] subdirectories = System.IO.Directory.GetDirectories(snapshotDir);
            foreach (string subdirectory in subdirectories)
            {
                System.IO.Directory.Delete(subdirectory, true);
            }
        }

        // Create a list to track all exported files for the manifest
        List<string> exportedFiles = new List<string>();

        // Collect all annotation data from the graph
        var exportData = _graphView.CollectAnnotationData();

        // Add summary to the export data
        exportData.summary = _graphSummary;

        // Add ordered engagement level lists to the export data
        foreach (var node in _orderedHighEngagementNodes)
        {
            if (node.AssociatedObject != null)
            {
                exportData.highEngagementOrder.Add(node.AssociatedObject.name);
            }
        }

        foreach (var node in _orderedMediumEngagementNodes)
        {
            if (node.AssociatedObject != null)
            {
                exportData.mediumEngagementOrder.Add(node.AssociatedObject.name);
            }
        }

        foreach (var node in _orderedLowEngagementNodes)
        {
            if (node.AssociatedObject != null)
            {
                exportData.lowEngagementOrder.Add(node.AssociatedObject.name);
            }
        }

        // Get all nodes from the graph
        var nodes = _graphView.GetNodes();

        // Export snapshots for each node
        foreach (var node in nodes)
        {
            // Generate a unique filename based on the object name
            string safeName = System.Text.RegularExpressions.Regex.Replace(
                node.AssociatedObject.name,
                @"[^\w\.-]",
                "_");
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = $"GameObject_{safeName}_{timestamp}.png";
            string fullPath = System.IO.Path.Combine(snapshotDir, filename);

            // Create a preview of the GameObject
            if (node.AssociatedObject != null)
            {
                // Create a temporary editor for the GameObject
                Editor tempEditor = Editor.CreateEditor(node.AssociatedObject);

                // Create a render texture for the preview
                RenderTexture rt = new RenderTexture(512, 512, 24);
                RenderTexture.active = rt;

                // Clear the render texture
                GL.Clear(true, true, Color.gray);

                // Get the preview texture from the editor
                Texture2D previewTexture = tempEditor.RenderStaticPreview(
                    fullPath, null, 512, 512);

                if (previewTexture != null)
                {
                    // Save the preview texture
                    byte[] pngData = previewTexture.EncodeToPNG();
                    System.IO.File.WriteAllBytes(fullPath, pngData);

                    // Add to exported files list
                    exportedFiles.Add(filename);

                    //// Update the snapshot path in the export data
                    //foreach (var nodeAnnotation in exportData.nodeAnnotations)
                    //{
                    //    if (nodeAnnotation.objectName == node.AssociatedObject.name)
                    //    {
                    //        nodeAnnotation.snapshotPath = $"StreamingAssets/Export/{filename}";
                    //        break;
                    //    }
                    //}
                }

                // Clean up
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(tempEditor);
            }
        }

        // Export snapshots for each group
        var scopes = _graphView.GetScopes();
        for (int i = 0; i < scopes.Count; i++)
        {
            var scope = scopes[i];

            // Skip empty groups
            var nodesInGroup = scope.containedElements.OfType<HapticNode>().ToList();
            if (nodesInGroup.Count == 0)
                continue;

            // Generate a unique filename for the group snapshot
            string groupName = !string.IsNullOrEmpty(scope.title) ? scope.title : $"Group_{i + 1}";
            string safeGroupName = System.Text.RegularExpressions.Regex.Replace(
                groupName,
                @"[^\w\.-]",
                "_");
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = $"Arrangement_{safeGroupName}_{timestamp}.png";
            string fullPath = System.IO.Path.Combine(snapshotDir, filename);

            // Take a screenshot of the group arrangement
            CaptureGroupArrangement(scope, fullPath);

            // Add to exported files list if the file was created
            if (System.IO.File.Exists(fullPath))
            {
                exportedFiles.Add(filename);
            }

            // Find the corresponding group in the export data
            foreach (var groupRecord in exportData.groups)
            {
                if (groupRecord.title == scope.title)
                {
                    //// Add the main snapshot path to the group record
                    //groupRecord.arrangementSnapshotPath = $"StreamingAssets/Export/{filename}";

                    // Add additional angle paths if they exist
                    groupRecord.additionalViewAngles = new List<string>();

                    // Check for additional angle snapshots
                    string baseFilePath = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(fullPath),
                        System.IO.Path.GetFileNameWithoutExtension(fullPath));
                    string extension = System.IO.Path.GetExtension(fullPath);

                    for (int angle = 0; angle < 3; angle++) // Check for 3 angles
                    {
                        string angleFileName = $"{System.IO.Path.GetFileNameWithoutExtension(fullPath)}_angle{angle}{extension}";
                        string angleFilePath = System.IO.Path.Combine(snapshotDir, angleFileName);

                        if (System.IO.File.Exists(angleFilePath))
                        {
                            string relativeAnglePath = $"StreamingAssets/Export/{angleFileName}";
                            groupRecord.additionalViewAngles.Add(relativeAnglePath);

                            // Add to exported files list
                            exportedFiles.Add(angleFileName);
                        }
                    }

                    break;
                }
            }
        }

        // Serialize annotation data to JSON
        string jsonFileName = $"haptic_annotation_{System.DateTime.Now.ToString("yyyyMMdd_HHmmss")}.json";
        string jsonPath = System.IO.Path.Combine(snapshotDir, jsonFileName);
        string jsonResult = JsonUtility.ToJson(exportData, true);
        System.IO.File.WriteAllText(jsonPath, jsonResult);

        // Add to exported files list
        exportedFiles.Add(jsonFileName);

        Debug.Log($"Exported Haptic Annotation Data to {jsonPath}");

        // Create and save the manifest.json file
        ManifestData manifest = new ManifestData
        {
            files = exportedFiles
        };

        string manifestJson = JsonUtility.ToJson(manifest, true);
        string manifestPath = System.IO.Path.Combine(snapshotDir, "manifest.json");
        System.IO.File.WriteAllText(manifestPath, manifestJson);

        Debug.Log($"Created manifest.json with {exportedFiles.Count} files");

        // Refresh the asset database to show the new files
        AssetDatabase.Refresh();

        // Show a success message
        EditorUtility.DisplayDialog("Export Complete",
            $"Successfully exported {nodes.Count} node snapshots, {scopes.Count} group arrangements, annotation data, and manifest.json to {snapshotDir} folder.",
            "OK");
    }

    // Add this class to your script
    [Serializable]
    public class ManifestData
    {
        public List<string> files;
    }

    // Method to capture screenshots of a group arrangement from multiple angles
    private void CaptureGroupArrangement(HapticScope scope, string savePath)
    {
        // Get all nodes in the scope
        var nodesInGroup = scope.containedElements.OfType<HapticNode>().ToList();
        if (nodesInGroup.Count == 0)
            return;

        // Get the SceneView to capture the arrangement
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            Debug.LogWarning("No active SceneView found for capturing group arrangement.");
            return;
        }

        // Store the original camera settings
        Vector3 originalPosition = sceneView.camera.transform.position;
        Quaternion originalRotation = sceneView.camera.transform.rotation;
        float originalSize = sceneView.size;
        bool originalOrthographic = sceneView.orthographic;
        bool originalLockView = sceneView.isRotationLocked;

        // Store original object visibility states
        Dictionary<GameObject, bool> originalVisibility = new Dictionary<GameObject, bool>();

        try
        {
            // Temporarily hide all objects except those in the group
            HideObjectsNotInGroup(nodesInGroup, originalVisibility);

            // Calculate the bounds of all objects in the group
            Bounds groupBounds = CalculateGroupBounds(nodesInGroup);

            // ===== CONFIGURABLE PARAMETERS =====

            // Shot angle from horizontal (in degrees)
            // 90 = directly above, 45 = high angle shot, 30 = above shot, 0 = straight on
            float shotAngle = 40f;

            // Zoom factor - lower values = closer to objects (tighter framing)
            // 1.0 = standard distance, 0.8 = closer, 1.5 = farther away
            float zoomFactor = 0.9f;

            // Whether to capture multiple angles (0?, 120?, 240? around the center)
            bool captureMultipleAngles = true;

            // Number of angles to capture if captureMultipleAngles is true
            int numberOfAngles = 3;

            // Whether to use orthographic projection (true) or perspective (false)
            bool useOrthographic = false;

            // ===== END CONFIGURABLE PARAMETERS =====

            // Calculate base distance based on bounds size and zoom factor
            float distance = groupBounds.size.magnitude * zoomFactor;
            Vector3 center = groupBounds.center;

            // Convert shot angle to radians
            float shotAngleRad = shotAngle * Mathf.Deg2Rad;

            // Set the scene view size to fit the group
            sceneView.size = groupBounds.size.magnitude * 0.8f;
            sceneView.orthographic = useOrthographic;
            sceneView.isRotationLocked = true; // Lock the view to prevent accidental changes

            // If capturing multiple angles
            if (captureMultipleAngles)
            {
                // Calculate the base filename without extension
                string baseFilePath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(savePath),
                    System.IO.Path.GetFileNameWithoutExtension(savePath));
                string extension = System.IO.Path.GetExtension(savePath);

                // Capture from multiple angles
                for (int i = 0; i < numberOfAngles; i++)
                {
                    // Calculate the horizontal angle in radians
                    float horizontalAngle = (360f / numberOfAngles * i) * Mathf.Deg2Rad;

                    // Calculate camera position using spherical coordinates
                    float horizontalDistance = distance * Mathf.Cos(shotAngleRad);
                    float verticalDistance = distance * Mathf.Sin(shotAngleRad);

                    Vector3 cameraPosition = center + new Vector3(
                        horizontalDistance * Mathf.Sin(horizontalAngle),
                        verticalDistance,
                        horizontalDistance * Mathf.Cos(horizontalAngle)
                    );

                    // Position and orient the camera
                    sceneView.camera.transform.position = cameraPosition;
                    sceneView.camera.transform.LookAt(center);

                    // Force the scene view to update
                    sceneView.Repaint();
                    System.Threading.Thread.Sleep(300);

                    // Create the filename for this angle
                    string angleFilePath = $"{baseFilePath}_angle{i}{extension}";

                    // Capture the screenshot
                    CaptureScreenshot(sceneView, angleFilePath);

                    // If this is the first angle, also save it as the main screenshot
                    if (i == 0)
                    {
                        CaptureScreenshot(sceneView, savePath);
                    }
                }
            }
            else
            {
                // Calculate camera position for a single shot
                // Position for the specified angle shot
                float horizontalDistance = distance * Mathf.Cos(shotAngleRad);
                float verticalDistance = distance * Mathf.Sin(shotAngleRad);

                Vector3 cameraPosition = center + new Vector3(0, verticalDistance, -horizontalDistance);

                // Position and orient the camera
                sceneView.camera.transform.position = cameraPosition;
                sceneView.camera.transform.LookAt(center);

                // Force the scene view to update
                sceneView.Repaint();
                System.Threading.Thread.Sleep(300);

                // Capture the screenshot
                CaptureScreenshot(sceneView, savePath);
            }
        }
        finally
        {
            // Restore original object visibility
            RestoreObjectVisibility(originalVisibility);

            // Restore the original camera settings
            sceneView.camera.transform.position = originalPosition;
            sceneView.camera.transform.rotation = originalRotation;
            sceneView.size = originalSize;
            sceneView.orthographic = originalOrthographic;
            sceneView.isRotationLocked = originalLockView;
            sceneView.Repaint();
        }
    }

    // Helper method to capture a screenshot from the current scene view
    private void CaptureScreenshot(SceneView sceneView, string savePath)
    {
        // Capture the screenshot
        RenderTexture rt = new RenderTexture(1024, 1024, 24);
        sceneView.camera.targetTexture = rt;
        RenderTexture.active = rt;
        sceneView.camera.Render();

        // Read the pixels
        Texture2D screenshot = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
        screenshot.Apply();

        // Save the screenshot
        byte[] bytes = screenshot.EncodeToPNG();
        System.IO.File.WriteAllBytes(savePath, bytes);

        // Clean up
        RenderTexture.active = null;
        sceneView.camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(screenshot);
        UnityEngine.Object.DestroyImmediate(rt);
    }

    // Helper method to hide all objects except those in the group
    private void HideObjectsNotInGroup(List<HapticNode> nodesInGroup, Dictionary<GameObject, bool> originalVisibility)
    {
        // Get all GameObjects in the scene
        GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();

        // Create a list of GameObjects that are in the group
        List<GameObject> groupObjects = new List<GameObject>();
        foreach (var node in nodesInGroup)
        {
            if (node.AssociatedObject != null)
            {
                groupObjects.Add(node.AssociatedObject);

                // Also add all children
                foreach (Transform child in node.AssociatedObject.GetComponentsInChildren<Transform>())
                {
                    if (child.gameObject != node.AssociatedObject)
                    {
                        groupObjects.Add(child.gameObject);
                    }
                }
            }
        }

        // Hide all objects that are not in the group
        foreach (var obj in allObjects)
        {
            // Skip objects that are not visible in the hierarchy
            if (!obj.activeInHierarchy)
                continue;

            // Store the original visibility state
            originalVisibility[obj] = obj.activeSelf;

            // If the object is not in the group, hide it
            if (!groupObjects.Contains(obj))
            {
                obj.SetActive(false);
            }
        }
    }

    // Helper method to restore original object visibility
    private void RestoreObjectVisibility(Dictionary<GameObject, bool> originalVisibility)
    {
        foreach (var kvp in originalVisibility)
        {
            if (kvp.Key != null) // Check if the GameObject still exists
            {
                kvp.Key.SetActive(kvp.Value);
            }
        }
    }

    // Helper method to calculate the bounds of a group of nodes
    private Bounds CalculateGroupBounds(List<HapticNode> nodes)
    {
        if (nodes.Count == 0)
            return new Bounds(Vector3.zero, Vector3.zero);

        // Initialize bounds with the first node
        Bounds bounds = new Bounds(nodes[0].AssociatedObject.transform.position, Vector3.zero);

        // Expand bounds to include all nodes
        foreach (var node in nodes)
        {
            if (node.AssociatedObject != null)
            {
                // Include the renderer bounds if available
                Renderer renderer = node.AssociatedObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    bounds.Encapsulate(renderer.bounds);
                }
                else
                {
                    // Just use the transform position if no renderer
                    bounds.Encapsulate(node.AssociatedObject.transform.position);
                }

                // Include all child renderers
                foreach (Renderer childRenderer in node.AssociatedObject.GetComponentsInChildren<Renderer>())
                {
                    bounds.Encapsulate(childRenderer.bounds);
                }
            }
        }

        // Add some padding
        bounds.Expand(bounds.size.magnitude * 0.1f);

        return bounds;
    }

    public void LoadGraph(HapticAnnotationGraph graph)
    {
        if (graph == null) return;

        _currentGraph = graph;

        // Clear the current graph
        _graphView.ClearGraph();

        // Load the graph summary
        _graphSummary = graph.Summary;

        // If there's saved graph data, deserialize and restore it
        if (!string.IsNullOrEmpty(graph.GraphData))
        {
            DeserializeGraph(graph.GraphData);
        }

        // Update the inspector
        UpdateInspector(null);

        // Reset the unsaved changes flag
        _hasUnsavedChanges = false;

        // Update the window title to show the current graph
        titleContent = new GUIContent($"Haptic Annotation ({graph.name})", titleContent.image);
    }

    public void SaveGraph()
    {
        // If there's no current graph, create a new one
        if (_currentGraph == null)
        {
            // Create a save file dialog to choose where to save the new graph
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Haptic Annotation Graph",
                "New Haptic Annotation Graph",
                "asset",
                "Choose a location to save the new graph"
            );

            if (string.IsNullOrEmpty(path))
            {
                // User canceled the save dialog
                return;
            }

            // Create a new graph asset
            _currentGraph = ScriptableObject.CreateInstance<HapticAnnotationGraph>();

            // Save the asset to the selected path
            AssetDatabase.CreateAsset(_currentGraph, path);
        }

        // Save the graph summary
        _currentGraph.Summary = _graphSummary;

        // Serialize the graph data
        _currentGraph.GraphData = SerializeGraph();

        // Update the referenced objects list
        UpdateReferencedObjects();

        // Mark the asset as dirty to ensure changes are saved
        EditorUtility.SetDirty(_currentGraph);
        AssetDatabase.SaveAssets();

        // Reset the unsaved changes flag
        _hasUnsavedChanges = false;

        // Update the window title to remove the asterisk
        titleContent = new GUIContent($"Haptic Annotation ({_currentGraph.name})", titleContent.image);

        // Show a brief notification
        ShowNotification(new GUIContent("Graph saved"));
    }

    private void UpdateReferencedObjects()
    {
        if (_currentGraph == null) return;

        // Clear the current list
        _currentGraph.ReferencedObjects.Clear();

        // Add all objects referenced in the graph
        var nodes = _graphView.GetNodes();
        foreach (var node in nodes)
        {
            if (node.AssociatedObject != null)
            {
                _currentGraph.AddReferencedObject(node.AssociatedObject);
            }
        }
    }

    private string SerializeGraph()
    {
        // Create a serializable representation of the graph
        var graphData = new SerializableGraphData();

        // Collect nodes
        var nodes = _graphView.GetNodes();

        // Dictionary to map nodes to their IDs
        var nodeIdMap = new Dictionary<HapticNode, string>();

        foreach (var node in nodes)
        {
            // Generate a unique ID for the node
            string nodeId = node.GetHashCode().ToString();
            nodeIdMap[node] = nodeId;

            // Count the number of output and input ports
            int outputPortCount = node.outputContainer.Query<Port>().ToList().Count;
            int inputPortCount = node.inputContainer.Query<Port>().ToList().Count;

            var nodeData = new SerializableNodeData
            {
                id = nodeId,
                objectName = node.AssociatedObject.name,
                objectPath = GetGameObjectPath(node.AssociatedObject),
                position = new SerializableVector2 { x = node.GetPosition().x, y = node.GetPosition().y },
                isDirectContacted = node.IsDirectContacted,
                description = node.Description,
                engagementLevel = node.EngagementLevel,
                outputPortCount = outputPortCount,
                inputPortCount = inputPortCount,

                // Haptic properties
                inertia = node.Inertia,
                inertiaValue = node.InertiaValue,
                interactivity = node.Interactivity,
                interactivityValue = node.InteractivityValue,
                outline = node.Outline,
                outlineValue = node.OutlineValue,
                texture = node.Texture,
                textureValue = node.TextureValue,
                hardness = node.Hardness,
                hardnessValue = node.HardnessValue,
                temperature = node.Temperature,
                temperatureValue = node.TemperatureValue
            };

            graphData.nodes.Add(nodeData);
        }

        // Save the ordered lists
        foreach (var node in _orderedHighEngagementNodes)
        {
            if (nodeIdMap.ContainsKey(node))
            {
                graphData.orderedHighEngagementNodeIds.Add(nodeIdMap[node]);
            }
        }

        foreach (var node in _orderedMediumEngagementNodes)
        {
            if (nodeIdMap.ContainsKey(node)) 
            {
                graphData.orderedMediumEngagementNodeIds.Add(nodeIdMap[node]);
            }
        }

        foreach (var node in _orderedLowEngagementNodes)
        {
            if (nodeIdMap.ContainsKey(node))
            {
                graphData.orderedLowEngagementNodeIds.Add(nodeIdMap[node]);
            }
        }

        // Collect edges
        var edges = _graphView.edges.ToList();
        foreach (var edge in edges)
        {
            var outputNode = edge.output?.node as HapticNode;
            var inputNode = edge.input?.node as HapticNode;

            if (outputNode != null && inputNode != null)
            {
                // Get the port indices
                int sourcePortIndex = outputNode.GetPortIndex(edge.output);
                int targetPortIndex = inputNode.GetPortIndex(edge.input);

                var edgeData = new SerializableEdgeData
                {
                    sourceNodeId = outputNode.GetHashCode().ToString(),
                    targetNodeId = inputNode.GetHashCode().ToString(),
                    annotationText = inputNode.GetAnnotationTextForPort(edge.input),
                    sourcePortIndex = sourcePortIndex,
                    targetPortIndex = targetPortIndex
                };

                graphData.edges.Add(edgeData);
            }
        }

        // Serialize scopes
        var scopes = _graphView.GetScopes();
        foreach (var scope in scopes)
        {
            var scopeData = new SerializableScopeData
            {
                title = scope.title,
                position = new SerializableVector2 { x = scope.GetPosition().x, y = scope.GetPosition().y },
                size = new SerializableVector2 { x = scope.GetPosition().width, y = scope.GetPosition().height },
                color = new SerializableColor
                {
                    r = scope.ScopeColor.r,
                    g = scope.ScopeColor.g,
                    b = scope.ScopeColor.b,
                    a = scope.ScopeColor.a
                }
            };

            // Store the IDs of nodes contained in this scope
            foreach (var element in scope.containedElements)
            {
                if (element is HapticNode node && nodeIdMap.ContainsKey(node))
                {
                    scopeData.containedNodeIds.Add(nodeIdMap[node]);
                }
            }

            graphData.scopes.Add(scopeData);
        }

        // Serialize to JSON
        return JsonUtility.ToJson(graphData);
    }

    private void DeserializeGraph(string jsonData)
    {
        if (string.IsNullOrEmpty(jsonData)) return;

        try
        {
            var graphData = JsonUtility.FromJson<SerializableGraphData>(jsonData);

            // Dictionary to map node IDs to actual nodes
            var nodeMap = new Dictionary<string, HapticNode>();

            // Create nodes
            foreach (var nodeData in graphData.nodes)
            {
                // Find the GameObject by path or name
                GameObject obj = FindGameObjectByPath(nodeData.objectPath);
                if (obj == null)
                {
                    // Try to find by name as fallback
                    obj = GameObject.Find(nodeData.objectName);
                }

                if (obj != null)
                {
                    // Create the node
                    var position = new Vector2(nodeData.position.x, nodeData.position.y);

                    // Create the node and get the reference
                    var node = _graphView.AddGameObjectNode(obj, position);

                    if (node != null)
                    {
                        // Set node properties
                        node.IsDirectContacted = nodeData.isDirectContacted;
                        node.Description = nodeData.description;
                        node.SetEngagementLevel(nodeData.engagementLevel);

                        // Set haptic properties
                        node.Inertia = nodeData.inertia;
                        node.InertiaValue = nodeData.inertiaValue;
                        node.Interactivity = nodeData.interactivity;
                        node.InteractivityValue = nodeData.interactivityValue;
                        node.Outline = nodeData.outline;
                        node.OutlineValue = nodeData.outlineValue;
                        node.Texture = nodeData.texture;
                        node.TextureValue = nodeData.textureValue;
                        node.Hardness = nodeData.hardness;
                        node.HardnessValue = nodeData.hardnessValue;
                        node.Temperature = nodeData.temperature;
                        node.TemperatureValue = nodeData.temperatureValue;

                        // Ensure the node has the correct number of ports
                        // Add additional output ports if needed
                        for (int i = 1; i < nodeData.outputPortCount; i++)
                        {
                            node.AddDirectPort();
                        }

                        // Add additional input ports if needed
                        for (int i = 1; i < nodeData.inputPortCount; i++)
                        {
                            node.AddToolMediatedPort();
                        }

                        // Add to the map
                        nodeMap[nodeData.id] = node;
                    }
                }
            }

            // Create edges with more precise port targeting
            foreach (var edgeData in graphData.edges)
            {
                if (nodeMap.TryGetValue(edgeData.sourceNodeId, out var sourceNode) &&
                    nodeMap.TryGetValue(edgeData.targetNodeId, out var targetNode))
                {
                    // Connect the nodes with specific port indices
                    ConnectNodesWithPortIndices(sourceNode, targetNode,
                        edgeData.sourcePortIndex, edgeData.targetPortIndex,
                        edgeData.annotationText);
                }
            }

            // Restore the ordered lists
            _orderedHighEngagementNodes.Clear();
            _orderedMediumEngagementNodes.Clear();
            _orderedLowEngagementNodes.Clear();

            // Restore scopes
            foreach (var scopeData in graphData.scopes)
            {
                // Create a new scope
                var position = new Rect(
                    scopeData.position.x,
                    scopeData.position.y,
                    scopeData.size.x,
                    scopeData.size.y
                );

                var scope = _graphView.CreateScope(position, scopeData.title);

                // Set the color
                scope.ScopeColor = new Color(
                    scopeData.color.r,
                    scopeData.color.g,
                    scopeData.color.b,
                    scopeData.color.a
                );

                // Add the nodes to the scope
                foreach (var nodeId in scopeData.containedNodeIds)
                {
                    if (nodeMap.TryGetValue(nodeId, out var node))
                    {
                        scope.AddElement(node);
                    }
                }
            }

            // Restore high engagement nodes order
            foreach (var nodeId in graphData.orderedHighEngagementNodeIds)
            {
                if (nodeMap.TryGetValue(nodeId, out var node) && node.EngagementLevel == 2)
                {
                    _orderedHighEngagementNodes.Add(node);
                }
            }

            // Restore medium engagement nodes order
            foreach (var nodeId in graphData.orderedMediumEngagementNodeIds)
            {
                if (nodeMap.TryGetValue(nodeId, out var node) && node.EngagementLevel == 1)
                {
                    _orderedMediumEngagementNodes.Add(node);
                }
            }

            // Restore low engagement nodes order
            foreach (var nodeId in graphData.orderedLowEngagementNodeIds)
            {
                if (nodeMap.TryGetValue(nodeId, out var node) && node.EngagementLevel == 0)
                {
                    _orderedLowEngagementNodes.Add(node);
                }
            }

            // Add any nodes that weren't in the ordered lists
            var allNodes = _graphView.GetNodes();

            // Add missing high engagement nodes
            foreach (var node in allNodes.Where(n => n.EngagementLevel == 2))
            {
                if (!_orderedHighEngagementNodes.Contains(node))
                {
                    _orderedHighEngagementNodes.Add(node);
                }
            }

            // Add missing medium engagement nodes
            foreach (var node in allNodes.Where(n => n.EngagementLevel == 1))
            {
                if (!_orderedMediumEngagementNodes.Contains(node))
                {
                    _orderedMediumEngagementNodes.Add(node);
                }
            }

            // Add missing low engagement nodes
            foreach (var node in allNodes.Where(n => n.EngagementLevel == 0))
            {
                if (!_orderedLowEngagementNodes.Contains(node))
                {
                    _orderedLowEngagementNodes.Add(node);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error deserializing graph: {e.Message}");
        }
    }

    // Helper method to set node engagement level
    //public void SetEngagementLevel(int level)
    //{
    //    // Validate the level (0-2)
    //    if (level < 0 || level > 2)
    //        return;

    //    // Find the radio buttons in the container
    //    var radioGroupContainer = mainContainer.Q(className: "radio-group-container");
    //    if (radioGroupContainer != null)
    //    {
    //        var radioButtons = radioGroupContainer.Query<Toggle>().ToList();

    //        // If we have the expected radio buttons
    //        if (radioButtons.Count >= 3)
    //        {
    //            // Set the appropriate radio button based on level
    //            // Assuming the order is: High (0), Medium (1), Low (2)
    //            int buttonIndex = level; // The index matches our level directly

    //            // Trigger the radio button click
    //            radioButtons[buttonIndex].SetValueWithoutNotify(true);

    //            // Deselect other radio buttons
    //            for (int i = 0; i < radioButtons.Count; i++)
    //            {
    //                if (i != buttonIndex)
    //                {
    //                    radioButtons[i].SetValueWithoutNotify(false);
    //                }
    //            }
    //        }
    //    }

    //    // Set the engagement level field directly
    //    _engagementLevel = level;
    //}

    // Helper method to connect nodes
    private void ConnectNodes(HapticNode sourceNode, HapticNode targetNode, string annotationText)
    {
        // Get all output ports from the source node
        var outputPorts = sourceNode.outputContainer.Query<Port>().ToList();

        // Get all input ports from the target node
        var inputPorts = targetNode.inputContainer.Query<Port>().ToList();

        // Find an available output port
        Port outputPort = null;
        foreach (var port in outputPorts)
        {
            // Use the first available port
            outputPort = port;
            break;
        }

        // Find an available input port
        Port inputPort = null;
        foreach (var port in inputPorts)
        {
            // Use the first available port that isn't connected
            if (!port.connected)
            {
                inputPort = port;
                break;
            }
        }

        // If we found both ports, connect them
        if (outputPort != null && inputPort != null)
        {
            // Create an edge
            var edge = new Edge
            {
                output = outputPort,
                input = inputPort
            };

            // Connect the ports
            outputPort.Connect(edge);
            inputPort.Connect(edge);

            // Add the edge to the graph
            _graphView.AddElement(edge);

            // Set the annotation text
            targetNode.SetAnnotationTextForPort(inputPort, annotationText);
        }
    }

    private string GetGameObjectPath(GameObject obj)
    {
        if (obj == null) return string.Empty;

        string path = obj.name;
        Transform parent = obj.transform.parent;

        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    private GameObject FindGameObjectByPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        return GameObject.Find(path);
    }

    // Add a method to connect nodes with specific port indices
    private void ConnectNodesWithPortIndices(HapticNode sourceNode, HapticNode targetNode,
        int sourcePortIndex, int targetPortIndex, string annotationText)
    {
        // Get all output ports from the source node
        var outputPorts = sourceNode.outputContainer.Query<Port>().ToList();

        // Get all input ports from the target node
        var inputPorts = targetNode.inputContainer.Query<Port>().ToList();

        // Check if the indices are valid
        if (sourcePortIndex < outputPorts.Count && targetPortIndex < inputPorts.Count)
        {
            var outputPort = outputPorts[sourcePortIndex];
            var inputPort = inputPorts[targetPortIndex];

            // Check if the ports are already connected
            bool alreadyConnected = false;
            foreach (var connection in inputPort.connections)
            {
                if (connection.output == outputPort)
                {
                    alreadyConnected = true;
                    break;
                }
            }

            // Only connect if not already connected
            if (!alreadyConnected && !inputPort.connected)
            {
                // Create an edge
                var edge = new Edge
                {
                    output = outputPort,
                    input = inputPort
                };

                // Connect the ports
                outputPort.Connect(edge);
                inputPort.Connect(edge);

                // Add the edge to the graph
                _graphView.AddElement(edge);

                // Set the annotation text
                targetNode.SetAnnotationTextForPort(inputPort, annotationText);
            }
        }
    }

    // Add these serializable classes for graph data
    [Serializable]
    private class SerializableGraphData
    {
        public List<SerializableNodeData> nodes = new List<SerializableNodeData>();
        public List<SerializableEdgeData> edges = new List<SerializableEdgeData>();
        public List<SerializableScopeData> scopes = new List<SerializableScopeData>();

        // Add ordered lists for engagement levels
        public List<string> orderedHighEngagementNodeIds = new List<string>();
        public List<string> orderedMediumEngagementNodeIds = new List<string>();
        public List<string> orderedLowEngagementNodeIds = new List<string>();
    }

    [Serializable]
    private class SerializableNodeData
    {
        public string id;
        public string objectName;
        public string objectPath;
        public SerializableVector2 position;
        public bool isDirectContacted;
        public string description;
        public int engagementLevel;

        // Haptic properties
        public string inertia;
        public float inertiaValue;
        public string interactivity;
        public float interactivityValue;
        public string outline;
        public float outlineValue;
        public string texture;
        public float textureValue;
        public string hardness;
        public float hardnessValue;
        public string temperature;
        public float temperatureValue;

        // Add fields to track port counts
        public int outputPortCount = 1; // Number of Direct ports
        public int inputPortCount = 1;  // Number of Mediated ports
    }

    [Serializable]
    private class SerializableEdgeData
    {
        public string sourceNodeId;
        public string targetNodeId;
        public string annotationText;
        public int sourcePortIndex = 0; // Index of the output port
        public int targetPortIndex = 0; // Index of the input port
    }

    [Serializable]
    private class SerializableVector2
    {
        public float x;
        public float y;
    }

    // Add this serializable class for scope data
    [Serializable]
    private class SerializableScopeData
    {
        public string title = "Group";
        public SerializableVector2 position;
        public SerializableVector2 size;
        public SerializableColor color;
        public List<string> containedNodeIds = new List<string>();
    }

    // Add this serializable class for color
    [Serializable]
    private class SerializableColor
    {
        public float r = 0.2f;
        public float g = 0.3f;
        public float b = 0.4f;
        public float a = 0.3f;
    }
}