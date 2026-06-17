using UnityEditor;
using UnityEngine;
using WFC;

[CustomEditor(typeof(WaveGenerator))]
public class WaveGeneratorEditor : UnityEditor.Editor
{
    private bool showGrid = true;
    private static readonly Color GridColor = new Color(0.15f, 0.8f, 1f, 0.25f);

    private SerializedProperty widthProp;
    private SerializedProperty heightProp;
    private SerializedProperty tilesFolderProp;
    private SerializedProperty baseTilePrefabProp;
    private SerializedProperty useBaseTileInSelectionProp;
    private SerializedProperty emptyTilePrefabProp;
    private SerializedProperty tileSizeProp;
    private SerializedProperty parentContainerProp;
    private SerializedProperty containerNameProp;
    private SerializedProperty clearContainerBeforeGenerateProp;
    private SerializedProperty showCollapseMarkersProp;
    private SerializedProperty collapseMarkerScaleProp;
    private SerializedProperty maxTileDepthProp;

    private void OnEnable()
    {
        widthProp                        = serializedObject.FindProperty("width");
        heightProp                       = serializedObject.FindProperty("height");
        tilesFolderProp                  = serializedObject.FindProperty("tilesFolder");
        baseTilePrefabProp               = serializedObject.FindProperty("baseTilePrefab");
        useBaseTileInSelectionProp       = serializedObject.FindProperty("useBaseTileInSelection");
        emptyTilePrefabProp              = serializedObject.FindProperty("emptyTilePrefab");
        tileSizeProp                     = serializedObject.FindProperty("tileSize");
        parentContainerProp              = serializedObject.FindProperty("parentContainer");
        containerNameProp                = serializedObject.FindProperty("containerName");
        clearContainerBeforeGenerateProp = serializedObject.FindProperty("clearContainerBeforeGenerate");
        showCollapseMarkersProp          = serializedObject.FindProperty("showCollapseMarkers");
        collapseMarkerScaleProp          = serializedObject.FindProperty("collapseMarkerScale");
        maxTileDepthProp                 = serializedObject.FindProperty("maxTileDepth");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        WaveGenerator gen = (WaveGenerator)target;

        // Grid Size
        EditorGUILayout.LabelField("Grid Size", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(widthProp);
        EditorGUILayout.PropertyField(heightProp);

        GUILayout.Space(6);

        // Tiles
        EditorGUILayout.LabelField("Tiles", EditorStyles.boldLabel);
        DrawTilesFolderPicker();
        EditorGUILayout.PropertyField(baseTilePrefabProp);
        EditorGUILayout.PropertyField(useBaseTileInSelectionProp);
        EditorGUILayout.PropertyField(emptyTilePrefabProp);
        EditorGUILayout.PropertyField(tileSizeProp);

        GUILayout.Space(6);

        // Generation
        EditorGUILayout.LabelField("Generation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(parentContainerProp);
        EditorGUILayout.PropertyField(containerNameProp);
        EditorGUILayout.PropertyField(clearContainerBeforeGenerateProp);
        EditorGUILayout.PropertyField(maxTileDepthProp);

        GUILayout.Space(6);

        // Debug
        EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(showCollapseMarkersProp);
        EditorGUILayout.PropertyField(collapseMarkerScaleProp);

        GUILayout.Space(8);
        showGrid = EditorGUILayout.ToggleLeft("Show Grid", showGrid);

        serializedObject.ApplyModifiedProperties();

        // Buttons
        GUILayout.Space(8);
        if (GUILayout.Button("Generate"))
            gen.Generate();

        if (GUILayout.Button("Clear Generated"))
        {
            if (gen.transform.Find(gen.containerName) is Transform container)
            {
                if (EditorUtility.DisplayDialog("Clear Generated",
                        "Are you sure you want to remove all generated tiles under '" + gen.containerName + "'?",
                        "Yes", "No"))
                {
                    Undo.RegisterCompleteObjectUndo(container.gameObject, "Clear Generated");
                    for (int i = container.childCount - 1; i >= 0; i--)
                        Undo.DestroyObjectImmediate(container.GetChild(i).gameObject);
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Clear Generated",
                    "No container named '" + gen.containerName + "' found under the generator.", "OK");
            }
        }

        if (GUILayout.Button("Refresh Prefabs (folder)"))
        {
            var method = typeof(WaveGenerator).GetMethod("RefreshTilePrefabs",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(gen, null);
            EditorUtility.SetDirty(gen);
        }
    }

    private void DrawTilesFolderPicker()
    {
        string currentPath = tilesFolderProp.stringValue;
        DefaultAsset currentFolder = string.IsNullOrEmpty(currentPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<DefaultAsset>(currentPath);

        EditorGUI.BeginChangeCheck();
        DefaultAsset picked = (DefaultAsset)EditorGUILayout.ObjectField(
            new GUIContent("Tiles Folder", "Drag a folder from the Project window to set the tile source folder."),
            currentFolder, typeof(DefaultAsset), false);

        if (EditorGUI.EndChangeCheck())
        {
            if (picked == null)
            {
                tilesFolderProp.stringValue = string.Empty;
            }
            else
            {
                string path = AssetDatabase.GetAssetPath(picked);
                if (AssetDatabase.IsValidFolder(path))
                    tilesFolderProp.stringValue = path;
                else
                    EditorUtility.DisplayDialog("Invalid Selection",
                        "Please select a folder (not a file) from the Project window.", "OK");
            }
        }

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("  Path",
                string.IsNullOrEmpty(tilesFolderProp.stringValue)
                    ? "(none – falls back to default)"
                    : tilesFolderProp.stringValue);
        }
    }

    private void OnSceneGUI()
    {
        if (!showGrid) return;

        WaveGenerator gen = (WaveGenerator)target;
        if (gen == null) return;

        int width  = Mathf.Max(0, gen.width);
        int height = Mathf.Max(0, gen.height);
        if (width == 0 || height == 0) return;

        float cellX = Mathf.Abs(gen.tileSize.x);
        float cellZ = Mathf.Abs(gen.tileSize.y);
        if (cellX <= 0f || cellZ <= 0f) return;

        Vector3 right   = new Vector3(cellX, 0f, 0f);
        Vector3 forward = new Vector3(0f, 0f, cellZ);
        Vector3 origin  = gen.transform.position - (right * 0.5f) - (forward * 0.5f);

        Handles.color = GridColor;
        for (int x = 0; x <= width; x++)
        {
            Vector3 start = origin + right * x;
            Handles.DrawLine(start, start + forward * height);
        }
        for (int y = 0; y <= height; y++)
        {
            Vector3 start = origin + forward * y;
            Handles.DrawLine(start, start + right * width);
        }
    }
}
