using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Editor.WFC
{
    /// <summary>
    /// Editor tool window for building tiles from textures using a color palette.
    /// Analyzes texture pixels and creates cube objects based on the tile-palette.json colors.
    /// </summary>
    public class TextureTileBuilder : EditorWindow
    {
        private TileColorPalette _palette;
        private bool _showDebugInfo;
        private const float CubeSize = 1f;
        private const string GeneratedTilesPath = "Assets/Prefabs/Tiles/Generated";

        [MenuItem("Tools/WFC/Texture Tile Builder")]
        public static void ShowWindow()
        {
            GetWindow<TextureTileBuilder>("Texture Tile Builder");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Texture Tile Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Color Palette", EditorStyles.boldLabel);
            if (_palette == null)
            {
                LoadPalette();
            }

            if (_palette != null && _palette.colors != null)
            {
                EditorGUILayout.LabelField($"Loaded {_palette.colors.Count} colors from palette:", EditorStyles.miniLabel);
                foreach (var color in _palette.colors)
                {
                    string hex = ColorUtility.ToHtmlStringRGB(color.Color);
                    EditorGUILayout.LabelField($"  {color.id}: #{hex} - {color.purpose}", EditorStyles.miniLabel);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Failed to load color palette. Check if tile-palette.json exists at Assets/tile-palette.json", MessageType.Error);
            }

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Build All Tiles from Textures", GUILayout.Height(40)))
            {
                BuildAllTilesFromTextures();
            }

            if (GUILayout.Button("Clear All Generated Objects from All Tiles", GUILayout.Height(40)))
            {
                ClearGeneratedObjectsFromAllTiles();
            }

            EditorGUILayout.Space();

            _showDebugInfo = EditorGUILayout.Foldout(_showDebugInfo, "Debug Info");
            if (_showDebugInfo)
            {
                EditorGUILayout.TextArea($"Generated Folder: {GeneratedTilesPath}\n" +
                                          $"Palette Loaded: {(_palette != null && _palette.colors != null)}\n" +
                                          $"Palette Colors: {(_palette?.colors?.Count ?? 0)}", 
                                          EditorStyles.helpBox, GUILayout.Height(60));
            }
        }

        private void BuildAllTilesFromTextures()
        {
            if (_palette == null || _palette.colors == null || _palette.colors.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "Palette is not loaded.", "OK");
                return;
            }

            string[] prefabGUIDs = AssetDatabase.FindAssets("t:Prefab", new[] { GeneratedTilesPath });
            if (prefabGUIDs.Length == 0)
            {
                EditorUtility.DisplayDialog("Error", $"No prefabs found in {GeneratedTilesPath}", "OK");
                return;
            }

            int totalProcessed = 0;
            int totalCreated = 0;
            int totalPrefabsProcessed = 0;

            try
            {
                foreach (string guid in prefabGUIDs)
                {
                    string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = PrefabUtility.LoadPrefabContents(prefabPath);

                    if (prefab == null) continue;

                    try
                    {
                        int pixelsProcessed = 0;
                        int objectsCreated = 0;

                        Texture2D texture = GetTextureFromPrefab(prefab);
                        texture = GetReadableTexture(texture);
                        if (texture == null)
                        {
                            Debug.LogWarning($"Prefab '{prefab.name}' does not have a readable texture. Skipping.");
                            continue;
                        }

                        Transform container = GetOrCreateGeneratedContainer(prefab.transform);

                        for (int y = 0; y < texture.height; y++)
                        {
                            for (int x = 0; x < texture.width; x++)
                            {
                                Color pixelColor = texture.GetPixel(x, y);
                                pixelsProcessed++;

                                PaletteColor matchedColor = FindClosestColor(pixelColor);
                                if (matchedColor == null || matchedColor.id == "empty")
                                {
                                    continue;
                                }

                                GameObject cube = CreateCube(x, y, matchedColor, container);
                                if (cube != null)
                                {
                                    objectsCreated++;
                                }
                            }
                        }

                        if (objectsCreated > 0)
                        {
                            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
                            totalProcessed += pixelsProcessed;
                            totalCreated += objectsCreated;
                            totalPrefabsProcessed++;
                            Debug.Log($"Built {prefab.name}: {pixelsProcessed} pixels, {objectsCreated} objects");
                        }
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(prefab);
                    }
                }

                EditorUtility.DisplayDialog("Success", $"Built {totalPrefabsProcessed} tiles.\nTotal pixels: {totalProcessed}\nTotal objects: {totalCreated}", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error building tiles: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("Error", $"Error building tiles: {ex.Message}", "OK");
            }
        }

        private void ClearGeneratedObjectsFromAllTiles()
        {
            string[] prefabGUIDs = AssetDatabase.FindAssets("t:Prefab", new[] { GeneratedTilesPath });
            if (prefabGUIDs.Length == 0)
            {
                EditorUtility.DisplayDialog("Error", $"No prefabs found in {GeneratedTilesPath}", "OK");
                return;
            }

            int clearedCount = 0;

            try
            {
                foreach (string guid in prefabGUIDs)
                {
                    string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = PrefabUtility.LoadPrefabContents(prefabPath);

                    if (prefab == null) continue;

                    try
                    {
                        Transform container = prefab.transform.Find("_GeneratedTiles");
                        if (container != null)
                        {
                            DestroyImmediate(container.gameObject);
                            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
                            clearedCount++;
                        }
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(prefab);
                    }
                }

                EditorUtility.DisplayDialog("Success", $"Cleared generated objects from {clearedCount} tiles.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error clearing objects: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Error clearing objects: {ex.Message}", "OK");
            }
        }

        private GameObject CreateCube(int x, int y, PaletteColor paletteColor, Transform parent)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"{paletteColor.id}_{x}_{y}";

            // Remove collider to avoid performance issues
            Collider collider = cube.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyImmediate(collider);
            }

            // Position the cube
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = new Vector3(x * CubeSize, 0, y * CubeSize);
            cube.transform.localScale = Vector3.one * CubeSize;

            // Apply color to the material
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = paletteColor.Color;
            MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material = mat;
            }

            return cube;
        }

        private Transform GetOrCreateGeneratedContainer(Transform parentTransform)
        {
            const string containerName = "_GeneratedTiles";
            Transform existing = parentTransform.Find(containerName);
            
            if (existing != null)
            {
                return existing;
            }

            GameObject containerObj = new GameObject(containerName);
            containerObj.transform.SetParent(parentTransform, false);
            containerObj.transform.localPosition = Vector3.zero;
            Undo.RegisterCreatedObjectUndo(containerObj, "Create Generated Container");
            return containerObj.transform;
        }


        private Texture2D GetTextureFromPrefab(GameObject prefab)
        {
            if (prefab == null) return null;

            // First, try to get texture from the prefab's own MeshRenderer
            MeshRenderer meshRenderer = prefab.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.sharedMaterials != null)
            {
                foreach (Material material in meshRenderer.sharedMaterials)
                {
                        if (material != null)
                        {
                            Texture2D texture = TryGetTextureFromMaterial(material);
                            if (texture != null)
                            {
                                Debug.Log($"Found texture '{texture.name}' in prefab '{prefab.name}' main MeshRenderer");
                                return texture;
                            }
                        }
                }
            }

            // Then check all child MeshRenderers
            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>();
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer != null && renderer.sharedMaterials != null)
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material != null)
                        {
                            Texture2D texture = TryGetTextureFromMaterial(material);
                            if (texture != null)
                            {
                                Debug.Log($"Found texture '{texture.name}' in prefab '{prefab.name}' sub MeshRenderer '{renderer.gameObject.name}'");
                                return texture;
                            }
                        }
                    }
                }
            }

            Debug.LogWarning($"No texture found in prefab '{prefab.name}' or its children.");
            return null;
        }

        private Texture2D TryGetTextureFromMaterial(Material material)
        {
            if (material == null) return null;

            // Try mainTexture first
            if (material.mainTexture is Texture2D texture)
            {
                return texture;
            }

            // Try all texture properties in the material
            string[] texturePropertyNames = material.GetTexturePropertyNames();
            foreach (string propName in texturePropertyNames)
            {
                Texture tex = material.GetTexture(propName);
                if (tex is Texture2D texture2D)
                {
                    Debug.Log($"Found texture '{tex.name}' in material property '{propName}'");
                    return texture2D;
                }
            }

            return null;
        }

        private PaletteColor FindClosestColor(Color pixelColor)
        {
            if (_palette?.colors == null || _palette.colors.Count == 0)
                return null;

            PaletteColor closest = null;
            float closestDistance = float.MaxValue;

            foreach (var paletteColor in _palette.colors)
            {
                float distance = ColorDistance(pixelColor, paletteColor.Color);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = paletteColor;
                }
            }

            return closest;
        }

        private float ColorDistance(Color c1, Color c2)
        {
            // Use Euclidean distance in RGB space
            float dr = c1.r - c2.r;
            float dg = c1.g - c2.g;
            float db = c1.b - c2.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }

        private void LoadPalette()
        {
            string absolutePath = Path.Combine(Application.dataPath, "tile-palette.json");
            if (!File.Exists(absolutePath))
            {
                Debug.LogError($"Palette file not found at {absolutePath}");
                return;
            }

            try
            {
                string json = File.ReadAllText(absolutePath);
                TileColorPaletteRaw rawPalette = JsonUtility.FromJson<TileColorPaletteRaw>(json);

                // Create palette with list
                _palette = new TileColorPalette
                {
                    colors = new List<PaletteColor>(rawPalette.colors ?? new PaletteColor[0])
                };

                // Convert RGBA arrays to Color objects
                if (_palette?.colors != null)
                {
                    foreach (var colorData in _palette.colors)
                    {
                        if (colorData.rgba != null && colorData.rgba.Length >= 4)
                        {
                            colorData.Color = new Color(
                                colorData.rgba[0] / 255f,
                                colorData.rgba[1] / 255f,
                                colorData.rgba[2] / 255f,
                                colorData.rgba[3] / 255f
                            );
                        }
                    }
                }

                Debug.Log($"Loaded {_palette?.colors?.Count ?? 0} colors from palette.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load palette: {ex.Message}");
            }
        }

        private bool IsTextureReadable(Texture2D texture)
        {
            try
            {
                // Try to read a pixel to verify texture is readable
                texture.GetPixel(0, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private Texture2D GetReadableTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return null;
            }

            if (IsTextureReadable(texture))
            {
                return texture;
            }

            Texture2D readableCopy = CreateReadableCopy(texture);
            if (readableCopy != null)
            {
                Debug.Log($"Created readable copy for texture '{texture.name}'.");
            }

            return readableCopy;
        }

        private Texture2D CreateReadableCopy(Texture2D sourceTexture)
        {
            if (sourceTexture == null)
            {
                return null;
            }

            RenderTexture renderTexture = null;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                renderTexture = RenderTexture.GetTemporary(
                    sourceTexture.width,
                    sourceTexture.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);

                Graphics.Blit(sourceTexture, renderTexture);
                RenderTexture.active = renderTexture;

                Texture2D readableTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
                readableTexture.ReadPixels(new Rect(0, 0, sourceTexture.width, sourceTexture.height), 0, 0);
                readableTexture.Apply();
                return readableTexture;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to create readable copy for texture '{sourceTexture.name}': {ex.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }
            }
        }

        #pragma warning disable IDE1006

        [Serializable]
        private class TileColorPaletteRaw
        {
            public PaletteColor[] colors;
        }

        [Serializable]
        private class TileColorPalette
        {
            public List<PaletteColor> colors;
        }

        [Serializable]
        private class PaletteColor
        {
            public string id;
            public string purpose;
            public int[] rgba;
            
            [NonSerialized]
            public Color Color;
        }

        #pragma warning restore IDE1006
    }
}



