using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WFC
{
    public sealed class TilePaletteEntry
    {
        public string Id { get; }
        public string Purpose { get; }
        public Color32 Color { get; }

        internal TilePaletteEntry(string id, string purpose, Color32 color)
        {
            Id = id;
            Purpose = purpose;
            Color = color;
        }
    }

    public sealed class TilePalette
    {
        public const string DefaultAssetPath = "Assets/tile-palette.json";
        private const int ColorMatchTolerance = 10;

        private readonly string _defaultId;
        private readonly List<TilePaletteEntry> _entries;
        private readonly Dictionary<string, TilePaletteEntry> _entriesById;
        private readonly Dictionary<string, TilePaletteEntry> _entriesByPurpose;
        private readonly Dictionary<int, TilePaletteEntry> _entriesByColor;

        public string DefaultId => _defaultId;
        public IReadOnlyList<TilePaletteEntry> Entries => _entries;
        public TilePaletteEntry DefaultEntry => TryGet(DefaultId, out TilePaletteEntry entry) ? entry : null;

        private TilePalette(string defaultId, List<TilePaletteEntry> entries, Dictionary<string, TilePaletteEntry> entriesById, Dictionary<string, TilePaletteEntry> entriesByPurpose, Dictionary<int, TilePaletteEntry> entriesByColor)
        {
            _defaultId = defaultId;
            _entries = entries;
            _entriesById = entriesById;
            _entriesByPurpose = entriesByPurpose;
            _entriesByColor = entriesByColor;
        }

        public static TilePalette LoadFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Palette JSON cannot be null or empty.", nameof(json));
            }

            PaletteFile paletteFile = JsonUtility.FromJson<PaletteFile>(json);
            if (paletteFile == null)
            {
                throw new FormatException("Palette JSON could not be parsed.");
            }

            return FromPaletteFile(paletteFile);
        }

        public static TilePalette LoadFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Palette file path cannot be null or empty.", nameof(filePath));
            }

            string normalizedPath = Path.GetFullPath(filePath);
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException($"Palette file not found: {normalizedPath}", normalizedPath);
            }

            return LoadFromJson(File.ReadAllText(normalizedPath));
        }

        public static TilePalette LoadFromAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentException("Asset path cannot be null or empty.", nameof(assetPath));
            }

            string normalized = assetPath.Replace('\\', '/');
            string absolutePath = normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(Path.Combine(Application.dataPath, normalized.Substring("Assets/".Length)))
                : Path.GetFullPath(normalized);

            return LoadFromFile(absolutePath);
        }

        public static TilePalette LoadDefault()
        {
            return LoadFromAssetPath(DefaultAssetPath);
        }

        public bool Contains(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && _entriesById.ContainsKey(id.Trim());
        }

        public bool TryGet(string id, out TilePaletteEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            return _entriesById.TryGetValue(id.Trim(), out entry);
        }

        public TilePaletteEntry Get(string id)
        {
            if (TryGet(id, out TilePaletteEntry entry))
            {
                return entry;
            }

            throw new KeyNotFoundException($"Palette id not found: {id}");
        }

        public bool TryGetColor(string id, out Color32 color)
        {
            if (TryGet(id, out TilePaletteEntry entry))
            {
                color = entry.Color;
                return true;
            }

            color = default;
            return false;
        }

        public Color32 GetColor(string id)
        {
            return Get(id).Color;
        }

        public bool TryGetPurpose(string id, out string purpose)
        {
            if (TryGet(id, out TilePaletteEntry entry))
            {
                purpose = entry.Purpose;
                return true;
            }

            purpose = null;
            return false;
        }

        public bool TryGetByPurpose(string purpose, out TilePaletteEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(purpose))
            {
                return false;
            }

            return _entriesByPurpose.TryGetValue(purpose.Trim(), out entry);
        }

        public bool TryGetColorByPurpose(string purpose, out Color32 color)
        {
            if (TryGetByPurpose(purpose, out TilePaletteEntry entry))
            {
                color = entry.Color;
                return true;
            }

            color = default;
            return false;
        }

        public Color32 GetColorByPurpose(string purpose)
        {
            return TryGetColorByPurpose(purpose, out Color32 color)
                ? color
                : throw new KeyNotFoundException($"Palette purpose not found: {purpose}");
        }

        public bool TryGet(Color32 color, out TilePaletteEntry entry)
        {
            if (_entriesByColor.TryGetValue(PackColor(color), out entry))
            {
                return true;
            }

            int bestScore = int.MaxValue;
            TilePaletteEntry bestEntry = null;

            for (int i = 0; i < _entries.Count; i++)
            {
                TilePaletteEntry candidate = _entries[i];
                if (!IsWithinTolerance(color, candidate.Color))
                {
                    continue;
                }

                int score = GetColorDistanceScore(color, candidate.Color);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestEntry = candidate;
                }
            }

            entry = bestEntry;
            return entry != null;
        }

        public bool TryGetPurpose(Color32 color, out string purpose)
        {
            if (TryGet(color, out TilePaletteEntry entry))
            {
                purpose = entry.Purpose;
                return true;
            }

            purpose = null;
            return false;
        }

        public string GetPurpose(Color32 color)
        {
            return TryGet(color, out TilePaletteEntry entry)
                ? entry.Purpose
                : throw new KeyNotFoundException($"Palette color not found: {color}");
        }

        public string GetPurpose(string id)
        {
            return Get(id).Purpose;
        }

        private static TilePalette FromPaletteFile(PaletteFile paletteFile)
        {
            if (paletteFile.colors == null || paletteFile.colors.Length == 0)
            {
                throw new FormatException("Palette file must contain at least one color entry.");
            }

            var entries = new List<TilePaletteEntry>(paletteFile.colors.Length);
            var entriesById = new Dictionary<string, TilePaletteEntry>(StringComparer.OrdinalIgnoreCase);
            var entriesByPurpose = new Dictionary<string, TilePaletteEntry>(StringComparer.OrdinalIgnoreCase);
            var entriesByColor = new Dictionary<int, TilePaletteEntry>();

            foreach (PaletteColorFile colorFile in paletteFile.colors)
            {
                if (colorFile == null)
                {
                    continue;
                }

                string id = (colorFile.id ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new FormatException("Palette entry is missing an id.");
                }

                if (entriesById.ContainsKey(id))
                {
                    throw new FormatException($"Duplicate palette id found: {id}");
                }

                string purpose = string.IsNullOrWhiteSpace(colorFile.purpose) ? id : colorFile.purpose.Trim();
                if (entriesByPurpose.ContainsKey(purpose))
                {
                    throw new FormatException($"Duplicate palette purpose found: {purpose}");
                }

                Color32 color = ParseColor(colorFile.rgba, id);
                int packedColor = PackColor(color);
                if (entriesByColor.ContainsKey(packedColor))
                {
                    throw new FormatException($"Duplicate palette color found for id '{id}'.");
                }

                TilePaletteEntry entry = new TilePaletteEntry(id, purpose, color);
                entries.Add(entry);
                entriesById.Add(id, entry);
                entriesByPurpose.Add(purpose, entry);
                entriesByColor.Add(packedColor, entry);
            }

            if (entries.Count == 0)
            {
                throw new FormatException("Palette file did not contain any valid color entries.");
            }

            string defaultId = string.IsNullOrWhiteSpace(paletteFile.@default) ? entries[0].Id : paletteFile.@default.Trim();
            if (!entriesById.ContainsKey(defaultId))
            {
                throw new FormatException($"Default palette id '{defaultId}' is not defined in the colors array.");
            }

            return new TilePalette(defaultId, entries, entriesById, entriesByPurpose, entriesByColor);
        }

        private static Color32 ParseColor(int[] rgba, string id)
        {
            if (rgba == null || rgba.Length < 3)
            {
                throw new FormatException($"Palette entry '{id}' must define an 'rgba' array with at least 3 values.");
            }

            byte r = ClampToByte(rgba[0]);
            byte g = ClampToByte(rgba[1]);
            byte b = ClampToByte(rgba[2]);
            byte a = rgba.Length >= 4 ? ClampToByte(rgba[3]) : (byte)255;
            return new Color32(r, g, b, a);
        }

        private static byte ClampToByte(int value)
        {
            return (byte)Mathf.Clamp(value, 0, 255);
        }

        private static int PackColor(Color32 color)
        {
            return (color.r << 24) | (color.g << 16) | (color.b << 8) | color.a;
        }

        private static bool IsWithinTolerance(Color32 first, Color32 second)
        {
            return Mathf.Abs(first.r - second.r) <= ColorMatchTolerance
                && Mathf.Abs(first.g - second.g) <= ColorMatchTolerance
                && Mathf.Abs(first.b - second.b) <= ColorMatchTolerance
                && Mathf.Abs(first.a - second.a) <= ColorMatchTolerance;
        }

        private static int GetColorDistanceScore(Color32 first, Color32 second)
        {
            int dr = first.r - second.r;
            int dg = first.g - second.g;
            int db = first.b - second.b;
            int da = first.a - second.a;

            return (dr * dr) + (dg * dg) + (db * db) + (da * da);
        }

        [Serializable]
        private sealed class PaletteFile
        {
            public string @default;
            public PaletteColorFile[] colors;
        }

        [Serializable]
        private sealed class PaletteColorFile
        {
            public string id;
            public string purpose;
            public int[] rgba;
        }
    }
}

