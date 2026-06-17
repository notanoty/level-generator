using UnityEngine;

namespace WFC
{
    public sealed class TilePaletteEntry
    {
        public enum AllowedRotation
        {
            All = 0,
            Degrees90 = 1
        }

        public string Id { get; }
        public string Purpose { get; }
        public Color32 Color { get; }
        public float Height { get; }
        public AllowedRotation AllowedRotationMode { get; }
        public readonly GameObject[] GameObjects;
        public Material Material { get; }

        internal TilePaletteEntry(string id, string purpose, Color32 color, float height, AllowedRotation allowedRotation = AllowedRotation.All, GameObject[] gameObjects = null, Material material = null)
        {
            Id = id;
            Purpose = purpose;
            Color = color;
            Height = height;
            AllowedRotationMode = allowedRotation;
            GameObjects = gameObjects;
            Material = material;
        }
    }
}