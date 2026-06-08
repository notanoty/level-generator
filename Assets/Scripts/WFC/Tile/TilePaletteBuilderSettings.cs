using System;
using System.Collections.Generic;
using UnityEngine;

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

		private void OnValidate()
		{
			if (!initializedFromDefaults && colors.Count == 0)
			{
				LoadDefaultsFromJson();
			}
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
					height = entry.Height
				});
			}

			initializedFromDefaults = true;
		}

		public TilePalette BuildPalette()
		{
			if (colors.Count == 0)
			{
				LoadDefaultsFromJson();
			}

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
					height = setting.height
				};
			}

			return TilePalette.LoadFromJson(JsonUtility.ToJson(paletteFile));
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
		}
	}
}

