using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace WFC
{
	[DisallowMultipleComponent]
	public class TilePaletteBuilderSettings : MonoBehaviour
	{
		[Serializable]
		public sealed class PaletteColorSetting
		{
			public string id;
			public string purpose;
			public Color32 color = new Color32(255, 255, 255, 255);
			public float height = 1f;
			public TilePaletteEntry.AllowedRotation allowedRotation = TilePaletteEntry.AllowedRotation.All;
			[FormerlySerializedAs("texture")]
			public Material material;
			[FormerlySerializedAs("gameObject")]
			public GameObject[] gameObjects;
		}

		[SerializeField]
		private string defaultId;

		[SerializeField]
		private List<PaletteColorSetting> colors = new List<PaletteColorSetting>();

		[SerializeField]
		private bool initializedFromDefaults;

		public string DefaultId => defaultId;
		public IReadOnlyList<PaletteColorSetting> Colors => colors;

		private void Reset()
		{
			LoadDefaultsFromJson();
		}

		public void ResetPaletteFromJson()
		{
			LoadDefaultsFromJson();
			Debug.Log("Reset Palette From JSON");
		}

		private void OnValidate()
		{
			if (!initializedFromDefaults && colors.Count == 0)
			{
				LoadDefaultsFromJson();
			}

			SaveToJsonFile();
		}

		[ContextMenu("Load Defaults From JSON")]
		private void LoadDefaultsFromJson()
		{
			TilePalette palette;
			try
			{
				palette = TilePalette.LoadDefault();
			}
			catch (Exception exception)
			{
				Debug.LogWarning($"Failed to load default tile palette JSON: {exception.Message}", this);
				return;
			}

			defaultId = palette.DefaultId;
			colors.Clear();

			foreach (TilePaletteEntry entry in palette.Entries)
			{
				colors.Add(new PaletteColorSetting
				{
					id = entry.Id,
					purpose = entry.Purpose,
					color = entry.Color,
					height = entry.Height,
					allowedRotation = entry.AllowedRotationMode,
					gameObjects = null,
					material = null
				});
			}

			initializedFromDefaults = true;
		}

		[ContextMenu("Save To JSON")]
		public void SaveToJsonFile()
		{
			if (!Application.isEditor)
			{
				return;
			}

			if (colors.Count == 0)
			{
				return;
			}

			string jsonPath = Path.GetFullPath(Path.Combine(Application.dataPath, "tile-palette.json"));
			PaletteFile paletteFile = CreatePaletteFile();
			TouchPaletteFile(paletteFile);
			string json = NormalizeLineEndings(JsonUtility.ToJson(paletteFile, true));

			if (File.Exists(jsonPath))
			{
				string existingJson = NormalizeLineEndings(File.ReadAllText(jsonPath));
				if (string.Equals(existingJson, json, StringComparison.Ordinal))
				{
					return;
				}
			}

			File.WriteAllText(jsonPath, json);
			Debug.Log($"Saved tile palette JSON to {jsonPath}", this);
		}

		public TilePalette BuildPalette()
		{
			if (colors.Count == 0)
			{
				LoadDefaultsFromJson();
			}

			List<TilePaletteEntry> entries = new List<TilePaletteEntry>(colors.Count);
			for (int i = 0; i < colors.Count; i++)
			{
				PaletteColorSetting setting = colors[i];
				entries.Add(new TilePaletteEntry(setting.id, setting.purpose, setting.color, setting.height, setting.allowedRotation, setting.gameObjects, setting.material));
			}

			string resolvedDefaultId = string.IsNullOrWhiteSpace(defaultId) && colors.Count > 0 ? colors[0].id : defaultId;
			return TilePalette.Create(resolvedDefaultId, entries);
		}

		private PaletteFile CreatePaletteFile()
		{
			PaletteFile paletteFile = new PaletteFile
			{
				@default = string.IsNullOrWhiteSpace(defaultId) && colors.Count > 0 ? colors[0].id : defaultId,
				colors = new PaletteColorFile[colors.Count]
			};

			for (int i = 0; i < colors.Count; i++)
			{
				PaletteColorSetting setting = colors[i];
				paletteFile.colors[i] = new PaletteColorFile
				{
					id = setting.id,
					purpose = setting.purpose,
					rgba = new int[] { setting.color.r, setting.color.g, setting.color.b, setting.color.a },
					height = setting.height,
					allowedRotation = setting.allowedRotation
				};
			}

			return paletteFile;
		}

		private static void TouchPaletteFile(PaletteFile paletteFile)
		{
			if (paletteFile == null)
			{
				return;
			}

			_ = paletteFile.@default;
			if (paletteFile.colors == null)
			{
				return;
			}

			for (int i = 0; i < paletteFile.colors.Length; i++)
			{
				PaletteColorFile colorFile = paletteFile.colors[i];
				if (colorFile == null)
				{
					continue;
				}

				_ = colorFile.id;
				_ = colorFile.purpose;
				_ = colorFile.rgba;
				_ = colorFile.height;
				_ = colorFile.allowedRotation;
			}
		}

		private static string NormalizeLineEndings(string text)
		{
			return string.IsNullOrEmpty(text)
				? text
				: text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
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
			public float height = 1f;
			public TilePaletteEntry.AllowedRotation allowedRotation = TilePaletteEntry.AllowedRotation.All;
		}
	}
}

