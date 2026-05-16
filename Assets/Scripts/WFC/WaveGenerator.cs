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
        
        public void Generate()
        {
        #if UNITY_EDITOR
            RefreshTilePrefabs();

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
            
            SetStartTile();

            int maxSteps = width * height;
            for (int i = 0; i < maxSteps; i++)
            {
                if (!HandleWaveFunctionCollapse())
                {
                    break;
                }
            }
        #endif
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
                    tilePossibilities[x, y] =  new List<Tile>(selection);
                }
            } 
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
            Tile tile;
            if (baseTilePrefab)
            {
                tile = baseTilePrefab.GetComponent<Tile>();
                tilePossibilities[x, y] = new List<Tile> { tile };
                CollapseNearbyTiles(tile, x, y);
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
            List<Tile> selectedPossibilities = tilePossibilities[x, y];
            if (selectedPossibilities == null || selectedPossibilities.Count == 0)
            {
                Debug.LogWarning("No possibilities left for cell (" + x + ", " + y + ").");
                return null;
            }
            
            Tile tile = selectedPossibilities[Random.Range(0, selectedPossibilities.Count)];
            tilePossibilities[x, y] = new List<Tile> { tile };
            
            CollapseNearbyTiles(tile, x, y);
            
            return tile;
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
                    if(leastEntropyCell == null || cellCount < leastEntropyCell.Count)
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
                Tile tile = Collapse(leastEntropyCellX, leastEntropyCellY);
                PlaceTile(tile, leastEntropyCellX, leastEntropyCellY);
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

            if (collapsedTiles != null && collapsedTiles[x, y] != null)
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
                instance.transform.rotation = Quaternion.identity;

                Transform container = GetOrCreateContainer();
                instance.transform.SetParent(container, true);
                collapsedTiles[x, y] = tile;
            }
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

    }
}