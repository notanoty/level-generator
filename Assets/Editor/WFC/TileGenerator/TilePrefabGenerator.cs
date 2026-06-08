using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WFC;
using ConnectedTile = WFC.ConnectedTile;
using Direction = WFC.Direction;
using Tile = WFC.Tile;

namespace Editor.WFC
{
    /// <summary>
    /// Generates tile prefabs from folders under Assets/TileData.
    /// Each source folder must contain a tile.json file and a tile.png texture.
    /// </summary>
    public static class TilePrefabGenerator
    {
        private const string TileDataRoot = "Assets/TileData";
        private const string GeneratedPrefabRoot = "Assets/Prefabs/Tiles/Generated";
        private const string BasePrefabPath = "Assets/Prefabs/Tiles/BaseTile.prefab";
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        [MenuItem("Tools/WFC/Generate Tile Prefabs From Tile Data")]
        public static void GenerateAllTilePrefabs()
        {
            if (!AssetDatabase.IsValidFolder(TileDataRoot))
            {
                Debug.LogError($"Tile data root folder not found: {TileDataRoot}");
                return;
            }

            EnsureFolderExists(GeneratedPrefabRoot);

            string tileDataRootAbsolute = Path.Combine(Application.dataPath, "TileData");
            if (!Directory.Exists(tileDataRootAbsolute))
            {
                Debug.LogError($"Tile data directory not found on disk: {tileDataRootAbsolute}");
                return;
            }

            string[] folders = Directory.GetDirectories(tileDataRootAbsolute, "*", SearchOption.AllDirectories);
            int generatedCount = 0;
            int skippedCount = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string absoluteFolder in folders)
                {
                    string assetFolder = AbsoluteToAssetPath(absoluteFolder);
                    if (string.IsNullOrEmpty(assetFolder))
                    {
                        continue;
                    }

                    if (!HasTileBundle(absoluteFolder))
                    {
                        continue;
                    }

                    if (GenerateTilePrefab(assetFolder))
                    {
                        generatedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"Tile prefab generation complete. Generated: {generatedCount}, skipped: {skippedCount}.");
        }

        private static bool GenerateTilePrefab(string tileDataFolder)
        {
            string jsonPath = Path.Combine(tileDataFolder, "tile.json").Replace('\\', '/');
            string texturePath = Path.Combine(tileDataFolder, "tile.png").Replace('\\', '/');

            if (!File.Exists(AssetPathToAbsolute(jsonPath)) || !File.Exists(AssetPathToAbsolute(texturePath)))
            {
                Debug.LogWarning($"Skipping {tileDataFolder}: expected tile.json and tile.png.");
                return false;
            }

            TileDataFile data;
            try
            {
                string json = File.ReadAllText(AssetPathToAbsolute(jsonPath));
                data = JsonUtility.FromJson<TileDataFile>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Skipping {tileDataFolder}: failed to read tile.json. {ex.Message}");
                return false;
            }

            if (data == null)
            {
                Debug.LogWarning($"Skipping {tileDataFolder}: tile.json could not be parsed.");
                return false;
            }

            string prefabName = GetPrefabName(tileDataFolder);
            string prefabPath = $"{GeneratedPrefabRoot}/{prefabName}.prefab";

            EnsureTextureReadable(texturePath);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
            {
                Debug.LogWarning($"Tile texture could not be loaded at {texturePath}. The prefab will still be created, but the Tile component will have no texture assigned.");
            }

            GameObject prefabContents = null;
            try
            {
                GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
                if (basePrefab == null)
                {
                    Debug.LogError($"Could not load base prefab at {BasePrefabPath}.");
                    return false;
                }

                prefabContents = PrefabUtility.InstantiatePrefab(basePrefab) as GameObject;
                if (prefabContents == null)
                {
                    Debug.LogError($"Could not instantiate base prefab at {BasePrefabPath}.");
                    return false;
                }

                prefabContents.name = prefabName;
                prefabContents.tag = "Untagged";

                Tile tile = prefabContents.GetComponent<Tile>();
                if (tile == null)
                {
                    Debug.LogError($"Base prefab at {BasePrefabPath} does not contain a WFC.Tile component.");
                    return false;
                }

                Material tileMaterial = GetOrCreateTileMaterial(tileDataFolder, texture);
                if (tileMaterial != null)
                {
                    ApplyMaterialToRenderers(prefabContents, tileMaterial);
                }

                ApplyTileData(tile, data, tileDataFolder);

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
                if (savedPrefab == null)
                {
                    Debug.LogError($"Failed to save generated prefab at {prefabPath}.");
                    return false;
                }

                Debug.Log($"Generated tile prefab: {prefabPath}");
                return true;
            }
            finally
            {
                if (prefabContents != null)
                {
                    UnityEngine.Object.DestroyImmediate(prefabContents);
                }
            }
        }

        private static void ApplyMaterialToRenderers(GameObject root, Material tileMaterial)
        {
            if (root == null || tileMaterial == null)
            {
                return;
            }

            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer != null)
                {
                    renderer.sharedMaterial = tileMaterial;
                    EditorUtility.SetDirty(renderer);
                }
            }
        }

        private static void ApplyTileData(Tile tile, TileDataFile data, string tileDataFolder)
        {
            MockPrefabData mock = data.mock_prefab;
            TileComponentData component = mock != null ? mock.tile_component : null;

            tile.connections = ResolveConnections(data, component, tileDataFolder);
            tile.rotation = NormalizeRotation(component != null ? component.rotation : 0);
            tile.allowRotationVariants = component == null || component.allow_rotation_variants;

            tile.allowedRotationSteps = BuildAllowedRotationSteps(component, tile.allowRotationVariants);
            tile.gridSize = ResolveGridSize(component, data);
            tile.snapPlane = ResolveSnapPlane(component);

            tile.tileType = ParseTileType(component != null ? component.tile_type : null);
            ApplyTypedConnections(tile, component != null ? component.typed_connections : null);

            if (tile.connectedTiles == null)
            {
                tile.connectedTiles = new List<ConnectedTile>();
            }
            else
            {
                tile.connectedTiles.Clear();
            }
        }

        private static TileSurfaceType ParseTileType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return TileSurfaceType.Default;
            }

            type = NormalizeTypeString(type);
            if (Enum.TryParse(type, true, out TileSurfaceType parsed))
            {
                return parsed;
            }

            return TileSurfaceType.Default;
        }

        private static void ApplyTypedConnections(Tile tile, TypedConnectionData[] typedConnections)
        {
            if (tile == null)
            {
                return;
            }

            if (tile.typeConnections == null)
            {
                tile.typeConnections = new List<DirectionalTypeConnection>();
            }
            else
            {
                tile.typeConnections.Clear();
            }

            if (typedConnections == null || typedConnections.Length == 0)
            {
                return;
            }

            foreach (TypedConnectionData entry in typedConnections)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.direction))
                {
                    continue;
                }

                if (!Enum.TryParse(entry.direction, true, out Direction direction))
                {
                    continue;
                }

                tile.typeConnections.Add(new DirectionalTypeConnection
                {
                    direction = direction,
                    allowedTypes = ParseConnectionTypeMask(entry.types)
                });
            }
        }

        private static ConnectionTypeMask ParseConnectionTypeMask(string[] types)
        {
            if (types == null || types.Length == 0)
            {
                return ConnectionTypeMask.All;
            }

            ConnectionTypeMask result = ConnectionTypeMask.None;
            foreach (string type in types)
            {
                if (string.IsNullOrWhiteSpace(type)) continue;
                string normalized = NormalizeTypeString(type);
                if (Enum.TryParse(normalized, true, out ConnectionTypeMask parsed))
                {
                    result |= parsed;
                }
            }

            return result == ConnectionTypeMask.None ? ConnectionTypeMask.All : result;
        }

        private static string NormalizeTypeString(string type)
        {
            return string.Equals(type, "forrest", StringComparison.OrdinalIgnoreCase) ? "Forest" : type;
        }

        private static Direction ResolveConnections(TileDataFile data, TileComponentData component, string tileDataFolder)
        {
            Direction explicitConnections = ParseConnectionList(component != null ? component.connections : null);
            if (explicitConnections != Direction.None)
            {
                return explicitConnections;
            }

            Direction inferred = InferConnectionsFromDrawing(data);
            if (inferred != Direction.None)
            {
                return inferred;
            }

            Debug.LogWarning($"No connections were defined or inferred for {tileDataFolder}. Defaulting to all directions.");
            return Direction.All;
        }

        private static Direction ParseConnectionList(string[] connections)
        {
            if (connections == null || connections.Length == 0)
            {
                return Direction.None;
            }

            Direction result = Direction.None;
            foreach (string connection in connections)
            {
                if (string.IsNullOrWhiteSpace(connection))
                {
                    continue;
                }

                if (Enum.TryParse(connection, true, out Direction parsed))
                {
                    result |= parsed;
                }
            }

            return result;
        }

        private static Direction InferConnectionsFromDrawing(TileDataFile data)
        {
            if (data == null || data.cells == null || data.cells.Length == 0)
            {
                return Direction.None;
            }

            bool north = HasPaintOnEdge(data.cells, Edge.Top);
            bool east = HasPaintOnEdge(data.cells, Edge.Right);
            bool south = HasPaintOnEdge(data.cells, Edge.Bottom);
            bool west = HasPaintOnEdge(data.cells, Edge.Left);

            Direction result = Direction.None;
            if (north) result |= Direction.North;
            if (east) result |= Direction.East;
            if (south) result |= Direction.South;
            if (west) result |= Direction.West;
            return result;
        }

        private enum Edge
        {
            Top,
            Right,
            Bottom,
            Left
        }

        private static bool HasPaintOnEdge(string[] cells, Edge edge)
        {
            if (cells == null || cells.Length == 0)
            {
                return false;
            }

            int height = cells.Length;
            int width = cells[0]?.Length ?? 0;
            if (width == 0)
            {
                return false;
            }

            switch (edge)
            {
                case Edge.Top:
                    for (int x = 0; x < width; x++)
                    {
                        if (IsPaint(cells[0], x)) return true;
                    }
                    return false;
                case Edge.Bottom:
                    for (int x = 0; x < width; x++)
                    {
                        if (IsPaint(cells[height - 1], x)) return true;
                    }
                    return false;
                case Edge.Left:
                    for (int y = 0; y < height; y++)
                    {
                        if (IsPaint(cells[y], 0)) return true;
                    }
                    return false;
                case Edge.Right:
                    for (int y = 0; y < height; y++)
                    {
                        if (IsPaint(cells[y], width - 1)) return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static bool IsPaint(string row, int index)
        {
            return !string.IsNullOrEmpty(row) && index >= 0 && index < row.Length && row[index] != '0';
        }

        private static List<int> BuildAllowedRotationSteps(TileComponentData component, bool allowRotationVariants)
        {
            var steps = new List<int>();

            if (!allowRotationVariants)
            {
                steps.Add(0);
                return steps;
            }

            if (component != null && component.allowed_rotation_steps != null && component.allowed_rotation_steps.Length > 0)
            {
                foreach (int step in component.allowed_rotation_steps)
                {
                    int normalized = NormalizeRotation(step);
                    if (!steps.Contains(normalized))
                    {
                        steps.Add(normalized);
                    }
                }

                if (steps.Count > 0)
                {
                    return steps;
                }
            }

            steps.Add(0);
            steps.Add(1);
            steps.Add(2);
            steps.Add(3);
            return steps;
        }

        private static Vector2 ResolveGridSize(TileComponentData component, TileDataFile data)
        {
            if (component != null && component.grid_size != null && component.grid_size.Length >= 2)
            {
                float width = Mathf.Max(1, component.grid_size[0]);
                float height = Mathf.Max(1, component.grid_size[1]);
                return new Vector2(width, height);
            }

            if (data != null && data.width > 0 && data.height > 0)
            {
                return new Vector2(data.width, data.height);
            }

            return new Vector2(100f, 100f);
        }

        private static Tile.SnapPlane ResolveSnapPlane(TileComponentData component)
        {
            if (component == null || string.IsNullOrWhiteSpace(component.snap_plane))
            {
                return Tile.SnapPlane.XZ;
            }

            if (Enum.TryParse(component.snap_plane, true, out Tile.SnapPlane parsed))
            {
                return parsed;
            }

            return Tile.SnapPlane.XZ;
        }

        private static string GetPrefabName(string tileDataFolder)
        {
            return SanitizeAssetName(Path.GetFileName(tileDataFolder));
        }

        private static Material GetOrCreateTileMaterial(string tileDataFolder, Texture2D texture)
        {
            string materialPath = Path.Combine(tileDataFolder, "tile.mat").Replace('\\', '/');
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material != null)
            {
                if (texture != null)
                {
                    material.SetTexture(BaseMapId, texture);
                    material.SetTexture(MainTexId, texture);
                    material.mainTexture = texture;
                }
                EditorUtility.SetDirty(material);
                return material;
            }

            if (texture == null)
            {
                return null;
            }

            Material generated = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            generated.name = Path.GetFileName(tileDataFolder);
            generated.SetTexture(BaseMapId, texture);
            generated.SetTexture(MainTexId, texture);
            generated.mainTexture = texture;

            AssetDatabase.CreateAsset(generated, materialPath);
            return generated;
        }

        private static void EnsureTextureReadable(string texturePath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            bool needsReimport = false;

            if (!importer.isReadable)
            {
                importer.isReadable = true;
                needsReimport = true;
            }

            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                needsReimport = true;
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
            }
        }

        private static string SanitizeAssetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "GeneratedTile";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (Array.IndexOf(invalid, c) >= 0)
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Trim();
        }

        private static bool HasTileBundle(string tileDataFolder)
        {
            string jsonPath = Path.Combine(tileDataFolder, "tile.json");
            string texturePath = Path.Combine(tileDataFolder, "tile.png");
            return File.Exists(jsonPath) && File.Exists(texturePath);
        }


        private static void EnsureFolderExists(string assetFolderPath)
        {
            string[] segments = assetFolderPath.Replace('\\', '/').Split('/');
            if (segments.Length == 0 || segments[0] != "Assets")
            {
                return;
            }

            string current = "Assets";
            for (int i = 1; i < segments.Length; i++)
            {
                string next = segments[i];
                string candidate = $"{current}/{next}";
                if (!AssetDatabase.IsValidFolder(candidate))
                {
                    AssetDatabase.CreateFolder(current, next);
                }
                current = candidate;
            }
        }

        private static string AbsoluteToAssetPath(string absolutePath)
        {
            string dataPath = Application.dataPath.Replace('\\', '/');
            string normalized = absolutePath.Replace('\\', '/');
            if (!normalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return "Assets" + normalized.Substring(dataPath.Length);
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            string dataPath = Application.dataPath.Replace('\\', '/');
            string normalized = assetPath.Replace('\\', '/');
            if (!normalized.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return Path.GetFullPath(Path.Combine(dataPath, normalized.Substring("Assets".Length).TrimStart('/')));
        }

        private static int NormalizeRotation(int rotation)
        {
            int normalized = rotation % 4;
            return normalized < 0 ? normalized + 4 : normalized;
        }

        #pragma warning disable IDE1006

        [Serializable]
        private class TileDataFile
        {
            public int width;
            public int height;
            public int[] background_color;
            public int[] paint_color;
            public string[] cells;
            public MockPrefabData mock_prefab;
        }

        [Serializable]
        private class MockPrefabData
        {
            public string prefab_name;
            public string unity_prefab_path;
            public SourceTileData source_tile_data;
            public TileComponentData tile_component;
        }

        [Serializable]
        private class SourceTileData
        {
            public string texture_path;
            public string json_path;
            public int[] grid_size;
            public int[] background_color;
            public int[] paint_color;
        }

        [Serializable]
        private class TileComponentData
        {
            public string texture;
            public string[] connections;
            public int rotation;
            public bool allow_rotation_variants = true;
            public int[] allowed_rotation_steps;
            public int[] grid_size;
            public string snap_plane;
            public string tile_type;
            public TypedConnectionData[] typed_connections;
        }

        [Serializable]
        private class TypedConnectionData
        {
            public string direction;
            public string[] types;
        }

        #pragma warning restore IDE1006
    }
}
