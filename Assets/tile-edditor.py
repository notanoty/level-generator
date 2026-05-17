from __future__ import annotations

import argparse
import json
import re
import struct
import tempfile
import tkinter as tk
from dataclasses import dataclass
from pathlib import Path
from tkinter import messagebox, ttk
from typing import List, Sequence, Tuple
import zlib


RGBA = Tuple[int, int, int, int]

BACKGROUND: RGBA = (128, 128, 128, 255)
PAINT: RGBA = (0, 170, 255, 255)

DEFAULT_WIDTH = 64
DEFAULT_HEIGHT = 64
DEFAULT_CELL_SIZE = 24


def clamp(value: int, low: int, high: int) -> int:
	return max(low, min(high, value))


def rgba_to_hex(color: RGBA) -> str:
	return "#%02x%02x%02x" % color[:3]


def sanitize_name(name: str) -> str:
	cleaned = re.sub(r"[^A-Za-z0-9._-]+", "_", name.strip())
	cleaned = cleaned.strip("._-")
	return cleaned or "Tile"


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
	width: int = DEFAULT_WIDTH
	height: int = DEFAULT_HEIGHT
	background_color: RGBA = BACKGROUND
	paint_color: RGBA = PAINT

	def __post_init__(self) -> None:
		self.width = max(1, int(self.width))
		self.height = max(1, int(self.height))
		self.cells: List[List[bool]] = [[False for _ in range(self.width)] for _ in range(self.height)]

	def resize(self, width: int, height: int) -> None:
		width = max(1, int(width))
		height = max(1, int(height))
		new_cells = [[False for _ in range(width)] for _ in range(height)]

		copy_height = min(self.height, height)
		copy_width = min(self.width, width)
		for y in range(copy_height):
			for x in range(copy_width):
				new_cells[y][x] = self.cells[y][x]

		self.width = width
		self.height = height
		self.cells = new_cells

	def clear(self) -> None:
		for y in range(self.height):
			for x in range(self.width):
				self.cells[y][x] = False

	def set_cell(self, x: int, y: int, painted: bool) -> None:
		if 0 <= x < self.width and 0 <= y < self.height:
			self.cells[y][x] = painted

	def is_painted(self, x: int, y: int) -> bool:
		return 0 <= x < self.width and 0 <= y < self.height and self.cells[y][x]

	def pixels(self) -> List[RGBA]:
		flat: List[RGBA] = []
		for y in range(self.height):
			for x in range(self.width):
				flat.append(self.paint_color if self.cells[y][x] else self.background_color)
		return flat

	def to_json(self) -> dict:
		return {
			"width": self.width,
			"height": self.height,
			"background_color": list(self.background_color),
			"paint_color": list(self.paint_color),
			"cells": ["".join("1" if cell else "0" for cell in row) for row in self.cells],
			"mock_prefab": {
				"prefab_name": "TilePrefab_TestVariant",
				"unity_prefab_path": "Assets/Prefabs/Tiles/TilePrefab_TestVariant.prefab",
				"source_tile_data": {
					"texture_path": "Assets/TileData/TilePrefab_TestVariant/tile.png",
					"json_path": "Assets/TileData/TilePrefab_TestVariant/tile.json",
					"grid_size": [self.width, self.height],
					"background_color": list(self.background_color),
					"paint_color": list(self.paint_color),
				},
				"tile_component": {
					"texture": "tile.png",
					"connections": ["North", "East", "South", "West"],
					"rotation": 0,
					"allow_rotation_variants": True,
					"allowed_rotation_steps": [0, 1, 2, 3],
					"grid_size": [100, 100],
					"snap_plane": "XZ",
				},
			},
		}

	def save(self, tile_name: str, output_root: Path) -> Path:
		folder = unique_folder(output_root, tile_name)
		folder.mkdir(parents=True, exist_ok=False)

		(folder / "tile.json").write_text(json.dumps(self.to_json(), indent=2), encoding="utf-8")
		write_png(folder / "tile.png", self.width, self.height, self.pixels())
		return folder


class TilePaintApp:
	def __init__(self, root: tk.Tk) -> None:
		self.root = root
		self.root.title("Tile Paint Tool")
		self.root.minsize(720, 560)

		self.output_root = Path(__file__).resolve().parent / "TileData"
		self.model = TileModel()
		self.cell_size = DEFAULT_CELL_SIZE
		self.drag_paint_state: bool | None = None

		self.tile_name_var = tk.StringVar(value="Tile01")
		self.width_var = tk.IntVar(value=self.model.width)
		self.height_var = tk.IntVar(value=self.model.height)
		self.status_var = tk.StringVar(value="Left click to paint. Right click to erase.")

		self._build_ui()
		self._fit_cell_size_to_screen()
		self._refresh_canvas_size()
		self.redraw_all()

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

		controls = ttk.Frame(container)
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

		self.canvas = tk.Canvas(container, highlightthickness=1, highlightbackground="#555555", bg=rgba_to_hex(BACKGROUND))
		self.canvas.pack(fill="both", expand=True)
		self.canvas.bind("<Button-1>", lambda event: self._paint_from_event(event, True))
		self.canvas.bind("<B1-Motion>", lambda event: self._paint_from_event(event, True))
		self.canvas.bind("<Button-3>", lambda event: self._paint_from_event(event, False))
		self.canvas.bind("<B3-Motion>", lambda event: self._paint_from_event(event, False))

		footer = ttk.Frame(container)
		footer.pack(fill="x", pady=(8, 0))
		ttk.Label(footer, textvariable=self.status_var).pack(side="left")
		ttk.Label(footer, text=f"Paint color: {rgba_to_hex(PAINT)}").pack(side="right")

	def _refresh_canvas_size(self) -> None:
		self.canvas.configure(width=self.model.width * self.cell_size, height=self.model.height * self.cell_size)

	def _cell_from_event(self, event: tk.Event) -> tuple[int, int] | None:
		x = event.x // self.cell_size
		y = event.y // self.cell_size
		if 0 <= x < self.model.width and 0 <= y < self.model.height:
			return x, y
		return None

	def _paint_from_event(self, event: tk.Event, painted: bool) -> None:
		cell = self._cell_from_event(event)
		if cell is None:
			return

		x, y = cell
		if self.model.is_painted(x, y) == painted:
			return

		self.model.set_cell(x, y, painted)
		self.draw_cell(x, y)
		self.drag_paint_state = painted
		self.status_var.set(f"Updated cell ({x}, {y}).")

	def draw_cell(self, x: int, y: int) -> None:
		x0 = x * self.cell_size
		y0 = y * self.cell_size
		x1 = x0 + self.cell_size
		y1 = y0 + self.cell_size
		fill = rgba_to_hex(self.model.paint_color if self.model.is_painted(x, y) else self.model.background_color)
		tag = f"cell_{x}_{y}"
		self.canvas.delete(tag)
		self.canvas.create_rectangle(x0, y0, x1, y1, fill=fill, outline="#666666", tags=tag)

	def redraw_all(self) -> None:
		self.canvas.delete("all")
		for y in range(self.model.height):
			for x in range(self.model.width):
				self.draw_cell(x, y)

	def new_tile(self) -> None:
		self.model.resize(self.width_var.get(), self.height_var.get())
		self.model.clear()
		self._refresh_canvas_size()
		self.redraw_all()
		self.status_var.set("Created a new empty tile.")

	def clear_tile(self) -> None:
		self.model.clear()
		self.redraw_all()
		self.status_var.set("Cleared tile.")

	def save_tile(self) -> None:
		tile_name = self.tile_name_var.get().strip()
		if not tile_name:
			messagebox.showerror("Save Tile", "Please enter a tile name.")
			return

		try:
			saved_folder = self.model.save(tile_name, self.output_root)
		except Exception as exc:  # pragma: no cover - surfaced to UI
			messagebox.showerror("Save Tile", f"Could not save tile:\n{exc}")
			return

		self.status_var.set(f"Saved to {saved_folder}")
		messagebox.showinfo("Save Tile", f"Saved tile data to:\n{saved_folder}")


def run_self_test() -> None:
	with tempfile.TemporaryDirectory() as temp_dir:
		root = Path(temp_dir)
		model = TileModel(width=4, height=4)
		model.set_cell(1, 1, True)
		model.set_cell(2, 1, True)
		model.set_cell(1, 2, True)

		first = model.save("Example Tile", root)
		second = model.save("Example Tile", root)

		assert first.exists()
		assert (first / "tile.json").exists()
		assert (first / "tile.png").exists()
		assert second.exists()
		assert second != first

		png_data = (first / "tile.png").read_bytes()
		assert png_data.startswith(b"\x89PNG\r\n\x1a\n")

		payload = json.loads((first / "tile.json").read_text(encoding="utf-8"))
		assert payload["width"] == 4 and payload["height"] == 4
		assert payload["cells"][1][1] == "1"

	print("Self-test passed.")


def parse_args() -> argparse.Namespace:
	parser = argparse.ArgumentParser(description="Simple one-color tile paint tool.")
	parser.add_argument("--self-test", action="store_true", help="Run a headless save/export test and exit.")
	return parser.parse_args()


def main() -> None:
	args = parse_args()
	if args.self_test:
		run_self_test()
		return

	root = tk.Tk()
	TilePaintApp(root)
	root.mainloop()


if __name__ == "__main__":
	main()

