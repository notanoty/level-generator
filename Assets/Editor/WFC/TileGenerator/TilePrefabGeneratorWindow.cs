using UnityEditor;
using UnityEngine;

namespace Editor.WFC.TileGenerator
{
    public class TilePrefabGeneratorWindow : EditorWindow
    {
        [SerializeField]
        private DefaultAsset selectedTileDataFolder;

        public static void ShowWindow()
        {
            TilePrefabGeneratorWindow window = GetWindow<TilePrefabGeneratorWindow>("Tile Prefab Generator");
            window.minSize = new Vector2(420f, 160f);
            window.InitializeSelectionFromProject();
            window.Show();
        }

        private void OnEnable()
        {
            if (selectedTileDataFolder == null)
            {
                InitializeSelectionFromProject();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Tile Prefab Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                "Pick a folder under Assets/TileData to regenerate only that folder and its subfolders. Leave it empty to regenerate all tile data.",
                MessageType.Info);

            selectedTileDataFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Tile Data Folder",
                selectedTileDataFolder,
                typeof(DefaultAsset),
                false);

            string selectedFolderPath = GetSelectedFolderPath();
            bool hasValidSelection = IsValidTileDataFolder(selectedFolderPath);

            if (selectedTileDataFolder != null && !hasValidSelection)
            {
                EditorGUILayout.HelpBox("The selected asset must be a folder inside Assets/TileData. If this stays invalid, generating will fall back to all tile data.", MessageType.Warning);
            }

            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Current Selection"))
                {
                    InitializeSelectionFromProject();
                }

                if (GUILayout.Button("Clear"))
                {
                    selectedTileDataFolder = null;
                }
            }

            EditorGUILayout.Space(10f);

            string buttonLabel = hasValidSelection ? "Generate Selected Folder" : "Generate All Tile Data";
            if (GUILayout.Button(buttonLabel, GUILayout.Height(32f)))
            {
                if (hasValidSelection)
                {
                    TilePrefabGenerator.GenerateTilePrefabsForFolder(selectedFolderPath);
                }
                else
                {
                    TilePrefabGenerator.GenerateAllTilePrefabs();
                }
            }
        }

        private void InitializeSelectionFromProject()
        {
            selectedTileDataFolder = GetFolderAssetFromObject(Selection.activeObject);
        }

        private string GetSelectedFolderPath()
        {
            return GetFolderPathFromAsset(selectedTileDataFolder);
        }

        private static string GetFolderPathFromAsset(Object asset)
        {
            if (asset == null)
            {
                return null;
            }

            string path = AssetDatabase.GetAssetPath(asset).Replace('\\', '/');
            return AssetDatabase.IsValidFolder(path) ? path : null;
        }

        private static DefaultAsset GetFolderAssetFromObject(Object asset)
        {
            string folderPath = GetFolderPathFromAsset(asset);
            return string.IsNullOrEmpty(folderPath) ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
        }

        private static bool IsValidTileDataFolder(string assetFolderPath)
        {
            return !string.IsNullOrWhiteSpace(assetFolderPath) &&
                   AssetDatabase.IsValidFolder(assetFolderPath) &&
                   IsUnderTileDataRoot(assetFolderPath);
        }

        private static bool IsUnderTileDataRoot(string assetFolderPath)
        {
            string normalized = assetFolderPath.Replace('\\', '/');
            return string.Equals(normalized, "Assets/TileData", System.StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("Assets/TileData/", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}




