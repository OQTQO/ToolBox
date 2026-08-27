from pathlib import Path
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "ToolBox.Host" / "Assets"
SCALE = 4
CANVAS = 256 * SCALE


def scaled_box(box):
    return tuple(int(value * SCALE) for value in box)


def rounded_rectangle(draw, box, radius, fill):
    draw.rounded_rectangle(scaled_box(box), radius=radius * SCALE, fill=fill)


def render_app_icon():
    image = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    pixels = image.load()
    top = (76, 140, 255)
    bottom = (24, 84, 189)
    for y in range(CANVAS):
        mix = y / max(CANVAS - 1, 1)
        color = tuple(round(top[i] * (1 - mix) + bottom[i] * mix) for i in range(3)) + (255,)
        for x in range(CANVAS):
            pixels[x, y] = color

    mask = Image.new("L", (CANVAS, CANVAS), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, CANVAS - 1, CANVAS - 1), radius=60 * SCALE, fill=255)
    image.putalpha(mask)
    draw = ImageDraw.Draw(image)
    rounded_rectangle(draw, (52, 60, 204, 104), 16, "white")
    rounded_rectangle(draw, (108, 100, 152, 200), 16, "white")
    draw.ellipse(scaled_box((52, 156, 84, 188)), fill="#AFCBFF")
    draw.ellipse(scaled_box((172, 156, 204, 188)), fill="#AFCBFF")
    draw.line(scaled_box((84, 172, 108, 172)), fill="#AFCBFF", width=12 * SCALE)
    draw.line(scaled_box((152, 172, 172, 172)), fill="#AFCBFF", width=12 * SCALE)
    return image.resize((256, 256), Image.Resampling.LANCZOS)


def render_tray_icon():
    image = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    rounded_rectangle(draw, (12, 12, 244, 244), 54, "#2868DF")
    rounded_rectangle(draw, (45, 54, 211, 101), 15, "white")
    rounded_rectangle(draw, (106, 96, 150, 211), 15, "white")
    return image.resize((256, 256), Image.Resampling.LANCZOS)


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
