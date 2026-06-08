using UnityEngine;

namespace WFC
{
    public sealed class TilePaletteEntry
    {
        public string Id { get; }
        public string Purpose { get; }
        public Color32 Color { get; }
        public float Height { get; }

        internal TilePaletteEntry(string id, string purpose, Color32 color, float height)
        {
            Id = id;
            Purpose = purpose;
            Color = color;
            Height = height;
        }
    }
}