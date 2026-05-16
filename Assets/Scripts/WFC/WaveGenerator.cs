using System.Collections.Generic;
using Unity.VisualScripting;
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
        public GameObject emptyTilePrefab;

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
        
        public void Generate()
        {
        #if UNITY_EDITOR
            FillTilePossibilitiesArray();
            
            SetStartTile();
            
        #endif
        }

        private void FillTilePossibilitiesArray()
        {
            if (!useBaseTileInSelection)
            {
                tilePrefabs.RemoveAll(t => t.gameObject == baseTilePrefab);
            }
            
            tilePossibilities = new List<Tile>[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    tilePossibilities[x, y] =  new List<Tile>(tilePrefabs);
                }
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
            }
            else
            {
                tile = Collapse(x, y);
            }

            PlaceTile(tile, x, y);
        }

        private Tile Collapse(int x, int y)
        {
            List<Tile> selectedPossibilities = tilePossibilities[x, y];
            if (selectedPossibilities == null || selectedPossibilities.Count == 0)
            {
                Debug.LogWarning("No possibilities left for cell (" + x + ", " + y + "). Placing empty tile.");
                return null;
            }
            
            Tile tile = Random.Range(0, selectedPossibilities.Count) is int index ? selectedPossibilities[index] : null;
            
            tilePossibilities[x, y] = new List<Tile> { tile };
            
            CollapseNearbyTiles(tile, x, y);
            
            //This should also remove possibilities of the nearby cells 
            
            return tile;
        }

        private void CollapseNearbyTiles(Tile tile, int x, int y)
        {
            // tilePrefabs - 
        }

        private void PlaceTile(Tile tile, int x, int y)
        {
            if (tile == null)
            {
                Debug.LogWarning("Trying to place a null tile at (" + x + ", " + y + "). Skipping.");
                return;
            }

            Vector3 position = new Vector3(x * tileSize.x, 0f, y * tileSize.y);
            GameObject instance = PrefabUtility.InstantiatePrefab(tile.gameObject) as GameObject;
            if (instance != null)
            {
                instance.transform.position = transform.position + position;
                instance.transform.rotation = Quaternion.identity;

                if (parentContainer != null)
                {
                    instance.transform.SetParent(parentContainer, true);
                }
                else
                {
                    Transform container = transform.Find(containerName);
                    if (container == null)
                    {
                        GameObject containerGO = new GameObject(containerName);
                        containerGO.transform.SetParent(transform, false);
                        container = containerGO.transform;
                    }
                    instance.transform.SetParent(container, true);
                }
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

    }
}


