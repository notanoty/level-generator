using UnityEditor;
using UnityEngine;
using WFC;

[CustomEditor(typeof(WaveGenerator))]
public class WaveGeneratorEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        WaveGenerator gen = (WaveGenerator)target;

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
}

