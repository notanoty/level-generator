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
        private List<TileVariant> tileVariants = new List<TileVariant>();
        
        private List<TileVariant>[,] tilePossibilities;
        private TileVariant[,] collapsedTiles;
        private GameObject[,] spawnedTiles;
        
        public void Generate()
        {
        #if UNITY_EDITOR
            RefreshTilePrefabs();
            BuildTileVariants();

            if (tileVariants.Count == 0)
            {
                Debug.LogWarning("No tile variants found. Check your tile prefabs and rotation settings.");
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
            collapsedTiles = new TileVariant[width, height];
            spawnedTiles = new GameObject[width, height];
            
            SetStartTile();

            int maxSteps = width * height;
            for (int i = 0; i < maxSteps; i++)
            {
                if (!HandleWaveFunctionCollapse())
                {
                    break;
                }
            }

            PruneIsolatedTiles();
        #endif
        }


        private void FillTilePossibilitiesArray()
        {
            List<TileVariant> selection = tileVariants;
            if (!useBaseTileInSelection && baseTilePrefab != null)
            {
                selection = new List<TileVariant>(tileVariants);
                selection.RemoveAll(t => t == null || t.Tile == null || t.Tile.gameObject == baseTilePrefab);
            }

            tilePossibilities = new List<TileVariant>[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    List<TileVariant> filtered = new List<TileVariant>(selection);
                    FilterOutwardConnections(filtered, x, y);
                    tilePossibilities[x, y] = filtered;
                }
            }
        }

        private void FilterOutwardConnections(List<TileVariant> candidates, int x, int y)
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

            candidates.RemoveAll(variant =>
                (atNorth && DirectionUtils.Has(variant.RotatedConnections, Direction.North)) ||
                (atSouth && DirectionUtils.Has(variant.RotatedConnections, Direction.South)) ||
                (atWest && DirectionUtils.Has(variant.RotatedConnections, Direction.West)) ||
                (atEast && DirectionUtils.Has(variant.RotatedConnections, Direction.East))
            );
        }
        
        private void SetCalculationForPossibleTiles()
        {
            foreach (TileVariant variant in tileVariants)
            {
                variant.PossibleByDirection.Clear();
                variant.ImpossibleByDirection.Clear();

                foreach (Direction direction in CardinalDirections)
                {
                    variant.PossibleByDirection[direction] = new List<TileVariant>();
                    variant.ImpossibleByDirection[direction] = new List<TileVariant>();

                    foreach (TileVariant other in tileVariants)
                    {
                        bool isCompatible = IsCompatibleWith(variant, other, direction);

                        if (variant.Tile != null
                            && variant.Tile.PossibleTilesByDirection != null
                            && variant.Tile.PossibleTilesByDirection.TryGetValue(direction, out List<Tile> allowedTiles)
                            && allowedTiles != null
                            && allowedTiles.Count > 0
                            && (other == null || other.Tile == null || !allowedTiles.Contains(other.Tile)))
                        {
                            isCompatible = false;
                        }

                        if (isCompatible)
                        {
                            variant.PossibleByDirection[direction].Add(other);
                        }
                        else
                        {
                            variant.ImpossibleByDirection[direction].Add(other);
                        }
                    }
                }
            }
        }

        private void SetStartTile()
        {
            int y = Random.Range(0, height);
            int x = Random.Range(0, width);
            TileVariant variant;
            if (baseTilePrefab)
            {
                Tile baseTile = baseTilePrefab.GetComponent<Tile>();
                variant = FindVariant(baseTile, baseTile != null ? baseTile.rotation : 0);
                if (variant == null)
                {
                    variant = FindFirstVariant(baseTile);
                }

                if (variant != null)
                {
                    tilePossibilities[x, y] = new List<TileVariant> { variant };
                    CollapseNearbyTiles(variant, x, y);
                }
            }
            else
            {
                variant = Collapse(x, y);
            }

            PlaceTile(variant, x, y);
        }

        // ReSharper disable Unity.PerformanceAnalysis
        private TileVariant Collapse(int x, int y)
        {
            // ConstrainCellToCollapsedNeighbors(x, y);
            List<TileVariant> selectedPossibilities = tilePossibilities[x, y];
            if (selectedPossibilities == null || selectedPossibilities.Count == 0)
            {
                Debug.LogWarning("No possibilities left for cell (" + x + ", " + y + ").");
                return null;
            }

            TileVariant variant = selectedPossibilities[Random.Range(0, selectedPossibilities.Count)];
            tilePossibilities[x, y] = new List<TileVariant> { variant };

            CollapseNearbyTiles(variant, x, y);

            return variant;
        }

        private void CollapseNearbyTiles(TileVariant variant, int x, int y)
        {
            if (variant == null) return;

            if (y - 1 >= 0 && tilePossibilities[x, y - 1].Count > 0)
            {
                tilePossibilities[x, y - 1].RemoveAll(t => variant.ImpossibleByDirection[Direction.North].Contains(t));
            }

            if (y + 1 < height && tilePossibilities[x, y + 1].Count > 0)
            {
                tilePossibilities[x, y + 1].RemoveAll(t => variant.ImpossibleByDirection[Direction.South].Contains(t));
            }

            if (x - 1 >= 0 && tilePossibilities[x - 1, y].Count > 0)
            {
                tilePossibilities[x - 1, y].RemoveAll(t => variant.ImpossibleByDirection[Direction.West].Contains(t));
            }

            if (x + 1 < width && tilePossibilities[x + 1, y].Count > 0)
            {
                tilePossibilities[x + 1, y].RemoveAll(t => variant.ImpossibleByDirection[Direction.East].Contains(t));
            }
        }
        
        // ReSharper disable Unity.PerformanceAnalysis
        private bool HandleWaveFunctionCollapse()
        {
            bool collapsedSomething = false;
            
            List<TileVariant> leastEntropyCell = null;
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
                    List<TileVariant> cell = tilePossibilities[x, y];

                    int cellCount = cell.Count;
                    if (cellCount == 0)
                    {
                        Debug.LogWarning("Cell (" + x + ", " + y + ") has no possibilities left. Skipping.");
                        continue;
                    }

                    if (cellCount == 1)
                    {
                        TileVariant variant = Collapse(x, y);
                        PlaceTile(variant, x, y);
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
                TileVariant variant = Collapse(leastEntropyCellX, leastEntropyCellY);
                PlaceTile(variant, leastEntropyCellX, leastEntropyCellY);
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

            List<TileVariant> candidates = tilePossibilities[x, y];
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            // Remove any variants that would not match already-collapsed neighbors.
            if (y - 1 >= 0 && collapsedTiles[x, y - 1] != null)
            {
                TileVariant neighbor = collapsedTiles[x, y - 1];
                candidates.RemoveAll(v => !IsCompatibleWith(v, neighbor, Direction.North));
            }

            if (y + 1 < height && collapsedTiles[x, y + 1] != null)
            {
                TileVariant neighbor = collapsedTiles[x, y + 1];
                candidates.RemoveAll(v => !IsCompatibleWith(v, neighbor, Direction.South));
            }

            if (x - 1 >= 0 && collapsedTiles[x - 1, y] != null)
            {
                TileVariant neighbor = collapsedTiles[x - 1, y];
                candidates.RemoveAll(v => !IsCompatibleWith(v, neighbor, Direction.West));
            }

            if (x + 1 < width && collapsedTiles[x + 1, y] != null)
            {
                TileVariant neighbor = collapsedTiles[x + 1, y];
                candidates.RemoveAll(v => !IsCompatibleWith(v, neighbor, Direction.East));
            }
        }

        private void PlaceTile(TileVariant variant, int x, int y)
        {
            if (variant == null || variant.Tile == null)
            {
                Debug.LogWarning("Trying to place a null tile at (" + x + ", " + y + "). Skipping.");
                return;
            }

            if (collapsedTiles != null && collapsedTiles[x, y] != null)
            {
                return;
            }

            Vector3 position = new Vector3(x * tileSize.x, 0f, y * tileSize.y);
            GameObject instance;
            #if UNITY_EDITOR
            instance = PrefabUtility.InstantiatePrefab(variant.Tile.gameObject) as GameObject;
            #else
            instance = Instantiate(variant.Tile.gameObject);
            #endif
            if (instance != null)
            {
                instance.transform.position = transform.position + position;
                instance.transform.rotation = variant.Tile.transform.rotation * Quaternion.Euler(0f, variant.Rotation * 90f, 0f);

                Transform container = GetOrCreateContainer();
                instance.transform.SetParent(container, true);
                if (spawnedTiles != null)
                {
                    spawnedTiles[x, y] = instance;
                }
                collapsedTiles[x, y] = variant;
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
                        TileVariant variant = collapsedTiles != null ? collapsedTiles[x, y] : null;
                        if (variant == null || IsBaseTileVariant(variant))
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

        private bool IsBaseTileVariant(TileVariant variant)
        {
            return baseTilePrefab != null
                   && variant != null
                   && variant.Tile != null
                   && variant.Tile.gameObject == baseTilePrefab;
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

            foreach (Tile tile in tilePrefabs)
            {
                if (tile == null) continue;
                tile.CalculatePossibleTiles(tilePrefabs);
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

        private void BuildTileVariants()
        {
            tileVariants.Clear();

            foreach (Tile tile in tilePrefabs)
            {
                if (tile == null) continue;

                int baseRotation = NormalizeRotation(tile.rotation);
                List<int> rotations = GetAllowedRotations(tile);
                foreach (int rotation in rotations)
                {
                    int normalized = NormalizeRotation(baseRotation + rotation);
                    Direction rotatedConnections = tile.GetConnectionsForRotation(normalized);

                    tileVariants.Add(new TileVariant
                    {
                        Tile = tile,
                        Rotation = normalized,
                        RotatedConnections = rotatedConnections
                    });
                }
            }
        }

        private static List<int> GetAllowedRotations(Tile tile)
        {
            if (tile == null)
            {
                return new List<int> { 0 };
            }

            if (!tile.allowRotationVariants)
            {
                return new List<int> { 0 };
            }

            if (tile.allowedRotationSteps == null || tile.allowedRotationSteps.Count == 0)
            {
                return new List<int> { 0, 1, 2, 3 };
            }

            List<int> rotations = new List<int>();
            foreach (int rotation in tile.allowedRotationSteps)
            {
                rotations.Add(NormalizeRotation(rotation));
            }

            return rotations;
        }

        private TileVariant FindVariant(Tile tile, int rotation)
        {
            if (tile == null) return null;
            int normalized = NormalizeRotation(rotation);
            return tileVariants.Find(v => v.Tile == tile && v.Rotation == normalized);
        }

        private TileVariant FindFirstVariant(Tile tile)
        {
            if (tile == null) return null;
            return tileVariants.Find(v => v.Tile == tile);
        }

        private static int NormalizeRotation(int rotation)
        {
            int r = rotation % 4;
            return r < 0 ? r + 4 : r;
        }

        private static bool IsCompatibleWith(TileVariant variant, TileVariant other, Direction direction)
        {
            if (variant == null || other == null || variant.Tile == null || other.Tile == null)
            {
                return false;
            }

            Direction thisCon = variant.RotatedConnections;
            Direction otherCon = other.RotatedConnections;
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


            return true;

            // bool byType = IsTypeCompatible(variant, other, direction);
            // bool byExplicitRule = HasExplicitConnection(variant, other, direction)
            // && HasExplicitConnection(other, variant, DirectionUtils.Opposite(direction));
            // return byExplicitRule || byType;
        }

        private static bool IsTypeCompatible(TileVariant variant, TileVariant other, Direction direction)
        {
            ConnectionTypeMask allowedFromThis = variant.Tile.GetAllowedTypesForRotation(direction, variant.Rotation);
            ConnectionTypeMask allowedFromOther = other.Tile.GetAllowedTypesForRotation(DirectionUtils.Opposite(direction), other.Rotation);
            ConnectionTypeMask otherType = Tile.MaskForTileType(other.Tile.tileType);
            ConnectionTypeMask thisType = Tile.MaskForTileType(variant.Tile.tileType);
            return (allowedFromThis & otherType) != 0 && (allowedFromOther & thisType) != 0;
        }

        private static bool HasExplicitConnection(TileVariant variant, TileVariant other, Direction direction)
        {
            List<ConnectedTile> connectedTiles = variant.Tile.connectedTiles;
            if (connectedTiles == null || connectedTiles.Count == 0)
            {
                return false;
            }

            foreach (var connection in connectedTiles)
            {
                if (connection == null || connection.tile == null) continue;
                if (connection.tile != other.Tile) continue;

                Direction rotatedDir = DirectionUtils.Rotate(connection.direction, variant.Rotation);
                if (DirectionUtils.Has(rotatedDir, direction))
                {
                    return true;
                }
            }

            return false;
        }

    }
}