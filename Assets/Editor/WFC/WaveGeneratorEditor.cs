using UnityEditor;
using UnityEngine;
using WFC;

[CustomEditor(typeof(WaveGenerator))]
public class WaveGeneratorEditor : UnityEditor.Editor
{
    private bool showGrid = true;
    private static readonly Color GridColor = new Color(0.15f, 0.8f, 1f, 0.25f);

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        WaveGenerator gen = (WaveGenerator)target;

        GUILayout.Space(8);
        showGrid = EditorGUILayout.ToggleLeft("Show Grid", showGrid);

        GUILayout.Space(8);
        if (GUILayout.Button("Generate"))
        {
            gen.Generate();
        }

        if (GUILayout.Button("Clear Generated"))
        {
            if (gen.transform.Find(gen.containerName) is Transform container)
            {
                if (container != null)
                {
                    if (EditorUtility.DisplayDialog("Clear Generated", "Are you sure you want to remove all generated tiles under '" + gen.containerName + "'?", "Yes", "No"))
                    {
                        Undo.RegisterCompleteObjectUndo(container.gameObject, "Clear Generated");
                        for (int i = container.childCount - 1; i >= 0; i--)
                        {
                            var child = container.GetChild(i).gameObject;
                            Undo.DestroyObjectImmediate(child);
                        }
                    }
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Clear Generated", "No container named '" + gen.containerName + "' found under the generator.", "OK");
            }
        }

        if (GUILayout.Button("Refresh Prefabs (folder)"))
        {
            var method = typeof(WaveGenerator).GetMethod("RefreshTilePrefabs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(gen, null);
            EditorUtility.SetDirty(gen);
        }
    }

    private void OnSceneGUI()
    {
        if (!showGrid)
        {
            return;
        }

        WaveGenerator gen = (WaveGenerator)target;
        if (gen == null)
        {
            return;
        }

        int width = Mathf.Max(0, gen.width);
        int height = Mathf.Max(0, gen.height);
        if (width == 0 || height == 0)
        {
            return;
        }

        float cellX = Mathf.Abs(gen.tileSize.x);
        float cellZ = Mathf.Abs(gen.tileSize.y);
        if (cellX <= 0f || cellZ <= 0f)
        {
            return;
        }

        Vector3 right = new Vector3(cellX, 0f, 0f);
        Vector3 forward = new Vector3(0f, 0f, cellZ);
        Vector3 origin = gen.transform.position - (right * 0.5f) - (forward * 0.5f);

        Handles.color = GridColor;
        for (int x = 0; x <= width; x++)
        {
            Vector3 start = origin + right * x;
            Vector3 end = start + forward * height;
            Handles.DrawLine(start, end);
        }

        for (int y = 0; y <= height; y++)
        {
            Vector3 start = origin + forward * y;
            Vector3 end = start + right * width;
            Handles.DrawLine(start, end);
        }
    }
}
