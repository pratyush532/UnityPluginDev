// Author: MohantyPratyush

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Editor tool for filtering, visualizing, and deleting child GameObjects in the current scene
/// based on their bounding box volume. Includes mesh statistics, search/filter, and undo support.
/// </summary>
public class MaxSizeFilterTool : EditorWindow
{
    private GameObject selectedObject;
    private string searchQuery = "";
    private Vector2 scrollPos;
    private List<GameObject> allObjects = new List<GameObject>();
    private List<GameObject> filteredObjects = new List<GameObject>();

    private int triangleCount = 0;
    private int vertexCount = 0;
    private int emptyCount = 0;
    private int childCount = 0;

    private float objectVolume = 0f;
    private float sliderValue = 0f;
    private Vector2 scrollPosChildObjects;
    
    private bool showBoundingBox = false;   
    private bool showAllChildBounds = false;
    private int lastDeletedCount = 0;
    private List<GameObject> lastDeletedObjects = new List<GameObject>();

    /// <summary>
    /// Adds a menu item to open the MaxSizeFilterTool window.
    /// </summary>
    [MenuItem("Tools/Filter Small Objects")]
    public static void ShowWindow()
    {
        GetWindow<MaxSizeFilterTool>("Filter Small Objects");
    }

    /// <summary>
    /// Called when the window is enabled. Refreshes the list of all GameObjects and subscribes to the Scene GUI event.
    /// </summary>
    private void OnEnable()
    {
        RefreshObjectList();
        SceneView.duringSceneGui += OnSceneGUI;
    }

    /// <summary>
    /// Called when the window is disabled. Unsubscribes from the Scene GUI event.
    /// </summary>
    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    /// <summary>
    /// Draws the UI for the Editor window, including search, selection, mesh stats, filtering, and deletion controls.
    /// </summary>
    private void OnGUI()
    {
        GUILayout.Label("Search and Select a GameObject", EditorStyles.boldLabel);

        searchQuery = EditorGUILayout.TextField("Search:", searchQuery);
        if (GUILayout.Button("Refresh List"))
        {
            RefreshObjectList();
        }

        filteredObjects = allObjects
            .Where(obj => string.IsNullOrEmpty(searchQuery) || obj.name.ToLower().Contains(searchQuery.ToLower()))
            .ToList();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(150));
        foreach (var obj in filteredObjects)
        {
            if (GUILayout.Button(obj.name))
            {
                selectedObject = obj;
                GetObjectInfo(selectedObject);
            }
        }
        EditorGUILayout.EndScrollView();

        if (selectedObject != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selected Object:", selectedObject.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Child Objects:", childCount.ToString());
            EditorGUILayout.LabelField("Empty Objects:", emptyCount.ToString());
            EditorGUILayout.LabelField("Total Vertices:", vertexCount.ToString());
            EditorGUILayout.LabelField("Total Triangles:", triangleCount.ToString());

            EditorGUILayout.LabelField("Object Volume (m³):", objectVolume.ToString("F6"));
            sliderValue = EditorGUILayout.Slider("Filter by Volume", sliderValue, 0f, objectVolume);

            EditorGUILayout.Space();
            GUILayout.Label("Child Objects (volume < slider):", EditorStyles.boldLabel);
            scrollPosChildObjects = EditorGUILayout.BeginScrollView(scrollPosChildObjects, GUILayout.Height(200));

            int filteredObjectCount = 0;
            DisplayAllDescendantObjectsWithVolumeLessThanSlider(selectedObject.transform, ref filteredObjectCount);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField($"Filtered Objects: {filteredObjectCount}");

            // Bounding Box Toggles
            showBoundingBox = EditorGUILayout.Toggle("Show Main Bounding Box", showBoundingBox);
            showAllChildBounds = EditorGUILayout.Toggle("Show All Child Bounds", showAllChildBounds);

            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Delete Filtered Objects"))
            {
                DeleteFilteredObjects();
            }

            if (GUILayout.Button("Undo Last Delete") && lastDeletedCount > 0)
            {
                UndoLastDelete();
            }
            GUILayout.EndHorizontal();

            if (lastDeletedCount > 0)
            {
                EditorGUILayout.HelpBox($"Deleted {lastDeletedCount} objects", MessageType.Info);
            }

            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// Refreshes the cached list of all GameObjects in the current scene.
    /// </summary>
    private void RefreshObjectList()
    {
        allObjects.Clear();
        allObjects.AddRange(FindObjectsOfType<GameObject>());
        filteredObjects = new List<GameObject>(allObjects);
    }

    /// <summary>
    /// Calculates mesh statistics and bounding box volume for the selected GameObject.
    /// Also counts empty child objects.
    /// </summary>
    /// <param name="obj">The GameObject to analyze.</param>
    private void GetObjectInfo(GameObject obj)
    {
        childCount = obj.transform.childCount;
        emptyCount = 0;
        triangleCount = 0;
        vertexCount = 0;
        objectVolume = 0f;

        MeshFilter[] meshes = obj.GetComponentsInChildren<MeshFilter>();
        Bounds combinedBounds = new Bounds(obj.transform.position, Vector3.zero);

        foreach (var meshFilter in meshes)
        {
            if (meshFilter.sharedMesh != null)
            {
                triangleCount += meshFilter.sharedMesh.triangles.Length / 3;
                vertexCount += meshFilter.sharedMesh.vertexCount;
                
                // Calculate bounds in world space
                var bounds = meshFilter.sharedMesh.bounds;
                bounds.center = meshFilter.transform.TransformPoint(bounds.center);
                bounds.extents = meshFilter.transform.TransformVector(bounds.extents);
                combinedBounds.Encapsulate(bounds);
            }
        }

        objectVolume = combinedBounds.size.x * combinedBounds.size.y * combinedBounds.size.z;

        foreach (Transform child in obj.GetComponentsInChildren<Transform>())
        {
            if (child.gameObject.GetComponent<MeshRenderer>() == null && child.childCount == 0)
            {
                emptyCount++;
            }
        }
    }

    /// <summary>
    /// Recursively displays all descendant objects whose bounding box volume is less than the slider value.
    /// Increments the filteredObjectCount for each qualifying object.
    /// </summary>
    /// <param name="parentTransform">Transform to start searching from.</param>
    /// <param name="filteredObjectCount">Reference to the count of filtered objects.</param>
    private void DisplayAllDescendantObjectsWithVolumeLessThanSlider(Transform parentTransform, ref int filteredObjectCount)
    {
        foreach (Transform child in parentTransform)
        {
            float childVolume = GetObjectVolume(child.gameObject);
            if (childVolume < sliderValue)
            {
                EditorGUILayout.LabelField($"Child Object: {child.gameObject.name} - Volume: {childVolume:F6}");
                filteredObjectCount++;
            }

            DisplayAllDescendantObjectsWithVolumeLessThanSlider(child, ref filteredObjectCount);
        }
    }

    /// <summary>
    /// Calculates the bounding box volume for a GameObject and all its children.
    /// </summary>
    /// <param name="obj">The GameObject to calculate volume for.</param>
    /// <returns>Volume of the combined bounds.</returns>
    private float GetObjectVolume(GameObject obj)
    {
        MeshFilter[] meshes = obj.GetComponentsInChildren<MeshFilter>();
        Bounds combinedBounds = new Bounds(obj.transform.position, Vector3.zero);

        foreach (var meshFilter in meshes)
        {
            if (meshFilter.sharedMesh != null)
            {
                var bounds = meshFilter.sharedMesh.bounds;
                bounds.center = meshFilter.transform.TransformPoint(bounds.center);
                bounds.extents = meshFilter.transform.TransformVector(bounds.extents);
                combinedBounds.Encapsulate(bounds);
            }
        }

        return combinedBounds.size.x * combinedBounds.size.y * combinedBounds.size.z;
    }

    /// <summary>
    /// Deletes all child objects of the selected object whose bounding box volume is less than the slider value.
    /// Supports undo functionality.
    /// </summary>
    private void DeleteFilteredObjects()
    {
        lastDeletedObjects.Clear();
        lastDeletedCount = 0;
        
        // Record all objects that will be deleted for undo
        foreach (Transform child in selectedObject.transform)
        {
            float childVolume = GetObjectVolume(child.gameObject);
            if (childVolume < sliderValue)
            {
                lastDeletedObjects.Add(child.gameObject);
                lastDeletedCount++;
            }
        }

        // Perform deletion with undo support
        Undo.RegisterCompleteObjectUndo(selectedObject, "Delete Filtered Children");
        foreach (var obj in lastDeletedObjects)
        {
            Undo.DestroyObjectImmediate(obj);
        }
        
        // Refresh the object info after deletion
        GetObjectInfo(selectedObject);
        Repaint();
    }

    /// <summary>
    /// Undoes the last deletion of filtered objects.
    /// </summary>
    private void UndoLastDelete()
    {
        if (lastDeletedCount > 0)
        {
            Undo.PerformUndo();
            lastDeletedCount = 0;
            lastDeletedObjects.Clear();
            GetObjectInfo(selectedObject);
            Repaint();
        }
    }

    /// <summary>
    /// Handles drawing bounding boxes in the Scene view for the selected object and/or its children.
    /// </summary>
    /// <param name="sceneView">The current SceneView.</param>
    private void OnSceneGUI(SceneView sceneView)
    {
        if (selectedObject == null) return;

        if (showBoundingBox)
        {
            DrawBounds(selectedObject, Color.green);
        }

        if (showAllChildBounds)
        {
            foreach (Transform child in selectedObject.GetComponentsInChildren<Transform>())
            {
                DrawBounds(child.gameObject, new Color(1, 0.5f, 0, 0.5f));
            }
        }
    }

    /// <summary>
    /// Draws a wireframe bounding box for the given GameObject in the Scene view.
    /// </summary>
    /// <param name="obj">The GameObject to draw bounds for.</param>
    /// <param name="color">The color of the bounding box.</param>
    private void DrawBounds(GameObject obj, Color color)
    {
        MeshFilter[] meshes = obj.GetComponentsInChildren<MeshFilter>();
        if (meshes.Length == 0) return;

        Bounds combinedBounds = meshes[0].sharedMesh.bounds;
        combinedBounds.center = meshes[0].transform.TransformPoint(combinedBounds.center);
        combinedBounds.extents = meshes[0].transform.TransformVector(combinedBounds.extents);

        for (int i = 1; i < meshes.Length; i++)
        {
            if (meshes[i].sharedMesh != null)
            {
                var bounds = meshes[i].sharedMesh.bounds;
                bounds.center = meshes[i].transform.TransformPoint(bounds.center);
                bounds.extents = meshes[i].transform.TransformVector(bounds.extents);
                combinedBounds.Encapsulate(bounds);
            }
        }

        Handles.color = color;
        Handles.DrawWireCube(combinedBounds.center, combinedBounds.size);
    }
}
