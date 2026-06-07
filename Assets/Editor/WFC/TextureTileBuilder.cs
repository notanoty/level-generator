using UnityEditor;
using UnityEngine;
using WFC;

namespace Editor.WFC
{
    public class TextureTileBuilder : EditorWindow
    {
        private GameObject _tileForOne;
        private TilePalette _tilePalette;

        [MenuItem("Tools/WFC/Texture Tile Builder")]
        public static void ShowWindow()
        {
            GetWindow<TextureTileBuilder>("Texture Tile Builder");
        }

        private void OnGUI()
        {
            if (_tilePalette == null)
            {
                _tilePalette = TilePalette.LoadDefault();
            }

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
        }

        private void GenerateOne(GameObject tile)
        {
            if (tile == null)
            {
                Debug.LogWarning("Please assign a GameObject before generating one tile.");
                return;
            }

            Texture2D texture = GetTexture(tile);

            if (texture == null)
            {
                Debug.LogWarning($"No texture found on material for '{tile.name}'.");
                return;
            }

            Debug.Log($"Texture found on '{tile.name}': {texture.name}");
            
            int width = texture.width;
            int height = texture.height;
            Color32[] pixels = texture.GetPixels32();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Color32 pixelColor = pixels[(y * width) + x];
                    if (_tilePalette.TryGetPurpose(pixelColor, out string purpose))
                    {
                        BuildObject(x, y, tile, pixelColor);
                        Debug.Log($"Pixel at ({x}, {y}) has color {pixelColor} which corresponds to purpose '{purpose}' in the palette.");
                    }
                    else
                    {
                        Debug.LogWarning($"Pixel at ({x}, {y}) has color {pixelColor} which does not correspond to any purpose in the palette.");
                    }
                }
            }
            
        }


        
        private void Clear()
        {
        }

        private void BuildObject(int x, int y, GameObject tile, Color32 pixelColor)
        {
            if (tile == null)
            {
                return;
            }

            Renderer tileRenderer = tile.GetComponent<Renderer>();
            if (tileRenderer == null)
            {
                return;
            }

            Texture2D texture = GetTexture(tile);
            if (texture == null)
            {
                return;
            }

            Bounds bounds = tileRenderer.bounds;
            float tileWidth = bounds.size.x;
            float tileDepth = bounds.size.z;
            
            float cellWidth = tileWidth / texture.width;
            float cellDepth = tileDepth / texture.height;

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Pixel_{x}_{y}";
            cube.transform.SetParent(tile.transform, false);
            cube.transform.localPosition = new Vector3(x * cellWidth / 10f - tileWidth * 0.5f + cellWidth * 0.05f, 0, y * cellDepth / 10f - tileDepth * 0.5f + cellDepth * 0.05f);
            cube.transform.localScale = new Vector3(cellWidth / 10f, 0.1f, cellDepth / 10f);

#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(cube, "Build Pixel Cube");
#endif

            Renderer cubeRenderer = cube.GetComponent<Renderer>();
            if (cubeRenderer != null)
            {
                Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (material.shader == null)
                {
                    material = new Material(Shader.Find("Standard"));
                }

                material.color = pixelColor;
                cubeRenderer.sharedMaterial = material;
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
    }
}
