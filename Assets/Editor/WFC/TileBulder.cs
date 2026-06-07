using UnityEditor;
using UnityEngine;

namespace Editor.WFC
{
    public class TileBulder
    {

        public TileBulder(TextureTileBuilder textureTileBuilder)
        {
        }

        public void BuildObject(int x, int y, GameObject tile, Color32 pixelColor, Texture2D texture)
        {
            if (!tile)
            {
                return;
            }

            Renderer tileRenderer = tile.GetComponent<Renderer>();
            if (!tileRenderer)
            {
                return;
            }
            
            if (!texture)
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
            cube.transform.localScale = new Vector3(cellWidth / 10f, 1f, cellDepth / 10f);

#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(cube, "Build Pixel Cube");
#endif

            Renderer cubeRenderer = cube.GetComponent<Renderer>();
            if (cubeRenderer)
            {
                Shader shader = ResolveSupportedShader();
                if (!shader)
                {
                    Debug.LogWarning("No supported shader found for generated cube material.");
                    return;
                }

                Material material = new Material(shader);
                material.color = pixelColor;
                cubeRenderer.sharedMaterial = material;
            }
        }

        public void PersistGeneratedCubeMaterials(GameObject root, string prefabPath)
        {
            string prefabDir = System.IO.Path.GetDirectoryName(prefabPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(prefabDir))
            {
                return;
            }

            string materialsFolder = prefabDir + "/_GeneratedMaterials";
            EnsureFolder(materialsFolder);

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                if (r == null || !r.name.StartsWith("Pixel_"))
                {
                    continue;
                }

                Material src = r.sharedMaterial;
                if (src == null)
                {
                    continue;
                }

                string srcPath = AssetDatabase.GetAssetPath(src);
                if (!string.IsNullOrEmpty(srcPath))
                {
                    continue;
                }

                Color c = src.color;
                string matName = $"Mat_{(int)(c.r * 255f)}_{(int)(c.g * 255f)}_{(int)(c.b * 255f)}_{(int)(c.a * 255f)}.mat";
                string matPath = materialsFolder + "/" + matName;

                Material assetMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (assetMat == null)
                {
                    Shader shader = ResolveSupportedShader();
                    if (shader == null)
                    {
                        continue;
                    }

                    assetMat = new Material(shader);
                    assetMat.color = c;
                    AssetDatabase.CreateAsset(assetMat, matPath);
                }

                r.sharedMaterial = assetMat;
            }
        }

        private static Shader ResolveSupportedShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Standard");
            if (shader != null)
            {
                return shader;
            }

            return Shader.Find("Sprites/Default");
        }

        private static void EnsureFolder(string fullFolder)
        {
            if (AssetDatabase.IsValidFolder(fullFolder))
            {
                return;
            }

            string[] parts = fullFolder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}