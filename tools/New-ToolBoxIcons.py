from pathlib import Path
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "ToolBox.Host" / "Assets"
SCALE = 4
CANVAS = 256 * SCALE
INK = "#16251C"
ACCENT = "#CFFF52"


def scaled_box(box):
    return tuple(int(value * SCALE) for value in box)


def render_app_icon():
    image = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle(
        scaled_box((4, 4, 252, 252)),
        radius=56 * SCALE,
        fill=ACCENT,
        outline=INK,
        width=3 * SCALE,
    )
    draw_toolbox(draw, SCALE)
    return image.resize((256, 256), Image.Resampling.LANCZOS)


def render_tray_icon():
    return render_app_icon()


def draw_toolbox(draw, scale):
    """Draw the small toolbox mark used by every ToolBox icon size."""
    # The source geometry uses the SVG viewBox (64 units), while the raster
    # canvas is 256px at a 4x oversampling factor.
    geometry_scale = 4 * scale

    def geometry_box(box):
        return tuple(int(value * geometry_scale) for value in box)

    draw.rounded_rectangle(
        geometry_box((10, 27, 54, 52)),
        radius=5 * geometry_scale,
        outline=INK,
        width=5 * geometry_scale,
    )
    draw.line(
        geometry_box((22, 27, 22, 16)),
        fill=INK,
        width=5 * geometry_scale,
    )
    draw.line(
        geometry_box((42, 27, 42, 16)),
        fill=INK,
        width=5 * geometry_scale,
    )
    draw.line(
        geometry_box((22, 16, 42, 16)),
        fill=INK,
        width=5 * geometry_scale,
    )
    draw.line(
        geometry_box((10, 37, 54, 37)),
        fill=INK,
        width=5 * geometry_scale,
    )
    draw.line(
        geometry_box((20, 44, 28, 44)),
        fill=INK,
        width=4 * geometry_scale,
    )
    draw.line(
        geometry_box((36, 44, 44, 44)),
        fill=INK,
        width=4 * geometry_scale,
    )


def save_ico(image, path):
    image.save(path, format="ICO", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])


def main():
    ASSETS.mkdir(parents=True, exist_ok=True)
    app = render_app_icon()
    tray = render_tray_icon()
    save_ico(app, ASSETS / "ToolBox.ico")
    save_ico(tray, ASSETS / "ToolBox.Tray.ico")
    app.save(ASSETS / "ToolBox-256.png")
    tray.resize((32, 32), Image.Resampling.LANCZOS).save(ASSETS / "ToolBox.Tray-32.png")


if __name__ == "__main__":
    main()
