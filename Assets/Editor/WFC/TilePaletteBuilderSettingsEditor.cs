using UnityEditor;
using UnityEngine;
using WFC;

[CustomEditor(typeof(TilePaletteBuilderSettings))]
public class TilePaletteBuilderSettingsEditor : UnityEditor.Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		TilePaletteBuilderSettings settings = (TilePaletteBuilderSettings)target;

		GUILayout.Space(8f);
		if (GUILayout.Button("Reset Palette From JSON"))
		{
			Undo.RecordObject(settings, "Reset Palette From JSON");
			settings.ResetPaletteFromJson();
			EditorUtility.SetDirty(settings);
		}
	}
}

