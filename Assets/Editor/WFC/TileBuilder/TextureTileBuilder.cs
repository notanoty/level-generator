using System;
using UnityEditor;
using UnityEngine;
using WFC;

namespace Editor.WFC.TileBuilder
{
    public class TextureTileBuilder : EditorWindow
    {
        private GameObject _tileForOne;
        private TilePalette _tilePalette;
        private readonly TileBulder _tileBulder;

        public TextureTileBuilder()
        {
            _tileBulder = new TileBulder(this);
        }

        public TileBulder TileBulder
        {
            get { return _tileBulder; }
        }

        [MenuItem("Tools/WFC/Texture Tile Builder")]
        public static void ShowWindow()
        {
            GetWindow<TextureTileBuilder>("Texture Tile Builder");
        }

        private void OnGUI()
        {


            EditorGUILayout.LabelField("Texture Tile Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _tileForOne = (GameObject)EditorGUILayout.ObjectField("Game Object", _tileForOne, typeof(GameObject), true);

            if (GUILayout.Button("GenerateAll", GUILayout.Height(40)))
            {
                GenerateAll();
            }

            if (GUILayout.Button("Clear", GUILayout.Height(40)))
            {
                Clear();
            }
            if (GUILayout.Button("GenerateOne", GUILayout.Height(40)))
            {
                GenerateOne(_tileForOne);
            }
        }

        private void GenerateAll()
        {
            _tilePalette = ResolveTilePalette();
            
            if (_tilePalette == null)
            {
                return;
            }

            string generatedFolder = "Assets/Prefabs/Tiles/Generated";
            if (!AssetDatabase.IsValidFolder(generatedFolder))
            {
                Debug.LogWarning($"Generated folder not found: {generatedFolder}");
                return;
            }

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { generatedFolder });
            if (prefabGuids == null || prefabGuids.Length == 0)
            {
                Debug.LogWarning($"No prefabs found in {generatedFolder}.");
                return;
            }

            int processedCount = 0;
            int skippedCount = 0;

            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    skippedCount++;
                    Debug.LogWarning($"Skipping '{prefabPath}': prefab could not be loaded.");
                    continue;
                }

                try
                {
                    GenerateOne(prefab);
                    processedCount++;
                }
                catch (Exception exception)
                {
                    skippedCount++;
                    Debug.LogError($"Failed to generate for '{prefabPath}': {exception.Message}");
                }
            }

            Debug.Log($"GenerateAll finished for {generatedFolder}. Processed: {processedCount}, skipped: {skippedCount}.");
        }

        private void GenerateOne(GameObject tile)
        {
            ClearOne(tile);
            
            if (tile == null)
            {
                Debug.LogWarning("Please assign a GameObject before generating one tile.");
                return;
            }

            _tilePalette = ResolveTilePalette();
            if (_tilePalette == null)
            {
                return;
            }

            // Check if it's a prefab asset
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(tile);
            GameObject targetTile = tile;

            if (string.IsNullOrEmpty(prefabPath))
            {
                // Not a prefab asset, use as is
                prefabPath = null;
            }
            else
            {
                // Load prefab for editing
                targetTile = PrefabUtility.LoadPrefabContents(prefabPath);
            }

            try
            {
                Texture2D texture = GetTexture(targetTile);

                if (texture == null)
                {
                    Debug.LogWarning($"No texture found on material for '{targetTile.name}'.");
                    return;
                }

                Debug.Log($"Texture found on '{targetTile.name}': {texture.name}");

                Renderer tileRenderer = targetTile.GetComponent<Renderer>();
                if (tileRenderer == null)
                {
                    Debug.LogWarning($"No renderer found on '{targetTile.name}'.");
                    return;
                }

                Bounds bounds = tileRenderer.bounds;
                float tileWidth = bounds.size.x;
                float tileDepth = bounds.size.z;

                int width = texture.width;
                int height = texture.height;
                Color32[] pixels = texture.GetPixels32();
                bool[,] processed = new bool[64, 64];

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        if (processed[x, y])
                        {
                            continue;
                        }

                        Color32 pixelColor = pixels[(y * width) + x];
                        if (_tilePalette.TryGet(pixelColor, out TilePaletteEntry entry))
                        {
                              GameObject selectedGameObject = SelectGameObject(entry.GameObjects);
                              if (selectedGameObject != null)
                            {
                                  TileBulder.BuildObject(x, y, targetTile, selectedGameObject, entry.Height,
                                    tileWidth, tileDepth, width, height);
                                  Debug.Log($"Pixel at ({x}, {y}) has color {pixelColor} and spawned model '{selectedGameObject.name}' for purpose '{entry.Purpose}'.");
                            }
                            else
                            {
                                TileBulder.BuildObjectOptimized(x, y, targetTile, pixelColor, entry.Height,
                                    tileWidth, tileDepth, pixels, width, height, processed);
                                Debug.Log($"Pixel at ({x}, {y}) has color {pixelColor} which corresponds to purpose '{entry.Purpose}' in the palette.");
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"Pixel at ({x}, {y}) has color {pixelColor} which does not correspond to any purpose in the palette.");
                        }
                    }
                }

                if (!string.IsNullOrEmpty(prefabPath))
                {
                    TileBulder.PersistGeneratedCubeMaterials(targetTile, prefabPath);
                    PrefabUtility.SaveAsPrefabAsset(targetTile, prefabPath);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"Saved prefab to {prefabPath}");
                }
            }
            finally
            {
                // Unload prefab if it was loaded
                if (!string.IsNullOrEmpty(prefabPath))
                {
                    PrefabUtility.UnloadPrefabContents(targetTile);
                }
            }
        }

        private TilePalette ResolveTilePalette()
        {
            TilePaletteBuilderSettings settings = UnityEngine.Object.FindFirstObjectByType<TilePaletteBuilderSettings>();
            if (settings == null)
            {
                Debug.LogWarning("No TilePaletteBuilderSettings found in the current scene. Add one to configure tile palette generation.");
                return null;
            }

            try
            {
                // settings.ReloadPalette();
                return settings.BuildPalette();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to build tile palette from '{settings.name}': {exception.Message}");
                return null;
            }
        }

        private void Clear()
        {
            if (_tileForOne != null)
            {
                // Clear selected prefab only
                ClearOne(_tileForOne);
            }
            else
            {
                ClearAll();
            }
        }

        private static void ClearAll()
        {
            // Clear all prefabs in Generated folder
            string generatedFolder = "Assets/Prefabs/Tiles/Generated";
            string[] prefabGUIDs = AssetDatabase.FindAssets("t:Prefab", new[] { generatedFolder });

            foreach (string guid in prefabGUIDs)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    Transform[] children = prefab.GetComponentsInChildren<Transform>();
                    for (int i = children.Length - 1; i >= 0; i--)
                    {
                        if (children[i] != prefab.transform)
                        {
                            DestroyImmediate(children[i].gameObject);
                        }
                    }
                    PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefab);
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"Cleared all prefabs in {generatedFolder}");
        }

        private void ClearOne(GameObject tile)
        {
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(tile);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                GameObject prefab = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    Transform[] children = prefab.GetComponentsInChildren<Transform>();
                    for (int i = children.Length - 1; i >= 0; i--)
                    {
                        if (children[i] != prefab.transform)
                        {
                            DestroyImmediate(children[i].gameObject);
                        }
                    }
                    PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
                    Debug.Log($"Cleared prefab {prefabPath}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefab);
                }
            }
        }


        private Texture2D GetTexture(GameObject tile)
        {
            if (tile == null)
            {
                return null;
            }

            Renderer tileRenderer = tile.GetComponent<Renderer>();
            if (tileRenderer == null)
            {
                return null;
            }

            Material material = tileRenderer.sharedMaterial;
            if (material == null)
            {
                return null;
            }

            return material.mainTexture as Texture2D;
        }

          private static GameObject SelectGameObject(GameObject[] gameObjects)
          {
              if (gameObjects == null || gameObjects.Length == 0)
              {
                  return null;
              }

              int validCount = 0;
              for (int i = 0; i < gameObjects.Length; i++)
              {
                  if (gameObjects[i] != null)
                  {
                      validCount++;
                  }
              }

              if (validCount == 0)
              {
                  return null;
              }

              int targetIndex = UnityEngine.Random.Range(0, validCount);
              for (int i = 0; i < gameObjects.Length; i++)
              {
                  GameObject candidate = gameObjects[i];
                  if (candidate == null)
                  {
                      continue;
                  }

                  if (targetIndex == 0)
                  {
                      return candidate;
                  }

                  targetIndex--;
              }

              return null;
          }
    }
}
