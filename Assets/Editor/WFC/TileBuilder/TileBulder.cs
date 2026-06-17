using System;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Editor.WFC.TileBuilder
{
    public class TileBulder
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

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
            Material material, float tileWidth, float tileDepth, Color32[] pixels, int width, int height, bool[,] processed)
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

            FixShader(pixelColor, cube, material);
        }

        public void BuildObject(int x, int y, GameObject tile, GameObject model, float objectHeight,
            float tileWidth, float tileDepth, int width, int height)
        {
            if (!tile || !model)
            {
                return;
            }

            if (tileWidth <= 0f || tileDepth <= 0f || width <= 0 || height <= 0)
            {
                return;
            }

            GameObject instance = InstantiateSourceObject(model, tile);
            if (instance == null)
            {
                return;
            }

            instance.name = $"{model.name}_{x}_{y}";
            float cellWidth = tileWidth / width;
            float cellDepth = tileDepth / height;
            Vector3 parentScale = GetSafeScale(tile.transform.lossyScale);
            Vector3 localPosition = GetCellLocalPosition(x, y, 1, 1, cellWidth, cellDepth, tileWidth, tileDepth,
                0f);

            instance.transform.localScale = new Vector3(
                instance.transform.localScale.x / parentScale.x,
                instance.transform.localScale.y / parentScale.y,
                instance.transform.localScale.z / parentScale.z);

            instance.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            instance.transform.localPosition = new Vector3(
                localPosition.x,
                0f,
                localPosition.z);

            Bounds worldBounds = CalculateCombinedBounds(instance);
            if (worldBounds.size != Vector3.zero)
            {
                float targetBottomHeight = Mathf.Max(0f, objectHeight);
                float yOffset = (targetBottomHeight - worldBounds.min.y) / parentScale.y;
                instance.transform.localPosition += new Vector3(0f, yOffset, 0f);
            }
            else
            {
                instance.transform.localPosition = new Vector3(
                    localPosition.x,
                    Mathf.Max(0f, objectHeight),
                    localPosition.z);
            }

#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(instance, "Build Pixel Model");
#endif
        }

        private static void FixShader(Color32 pixelColor, GameObject cube, Material sourceMaterial = null)
        {
            Renderer cubeRenderer = cube.GetComponent<Renderer>();
            if (cubeRenderer)
            {
                if (sourceMaterial != null)
                {
                    cubeRenderer.sharedMaterial = sourceMaterial;
                    return;
                }

                Shader shader = ResolveSupportedShader();
                if (!shader)
                {
                    Debug.LogWarning("No supported shader found for generated cube material.");
                    return;
                }

                Material material = new Material(shader);
                ApplyMaterialAppearance(material, pixelColor, null);
                cubeRenderer.sharedMaterial = material;
            }
        }

        private static void ApplyMaterialAppearance(Material material, Color color, Texture2D texture)
        {
            if (material == null)
            {
                return;
            }

            if (texture != null)
            {
                if (material.HasProperty(BaseMapId))
                {
                    material.SetTexture(BaseMapId, texture);
                }

                if (material.HasProperty(MainTexId))
                {
                    material.SetTexture(MainTexId, texture);
                }

                material.mainTexture = texture;
                material.color = Color.white;
                return;
            }

            if (material.HasProperty(BaseMapId))
            {
                material.SetTexture(BaseMapId, null);
            }

            if (material.HasProperty(MainTexId))
            {
                material.SetTexture(MainTexId, null);
            }

            material.mainTexture = null;
            material.color = color;
        }

        private static GameObject CreateRectangleCube(GameObject tile, int x, int y, int rectWidth, int rectHeight,
            float cellWidth, float cellDepth, float tileWidth, float tileDepth, float height)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Pixel_{x}_{y}";
            cube.transform.SetParent(tile.transform, false);
            cube.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            cube.transform.localPosition = GetCellLocalPosition(x, y, rectWidth, rectHeight, cellWidth, cellDepth,
                tileWidth, tileDepth, height);
            cube.transform.localScale = new Vector3(cellWidth * rectWidth / 10f, Mathf.Max(0f, height), cellDepth * rectHeight / 10f);
            return cube;
        }

        private static GameObject InstantiateSourceObject(GameObject source, GameObject parent)
        {
            GameObject instance;

#if UNITY_EDITOR
            if (PrefabUtility.IsPartOfPrefabAsset(source))
            {
                instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            }
            else
            {
                instance = UnityEngine.Object.Instantiate(source);
            }
#else
            instance = UnityEngine.Object.Instantiate(source);
#endif

            if (instance == null)
            {
                return null;
            }

            instance.transform.SetParent(parent.transform, false);
            return instance;
        }

        private static Vector3 GetCellLocalPosition(int x, int y, int rectWidth, int rectHeight, float cellWidth,
            float cellDepth, float tileWidth, float tileDepth, float height)
        {
            int cellsWide = Mathf.Max(rectWidth, Mathf.RoundToInt(tileWidth / cellWidth));
            int cellsDeep = Mathf.Max(rectHeight, Mathf.RoundToInt(tileDepth / cellDepth));
            int mirroredX = Mathf.Max(0, cellsWide - x - rectWidth);
            int mirroredY = Mathf.Max(0, cellsDeep - y - rectHeight);

            return new Vector3(
                mirroredX * cellWidth / 10f - tileWidth * 0.5f + cellWidth * rectWidth * 0.05f + 45,
                Mathf.Max(0f, height) * 0.5f,
                mirroredY * cellDepth / 10f - tileDepth * 0.5f + cellDepth * rectHeight * 0.05f + 45);
        }

        private static Vector3 GetSafeScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Abs(scale.x) < 0.0001f ? 1f : scale.x,
                Mathf.Abs(scale.y) < 0.0001f ? 1f : scale.y,
                Mathf.Abs(scale.z) < 0.0001f ? 1f : scale.z);
        }

        private static Bounds CalculateCombinedBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return default;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            return bounds;
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
                Texture2D srcTexture = src.mainTexture as Texture2D;
                string textureKey = GetTextureKey(srcTexture);
                string matName =
                    $"Mat_{(int)(c.r * 255f)}_{(int)(c.g * 255f)}_{(int)(c.b * 255f)}_{(int)(c.a * 255f)}_{textureKey}.mat";
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
                    ApplyMaterialAppearance(assetMat, c, srcTexture);
                    AssetDatabase.CreateAsset(assetMat, matPath);
                }

                ApplyMaterialAppearance(assetMat, c, srcTexture);
                EditorUtility.SetDirty(assetMat);

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

        private static string GetTextureKey(Texture2D texture)
        {
            if (texture == null)
            {
                return "NoTex";
            }

            string texturePath = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(texturePath))
            {
                string guid = AssetDatabase.AssetPathToGUID(texturePath);
                if (!string.IsNullOrEmpty(guid))
                {
                    return guid;
                }

                return SanitizeForAssetName(texturePath);
            }

            return SanitizeForAssetName(texture.name);
        }

        private static string SanitizeForAssetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unnamed";
            }

            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            string sanitized = value;
            for (int i = 0; i < invalid.Length; i++)
            {
                sanitized = sanitized.Replace(invalid[i], '_');
            }

            return sanitized.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
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