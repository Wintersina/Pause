"""Validate the exported Frost damage atlas contract. Run after build_damage.py."""

from pathlib import Path

from PIL import Image, ImageChops, ImageStat


ROOT = Path(__file__).resolve().parent.parent
CELL = 384


def body_box(image: Image.Image):
    return image.getchannel("A").point(lambda v: 255 if v >= 128 else 0).getbbox()


def clear_edge(image: Image.Image) -> bool:
    a = image.getchannel("A")
    return (not a.crop((0, 0, CELL, 1)).getbbox()
            and not a.crop((0, CELL - 1, CELL, CELL)).getbbox()
            and not a.crop((0, 0, 1, CELL)).getbbox()
            and not a.crop((CELL - 1, 0, CELL, CELL)).getbbox())


def main():
    ref = Image.open(ROOT / "ref_idle_cell.png").convert("RGBA")
    damage = Image.open(ROOT / "Frost_damage.png")
    fx = Image.open(ROOT / "Frost_damage_fx.png")
    preview = Image.open(ROOT / "preview.png")
    assert (damage.size, damage.mode) == ((768, 1536), "RGBA")
    assert (fx.size, fx.mode) == ((2304, 768), "RGBA")
    assert (preview.size, preview.mode) == ((3840, 824), "RGBA")

    rbox = body_box(ref)
    allowed = (rbox[0] - 6, rbox[1] - 6, rbox[2] + 6, rbox[3] + 6)
    print("pristine solid bbox:", rbox, "allowed:", allowed)
    previous = None
    for row in range(4):
        a = damage.crop((0, row * CELL, CELL, (row + 1) * CELL))
        b = damage.crop((CELL, row * CELL, CELL * 2, (row + 1) * CELL))
        for image in (a, b):
            box = body_box(image)
            assert box is not None and clear_edge(image)
            assert (box[0] >= allowed[0] and box[1] >= allowed[1]
                    and box[2] <= allowed[2] and box[3] <= allowed[3]), box
        ab = ImageChops.difference(a.convert("RGB"), b.convert("RGB"))
        assert ab.getbbox() is not None
        if previous is not None:
            diff = ImageChops.difference(a.convert("RGB"), previous.convert("RGB"))
            mean = sum(ImageStat.Stat(diff).mean) / 3
            assert mean > 10, mean
            print(f"stage {row + 1}: bbox {body_box(a)}, change from previous {mean:.1f}/255")
        else:
            print(f"stage 1: bbox {body_box(a)}")
        previous = a

    for row in range(2):
        frames = []
        for col in range(6):
            frame = fx.crop((col * CELL, row * CELL,
                             (col + 1) * CELL, (row + 1) * CELL))
            assert clear_edge(frame)
            assert frame.getchannel("A").getbbox() is not None
            frames.append(frame.tobytes())
        assert len(set(frames)) == 6
    gif = Image.open(ROOT / "preview.gif")
    assert gif.size == (768, 768) and gif.n_frames == 6
    print("effects: 6 distinct smoke frames, 6 distinct arc frames; all cell borders clear")
    print("preview: RGBA contact sheet and 6-frame GIF valid")


if __name__ == "__main__":
    main()
