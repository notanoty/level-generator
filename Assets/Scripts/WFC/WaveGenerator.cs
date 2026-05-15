using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WFC
{
    /// <summary>
    /// Simple Wave Function Collapse-esque generator that places tile prefabs from Assets/Prefabs/Tiles into a grid.
    /// Use the custom inspector to "Generate" the level in the Scene view.
    /// </summary>
    [ExecuteAlways]
    public class WaveGenerator : MonoBehaviour
    {
        [Header("Grid Size")]
        public int width = 5;
        public int height = 5;

        [Header("Tiles")]
        [Tooltip("Folder (relative to project) where tile prefabs are stored. Default: Assets/Prefabs/Tiles")]
        public string tilesFolder = "Assets/Prefabs/Tiles";

        [Tooltip("Prefab to place as the starting tile (baseTile). If null the generator will pick one from the folder.")]
        public GameObject baseTilePrefab;

        [Tooltip("Prefab to use for cells where no valid tile can be placed. If null such cells will be left empty.")]
        public GameObject emptyTilePrefab;

        [Tooltip("World size of each tile cell. Defaults to 100x100.")]
        public Vector2 tileSize = new Vector2(100f, 100f);

        [Tooltip("Parent transform under which generated tiles will be placed. If null a new child container will be created.")]
        public Transform parentContainer;

        [Tooltip("Name of the child container GameObject that will hold generated tiles.")]
        public string containerName = "WFC_Generated";

        [Tooltip("If true, clear any previously generated tiles in the container before generating.")]
        public bool clearContainerBeforeGenerate = true;

        // Internal
        private List<GameObject> tilePrefabs = new List<GameObject>();

        /// <summary>
        /// Call this from the editor (or via inspector) to generate the level.
        /// This method will search the project folder defined by <see cref="tilesFolder"/> for prefabs that contain a Tile component.
        /// </summary>
        public void Generate()
        {
#if UNITY_EDITOR
            RefreshTilePrefabs();

            if (tilePrefabs.Count == 0)
            {
                Debug.LogWarning("No tile prefabs found in folder: " + tilesFolder);
                return;
            }

            if (width <= 0 || height <= 0)
            {
                Debug.LogWarning("Width and Height must be > 0");
                return;
            }

            Transform container = null;
            if (parentContainer != null && parentContainer.IsChildOf(transform))
            {
                container = parentContainer;
            }
            else if (parentContainer != null)
            {
                Debug.LogWarning("WaveGenerator parentContainer must be a child of the generator. Falling back to local container.", this);
            }

            if (container == null)
            {
                // find or create container child
                var existing = transform.Find(containerName);
                if (existing != null) container = existing;
                else
                {
                    var go = new GameObject(containerName);
                    Undo.RegisterCreatedObjectUndo(go, "Create WFC Container");
                    go.transform.SetParent(transform);
                    go.transform.localPosition = Vector3.zero;
                    container = go.transform;
                }
            }

            if (clearContainerBeforeGenerate)
            {
                ClearContainer(container);
            }

            // Grid is centered in local space under the container/generator
            float halfWidth = (width - 1) * 0.5f * tileSize.x;
            float halfHeight = (height - 1) * 0.5f * tileSize.y;

            // Prepare 2D array to store placed tiles
            GameObject[,] placed = new GameObject[width, height];

            // Determine base tile position (center)
            int baseX = width / 2;
            int baseY = height / 2;

            // Ensure base tile prefab
            if (baseTilePrefab == null)
            {
                baseTilePrefab = tilePrefabs[Random.Range(0, tilePrefabs.Count)];
            }

            // Place tiles row by row (y then x)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector3 localPos = new Vector3(x * tileSize.x - halfWidth, 0f, y * tileSize.y - halfHeight);

                    GameObject prefabToInstantiate = null;

                    // Determine prefab for this cell. The base cell prefers baseTilePrefab but falls back to any valid tile.
                    if (x == baseX && y == baseY)
                    {
                        bool placedBase = false;
                        if (baseTilePrefab != null)
                        {
                            prefabToInstantiate = baseTilePrefab;
                            placedBase = true;
                        }

                        if (!placedBase)
                        {
                            // fallback to any tile from the tiles list
                            prefabToInstantiate = tilePrefabs[Random.Range(0, tilePrefabs.Count)];
                        }
                    }
                    else
                    {
                        // Build candidate list based on neighbors already placed (left and down)
                        List<GameObject> candidates = new List<GameObject>(tilePrefabs);

                        // Filter by left neighbor
                        if (x - 1 >= 0 && placed[x - 1, y] != null)
                        {
                            var leftTile = placed[x - 1, y].GetComponent<Tile>();
                            candidates = FilterByNeighbor(candidates, leftTile, Direction.East);
                        }

                        // Filter by bottom neighbor (y-1). Here we treat increasing y as north; bottom is south
                        if (y - 1 >= 0 && placed[x, y - 1] != null)
                        {
                            var bottomTile = placed[x, y - 1].GetComponent<Tile>();
                            candidates = FilterByNeighbor(candidates, bottomTile, Direction.North);
                        }


                        if (candidates.Count == 0)
                        {
                            // No matching tile found — leave the spot empty
                            prefabToInstantiate = null;
                        }
                        else
                        {
                            prefabToInstantiate = candidates[Random.Range(0, candidates.Count)];
                        }
                    }

                    // If no prefab was chosen, use the emptyTilePrefab if provided, otherwise leave the cell empty
                    if (prefabToInstantiate == null)
                    {
                        if (emptyTilePrefab != null)
                        {
                            prefabToInstantiate = emptyTilePrefab;
                        }
                        else
                        {
                            // leave empty
                            placed[x, y] = null;
                            continue;
                        }
                    }

                    GameObject instance = null;
                    var prefabInstance = PrefabUtility.InstantiatePrefab(prefabToInstantiate);
                    if (prefabInstance != null) instance = prefabInstance as GameObject;
                    if (instance == null)
                    {
                        // fallback to direct instantiate
                        instance = Instantiate(prefabToInstantiate);
                    }

                    Undo.RegisterCreatedObjectUndo(instance, "WFC Instantiate Tile");

                    if (container != null)
                        instance.transform.SetParent(container, false);
                    instance.transform.localPosition = localPos;
                    instance.transform.localRotation = Quaternion.identity;

                    // If the prefab has a Tile component, ensure its rotation field matches 0
                    var tileComp = instance.GetComponent<Tile>();
                    if (tileComp != null)
                    {
                        // do nothing for now; user controls rotation in prefab
                    }

                    placed[x, y] = instance;
                }
            }

            // Select container in editor for easy inspection
            Selection.activeObject = container.gameObject;
#endif
        }

#if UNITY_EDITOR
        private void RefreshTilePrefabs()
        {
            tilePrefabs.Clear();

            if (string.IsNullOrEmpty(tilesFolder)) tilesFolder = "Assets/Prefabs/Tiles";

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { tilesFolder });
            if (guids == null || guids.Length == 0)
            {
                // Try GameObject search fallback
                guids = AssetDatabase.FindAssets("t:GameObject", new[] { tilesFolder });
            }

            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                if (go.GetComponent<Tile>() == null) continue;
                tilePrefabs.Add(go);
            }
        }

        private List<GameObject> FilterByNeighbor(List<GameObject> candidates, Tile neighbor, Direction requiredDirectionFromNeighbor)
        {
            if (neighbor == null) return candidates;
            List<GameObject> res = new List<GameObject>();
            foreach (var prefab in candidates)
            {
                if (prefab == null) continue;
                var tile = prefab.GetComponent<Tile>();
                if (tile == null) continue;

                // neighbor must be compatible with tile in requiredDirectionFromNeighbor
                // e.g. if neighbor is left, requiredDirectionFromNeighbor == East (neighbor's east must connect to candidate)
                bool a = neighbor.IsCompatibleWith(tile, requiredDirectionFromNeighbor);
                bool b = tile.IsCompatibleWith(neighbor, DirectionUtils.Opposite(requiredDirectionFromNeighbor));
                if (a && b) res.Add(prefab);
            }
            return res;
        }

        private void ClearContainer(Transform container)
        {
            if (container == null) return;
            // Destroy children with Undo support
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i).gameObject;
                Undo.DestroyObjectImmediate(child);
            }
        }
#endif

    }
}


