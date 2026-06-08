from __future__ import annotations

import argparse
import json
import re
import shutil
import struct
import tempfile
import tkinter as tk
from dataclasses import dataclass
from pathlib import Path
from tkinter import messagebox, ttk
from typing import List, Sequence, Tuple
import zlib


RGBA = Tuple[int, int, int, int]

DEFAULT_PALETTE_PATH = Path(__file__).resolve().parent / "tile-palette.json"
DIRECTION_OPTIONS = ("North", "East", "South", "West")
PATHWAY_COLOR_ID = "pathway"
CONNECTION_SLOT_COUNT = 10
CONNECTION_MARKER_RGBA: RGBA = (255, 0, 0, 255)
BRUSH_SHAPES = ("Square", "Circle")
TOOL_BRUSH = "Brush"
TOOL_FILL = "Fill"

DARK_BG = "#1f1f1f"
DARK_PANEL = "#2a2a2a"
DARK_PANEL_ALT = "#303030"
DARK_TEXT = "#f0f0f0"
DARK_ACCENT = "#4fa3ff"
DARK_BORDER = "#555555"

DEFAULT_WIDTH = 64
DEFAULT_HEIGHT = 64
DEFAULT_CELL_SIZE = 24
GALLERY_PANEL_WIDTH = 180
GALLERY_PREVIEW_MAX = 96
ROTATION_STEPS = (0, 1, 2, 3)
ROTATION_LABELS = {0: "0°", 1: "90°", 2: "180°", 3: "270°"}


def clamp(value: int, low: int, high: int) -> int:
	return max(low, min(high, value))


def rgba_to_hex(color: RGBA) -> str:
	return "#%02x%02x%02x" % color[:3]


def normalize_rotation_steps(steps: Sequence[int] | None) -> List[int]:
	if not steps:
		return list(ROTATION_STEPS)

	normalized: List[int] = []
	seen: set[int] = set()
	for step in steps:
		try:
			value = int(step)
		except (TypeError, ValueError):
			continue
		if value in ROTATION_STEPS and value not in seen:
			seen.add(value)
			normalized.append(value)
	return normalized


@dataclass(frozen=True)
class PaletteColor:
	id: str
	purpose: str
	rgba: RGBA
	height: float = 1.0


@dataclass
class TilePalette:
	default_id: str
	colors: Tuple[PaletteColor, ...]
	_by_id: dict[str, PaletteColor]

	@classmethod
	def load(cls, path: Path) -> "TilePalette":
		payload = json.loads(path.read_text(encoding="utf-8"))
		default_id = str(payload.get("default", "")).strip()
		raw_colors = payload.get("colors")
		if not isinstance(raw_colors, list) or not raw_colors:
			raise ValueError("Palette file must contain a non-empty 'colors' array.")

		colors: List[PaletteColor] = []
		seen_ids: set[str] = set()
		for index, entry in enumerate(raw_colors):
			if not isinstance(entry, dict):
				raise ValueError(f"Palette color at index {index} must be an object.")
			color_id = str(entry.get("id", "")).strip()
			if not color_id:
				raise ValueError(f"Palette color at index {index} is missing 'id'.")
			if color_id in seen_ids:
				raise ValueError(f"Duplicate palette id: {color_id}")
			seen_ids.add(color_id)

			purpose = str(entry.get("purpose", color_id)).strip() or color_id
			rgba_raw = entry.get("rgba")
			if not isinstance(rgba_raw, list) or len(rgba_raw) < 3:
				raise ValueError(f"Palette color '{color_id}' must define 'rgba' with at least 3 values.")
			components = [clamp(int(value), 0, 255) for value in rgba_raw[:3]]
			alpha = 255 if len(rgba_raw) < 4 else clamp(int(rgba_raw[3]), 0, 255)
			height_raw = entry.get("height", 1)
			height = float(height_raw)
			if not (height == height and height not in (float("inf"), float("-inf"))):
				raise ValueError(f"Palette color '{color_id}' must define a finite 'height' value.")
			colors.append(PaletteColor(color_id, purpose, (components[0], components[1], components[2], alpha), height))

		if default_id and default_id not in seen_ids:
			raise ValueError(f"Default palette id '{default_id}' is not defined in colors.")
		if not default_id:
			default_id = colors[0].id

		by_id = {color.id: color for color in colors}
		return cls(default_id, tuple(colors), by_id)

	def color(self, color_id: str) -> PaletteColor:
		return self._by_id[color_id]

	def rgba(self, color_id: str) -> RGBA:
		return self.color(color_id).rgba

	def is_valid(self, color_id: str) -> bool:
		return color_id in self._by_id


def slot_indices_along_axis(length: int, count: int) -> List[int]:
	if length <= 0:
		return []
	if length <= count:
		return list(range(length))
	start = (length - count) // 2
	return list(range(start, start + count))


def connection_cells_for_direction(width: int, height: int, direction: str) -> List[Tuple[int, int]]:
	width = max(1, int(width))
	height = max(1, int(height))
	if direction == "North":
		y = 0
		return [(x, y) for x in slot_indices_along_axis(width, CONNECTION_SLOT_COUNT)]
	if direction == "South":
		y = height - 1
		return [(x, y) for x in slot_indices_along_axis(width, CONNECTION_SLOT_COUNT)]
	if direction == "West":
		x = 0
		return [(x, y) for y in slot_indices_along_axis(height, CONNECTION_SLOT_COUNT)]
	if direction == "East":
		x = width - 1
		return [(x, y) for y in slot_indices_along_axis(height, CONNECTION_SLOT_COUNT)]
	return []


def connection_slot_positions(width: int, height: int, directions: Sequence[str]) -> List[Tuple[int, int]]:
	seen: set[Tuple[int, int]] = set()
	positions: List[Tuple[int, int]] = []
	for direction in directions:
		if direction not in DIRECTION_OPTIONS:
			continue
		for pos in connection_cells_for_direction(width, height, direction):
			if pos not in seen:
				seen.add(pos)
				positions.append(pos)
	return positions


def sanitize_name(name: str) -> str:
	cleaned = re.sub(r"[^A-Za-z0-9._-]+", "_", name.strip())
	cleaned = cleaned.strip("._-")
	return cleaned or "Tile"


def list_tile_folders(output_root: Path) -> List[Path]:
	if not output_root.is_dir():
		return []

	folders: List[Path] = []
	for entry in sorted(output_root.iterdir(), key=lambda path: path.name.lower()):
		if not entry.is_dir():
			continue
		if (entry / "tile.json").is_file() and (entry / "tile.png").is_file():
			folders.append(entry)
	return folders


def unique_folder(base: Path, tile_name: str) -> Path:
	base.mkdir(parents=True, exist_ok=True)
	folder = base / sanitize_name(tile_name)
	if not folder.exists():
		return folder

	suffix = 1
	while True:
		candidate = base / f"{folder.name}_{suffix}"
		if not candidate.exists():
			return candidate
		suffix += 1


def rotate_pixels_180(width: int, height: int, pixels: Sequence[RGBA]) -> List[RGBA]:
	if len(pixels) != width * height:
		raise ValueError("Pixel buffer size does not match image dimensions.")

	rotated: List[RGBA] = [pixels[0]] * (width * height)
	for y in range(height):
		for x in range(width):
			src_x = width - 1 - x
			src_y = height - 1 - y
			rotated[y * width + x] = pixels[src_y * width + src_x]
	return rotated


def write_png(path: Path, width: int, height: int, pixels: Sequence[RGBA]) -> None:
	if width <= 0 or height <= 0:
		raise ValueError("PNG dimensions must be greater than zero.")
	if len(pixels) != width * height:
		raise ValueError("Pixel buffer size does not match image dimensions.")

	raw = bytearray()
	for y in range(height):
		raw.append(0)  # no filter
		row_start = y * width
		for x in range(width):
			raw.extend(pixels[row_start + x])

	def chunk(chunk_type: bytes, data: bytes) -> bytes:
		return (
			struct.pack(">I", len(data))
			+ chunk_type
			+ data
			+ struct.pack(">I", zlib.crc32(chunk_type + data) & 0xFFFFFFFF)
		)

	ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
	compressed = zlib.compress(bytes(raw), level=9)
	png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", compressed) + chunk(b"IEND", b"")
	path.write_bytes(png)


@dataclass
class TileModel:
	palette: TilePalette
	width: int = DEFAULT_WIDTH
	height: int = DEFAULT_HEIGHT

	def __post_init__(self) -> None:
		self.width = max(1, int(self.width))
		self.height = max(1, int(self.height))
		default = self.palette.default_id
		self.cells: List[List[str]] = [[default for _ in range(self.width)] for _ in range(self.height)]

	def resize(self, width: int, height: int) -> None:
		width = max(1, int(width))
		height = max(1, int(height))
		default = self.palette.default_id
		new_cells = [[default for _ in range(width)] for _ in range(height)]

		copy_height = min(self.height, height)
		copy_width = min(self.width, width)
		for y in range(copy_height):
			for x in range(copy_width):
				cell_id = self.cells[y][x]
				new_cells[y][x] = cell_id if self.palette.is_valid(cell_id) else default

		self.width = width
		self.height = height
		self.cells = new_cells

	def clear(self) -> None:
		default = self.palette.default_id
		for y in range(self.height):
			for x in range(self.width):
				self.cells[y][x] = default

	def set_cell(self, x: int, y: int, color_id: str) -> None:
		if 0 <= x < self.width and 0 <= y < self.height and self.palette.is_valid(color_id):
			self.cells[y][x] = color_id

	def cell_color_id(self, x: int, y: int) -> str:
		if 0 <= x < self.width and 0 <= y < self.height:
			cell_id = self.cells[y][x]
			if self.palette.is_valid(cell_id):
				return cell_id
		return self.palette.default_id

	def brush_cells(self, center_x: int, center_y: int, size: int, shape: str) -> List[Tuple[int, int]]:
		size = max(1, int(size))
		shape = shape.capitalize()
		half = size // 2
		start_x = center_x - half
		start_y = center_y - half
		cells: List[Tuple[int, int]] = []

		for offset_y in range(size):
			for offset_x in range(size):
				if shape == "Circle":
					dx = offset_x - half
					dy = offset_y - half
					radius = size / 2.0
					if (dx * dx + dy * dy) > (radius * radius):
						continue
				cells.append((start_x + offset_x, start_y + offset_y))

		return cells

	def paint_brush(self, center_x: int, center_y: int, color_id: str, size: int = 1, shape: str = "Square") -> int:
		if not self.palette.is_valid(color_id):
			return 0

		changed = 0
		for x, y in self.brush_cells(center_x, center_y, size, shape):
			if 0 <= x < self.width and 0 <= y < self.height and self.cells[y][x] != color_id:
				self.cells[y][x] = color_id
				changed += 1
		return changed

	def flood_fill(self, start_x: int, start_y: int, color_id: str) -> int:
		if not (0 <= start_x < self.width and 0 <= start_y < self.height):
			return 0
		if not self.palette.is_valid(color_id):
			return 0

		target = self.cells[start_y][start_x]
		if target == color_id:
			return 0

		stack = [(start_x, start_y)]
		changed = 0

		while stack:
			x, y = stack.pop()
			if not (0 <= x < self.width and 0 <= y < self.height):
				continue
			if self.cells[y][x] != target:
				continue

			self.cells[y][x] = color_id
			changed += 1
			stack.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))

		return changed

	def connection_slots(self, connections: Sequence[str]) -> List[Tuple[int, int]]:
		return connection_slot_positions(self.width, self.height, connections)

	def unpainted_connection_slots(self, connections: Sequence[str]) -> List[Tuple[int, int]]:
		missing: List[Tuple[int, int]] = []
		for x, y in self.connection_slots(connections):
			if self.cell_color_id(x, y) != PATHWAY_COLOR_ID:
				missing.append((x, y))
		return missing

	def pixels(self) -> List[RGBA]:
		flat: List[RGBA] = []
		for y in range(self.height):
			for x in range(self.width):
				flat.append(self.palette.rgba(self.cell_color_id(x, y)))
		return flat

	def cells_as_color_ids(self) -> List[List[str]]:
		return [list(row) for row in self.cells]

	@classmethod
	def load_from_folder(cls, folder: Path, palette: TilePalette) -> Tuple["TileModel", List[str], List[int]]:
		json_path = folder / "tile.json"
		if not json_path.is_file():
			raise ValueError(f"Missing tile.json in {folder}")

		payload = json.loads(json_path.read_text(encoding="utf-8"))
		width = max(1, int(payload.get("width", DEFAULT_WIDTH)))
		height = max(1, int(payload.get("height", DEFAULT_HEIGHT)))
		raw_cells = payload.get("cells")
		if not isinstance(raw_cells, list):
			raise ValueError("tile.json is missing a valid 'cells' array.")

		model = cls(palette, width=width, height=height)
		default_id = palette.default_id
		for y, row in enumerate(raw_cells):
			if y >= model.height or not isinstance(row, list):
				continue
			for x, cell_id in enumerate(row):
				if x >= model.width:
					break
				color_id = str(cell_id).strip()
				if palette.is_valid(color_id):
					model.cells[y][x] = color_id
				else:
					model.cells[y][x] = default_id

		connections: List[str] = []
		allowed_rotation_steps = list(ROTATION_STEPS)
		mock = payload.get("mock_prefab")
		if isinstance(mock, dict):
			component = mock.get("tile_component")
			if isinstance(component, dict):
				raw_connections = component.get("connections")
				if isinstance(raw_connections, list):
					connections = [direction for direction in raw_connections if direction in DIRECTION_OPTIONS]
				raw_rotations = component.get("allowed_rotation_steps")
				if isinstance(raw_rotations, list):
					allowed_rotation_steps = normalize_rotation_steps(raw_rotations)

		return model, connections, allowed_rotation_steps

	def to_json(self, connections: Sequence[str], allowed_rotation_steps: Sequence[int] | None = None) -> dict:
		normalized_connections = [direction for direction in connections if direction in DIRECTION_OPTIONS]
		normalized_rotations = normalize_rotation_steps(allowed_rotation_steps)
		default_rgba = list(self.palette.rgba(self.palette.default_id))
		return {
			"width": self.width,
			"height": self.height,
			"palette": DEFAULT_PALETTE_PATH.name,
			"default_color": self.palette.default_id,
			"background_color": default_rgba,
			"cells": self.cells_as_color_ids(),
			"mock_prefab": {
				"prefab_name": "TilePrefab_TestVariant",
				"unity_prefab_path": "Assets/Prefabs/Tiles/TilePrefab_TestVariant.prefab",
				"source_tile_data": {
					"texture_path": "Assets/TileData/TilePrefab_TestVariant/tile.png",
					"json_path": "Assets/TileData/TilePrefab_TestVariant/tile.json",
					"grid_size": [self.width, self.height],
					"background_color": default_rgba,
					"palette": DEFAULT_PALETTE_PATH.name,
				},
				"tile_component": {
					"texture": "tile.png",
					"connections": normalized_connections,
					"rotation": 0,
					"allowed_rotation_steps": normalized_rotations,
					"grid_size": [100, 100],
					"snap_plane": "XZ",
				},
			},
		}

	def save(
		self,
		tile_name: str,
		output_root: Path,
		connections: Sequence[str],
		allowed_rotation_steps: Sequence[int] | None = None,
		*,
		target_folder: Path | None = None,
	) -> Path:
		missing = self.unpainted_connection_slots(connections)
		if missing:
			sample = ", ".join(f"({x}, {y})" for x, y in missing[:6])
			extra = f" and {len(missing) - 6} more" if len(missing) > 6 else ""
			raise ValueError(
				"Paint every red connection marker with pathway before saving. "
				f"Unpainted slots: {sample}{extra}."
			)

		if target_folder is not None:
			folder = target_folder
			folder.mkdir(parents=True, exist_ok=True)
		else:
			folder = unique_folder(output_root, tile_name)
			folder.mkdir(parents=True, exist_ok=False)

		(folder / "tile.json").write_text(json.dumps(self.to_json(connections, allowed_rotation_steps), indent=2), encoding="utf-8")
		export_pixels = rotate_pixels_180(self.width, self.height, self.pixels())
		write_png(folder / "tile.png", self.width, self.height, export_pixels)
		return folder


class TilePaintApp:
	def __init__(self, root: tk.Tk, palette_path: Path = DEFAULT_PALETTE_PATH) -> None:
		self.root = root
		self.root.title("Tile Paint Tool")
		self.root.minsize(920, 560)
		self._apply_dark_theme()

		try:
			self.palette = TilePalette.load(palette_path)
		except Exception as exc:
			messagebox.showerror("Palette", f"Could not load palette from:\n{palette_path}\n\n{exc}")
			raise SystemExit(1) from exc

		self.output_root = Path(__file__).resolve().parent / "TileData"
		self.output_root.mkdir(parents=True, exist_ok=True)
		self.model = TileModel(self.palette)
		self.current_tile_folder: Path | None = None
		self._clean_state: tuple | None = None
		self._gallery_photos: List[tk.PhotoImage] = []
		self.cell_size = DEFAULT_CELL_SIZE
		self.drag_paint_color_id: str | None = None
		self.direction_vars = {direction: tk.BooleanVar(value=True) for direction in DIRECTION_OPTIONS}
		self.direction_summary_var = tk.StringVar()
		self.rotation_vars = {step: tk.BooleanVar(value=True) for step in ROTATION_STEPS}
		self.rotation_summary_var = tk.StringVar()
		self.brush_size_var = tk.IntVar(value=1)
		self.brush_shape_var = tk.StringVar(value=BRUSH_SHAPES[0])
		self.tool_var = tk.StringVar(value=TOOL_BRUSH)
		self.active_color_id = tk.StringVar(value=self.palette.default_id)
		self.active_color_summary_var = tk.StringVar()

		self.tile_name_var = tk.StringVar(value="Tile01")
		self.width_var = tk.IntVar(value=self.model.width)
		self.height_var = tk.IntVar(value=self.model.height)
		self.status_var = tk.StringVar(
			value="Pick a palette color, then paint with the brush or fill. Right click resets cells to the default color."
		)

		self._build_ui()
		self._refresh_direction_summary()
		self._refresh_rotation_summary()
		self._refresh_active_color_summary()
		self._fit_cell_size_to_screen()
		self._refresh_canvas_size()
		self.redraw_all()
		self._commit_clean_state()
		self._refresh_tile_gallery()

	def _apply_dark_theme(self) -> None:
		self.root.configure(bg=DARK_BG)
		style = ttk.Style(self.root)
		if "clam" in style.theme_names():
			style.theme_use("clam")

		style.configure(".", background=DARK_BG, foreground=DARK_TEXT, fieldbackground=DARK_PANEL)
		style.configure("TFrame", background=DARK_BG)
		style.configure("TLabel", background=DARK_BG, foreground=DARK_TEXT)
		style.configure("TButton", background=DARK_PANEL, foreground=DARK_TEXT)
		style.map("TButton", background=[("active", DARK_PANEL_ALT), ("pressed", DARK_PANEL_ALT)])
		style.configure("TCheckbutton", background=DARK_BG, foreground=DARK_TEXT)
		style.map("TCheckbutton", background=[("active", DARK_BG)], foreground=[("active", DARK_TEXT)])
		style.configure("TRadiobutton", background=DARK_BG, foreground=DARK_TEXT)
		style.map("TRadiobutton", background=[("active", DARK_BG)], foreground=[("active", DARK_TEXT)])
		style.configure("TLabelframe", background=DARK_BG, foreground=DARK_TEXT)
		style.configure("TLabelframe.Label", background=DARK_BG, foreground=DARK_TEXT)
		style.configure("TEntry", fieldbackground=DARK_PANEL, foreground=DARK_TEXT, insertcolor=DARK_TEXT)
		style.configure("TSpinbox", fieldbackground=DARK_PANEL, foreground=DARK_TEXT, insertcolor=DARK_TEXT)

	def _fit_cell_size_to_screen(self) -> None:
		self.root.update_idletasks()
		screen_w = max(1, self.root.winfo_screenwidth())
		screen_h = max(1, self.root.winfo_screenheight())

		# Leave room for window chrome and controls so the grid starts fully visible.
		usable_w = max(1, screen_w - 80)
		usable_h = max(1, screen_h - 240)
		max_by_width = usable_w // self.model.width
		max_by_height = usable_h // self.model.height
		fit_size = max(4, min(DEFAULT_CELL_SIZE, max_by_width, max_by_height))
		self.cell_size = fit_size

	def _build_ui(self) -> None:
		container = ttk.Frame(self.root, padding=8)
		container.pack(fill="both", expand=True)

		content = ttk.Frame(container)
		content.pack(fill="both", expand=True)

		sidebar = ttk.Frame(content)
		sidebar.pack(side="left", fill="y", padx=(0, 10))

		connections_box = ttk.Labelframe(sidebar, text="Directions", padding=8)
		connections_box.pack(fill="x", pady=(0, 10))

		for direction in DIRECTION_OPTIONS:
			ttkk = ttk.Checkbutton(
				connections_box,
				text=direction,
				variable=self.direction_vars[direction],
				command=self._on_direction_changed,
			)
			ttkk.pack(anchor="w", pady=1)

		button_row = ttk.Frame(connections_box)
		button_row.pack(fill="x", pady=(8, 4))
		ttk.Button(button_row, text="All", command=self.select_all_directions).pack(side="left", expand=True, fill="x")
		ttk.Button(button_row, text="None", command=self.clear_all_directions).pack(side="left", expand=True, fill="x", padx=(6, 0))

		ttk.Label(connections_box, text="Selected:").pack(anchor="w", pady=(6, 0))
		ttk.Label(connections_box, textvariable=self.direction_summary_var, wraplength=150, justify="left").pack(anchor="w")
		ttk.Label(
			connections_box,
			text=(
				f"Each selected side shows {CONNECTION_SLOT_COUNT} red border guides. "
				"Paint them with pathway before saving."
			),
			wraplength=150,
			justify="left",
		).pack(anchor="w", pady=(4, 0))

		rotation_box = ttk.Labelframe(sidebar, text="Allowed Rotations", padding=8)
		rotation_box.pack(fill="x", pady=(0, 10))

		for step in ROTATION_STEPS:
			check = ttk.Checkbutton(rotation_box, text=ROTATION_LABELS[step], variable=self.rotation_vars[step], command=self._on_rotation_changed)
			check.pack(anchor="w", pady=1)

		ttk.Label(rotation_box, text="Selected:").pack(anchor="w", pady=(6, 0))
		ttk.Label(rotation_box, textvariable=self.rotation_summary_var, wraplength=150, justify="left").pack(anchor="w")
		ttk.Label(rotation_box, text="Saved to JSON only; does not affect painting or generation.", wraplength=150, justify="left").pack(anchor="w", pady=(4, 0))

		brush_box = ttk.Labelframe(sidebar, text="Brush", padding=8)
		brush_box.pack(fill="x", pady=(0, 10))

		brush_row = ttk.Frame(brush_box)
		brush_row.pack(fill="x", pady=(0, 6))
		ttk.Label(brush_row, text="Size:").pack(side="left")
		ttk.Spinbox(brush_row, from_=1, to=64, textvariable=self.brush_size_var, width=6, justify="center").pack(side="left", padx=(6, 0))

		shape_box = ttk.Frame(brush_box)
		shape_box.pack(fill="x")
		ttk.Label(shape_box, text="Shape:").pack(anchor="w")
		for shape in BRUSH_SHAPES:
			ttk.Radiobutton(shape_box, text=shape, value=shape, variable=self.brush_shape_var).pack(anchor="w", pady=1)

		ttk.Label(brush_box, text="Size is the brush diameter in cells.", wraplength=160, justify="left").pack(anchor="w", pady=(6, 0))

		color_box = ttk.Labelframe(sidebar, text="Colors", padding=8)
		color_box.pack(fill="x", pady=(0, 10))
		for palette_color in self.palette.colors:
			row = ttk.Frame(color_box)
			row.pack(fill="x", pady=2)
			swatch = tk.Canvas(row, width=22, height=22, highlightthickness=1, highlightbackground=DARK_BORDER, bg=DARK_BG)
			swatch.pack(side="left")
			swatch.create_rectangle(2, 2, 20, 20, fill=rgba_to_hex(palette_color.rgba), outline=DARK_BORDER)
			ttk.Radiobutton(
				row,
				text=palette_color.purpose,
				value=palette_color.id,
				variable=self.active_color_id,
				command=self._refresh_active_color_summary,
			).pack(side="left", padx=(6, 0))
		ttk.Label(color_box, textvariable=self.active_color_summary_var, wraplength=160, justify="left").pack(anchor="w", pady=(6, 0))
		ttk.Label(
			color_box,
			text=f"Palette: {DEFAULT_PALETTE_PATH.name}",
			wraplength=160,
			justify="left",
		).pack(anchor="w", pady=(4, 0))

		tool_box = ttk.Labelframe(sidebar, text="Tool", padding=8)
		tool_box.pack(fill="x", pady=(0, 10))
		for tool in (TOOL_BRUSH, TOOL_FILL):
			ttk.Radiobutton(tool_box, text=tool, value=tool, variable=self.tool_var).pack(anchor="w", pady=1)
		ttk.Label(tool_box, text="Fill floods the connected area from the clicked cell.", wraplength=160, justify="left").pack(anchor="w", pady=(6, 0))

		main = ttk.Frame(content)
		main.pack(side="left", fill="both", expand=True)

		controls = ttk.Frame(main)
		controls.pack(fill="x", pady=(0, 8))

		ttk.Label(controls, text="Tile name:").grid(row=0, column=0, sticky="w")
		ttk.Entry(controls, textvariable=self.tile_name_var, width=22).grid(row=0, column=1, padx=(4, 16), sticky="w")

		ttk.Label(controls, text="Width:").grid(row=0, column=2, sticky="w")
		ttk.Spinbox(controls, from_=1, to=128, textvariable=self.width_var, width=6).grid(row=0, column=3, padx=(4, 16), sticky="w")

		ttk.Label(controls, text="Height:").grid(row=0, column=4, sticky="w")
		ttk.Spinbox(controls, from_=1, to=128, textvariable=self.height_var, width=6).grid(row=0, column=5, padx=(4, 16), sticky="w")

		ttk.Button(controls, text="New / Resize", command=self.new_tile).grid(row=0, column=6, padx=(0, 8))
		ttk.Button(controls, text="Clear", command=self.clear_tile).grid(row=0, column=7, padx=(0, 8))
		ttk.Button(controls, text="Save", command=self.save_tile).grid(row=0, column=8)

		self.canvas = tk.Canvas(main, highlightthickness=1, highlightbackground=DARK_BORDER, bg=DARK_BG)
		self.canvas.pack(fill="both", expand=True)
		self.canvas.bind("<Button-1>", lambda event: self._paint_from_event(event, True))
		self.canvas.bind("<B1-Motion>", lambda event: self._paint_from_event(event, True))
		self.canvas.bind("<Button-3>", lambda event: self._paint_from_event(event, False))
		self.canvas.bind("<B3-Motion>", lambda event: self._paint_from_event(event, False))

		gallery_panel = ttk.Labelframe(content, text="Tile Library", padding=4)
		gallery_panel.pack(side="right", fill="y", padx=(10, 0))

		ttk.Label(
			gallery_panel,
			text="Left-click to open. Right-click for options.",
			wraplength=GALLERY_PANEL_WIDTH - 12,
			justify="left",
		).pack(anchor="w", pady=(0, 6))

		gallery_scroll_row = ttk.Frame(gallery_panel)
		gallery_scroll_row.pack(fill="both", expand=True)

		self.gallery_canvas = tk.Canvas(
			gallery_scroll_row,
			width=GALLERY_PANEL_WIDTH,
			highlightthickness=1,
			highlightbackground=DARK_BORDER,
			bg=DARK_PANEL,
		)
		gallery_scrollbar = ttk.Scrollbar(gallery_scroll_row, orient="vertical", command=self.gallery_canvas.yview)
		self.gallery_inner = ttk.Frame(self.gallery_canvas)
		self.gallery_inner.bind(
			"<Configure>",
			lambda _event: self.gallery_canvas.configure(scrollregion=self.gallery_canvas.bbox("all")),
		)
		self._gallery_window_id = self.gallery_canvas.create_window((0, 0), window=self.gallery_inner, anchor="nw")
		self.gallery_canvas.configure(yscrollcommand=gallery_scrollbar.set)
		self.gallery_canvas.pack(side="left", fill="both", expand=True)
		gallery_scrollbar.pack(side="right", fill="y")

		self.gallery_canvas.bind("<Configure>", self._on_gallery_canvas_configure)
		self.gallery_canvas.bind("<MouseWheel>", self._on_gallery_mousewheel)

		footer = ttk.Frame(container)
		footer.pack(fill="x", pady=(8, 0))
		ttk.Label(footer, textvariable=self.status_var).pack(side="left")
		self.footer_color_label = ttk.Label(footer, text="")
		self.footer_color_label.pack(side="right")

	def _refresh_active_color_summary(self) -> None:
		color_id = self.active_color_id.get()
		if not self.palette.is_valid(color_id):
			color_id = self.palette.default_id
			self.active_color_id.set(color_id)
		entry = self.palette.color(color_id)
		summary = f"{entry.purpose} ({entry.id})"
		self.active_color_summary_var.set(f"Active: {summary}")
		self.footer_color_label.configure(text=f"Active: {summary}  {rgba_to_hex(entry.rgba)}")

	def _on_gallery_canvas_configure(self, event: tk.Event) -> None:
		self.gallery_canvas.itemconfigure(self._gallery_window_id, width=event.width)

	def _bind_gallery_mousewheel(self, widget: tk.Widget) -> None:
		widget.bind("<MouseWheel>", self._on_gallery_mousewheel)

	def _on_gallery_mousewheel(self, event: tk.Event) -> None:
		delta = -1 * (event.delta // 120) if event.delta else 0
		if delta:
			self.gallery_canvas.yview_scroll(delta, "units")

	def _bind_gallery_tile_item(self, widgets: Sequence[tk.Widget], folder: Path) -> None:
		for widget in widgets:
			widget.bind("<Button-1>", lambda _event, tile_folder=folder: self._request_open_tile(tile_folder))
			widget.bind("<Button-3>", lambda event, tile_folder=folder: self._show_tile_gallery_menu(event, tile_folder))
			self._bind_gallery_mousewheel(widget)

	def _show_tile_gallery_menu(self, event: tk.Event, folder: Path) -> None:
		menu = tk.Menu(
			self.root,
			tearoff=0,
			bg=DARK_PANEL,
			fg=DARK_TEXT,
			activebackground=DARK_PANEL_ALT,
			activeforeground=DARK_TEXT,
		)
		menu.add_command(label="Delete", command=lambda: self._delete_tile(folder))
		try:
			menu.tk_popup(event.x_root, event.y_root)
		finally:
			menu.grab_release()

	def _reset_to_new_tile(self) -> None:
		self.model = TileModel(self.palette)
		self.current_tile_folder = None
		self.tile_name_var.set("Tile01")
		self.width_var.set(self.model.width)
		self.height_var.set(self.model.height)
		self._fit_cell_size_to_screen()
		self._refresh_canvas_size()
		self.redraw_all()
		self._commit_clean_state()

	def _delete_tile(self, folder: Path) -> None:
		if not messagebox.askyesno(
			"Delete Tile",
			f"Delete tile '{folder.name}'?\n\nThis permanently removes the folder from TileData.",
		):
			return

		is_current = (
			self.current_tile_folder is not None and folder.resolve() == self.current_tile_folder.resolve()
		)
		try:
			shutil.rmtree(folder)
		except Exception as exc:
			messagebox.showerror("Delete Tile", f"Could not delete tile:\n{folder}\n\n{exc}")
			return

		if is_current:
			self._reset_to_new_tile()

		self._refresh_tile_gallery()
		self.status_var.set(f"Deleted {folder.name}.")

	def _capture_state(self) -> tuple:
		return (
			self.model.width,
			self.model.height,
			tuple(tuple(row) for row in self.model.cells),
			tuple(self.selected_directions()),
			tuple(self.selected_rotation_steps()),
			self.tile_name_var.get().strip(),
			str(self.current_tile_folder.resolve()) if self.current_tile_folder else None,
		)

	def _commit_clean_state(self) -> None:
		self._clean_state = self._capture_state()

	def _is_dirty(self) -> bool:
		return self._clean_state is None or self._capture_state() != self._clean_state

	def _confirm_leave_current_tile(self) -> bool:
		if not self._is_dirty():
			return True

		answer = messagebox.askyesnocancel(
			"Unsaved changes",
			"The current tile has unsaved changes.\n\nSave before opening another tile?",
		)
		if answer is None:
			return False
		if answer:
			return self._save_tile(show_success=False)
		return True

	def _refresh_tile_gallery(self) -> None:
		for child in self.gallery_inner.winfo_children():
			child.destroy()
		self._gallery_photos.clear()

		folders = list_tile_folders(self.output_root)
		if not folders:
			ttk.Label(
				self.gallery_inner,
				text="No saved tiles yet.\nSave a tile to see it here.",
				wraplength=GALLERY_PANEL_WIDTH - 16,
				justify="left",
			).pack(anchor="w", padx=4, pady=8)
			return

		current_resolved = self.current_tile_folder.resolve() if self.current_tile_folder else None
		for folder in folders:
			png_path = folder / "tile.png"
			row = ttk.Frame(self.gallery_inner)
			row.pack(fill="x", padx=4, pady=6)

			if current_resolved is not None and folder.resolve() == current_resolved:
				highlight = tk.Frame(row, highlightthickness=2, highlightbackground=DARK_ACCENT)
				highlight.pack(fill="x")
				content_parent = highlight
			else:
				content_parent = row

			name_label = ttk.Label(content_parent, text=folder.name, wraplength=GALLERY_PANEL_WIDTH - 20)
			name_label.pack(anchor="w")

			try:
				photo = tk.PhotoImage(file=str(png_path))
			except tk.TclError:
				missing_label = ttk.Label(content_parent, text="(preview unavailable)", foreground=DARK_TEXT)
				missing_label.pack(anchor="w")
				self._bind_gallery_tile_item((row, content_parent, name_label, missing_label), folder)
				continue

			width = photo.width()
			height = photo.height()
			if width > GALLERY_PREVIEW_MAX or height > GALLERY_PREVIEW_MAX:
				factor = max((width + GALLERY_PREVIEW_MAX - 1) // GALLERY_PREVIEW_MAX, (height + GALLERY_PREVIEW_MAX - 1) // GALLERY_PREVIEW_MAX)
				factor = max(1, factor)
							# noinspection PyTypeChecker
				photo = photo.subsample(factor, factor)

			self._gallery_photos.append(photo)
			image_label = tk.Label(
				content_parent,
				image=photo,
				bg=DARK_PANEL,
				highlightthickness=1,
				highlightbackground=DARK_BORDER,
				cursor="hand2",
			)
			image_label.pack(anchor="w", pady=(4, 0))
			self._bind_gallery_tile_item((row, content_parent, name_label, image_label), folder)

	def _request_open_tile(self, folder: Path) -> None:
		if self.current_tile_folder is not None and folder.resolve() == self.current_tile_folder.resolve():
			return
		if not self._confirm_leave_current_tile():
			return

		try:
			model, connections, allowed_rotation_steps = TileModel.load_from_folder(folder, self.palette)
		except Exception as exc:
			messagebox.showerror("Open Tile", f"Could not open tile:\n{folder}\n\n{exc}")
			return

		self.model = model
		self.current_tile_folder = folder
		self.tile_name_var.set(folder.name)
		self.width_var.set(model.width)
		self.height_var.set(model.height)
		for direction in DIRECTION_OPTIONS:
			self.direction_vars[direction].set(direction in connections)
		for step in ROTATION_STEPS:
			self.rotation_vars[step].set(step in allowed_rotation_steps)
		self._refresh_direction_summary()
		self._refresh_rotation_summary()
		self._fit_cell_size_to_screen()
		self._refresh_canvas_size()
		self.redraw_all()
		self._commit_clean_state()
		self._refresh_tile_gallery()
		self.status_var.set(f"Opened {folder.name}.")

	def _refresh_rotation_summary(self) -> None:
		selected = self.selected_rotation_steps()
		self.rotation_summary_var.set(", ".join(ROTATION_LABELS[step] for step in selected) if selected else "None")

	def _on_rotation_changed(self) -> None:
		self._refresh_rotation_summary()

	def selected_rotation_steps(self) -> List[int]:
		return [step for step in ROTATION_STEPS if self.rotation_vars[step].get()]

	def _refresh_direction_summary(self) -> None:
		selected = self.selected_directions()
		self.direction_summary_var.set(", ".join(selected) if selected else "None")

	def _on_direction_changed(self) -> None:
		self._refresh_direction_summary()
		self.redraw_all()

	def _connection_slot_set(self) -> set[Tuple[int, int]]:
		return set(self.model.connection_slots(self.selected_directions()))

	def selected_directions(self) -> List[str]:
		return [direction for direction in DIRECTION_OPTIONS if self.direction_vars[direction].get()]

	def select_all_directions(self) -> None:
		for var in self.direction_vars.values():
			var.set(True)
		self._on_direction_changed()

	def clear_all_directions(self) -> None:
		for var in self.direction_vars.values():
			var.set(False)
		self._on_direction_changed()

	def _refresh_canvas_size(self) -> None:
		self.canvas.configure(width=self.model.width * self.cell_size, height=self.model.height * self.cell_size)

	def _cell_from_event(self, event: tk.Event) -> tuple[int, int] | None:
		x = event.x // self.cell_size
		y = event.y // self.cell_size
		if 0 <= x < self.model.width and 0 <= y < self.model.height:
			return x, y
		return None

	def _paint_color_for_click(self, use_active_color: bool) -> str:
		if use_active_color:
			color_id = self.active_color_id.get()
			if self.palette.is_valid(color_id):
				return color_id
		return self.palette.default_id

	def _paint_from_event(self, event: tk.Event, use_active_color: bool) -> None:
		cell = self._cell_from_event(event)
		if cell is None:
			return

		color_id = self._paint_color_for_click(use_active_color)
		purpose = self.palette.color(color_id).purpose
		x, y = cell
		if self.tool_var.get() == TOOL_FILL:
			changed = self.model.flood_fill(x, y, color_id)
			if changed == 0:
				return
			for fill_y in range(self.model.height):
				for fill_x in range(self.model.width):
					self.draw_cell(fill_x, fill_y)
			self.status_var.set(f"Filled area from ({x}, {y}) with {purpose}.")
		else:
			changed = self.model.paint_brush(x, y, color_id, self.brush_size_var.get(), self.brush_shape_var.get())
			if changed == 0:
				return

			for brush_x, brush_y in self.model.brush_cells(x, y, self.brush_size_var.get(), self.brush_shape_var.get()):
				self.draw_cell(brush_x, brush_y)

			self.status_var.set(f"Painted {purpose} with {self.brush_shape_var.get().lower()} brush at ({x}, {y}).")

		self.drag_paint_color_id = color_id

	def draw_cell(self, x: int, y: int) -> None:
		x0 = x * self.cell_size
		y0 = y * self.cell_size
		x1 = x0 + self.cell_size
		y1 = y0 + self.cell_size
		color_id = self.model.cell_color_id(x, y)
		is_connection_slot = (x, y) in self._connection_slot_set()
		if is_connection_slot and color_id != PATHWAY_COLOR_ID:
			fill = rgba_to_hex(CONNECTION_MARKER_RGBA)
		else:
			fill = rgba_to_hex(self.model.palette.rgba(color_id))
		tag = f"cell_{x}_{y}"
		self.canvas.delete(tag)
		self.canvas.create_rectangle(x0, y0, x1, y1, fill=fill, outline=DARK_BORDER, tags=tag)

	def redraw_all(self) -> None:
		self.canvas.delete("all")
		for y in range(self.model.height):
			for x in range(self.model.width):
				self.draw_cell(x, y)

	def new_tile(self) -> None:
		if not self._confirm_leave_current_tile():
			return

		self.model.resize(self.width_var.get(), self.height_var.get())
		self.model.clear()
		self.current_tile_folder = None
		self._refresh_rotation_summary()
		self._refresh_canvas_size()
		self.redraw_all()
		self._commit_clean_state()
		self._refresh_tile_gallery()
		self.status_var.set("Created a new empty tile.")

	def clear_tile(self) -> None:
		self.model.clear()
		self.redraw_all()
		self.status_var.set("Cleared tile.")

	def save_tile(self) -> None:
		self._save_tile(show_success=True)

	def _save_target_folder(self, tile_name: str) -> Path | None:
		if self.current_tile_folder is not None and self.current_tile_folder.name == sanitize_name(tile_name):
			return self.current_tile_folder
		return None

	def _save_tile(self, *, show_success: bool) -> bool:
		tile_name = self.tile_name_var.get().strip()
		if not tile_name:
			messagebox.showerror("Save Tile", "Please enter a tile name.")
			return False

		connections = self.selected_directions()
		allowed_rotation_steps = self.selected_rotation_steps()
		target_folder = self._save_target_folder(tile_name)
		try:
			saved_folder = self.model.save(
				tile_name,
				self.output_root,
				connections,
				allowed_rotation_steps,
				target_folder=target_folder,
			)
		except Exception as exc:  # pragma: no cover - surfaced to UI
			messagebox.showerror("Save Tile", f"Could not save tile:\n{exc}")
			return False

		self.current_tile_folder = saved_folder
		self.tile_name_var.set(saved_folder.name)
		self.redraw_all()
		self._commit_clean_state()
		self._refresh_tile_gallery()
		self.status_var.set(f"Saved to {saved_folder}")
		if show_success:
			messagebox.showinfo("Save Tile", f"Saved tile data to:\n{saved_folder}")
		return True


def run_self_test() -> None:
	palette = TilePalette.load(DEFAULT_PALETTE_PATH)
	pathway_id = "pathway"
	grass_id = "grass"

	with tempfile.TemporaryDirectory() as temp_dir:
		root = Path(temp_dir)
		model = TileModel(palette, width=4, height=4)
		assert model.paint_brush(2, 2, pathway_id, size=1, shape="Square") == 1
		model.clear()
		assert model.paint_brush(0, 0, pathway_id, size=3, shape="Square") == 4
		model.clear()
		assert model.paint_brush(1, 1, pathway_id, size=3, shape="Circle") >= 1
		model.clear()
		model.set_cell(0, 0, pathway_id)
		model.set_cell(1, 0, pathway_id)
		assert model.flood_fill(0, 0, grass_id) == 2
		assert model.cell_color_id(0, 0) == grass_id
		assert model.cell_color_id(1, 0) == grass_id

		for x, y in connection_slot_positions(4, 4, DIRECTION_OPTIONS):
			model.set_cell(x, y, pathway_id)

		first = model.save("Example Tile", root, DIRECTION_OPTIONS, [0, 1, 2, 3])
		second = model.save("Example Tile", root, ["North", "South"], [0, 1, 2, 3])

		assert first.exists()
		assert (first / "tile.json").exists()
		assert (first / "tile.png").exists()
		assert second.exists()
		assert second != first

		png_data = (first / "tile.png").read_bytes()
		assert png_data.startswith(b"\x89PNG\r\n\x1a\n")

		payload = json.loads((first / "tile.json").read_text(encoding="utf-8"))
		assert payload["width"] == 4 and payload["height"] == 4
		assert payload["default_color"] == palette.default_id
		assert payload["cells"][0][0] == grass_id
		assert payload["mock_prefab"]["tile_component"]["connections"] == ["North", "East", "South", "West"]
		assert payload["mock_prefab"]["tile_component"]["allowed_rotation_steps"] == [0, 1, 2, 3]

		north_slots = connection_cells_for_direction(4, 4, "North")
		assert len(north_slots) == 4
		assert all(y == 0 for _, y in north_slots)

		wide = TileModel(palette, width=20, height=8)
		east_slots = connection_cells_for_direction(20, 8, "East")
		assert len(east_slots) == CONNECTION_SLOT_COUNT
		assert all(x == 19 for x, _ in east_slots)

		wide.set_cell(0, 0, grass_id)
		for x, y in connection_slot_positions(20, 8, ["North"]):
			wide.set_cell(x, y, pathway_id)
		wide.save("ConnectionSlots", root, ["North"])
		conn_payload = json.loads((root / "ConnectionSlots" / "tile.json").read_text(encoding="utf-8"))
		for x, _ in connection_cells_for_direction(20, 8, "North"):
			assert conn_payload["cells"][0][x] == pathway_id

		model.clear()
		model.set_cell(0, 0, pathway_id)
		rotated = rotate_pixels_180(model.width, model.height, model.pixels())
		assert rotated[-1] == palette.rgba(pathway_id)

		try:
			empty = TileModel(palette, width=4, height=4)
			empty.save("MissingPathway", root, ["North"], [0, 1, 2, 3])
			raise AssertionError("Expected save to fail when connection slots are not pathway.")
		except ValueError:
			pass

		loaded, loaded_connections, loaded_rotations = TileModel.load_from_folder(first, palette)
		assert loaded.width == 4 and loaded.height == 4
		assert loaded_connections == ["North", "East", "South", "West"]
		assert loaded_rotations == [0, 1, 2, 3]
		assert first in list_tile_folders(root)

		loaded.set_cell(2, 2, grass_id)
		loaded.save("ignored", root, loaded_connections, loaded_rotations, target_folder=first)
		reloaded, _, reloaded_rotations = TileModel.load_from_folder(first, palette)
		assert reloaded.cell_color_id(2, 2) == grass_id
		assert reloaded_rotations == [0, 1, 2, 3]

		custom = TileModel(palette, width=4, height=4)
		for x, y in connection_slot_positions(4, 4, ["North"]):
			custom.set_cell(x, y, pathway_id)
		custom.save("RotationSubset", root, ["North"], [0, 2])
		custom_payload = json.loads((root / "RotationSubset" / "tile.json").read_text(encoding="utf-8"))
		assert custom_payload["mock_prefab"]["tile_component"]["allowed_rotation_steps"] == [0, 2]
		_, _, custom_rotations = TileModel.load_from_folder(root / "RotationSubset", palette)
		assert custom_rotations == [0, 2]

	print("Self-test passed.")


def parse_args() -> argparse.Namespace:
	parser = argparse.ArgumentParser(description="Tile paint tool with palette-defined colors.")
	parser.add_argument("--self-test", action="store_true", help="Run a headless save/export test and exit.")
	parser.add_argument(
		"--palette",
		type=Path,
		default=DEFAULT_PALETTE_PATH,
		help=f"Path to palette JSON (default: {DEFAULT_PALETTE_PATH.name})",
	)
	return parser.parse_args()


def main() -> None:
	args = parse_args()
	if args.self_test:
		run_self_test()
		return

	root = tk.Tk()
	TilePaintApp(root, palette_path=args.palette)
	root.mainloop()


if __name__ == "__main__":
	main()

