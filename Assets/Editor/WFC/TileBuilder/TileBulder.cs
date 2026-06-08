using System;
using UnityEditor;
using UnityEngine;

namespace Editor.WFC.TileBuilder
{
    public class TileBulder
    {
        public TileBulder(TextureTileBuilder textureTileBuilder)
        {
            _ = textureTileBuilder;
        }

        public void BuildObject(int x, int y, GameObject tile, Color32 pixelColor, Texture2D texture, float height)
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

            GameObject cube = CreateRectangleCube(tile, x, y, 1, 1, cellWidth, cellDepth, tileWidth, tileDepth, height);

#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(cube, "Build Pixel Cube");
#endif

            FixShader(pixelColor, cube);
        }


        public void BuildObjectOptimized(int x, int y, GameObject tile, Color32 pixelColor, float objectHeight,
            float tileWidth, float tileDepth, Color32[] pixels, int width, int height, bool[,] processed)
        {
            if (tileWidth <= 0f || tileDepth <= 0f)
            {
                return;
            }

            const string generatedRootName = "__OptimizedGenerated";
            if (tile.transform.Find(generatedRootName) != null)
            {
                return;
            }


            GetRectangleSize(x, y, processed, pixels, width, height, out int rectangleWidth, out int rectangleHeight);


            float cellWidth = tileWidth / width;
            float cellDepth = tileDepth / height;
            GameObject cube = CreateRectangleCube(tile, x, y, Math.Max(1, rectangleWidth), Math.Max(1, rectangleHeight),
                cellWidth, cellDepth, tileWidth, tileDepth, objectHeight);

#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(cube, "Build Optimized Pixel Cube");
#endif

            FixShader(pixelColor, cube);
        }

        private static void FixShader(Color32 pixelColor, GameObject cube)
        {
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

        private static GameObject CreateRectangleCube(GameObject tile, int x, int y, int rectWidth, int rectHeight,
            float cellWidth, float cellDepth, float tileWidth, float tileDepth, float height)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Pixel_{x}_{y}";
            cube.transform.SetParent(tile.transform, false);
            cube.transform.localPosition = new Vector3(
                x * cellWidth / 10f - tileWidth * 0.5f + cellWidth * rectWidth * 0.05f + 45,
                Mathf.Max(0f, height) * 0.5f,
                y * cellDepth / 10f - tileDepth * 0.5f + cellDepth * rectHeight * 0.05f + 45);
            cube.transform.localScale = new Vector3(cellWidth * rectWidth / 10f, Mathf.Max(0f, height), cellDepth * rectHeight / 10f);
            return cube;
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
                string matName =
                    $"Mat_{(int)(c.r * 255f)}_{(int)(c.g * 255f)}_{(int)(c.b * 255f)}_{(int)(c.a * 255f)}.mat";
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


        private static void GetRectangleSize(int startX, int startY, bool[,] processed, Color32[] pixels, int width,
            int height, out int rectangleWidth, out int rectangleHeight)
        {
            const int maxLoopIterations = 100;

            rectangleWidth = 0;
            rectangleHeight = 0;

            if (processed == null || pixels == null)
            {
                return;
            }

            if (startX < 0 || startY < 0 || startX >= width || startY >= height)
            {
                return;
            }

            if (startX >= processed.GetLength(0) || startY >= processed.GetLength(1) || processed[startX, startY])
            {
                return;
            }

            if (!TryGetPixelColor(pixels, width, height, startX, startY, out Color32 originalColor))
            {
                return;
            }

            int maxWidth = Mathf.Min(maxLoopIterations, Mathf.Min(width - startX, processed.GetLength(0) - startX));
            rectangleWidth = 1;
            for (int candidateWidth = 2; candidateWidth <= maxWidth; candidateWidth++)
            {
                bool canUseWidth = true;
                for (int xOffset = 0; xOffset < candidateWidth; xOffset++)
                {
                    if (!IsCellSameColorAndUnprocessed(processed, pixels, width, height, startX + xOffset, startY,
                            originalColor))
                    {
                        canUseWidth = false;
                        break;
                    }
                }

                if (!canUseWidth)
                {
                    break;
                }

                rectangleWidth = candidateWidth;
            }

            int maxHeight = Mathf.Min(maxLoopIterations, Mathf.Min(height - startY, processed.GetLength(1) - startY));
            rectangleHeight = 1;
            for (int candidateHeight = 2; candidateHeight <= maxHeight; candidateHeight++)
            {
                bool canUseHeight = true;
                int bottomRow = startY + candidateHeight - 1;
                for (int xOffset = 0; xOffset < rectangleWidth; xOffset++)
                {
                    if (!IsCellSameColorAndUnprocessed(processed, pixels, width, height, startX + xOffset, bottomRow,
                            originalColor))
                    {
                        canUseHeight = false;
                        break;
                    }
                }

                if (!canUseHeight)
                {
                    break;
                }

                rectangleHeight = candidateHeight;
            }

            for (int yOffset = 0; yOffset < rectangleHeight; yOffset++)
            {
                for (int xOffset = 0; xOffset < rectangleWidth; xOffset++)
                {
                    processed[startX + xOffset, startY + yOffset] = true;
                }
            }
        }


        private static bool IsCellSameColorAndUnprocessed(bool[,] processed, Color32[] pixels, int width, int height,
            int x, int y, Color32 originalColor)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return false;
            }

            if (x >= processed.GetLength(0) || y >= processed.GetLength(1) || processed[x, y])
            {
                return false;
            }

            return TryGetPixelColor(pixels, width, height, x, y, out Color32 currentColor)
                   && currentColor.r == originalColor.r
                   && currentColor.g == originalColor.g
                   && currentColor.b == originalColor.b
                   && currentColor.a == originalColor.a;
        }

        private static bool TryGetPixelColor(Color32[] pixels, int width, int height, int x, int y, out Color32 color)
        {
            color = default;

            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return false;
            }

            int index = y * width + x;
            if (index < 0 || index >= pixels.Length)
            {
                return false;
            }

            color = pixels[index];
            return true;
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