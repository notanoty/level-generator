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

        private List<Tile> tilePrefabs = new List<Tile>();

        private List<Tile>[,] tilePossibilities;
        private Tile[,] collapsedTiles;
        private GameObject[,] spawnedTiles;

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
            
            SetStartTile();

            for (int i = 0; i < 1000; i++)
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
                    List<Tile> filtered = new List<Tile>(selection);
                    FilterOutwardConnections(filtered, x, y);
                    tilePossibilities[x, y] = filtered;
                }
            }
        }

        private void FilterOutwardConnections(List<Tile> candidates, int x, int y)
        {
            if (candidates == null) return;

            bool atNorth = y == 0;
            bool atSouth = y == height - 1;
            bool atWest = x == 0;
            bool atEast = x == width - 1;

            if (!(atNorth || atSouth || atWest || atEast))
            {
                return;
            }

            candidates.RemoveAll(tile => tile == null ||
                (atNorth && DirectionUtils.Has(tile.connections, Direction.North)) ||
                (atSouth && DirectionUtils.Has(tile.connections, Direction.South)) ||
                (atWest && DirectionUtils.Has(tile.connections, Direction.West)) ||
                (atEast && DirectionUtils.Has(tile.connections, Direction.East))
            );
        }
        
        private void SetCalculationForPossibleTiles()
        {
            foreach (Tile tile in tilePrefabs)
            {
                if (tile == null) continue;

                tile.PossibleTilesByDirection.Clear();
                tile.ImpossibleTilesByDirection.Clear();

                foreach (Direction direction in CardinalDirections)
                {
                    tile.PossibleTilesByDirection[direction] = new List<Tile>();
                    tile.ImpossibleTilesByDirection[direction] = new List<Tile>();

                    foreach (Tile other in tilePrefabs)
                    {
                        bool isCompatible = IsCompatibleWith(tile, other, direction);

                        if (isCompatible)
                        {
                            tile.PossibleTilesByDirection[direction].Add(other);
                        }
                        else
                        {
                            tile.ImpossibleTilesByDirection[direction].Add(other);
                        }
                    }
                }
            }
        }

        private void SetStartTile()
        {
            int y = Random.Range(0, height);
            int x = Random.Range(0, width);
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

            foreach (var tile in selectedPossibilities)
            {
                Debug.Log("Possible tile: " + tile.name);
            }

            Tile tileChoice = selectedPossibilities[Random.Range(0, selectedPossibilities.Count)];
            tilePossibilities[x, y] = new List<Tile> { tileChoice };

            CollapseNearbyTiles(tileChoice, x, y);

            return tileChoice;
        }

        private void CollapseNearbyTiles(Tile tile, int x, int y)
        {
            if (tile == null) return;

            if (y - 1 >= 0 && tilePossibilities[x, y - 1].Count > 0)
            {
                tilePossibilities[x, y - 1].RemoveAll(t => tile.ImpossibleTilesByDirection[Direction.North].Contains(t));
            }

            if (y + 1 < height && tilePossibilities[x, y + 1].Count > 0)
            {
                tilePossibilities[x, y + 1].RemoveAll(t => tile.ImpossibleTilesByDirection[Direction.South].Contains(t));
            }

            if (x - 1 >= 0 && tilePossibilities[x - 1, y].Count > 0)
            {
                tilePossibilities[x - 1, y].RemoveAll(t => tile.ImpossibleTilesByDirection[Direction.West].Contains(t));
            }

            if (x + 1 < width && tilePossibilities[x + 1, y].Count > 0)
            {
                tilePossibilities[x + 1, y].RemoveAll(t => tile.ImpossibleTilesByDirection[Direction.East].Contains(t));
            }
        }
        
        // ReSharper disable Unity.PerformanceAnalysis
        private bool HandleWaveFunctionCollapse()
        {
            bool collapsedSomething = false;
            
            List<Tile> leastEntropyCell = null;
            int leastEntropyCellX = 0, leastEntropyCellY = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (collapsedTiles[x, y] != null)
                    {
                        continue;
                    }

                    ConstrainCellToCollapsedNeighbors(x, y);
                    List<Tile> cell = tilePossibilities[x, y];

                    int cellCount = cell.Count;
                    if (cellCount == 0)
                    {
                        Debug.LogWarning("Cell (" + x + ", " + y + ") has no possibilities left. Skipping.");
                        continue;
                    }

                    if (cellCount == 1)
                    {
                        Tile tile = Collapse(x, y);
                        PlaceTile(tile, x, y);
                        collapsedSomething = true;
                        continue;
                    }
                    if (leastEntropyCell == null || cellCount < leastEntropyCell.Count)
                    {
                        leastEntropyCell = cell;
                        leastEntropyCellX = x;
                        leastEntropyCellY = y;
                    }
                }
            }

            if (collapsedSomething)
            {
                return true;
            }

            if (leastEntropyCell != null)
            {
                Debug.Log($"Cell position: X {leastEntropyCellX} Y {leastEntropyCellY}");
                Tile tile = Collapse(leastEntropyCellX, leastEntropyCellY);
                PlaceTile(tile, leastEntropyCellX, leastEntropyCellY);
                return true;
            }

            return false;
        }

        private void ConstrainCellToCollapsedNeighbors(int x, int y)
        {
            if (tilePossibilities == null || collapsedTiles == null)
            {
                return;
            }

            List<Tile> candidates = tilePossibilities[x, y];
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            // Remove any tiles that would not match already-collapsed neighbors.
            if (y - 1 >= 0 && collapsedTiles[x, y - 1] != null)
            {
                Tile neighbor = collapsedTiles[x, y - 1];
                candidates.RemoveAll(v => !IsCompatibleWith(v, neighbor, Direction.North));
            }

            if (y + 1 < height && collapsedTiles[x, y + 1] != null)
            {
                Tile neighbor = collapsedTiles[x, y + 1];
                candidates.RemoveAll(v => !IsCompatibleWith(v, neighbor, Direction.South));
            }

            if (x - 1 >= 0 && collapsedTiles[x - 1, y] != null)
            {
                Tile neighbor = collapsedTiles[x - 1, y];
                candidates.RemoveAll(v => !IsCompatibleWith(v, neighbor, Direction.West));
            }

            if (x + 1 < width && collapsedTiles[x + 1, y] != null)
            {
                Tile neighbor = collapsedTiles[x + 1, y];
                candidates.RemoveAll(v => !IsCompatibleWith(v, neighbor, Direction.East));
            }
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
                instance.transform.rotation = tile.transform.rotation;

                Transform container = GetOrCreateContainer();
                instance.transform.SetParent(container, true);
                if (spawnedTiles != null)
                {
                    spawnedTiles[x, y] = instance;
                }
                collapsedTiles[x, y] = tile;
                ValidateConnectionsAt(x, y);
                
            }
        }

        private void ValidateConnectionsAt(int x, int y)
        {
            if (collapsedTiles == null)
            {
                return;
            }

            Tile center = collapsedTiles[x, y];
            if (center == null)
            {
                return;
            }

            ValidateNeighborConnection(center, x, y, x, y - 1, Direction.North);
            ValidateNeighborConnection(center, x, y, x + 1, y, Direction.East);
            ValidateNeighborConnection(center, x, y, x, y + 1, Direction.South);
            ValidateNeighborConnection(center, x, y, x - 1, y, Direction.West);
        }

        private void ValidateNeighborConnection(Tile center, int cx, int cy, int nx, int ny, Direction directionFromCenter)
        {
            if (nx < 0 || nx >= width || ny < 0 || ny >= height)
            {
                return;
            }

            Tile neighbor = collapsedTiles[nx, ny];
            if (neighbor == null)
            {
                return;
            }

            bool isCompatible = IsCompatibleWith(center, neighbor, directionFromCenter)
                && IsCompatibleWith(neighbor, center, DirectionUtils.Opposite(directionFromCenter));

            if (isCompatible)
            {
                Debug.Log($"Tiles connected correctly: ({cx},{cy}) -> ({nx},{ny}) dir={directionFromCenter}");
            }
            else
            {
                Debug.LogWarning($"Tiles connected incorrectly: ({cx},{cy}) -> ({nx},{ny}) dir={directionFromCenter}");
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

        private static readonly Direction[] CardinalDirections =
        {
            Direction.North,
            Direction.East,
            Direction.South,
            Direction.West
        };

        private static bool IsCompatibleWith(Tile tile, Tile other, Direction direction)
        {
            if (tile == null || other == null)
            {
                return false;
            }

            Direction thisCon = tile.connections;
            Direction otherCon = other.connections;
            bool thisHas = DirectionUtils.Has(thisCon, direction);
            bool otherHas = DirectionUtils.Has(otherCon, DirectionUtils.Opposite(direction));

            if (thisHas != otherHas)
            {
                return false;
            }

            if (!thisHas && !otherHas)
            {
                return true;
            }

            bool byType = IsTypeCompatible(tile, other, direction);
            bool byExplicitRule = HasExplicitConnection(tile, other, direction)
                                  && HasExplicitConnection(other, tile, DirectionUtils.Opposite(direction));
            return byExplicitRule || byType;
        }

        private static bool IsTypeCompatible(Tile tile, Tile other, Direction direction)
        {
            ConnectionTypeMask allowedFromThis = tile.GetAllowedTypesForDirection(direction);
            ConnectionTypeMask allowedFromOther = other.GetAllowedTypesForDirection(DirectionUtils.Opposite(direction));
            ConnectionTypeMask otherType = Tile.MaskForTileType(other.tileType);
            ConnectionTypeMask thisType = Tile.MaskForTileType(tile.tileType);
            return (allowedFromThis & otherType) != 0 && (allowedFromOther & thisType) != 0;
        }

        private static bool HasExplicitConnection(Tile tile, Tile other, Direction direction)
        {
            List<ConnectedTile> connectedTiles = tile.connectedTiles;
            if (connectedTiles == null || connectedTiles.Count == 0)
            {
                return false;
            }

            foreach (var connection in connectedTiles)
            {
                if (connection == null || connection.tile == null) continue;
                if (connection.tile != other) continue;

                if (DirectionUtils.Has(connection.direction, direction))
                {
                    return true;
                }
            }

            return false;
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