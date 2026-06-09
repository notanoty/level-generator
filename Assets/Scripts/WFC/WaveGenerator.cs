using System.Collections.Generic;
using System.Text;
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
    public partial class WaveGenerator : MonoBehaviour
    {
        [Header("Grid Size")]
        public int width = 5;
        public int height = 5;

        [Header("Tiles")]
        [Tooltip("Folder (relative to project) where tile prefabs are stored. Default: Assets/Prefabs/Tiles")]
        public string tilesFolder = "Assets/Prefabs/Tiles";

        [Tooltip("Prefab to place as the starting tile (baseTile). If null the generator will pick one from the folder.")]
        public GameObject baseTilePrefab;

        public bool useBaseTileInSelection;

        [Tooltip("Legacy fallback prefab. Generator now always places a regular tile when constraints fail.")]
        public Tile emptyTilePrefab;

        [Tooltip("World size of each tile cell. Defaults to 100x100.")]
        public Vector2 tileSize = new Vector2(100f, 100f);

        [Tooltip("Parent transform under which generated tiles will be placed. If null a new child container will be created.")]
        public Transform parentContainer;

        [Tooltip("Name of the child container GameObject that will hold generated tiles.")]
        public string containerName = "WFC_Generated";

        [Tooltip("If true, clear any previously generated tiles in the container before generating.")]
        public bool clearContainerBeforeGenerate = true;

        [Header("Debug")]
        [Tooltip("If true, spawn a small cube marker at each collapsed tile position.")]
        public bool showCollapseMarkers = true;

        [Tooltip("Relative size of the debug marker cube compared to the tile size.")]
        [Range(0.01f, 1f)]
        public float collapseMarkerScale = 0.1f;
        
        public int maxTileDepth;

        private List<Tile> tilePrefabs = new List<Tile>();

        private List<Tile>[,] tilePossibilities;
        private Tile[,] collapsedTiles;
        private GameObject[,] spawnedTiles;
        private int _startTileX = -1;
        private int _startTileY = -1;
        private bool _hasStartTile;
        private int _placementOrder;
        private static readonly Direction[] PropagationDirections =
        {
            Direction.North,
            Direction.East,
            Direction.South,
            Direction.West
        };

        public void Generate()
        {
        #if UNITY_EDITOR
            RefreshTilePrefabs();

            if (tilePrefabs.Count == 0)
            {
                Debug.LogWarning("No tile prefabs found. Check your tile prefabs and folder settings.");
                return;
            }

            if (width <= 0 || height <= 0)
            {
                Debug.LogWarning("Width and height must be greater than zero.");
                return;
            }

            Transform container = GetOrCreateContainer();
            if (clearContainerBeforeGenerate)
            {
                ClearContainer(container);
            }

            SetCalculationForPossibleTiles();
            FillTilePossibilitiesArray();
            collapsedTiles = new Tile[width, height];
            spawnedTiles = new GameObject[width, height];
            _placementOrder = 0;
            
            SetStartTile();

            for (int i = 0; i < maxTileDepth; i++)
            {
                if (!HandleWaveFunctionCollapse())
                {
                    break;
                }
            }

            LogTilePossibilities();
            PruneIsolatedTiles();
        #endif
        }

        public void LogTilePossibilities()
        {
#if UNITY_EDITOR
            if ((tilePrefabs == null || tilePrefabs.Count == 0))
            {
                RefreshTilePrefabs();
            }
#endif
            if (tilePrefabs == null || tilePrefabs.Count == 0)
            {
                Debug.LogWarning("No tile prefabs to log. Generate once or ensure prefabs are loaded.");
                return;
            }

            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine($"Tiles: {tilePrefabs.Count}");
            for (int i = 0; i < tilePrefabs.Count; i++)
            {
                Tile tile = tilePrefabs[i];
                sb.AppendLine($"[{i}] {FormatTileDetails(tile)}");
            }

            if (tilePossibilities != null)
            {
                sb.AppendLine("Grid possibilities:");
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        List<Tile> cell = tilePossibilities[x, y];
                        int count = cell != null ? cell.Count : 0;
                        sb.AppendLine($"Cell ({x},{y}) count={count}");
                        if (cell == null) continue;
                        for (int i = 0; i < cell.Count; i++)
                        {
                            sb.AppendLine($"  - {FormatTileSummary(cell[i])}");
                        }
                    }
                }
            }

            Debug.Log(sb.ToString());
        }

        private void FillTilePossibilitiesArray()
        {
            List<Tile> selection = tilePrefabs;
            if (!useBaseTileInSelection && baseTilePrefab != null)
            {
                selection = new List<Tile>(tilePrefabs);
                selection.RemoveAll(t => t == null || t.gameObject == baseTilePrefab);
            }

            tilePossibilities = new List<Tile>[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    List<Tile> candidates = new List<Tile>(selection);
                    FilterOutwardConnections(candidates, x, y);
                    tilePossibilities[x, y] = candidates;
                }
            }
        }

        private void FilterOutwardConnections(List<Tile> candidates, int x, int y)
        {
            if (candidates == null) return;

            bool atNorth = y == height - 1;
            bool atSouth = y == 0;
            bool atWest = x == 0;
            bool atEast = x == width - 1;

            if (!(atNorth || atSouth || atWest || atEast))
            {
                return;
            }

            candidates.RemoveAll(tile => tile == null ||
                (atNorth && tile.ConnectsTo(Direction.North)) ||
                (atSouth && tile.ConnectsTo(Direction.South)) ||
                (atWest && tile.ConnectsTo(Direction.West)) ||
                (atEast && tile.ConnectsTo(Direction.East))
            );
        }

        private void SetCalculationForPossibleTiles()
        {
            foreach (Tile tile in tilePrefabs)
            {
                tile.CalculatePossibleTiles(tilePrefabs);
            }
        }

        private void SetStartTile()
        {
            int y = Random.Range(0, height);
            int x = Random.Range(0, width);
            _startTileX = x;
            _startTileY = y;
            _hasStartTile = true;
            Tile tile;
            if (baseTilePrefab != null)
            {
                tile = baseTilePrefab.GetComponent<Tile>();
                if (tile != null)
                {
                    tilePossibilities[x, y] = new List<Tile> { tile };
                    CollapseNearbyTiles(tile, x, y);
                }
            }
            else
            {
                tile = Collapse(x, y);
            }

            PlaceTile(tile, x, y);
        }

        // ReSharper disable Unity.PerformanceAnalysis
        private Tile Collapse(int x, int y)
        {
            // ConstrainCellToCollapsedNeighbors(x, y);
            List<Tile> selectedPossibilities = tilePossibilities[x, y];
            if (selectedPossibilities == null || selectedPossibilities.Count == 0)
            {
                Debug.LogWarning("No possibilities left for cell (" + x + ", " + y + ").");
                return null;
            }


            Tile tileChoice = selectedPossibilities[Random.Range(0, selectedPossibilities.Count)];
            tilePossibilities[x, y] = new List<Tile> { tileChoice };

            CollapseNearbyTiles(tileChoice, x, y);

            return tileChoice;
        }

        private void CollapseNearbyTiles(Tile tile, int x, int y)
        {
            if (tile == null || tilePossibilities == null)
            {
                return;
            }

            PropagateConstraintsFrom(x, y);
        }

        private void PropagateConstraintsFrom(int startX, int startY)
        {
            if (tilePossibilities == null)
            {
                return;
            }

            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            bool[] queued = new bool[width * height];

            Enqueue(startX, startY);

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                queued[GetCellIndex(cell.x, cell.y)] = false;

                List<Tile> sourceCandidates = tilePossibilities[cell.x, cell.y];
                if (sourceCandidates == null || sourceCandidates.Count == 0)
                {
                    continue;
                }

                for (int i = 0; i < PropagationDirections.Length; i++)
                {
                    Direction direction = PropagationDirections[i];
                    Vector2Int neighbor = GetNeighbor(cell.x, cell.y, direction);
                    if (!IsInBounds(neighbor.x, neighbor.y))
                    {
                        continue;
                    }

                    List<Tile> neighborCandidates = tilePossibilities[neighbor.x, neighbor.y];
                    if (neighborCandidates == null || neighborCandidates.Count == 0)
                    {
                        continue;
                    }

                    int beforeCount = neighborCandidates.Count;
                    neighborCandidates.RemoveAll(candidate => !IsCompatibleWithAnySource(sourceCandidates, candidate, direction));

                    if (neighborCandidates.Count != beforeCount)
                    {
                        if (showCollapseMarkers)
                        {
                            CreateCollapseMarker(neighbor.x, neighbor.y, direction, GetOrCreateContainer());
                        }

                        if (neighborCandidates.Count > 0 && !queued[GetCellIndex(neighbor.x, neighbor.y)])
                        {
                            Enqueue(neighbor.x, neighbor.y);
                        }
                    }
                }
            }

            void Enqueue(int x, int y)
            {
                int index = GetCellIndex(x, y);
                if (!IsInBounds(x, y) || queued[index])
                {
                    return;
                }

                queue.Enqueue(new Vector2Int(x, y));
                queued[index] = true;
            }
        }

        private static bool IsCompatibleWithAnySource(List<Tile> sourceCandidates, Tile candidate, Direction direction)
        {
            if (candidate == null || sourceCandidates == null)
            {
                return false;
            }

            for (int i = 0; i < sourceCandidates.Count; i++)
            {
                Tile source = sourceCandidates[i];
                if (source != null && source.IsCompatibleWith(candidate, direction))
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector2Int GetNeighbor(int x, int y, Direction direction)
        {
            if (direction == Direction.North) return new Vector2Int(x, y + 1);
            if (direction == Direction.East) return new Vector2Int(x + 1, y);
            if (direction == Direction.South) return new Vector2Int(x, y - 1);
            if (direction == Direction.West) return new Vector2Int(x - 1, y);

            return new Vector2Int(x, y);
        }

        private bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }

        private int GetCellIndex(int x, int y)
        {
            return x + (y * width);
        }

        private void CreateCollapseMarker(int x, int y, Direction direction, Transform container)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = $"({x}, {y})_{direction}";

            marker.transform.position = transform.position + new Vector3(x * tileSize.x, 0f, y * tileSize.y);

            float markerSize = Mathf.Max(0.01f, Mathf.Min(tileSize.x, tileSize.y) * collapseMarkerScale);
            marker.transform.localScale = new Vector3(markerSize, markerSize, markerSize);

            Collider markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(markerCollider);
                }
                else
                {
                    DestroyImmediate(markerCollider);
                }
            }

            if (container != null)
            {
                marker.transform.SetParent(container, true);
            }
        }
        
        // ReSharper disable Unity.PerformanceAnalysis
        private bool HandleWaveFunctionCollapse()
        {
            List<Vector2Int> leastEntropyCells = null;
            int leastEntropyCount = int.MaxValue;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (collapsedTiles[x, y] != null)
                    {
                        continue;
                    }

                    List<Tile> cell = tilePossibilities[x, y];

                    int cellCount = cell.Count;
                    if (cellCount == 0)
                    {
                        Debug.LogWarning("Cell (" + x + ", " + y + ") has no possibilities left. Generation cannot continue.");
                        return false;
                    }

                    if (cellCount < leastEntropyCount)
                    {
                        leastEntropyCount = cellCount;
                        leastEntropyCells = new List<Vector2Int> { new Vector2Int(x, y) };
                    }
                    else if (cellCount == leastEntropyCount)
                    {
                        if (leastEntropyCells == null)
                        {
                            leastEntropyCells = new List<Vector2Int>();
                        }
                        leastEntropyCells.Add(new Vector2Int(x, y));
                    }
                }
            }

            if (leastEntropyCells != null && leastEntropyCells.Count > 0)
            {
                Vector2Int chosenCell = leastEntropyCells[Random.Range(0, leastEntropyCells.Count)];
                Debug.Log($"Cell position: X {chosenCell.x} Y {chosenCell.y}");
                Tile tile = Collapse(chosenCell.x, chosenCell.y);
                PlaceTile(tile, chosenCell.x, chosenCell.y);
                return true;
            }

            return false;
        }


        private void PlaceTile(Tile tile, int x, int y)
        {
            if (tile == null)
            {
                Debug.LogWarning("Trying to place a null tile at (" + x + ", " + y + "). Skipping.");
                return;
            }

            if (collapsedTiles == null || spawnedTiles == null)
            {
                collapsedTiles = collapsedTiles ?? new Tile[width, height];
                spawnedTiles = spawnedTiles ?? new GameObject[width, height];
            }

            if (collapsedTiles[x, y] != null)
            {
                return;
            }

            Vector3 position = new Vector3(x * tileSize.x, 0f, y * tileSize.y);
            GameObject instance;
            #if UNITY_EDITOR
            instance = PrefabUtility.InstantiatePrefab(tile.gameObject) as GameObject;
            #else
            instance = Instantiate(tile.gameObject);
            #endif
            if (instance != null)
            {
                instance.transform.position = transform.position + position;

                Transform container = GetOrCreateContainer();
                instance.transform.SetParent(container, true);
                instance.transform.rotation = Quaternion.identity;

                CreatePlacementCube(instance.transform);

                if (spawnedTiles != null)
                {
                    spawnedTiles[x, y] = instance;
                }
                collapsedTiles[x, y] = tile;
            }
        }

        private void CreatePlacementCube(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            int placementIndex = ++_placementOrder;
            GameObject placementCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placementCube.name = $"Placement_{placementIndex}";
            placementCube.transform.SetParent(parent, false);
            placementCube.transform.localPosition = Vector3.zero;
            placementCube.transform.localRotation = Quaternion.identity;

            float markerSize = Mathf.Max(0.05f, Mathf.Min(tileSize.x, tileSize.y) * 0.15f);
            placementCube.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);

            Collider placementCollider = placementCube.GetComponent<Collider>();
            if (placementCollider != null)
            {
#if UNITY_EDITOR
                DestroyImmediate(placementCollider);
#else
                Destroy(placementCollider);
#endif

            }
        }


        private void PruneIsolatedTiles()
        {
            bool removedAny;

            do
            {
                removedAny = false;

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        Tile tile = collapsedTiles != null ? collapsedTiles[x, y] : null;
                        if (tile == null || IsBaseTile(tile))
                        {
                            continue;
                        }

                        if (!IsSurroundedByEmptyTiles(x, y))
                        {
                            continue;
                        }

                        RemovePlacedTile(x, y);
                        removedAny = true;
                    }
                }
            }
            while (removedAny);
        }

        private bool IsSurroundedByEmptyTiles(int x, int y)
        {
            return IsEmptyCell(x, y - 1)
                && IsEmptyCell(x + 1, y)
                && IsEmptyCell(x, y + 1)
                && IsEmptyCell(x - 1, y);
        }

        private bool IsEmptyCell(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return true;
            }

            return collapsedTiles == null || collapsedTiles[x, y] == null;
        }

        private void RemovePlacedTile(int x, int y)
        {
            GameObject instance = spawnedTiles != null ? spawnedTiles[x, y] : null;
            if (instance != null)
            {
#if UNITY_EDITOR
                Undo.DestroyObjectImmediate(instance);
#else
                DestroyImmediate(instance);
#endif
            }

            if (spawnedTiles != null)
            {
                spawnedTiles[x, y] = null;
            }

            if (collapsedTiles != null)
            {
                collapsedTiles[x, y] = null;
            }
        }

        private bool IsBaseTile(Tile tile)
        {
            return baseTilePrefab != null
                   && tile != null
                   && tile.gameObject == baseTilePrefab;
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
                if (!go.GetComponent<Tile>()) continue;
                tilePrefabs.Add(go.GetComponent<Tile>());
            }
        }

        private void ClearContainer(Transform container)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i).gameObject;
                Undo.DestroyObjectImmediate(child);
            }
        }
        #endif

        private Transform GetOrCreateContainer()
        {
            if (parentContainer != null)
            {
                return parentContainer;
            }

            Transform container = transform.Find(containerName);
            if (container == null)
            {
                GameObject containerGO = new GameObject(containerName);
                containerGO.transform.SetParent(transform, false);
                container = containerGO.transform;
            }

            return container;
        }

        private static string FormatTileDetails(Tile tile)
        {
            if (tile == null)
            {
                return "<null tile>";
            }

            int connectedCount = tile.connectedTiles != null ? tile.connectedTiles.Count : 0;
            int possibleNorth = tile.PossibleTilesByDirection != null && tile.PossibleTilesByDirection.TryGetValue(Direction.North, out var pn) ? pn.Count : 0;
            int possibleEast = tile.PossibleTilesByDirection != null && tile.PossibleTilesByDirection.TryGetValue(Direction.East, out var pe) ? pe.Count : 0;
            int possibleSouth = tile.PossibleTilesByDirection != null && tile.PossibleTilesByDirection.TryGetValue(Direction.South, out var ps) ? ps.Count : 0;
            int possibleWest = tile.PossibleTilesByDirection != null && tile.PossibleTilesByDirection.TryGetValue(Direction.West, out var pw) ? pw.Count : 0;

            return $"{tile.name} | connections={tile.connections} | type={tile.tileType} | connectedTiles={connectedCount} | possibleByDir(N/E/S/W)={possibleNorth}/{possibleEast}/{possibleSouth}/{possibleWest}";
        }

        private static string FormatTileSummary(Tile tile)
        {
            if (tile == null)
            {
                return "<null>";
            }

            return $"{tile.name} (connections={tile.connections}, type={tile.tileType})";
        }
    }
}