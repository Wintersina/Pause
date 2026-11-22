from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont


ROOT = Path(__file__).resolve().parent
REF = ROOT / "ref"
KIT = ROOT / "kit"
SHIPS = KIT / "ships"

INK = "#0B0A14"
INK_2 = "#14122A"
DECK = "#11172A"
DECK_2 = "#18213B"
DECK_3 = "#20294A"
GRID = "#2E3A72"
GRID_DARK = "#10152C"
CYAN = "#16D7D0"
CYAN_DARK = "#087D89"
CYAN_SOFT = "#2E6B92"
AMBER = "#FFB83D"
AMBER_DARK = "#9E5B24"
RED = "#F03845"
WHITE = "#F5F2E8"
MUTED = "#7D87A9"


def font(size: int) -> ImageFont.FreeTypeFont:
    candidates = [
        "/System/Library/Fonts/Monaco.ttf",
        "/System/Library/Fonts/Supplemental/Andale Mono.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
    ]
    for candidate in candidates:
        if Path(candidate).exists():
            return ImageFont.truetype(candidate, size)
    return ImageFont.load_default()


def rgba(size: tuple[int, int], color=(0, 0, 0, 0)) -> Image.Image:
    return Image.new("RGBA", size, color)


def rect(d: ImageDraw.ImageDraw, xy, fill, outline=None, width=1):
    d.rectangle(xy, fill=fill, outline=outline, width=width)


def polygon(d: ImageDraw.ImageDraw, pts, fill, outline=None):
    if fill is not None:
        d.polygon(pts, fill=fill)
    if outline:
        d.line(list(pts) + [pts[0]], fill=outline, width=1, joint="curve")


def save(im: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    im.save(path)


def build_bay_floor() -> Image.Image:
    """Build the default 96x128 nose-UP bay with open, painted deck seams."""
    im = rgba((96, 128), DECK)
    d = ImageDraw.Draw(im)
    rect(d, (2, 0, 93, 127), DECK_2)
    rect(d, (4, 2, 91, 125), DECK)
    for y in range(8, 128, 16):
        rect(d, (5, y, 90, y), GRID_DARK)
    for x in (22, 73):
        rect(d, (x, 2, x, 125), "#171D38")

    # One-pixel deck paint, intentionally dimmer than the ship glow.
    rect(d, (1, 0, 1, 127), CYAN_DARK)
    rect(d, (94, 0, 94, 127), CYAN_DARK)
    rect(d, (7, 5, 7, 123), GRID)
    rect(d, (88, 5, 88, 123), GRID)

    # Nose guide: default ship orientation is nose-UP, backed into the bottom.
    d.line([(47, 18), (40, 25), (40, 73), (47, 80)], fill=GRID, width=1)
    d.line([(49, 80), (56, 73), (56, 25), (49, 18)], fill=GRID, width=1)
    rect(d, (47, 84, 48, 90), CYAN_DARK)
    rect(d, (47, 91, 48, 94), CYAN)
    rect(d, (10, 12, 20, 12), GRID)
    rect(d, (75, 12, 85, 12), GRID)
    rect(d, (10, 114, 20, 114), GRID)
    rect(d, (75, 114, 85, 114), GRID)
    save(im, KIT / "bay_floor.png")
    save(im.rotate(90, resample=Image.Resampling.NEAREST, expand=True), KIT / "bay_floor_h.png")
    return im


def build_bay_back() -> Image.Image:
    im = rgba((96, 24))
    d = ImageDraw.Draw(im)
    rect(d, (0, 5, 95, 23), INK_2)
    rect(d, (2, 3, 93, 20), DECK_3)
    rect(d, (4, 7, 91, 18), GRID_DARK)
    rect(d, (8, 8, 87, 8), GRID)
    rect(d, (8, 19, 87, 20), CYAN_DARK)
    # Charging post / clamp at the service wall.
    rect(d, (44, 0, 51, 7), INK)
    rect(d, (45, 0, 50, 6), CYAN_DARK)
    rect(d, (46, 0, 49, 3), CYAN)
    rect(d, (38, 14, 57, 17), AMBER_DARK)
    rect(d, (40, 13, 55, 16), AMBER)
    rect(d, (43, 12, 52, 14), WHITE)
    rect(d, (0, 21, 95, 23), INK)
    save(im, KIT / "bay_back.png")

    # Rotated wall used at the outside edge of each B bay column.
    back_v = im.rotate(90, resample=Image.Resampling.NEAREST, expand=True)
    save(back_v, KIT / "bay_back_v.png")
    return im


def build_dividers() -> None:
    # Thin, low-glow seam overlay for A's side-by-side bays.
    im = rgba((8, 128))
    d = ImageDraw.Draw(im)
    rect(d, (3, 4, 4, 123), CYAN_DARK)
    rect(d, (3, 12, 4, 15), CYAN_SOFT)
    rect(d, (3, 112, 4, 115), CYAN_SOFT)
    rect(d, (1, 27, 2, 28), AMBER_DARK)
    save(im, KIT / "bay_divider.png")

    # B's horizontal row seam across a 128x96 bay.
    im_h = rgba((128, 4))
    d = ImageDraw.Draw(im_h)
    rect(d, (8, 1, 119, 2), CYAN_DARK)
    rect(d, (16, 1, 33, 1), CYAN_SOFT)
    rect(d, (94, 1, 111, 1), CYAN_SOFT)
    rect(d, (5, 1, 6, 2), AMBER_DARK)
    rect(d, (121, 1, 122, 2), AMBER_DARK)
    save(im_h, KIT / "bay_divider_h.png")

    # B's short vertical seam/cap, useful at the outer corner of a column.
    im_v = rgba((4, 96))
    d = ImageDraw.Draw(im_v)
    rect(d, (1, 8, 2, 87), CYAN_DARK)
    rect(d, (1, 16, 1, 27), CYAN_SOFT)
    rect(d, (1, 68, 1, 79), CYAN_SOFT)
    save(im_v, KIT / "bay_divider_v.png")


def build_lane_tile() -> Image.Image:
    """A horizontal 96x48 lane; its rotated copy is the vertical B lane."""
    im = rgba((96, 48), DECK)
    d = ImageDraw.Draw(im)
    rect(d, (0, 0, 95, 47), DECK)
    for y in range(0, 48, 8):
        rect(d, (0, y, 95, y), GRID_DARK)
    for x in (18, 77):
        rect(d, (x, 0, x, 47), "#171D38")
    # Dashed centre line is periodic in X and sits at the lane's middle Y.
    for x in (1, 25, 49, 73):
        rect(d, (x, 22, x + 6, 25), AMBER_DARK)
        rect(d, (x, 22, x + 5, 23), AMBER)
    save(im, KIT / "lane_tile.png")
    save(im.rotate(90, resample=Image.Resampling.NEAREST, expand=True), KIT / "lane_tile_v.png")
    return im


def build_lane_arrows() -> None:
    def arrow() -> Image.Image:
        im = rgba((48, 48))
        d = ImageDraw.Draw(im)
        polygon(d, [(24, 8), (38, 23), (30, 23), (30, 38), (18, 38), (18, 23), (10, 23)], AMBER_DARK)
        polygon(d, [(24, 7), (36, 21), (29, 21), (29, 36), (19, 36), (19, 21), (12, 21)], AMBER)
        rect(d, (21, 23, 27, 34), "#F5A632")
        return im

    up = arrow()
    down = up.rotate(180, resample=Image.Resampling.NEAREST, expand=False)
    save(down, KIT / "lane_arrow.png")
    save(up, KIT / "lane_arrow_up.png")


def build_edges() -> None:
    left = rgba((16, 96))
    d = ImageDraw.Draw(left)
    rect(d, (0, 0, 15, 95), INK_2)
    rect(d, (3, 0, 8, 95), DECK_3)
    rect(d, (9, 0, 11, 95), CYAN_DARK)
    rect(d, (12, 0, 13, 95), GRID)
    for y in (7, 52, 87):
        rect(d, (4, y, 8, y + 3), CYAN)
    save(left, KIT / "lot_edge_left.png")
    save(left.transpose(Image.Transpose.FLIP_LEFT_RIGHT), KIT / "lot_edge_right.png")


DIGITS = {
    "0": ("11111", "10001", "10001", "10001", "10001", "10001", "11111"),
    "1": ("00100", "01100", "00100", "00100", "00100", "00100", "01110"),
    "2": ("11110", "00001", "00001", "01110", "10000", "10000", "11111"),
    "3": ("11110", "00001", "00001", "01110", "00001", "00001", "11110"),
    "4": ("10010", "10010", "10010", "11111", "00010", "00010", "00010"),
    "5": ("11111", "10000", "10000", "11110", "00001", "00001", "11110"),
    "6": ("01110", "10000", "10000", "11110", "10001", "10001", "01110"),
    "7": ("11111", "00001", "00010", "00100", "01000", "01000", "01000"),
    "8": ("01110", "10001", "10001", "01110", "10001", "10001", "01110"),
    "9": ("01110", "10001", "10001", "01111", "00001", "00001", "01110"),
}


def build_numbers() -> None:
    # Hand-authored 5x7 glyphs at 2x pixel weight: each digit cell is 12x16.
    im = rgba((120, 16))
    d = ImageDraw.Draw(im)
    for digit, rows in DIGITS.items():
        x0 = int(digit) * 12 + 1
        for y, row in enumerate(rows):
            for x, on in enumerate(row):
                if on == "1":
                    rect(d, (x0 + x * 2 + 1, 1 + y * 2 + 1, x0 + x * 2 + 2, 1 + y * 2 + 2), AMBER_DARK)
                    rect(d, (x0 + x * 2, 1 + y * 2, x0 + x * 2 + 1, 1 + y * 2 + 1), AMBER)
    save(im, KIT / "number_stencils.png")


def build_highlights() -> None:
    def highlight(size: tuple[int, int]) -> Image.Image:
        im = rgba(size)
        glow = rgba(size)
        gd = ImageDraw.Draw(glow)
        gd.rectangle((2, 2, size[0] - 3, size[1] - 3), outline=CYAN, width=2)
        im.alpha_composite(glow.filter(ImageFilter.GaussianBlur(2)))
        d = ImageDraw.Draw(im)
        d.rectangle((2, 2, size[0] - 3, size[1] - 3), outline=CYAN_DARK, width=1)
        d.line([(2, 2), (min(17, size[0] - 3), 2)], fill=CYAN, width=1)
        d.line([(2, 2), (2, min(17, size[1] - 3))], fill=CYAN, width=1)
        d.line([(max(2, size[0] - 18), size[1] - 3), (size[0] - 3, size[1] - 3)], fill=CYAN, width=1)
        return im

    save(highlight((96, 128)), KIT / "bay_highlight.png")
    save(highlight((128, 96)), KIT / "bay_highlight_h.png")


def build_sign() -> None:
    im = rgba((192, 32))
    d = ImageDraw.Draw(im)
    rect(d, (2, 2, 189, 29), INK_2)
    rect(d, (4, 4, 187, 27), DECK_3)
    rect(d, (7, 7, 184, 24), INK)
    rect(d, (7, 7, 184, 8), CYAN_DARK)
    rect(d, (7, 23, 184, 24), AMBER_DARK)
    d.text((14, 7), "SPACE DOCK", font=font(13), fill=CYAN)
    d.text((147, 11), "LOT", font=font(7), fill=AMBER)
    save(im, KIT / "lot_sign.png")


def remove_green(im: Image.Image) -> Image.Image:
    im = im.convert("RGBA")
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, _ = px[x, y]
            if g > 170 and g > r * 1.45 and g > b * 1.25:
                px[x, y] = (r, g, b, 0)
            else:
                px[x, y] = (r, g, b, 255)
    return im


def extract_ship_sheet(path: Path, name: str, cell_grid: int) -> list[Image.Image]:
    sheet = remove_green(Image.open(path))
    cell = sheet.width // cell_grid
    out: list[Image.Image] = []
    for row in range(cell_grid):
        for col in range(cell_grid):
            tile = sheet.crop((col * cell, row * cell, (col + 1) * cell, (row + 1) * cell))
            bbox = tile.getchannel("A").getbbox()
            if bbox:
                tile = tile.crop(bbox)
            # 76 wide x 104 tall keeps ships around 75-80% of a bay short side.
            tile.thumbnail((76, 104), Image.Resampling.NEAREST)
            canvas = rgba((84, 112))
            canvas.alpha_composite(tile, ((84 - tile.width) // 2, 4))
            save(canvas, SHIPS / f"{name}_{row * cell_grid + col + 1:02d}.png")
            out.append(canvas)
    return out


def make_silhouette(ship: Image.Image, index: int) -> Image.Image:
    mask = ship.getchannel("A")
    im = rgba(ship.size)
    body = Image.new("RGBA", ship.size, "#080A18")
    body.putalpha(mask)
    im.alpha_composite(body)
    d = ImageDraw.Draw(im)
    rect(d, (28, 44, 55, 47), "#242341")
    rect(d, (32, 48, 51, 50), "#12162C")
    if index % 2:
        rect(d, (27, 28, 29, 70), "#1D2341")
    return im


def load_assets():
    ships = sorted(SHIPS.glob("neoncomet_*.png"))[:6] + sorted(SHIPS.glob("paragon_*.png"))[:6]
    silhouettes = [make_silhouette(Image.open(p), i) for i, p in enumerate(ships)]
    return {
        "floor": Image.open(KIT / "bay_floor.png"),
        "floor_h": Image.open(KIT / "bay_floor_h.png"),
        "back": Image.open(KIT / "bay_back.png"),
        "back_v": Image.open(KIT / "bay_back_v.png"),
        "divider": Image.open(KIT / "bay_divider.png"),
        "divider_h": Image.open(KIT / "bay_divider_h.png"),
        "divider_v": Image.open(KIT / "bay_divider_v.png"),
        "lane": Image.open(KIT / "lane_tile.png"),
        "lane_v": Image.open(KIT / "lane_tile_v.png"),
        "arrow": Image.open(KIT / "lane_arrow.png"),
        "arrow_up": Image.open(KIT / "lane_arrow_up.png"),
        "edge_l": Image.open(KIT / "lot_edge_left.png"),
        "edge_r": Image.open(KIT / "lot_edge_right.png"),
        "highlight": Image.open(KIT / "bay_highlight.png"),
        "highlight_h": Image.open(KIT / "bay_highlight_h.png"),
        "ships": ships,
        "silhouettes": silhouettes,
    }


def tile_x(base: Image.Image, tile: Image.Image, x: int, y: int, width: int):
    for xx in range(x, x + width, tile.width):
        base.alpha_composite(tile, (xx, y))


def tile_y(base: Image.Image, tile: Image.Image, x: int, y: int, height: int):
    for yy in range(y, y + height, tile.height):
        base.alpha_composite(tile, (x, yy))


def draw_text(d: ImageDraw.ImageDraw, xy, text: str, size: int, fill=WHITE, anchor=None):
    d.text(xy, text, font=font(size), fill=fill, anchor=anchor)


def number_sprite(value: int) -> Image.Image:
    value = max(1, min(99, value))
    strip = Image.open(KIT / "number_stencils.png")
    tens, ones = divmod(value, 10)
    out = rgba((24, 16))
    out.alpha_composite(strip.crop((tens * 12, 0, tens * 12 + 12, 16)), (0, 0))
    out.alpha_composite(strip.crop((ones * 12, 0, ones * 12 + 12, 16)), (12, 0))
    return out


def draw_header_footer(page: Image.Image, footer_y: int) -> None:
    W, H = page.size
    d = ImageDraw.Draw(page)
    draw_text(d, (W // 2, 16), "STAR DUST 102  SPACE DOCK · TAP A SHIP", 7, WHITE, anchor="ma")
    rect(d, (0, footer_y, W - 1, footer_y), RED)
    rect(d, (0, footer_y + 1, W - 1, H - 1), INK_2)
    draw_text(d, (18, footer_y + 18), "Back", 8, WHITE)
    draw_text(d, (W - 18, footer_y + 18), "Lift-off", 8, CYAN, anchor="ra")


def paste_ship(base: Image.Image, ship: Image.Image, x: int, y: int, bay_size: tuple[int, int], angle: int):
    sprite = ship.rotate(angle, resample=Image.Resampling.NEAREST, expand=True) if angle else ship
    sx = x + (bay_size[0] - sprite.width) // 2
    sy = y + (bay_size[1] - sprite.height) // 2
    base.alpha_composite(sprite, (sx, sy))


def clipped_page(im: Image.Image, footer_y: int) -> Image.Image:
    W, H = im.size
    mask = Image.new("L", (W, H), 0)
    ImageDraw.Draw(mask).rectangle((0, 0, W - 1, footer_y - 1), fill=255)
    im.putalpha(ImageChops.multiply(im.getchannel("A"), mask))
    page = rgba((W, H), INK)
    page.alpha_composite(im)
    return page


def build_mock_a(name: str, size: tuple[int, int], bays: int, scale: int, visible_rows: int, total_ships: int = 16):
    W, H = size[0] // scale, size[1] // scale
    footer_h = 42 if W >= 500 else 48
    footer_y = H - footer_h
    lot_y = 31
    lot_w = bays * 96
    lot_x = max(6, (W - lot_w) // 2)
    row_pitch = 176
    max_rows = min(visible_rows + 1, max(1, (footer_y - lot_y + 48) // row_pitch + 1))
    lane_rows = max_rows - 1
    lot_bottom = min(footer_y - 3, lot_y + max_rows * 128 + lane_rows * 48)
    assets = load_assets()
    im = rgba((W, H), INK)

    # Quiet deck bed, then the old A row/lane rhythm.
    rect(ImageDraw.Draw(im), (lot_x - 3, lot_y, lot_x + lot_w + 2, lot_bottom), GRID_DARK)
    y_edge = lot_y
    while y_edge < lot_bottom:
        im.alpha_composite(assets["edge_l"], (lot_x - 18, y_edge))
        im.alpha_composite(assets["edge_r"], (lot_x + lot_w + 2, y_edge))
        y_edge += assets["edge_l"].height

    ship_no = 0
    selected = min(1, bays - 1)
    for row in range(max_rows):
        row_y = lot_y + row * row_pitch
        facing_down = row % 2 == 0
        for col in range(bays):
            x = lot_x + col * 96
            floor = assets["floor"].rotate(180, resample=Image.Resampling.NEAREST) if facing_down else assets["floor"]
            wall = assets["back"].rotate(180, resample=Image.Resampling.NEAREST) if facing_down else assets["back"]
            im.alpha_composite(floor, (x, row_y))
            im.alpha_composite(wall, (x, row_y if facing_down else row_y + 104))
            if col > 0:
                im.alpha_composite(assets["divider"], (x - 4, row_y))
            source = assets["ships"][ship_no % len(assets["ships"])]
            ship = assets["silhouettes"][ship_no % len(assets["silhouettes"])] if ship_no >= total_ships else Image.open(source)
            paste_ship(im, ship, x, row_y, (96, 128), 180 if facing_down else 0)
            if row == 0 and col == selected:
                im.alpha_composite(assets["highlight"], (x, row_y))
            label = number_sprite(row * bays + col + 1)
            im.alpha_composite(label, (x + 7, row_y + (6 if facing_down else 106)))
            ship_no += 1
        if row < max_rows - 1:
            lane_y = row_y + 128
            tile_x(im, assets["lane"], lot_x, lane_y, lot_w)
            arrow = assets["arrow"] if row % 2 == 0 else assets["arrow_up"]
            im.alpha_composite(arrow, (lot_x + lot_w // 2 - 24, lane_y))

    page = clipped_page(im, footer_y)
    draw_header_footer(page, footer_y)
    final = page.resize(size, Image.Resampling.NEAREST).convert("RGB")
    save(final, ROOT / f"mockA_{name}.png")
    # Keep the original filenames reproducible for existing kit consumers.
    save(final, ROOT / f"mock_{name}.png")


def build_mock_b(name: str, size: tuple[int, int], lanes: int, scale: int, visible_rows: int, total_ships: int = 16):
    W, H = size[0] // scale, size[1] // scale
    footer_h = 42 if W >= 500 else 48
    footer_y = H - footer_h
    lot_y = 31
    group_w = 128 + 48 + 128
    lot_w = lanes * group_w
    lot_x = (W - lot_w) // 2
    row_pitch = 96
    max_rows = min(visible_rows + 1, max(1, (footer_y - lot_y + 48) // row_pitch + 1))
    lot_bottom = min(footer_y - 3, lot_y + max_rows * 96)
    assets = load_assets()
    im = rgba((W, H), INK)
    rect(ImageDraw.Draw(im), (max(0, lot_x - 3), lot_y, min(W - 1, lot_x + lot_w + 2), lot_bottom), GRID_DARK)

    # Vertical outer edges and vertical drive lanes.
    for lane_i in range(lanes):
        group_x = lot_x + lane_i * group_w
        lane_x = group_x + 128
        tile_y(im, assets["lane_v"], lane_x, lot_y, lot_bottom - lot_y)
        # First arrow advertises the top exit; a second repeats after scrolling.
        for arrow_y in range(lot_y + 4, lot_bottom, 192):
            im.alpha_composite(assets["arrow_up"], (lane_x, arrow_y))
        if lane_i == 0:
            im.alpha_composite(assets["edge_l"], (lot_x - 18, lot_y))
        if lane_i == lanes - 1:
            im.alpha_composite(assets["edge_r"], (lot_x + lot_w + 2, lot_y))

    ship_no = 0
    selected = (0, 1 if max_rows > 1 else 0, "right")
    for row in range(max_rows):
        y = lot_y + row * row_pitch
        for lane_i in range(lanes):
            lane_x = lot_x + lane_i * group_w + 128
            left_x = lane_x - 128
            right_x = lane_x + 48
            for side, x, angle in (("left", left_x, 270), ("right", right_x, 90)):
                floor = assets["floor_h"] if side == "right" else assets["floor_h"].rotate(180, resample=Image.Resampling.NEAREST, expand=False)
                im.alpha_composite(floor, (x, y))
                # The rotated wall sits on the outer edge, away from the lane.
                wall = assets["back_v"]
                if side == "left" and lane_i == 0:
                    im.alpha_composite(wall, (x, y))
                elif side == "right" and lane_i == lanes - 1:
                    im.alpha_composite(wall.rotate(180, resample=Image.Resampling.NEAREST, expand=True), (x + 104, y))
                elif side == "left" and lane_i > 0:
                    im.alpha_composite(assets["divider_v"], (x, y))
                elif side == "right" and lane_i < lanes - 1:
                    im.alpha_composite(assets["divider_v"], (x + 124, y))
                if row > 0:
                    im.alpha_composite(assets["divider_h"], (x, y - 4))
                source = assets["ships"][ship_no % len(assets["ships"])]
                ship = assets["silhouettes"][ship_no % len(assets["silhouettes"])] if ship_no >= total_ships else Image.open(source)
                paste_ship(im, ship, x, y, (128, 96), angle)
                if (lane_i, row, side) == selected:
                    im.alpha_composite(assets["highlight_h"], (x, y))
                # Two-digit stencil lies on the outside service wall.
                label_x = x + 1 if side == "left" else x + 103
                im.alpha_composite(number_sprite(row * lanes * 2 + lane_i * 2 + (1 if side == "left" else 2)), (label_x, y + 5))
                ship_no += 1

    page = clipped_page(im, footer_y)
    draw_header_footer(page, footer_y)
    final = page.resize(size, Image.Resampling.NEAREST).convert("RGB")
    save(final, ROOT / f"mockB_{name}.png")


def main() -> None:
    KIT.mkdir(exist_ok=True)
    SHIPS.mkdir(exist_ok=True)
    build_bay_floor()
    build_bay_back()
    build_dividers()
    build_lane_tile()
    build_lane_arrows()
    build_edges()
    build_numbers()
    build_highlights()
    build_sign()
    extract_ship_sheet(REF / "ship_neoncomet.png", "neoncomet", 3)
    extract_ship_sheet(REF / "ship_paragon.png", "paragon", 3)

    # A: preserved horizontal-lane variant.
    build_mock_a("phone", (1080, 2340), bays=3, scale=3, visible_rows=4)
    build_mock_a("ipad", (1640, 2360), bays=4, scale=4, visible_rows=3)
    build_mock_a("wide", (2560, 1600), bays=6, scale=4, visible_rows=1)

    # B: new vertical-lane variant. One group fits phone/iPad; two fit wide.
    build_mock_b("phone", (1080, 2340), lanes=1, scale=3, visible_rows=6)
    build_mock_b("ipad", (1640, 2360), lanes=1, scale=4, visible_rows=4)
    build_mock_b("wide", (2560, 1600), lanes=2, scale=4, visible_rows=2)


if __name__ == "__main__":
    main()
