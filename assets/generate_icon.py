"""Build-time only: render the app's vector-style badge into a multi-size ICO."""
from pathlib import Path
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent
SCALE = 12
SIZE = 96 * SCALE

def p(values):
    return tuple(round(v * SCALE) for v in values)

def bezier(a, b, c, d, count=64):
    result = []
    for i in range(count + 1):
        t = i / count
        result.append(tuple((1-t)**3*a[k] + 3*(1-t)**2*t*b[k] + 3*(1-t)*t*t*c[k] + t**3*d[k] for k in (0, 1)))
    return result

image = Image.new("RGBA", (SIZE, SIZE))
mask = Image.new("L", (SIZE, SIZE))
ImageDraw.Draw(mask).rounded_rectangle(p((4, 4, 92, 92)), radius=23*SCALE, fill=255)
gradient = Image.new("RGBA", (SIZE, SIZE))
pixels = ImageDraw.Draw(gradient)
for y in range(SIZE):
    t = y / (SIZE - 1)
    color = tuple(round(a*(1-t) + b*t) for a, b in zip((91, 59, 155), (45, 29, 84))) + (255,)
    pixels.line((0, y, SIZE, y), fill=color)
image.paste(gradient, (0, 0), mask)
draw = ImageDraw.Draw(image)
draw.rounded_rectangle(p((5, 5, 91, 91)), radius=22*SCALE, outline=(255, 255, 255, 48), width=SCALE)
# A broad, continuous horseshoe silhouette stays legible at 16 pixels.
outline = [(24, 23), (24, 51)]
outline += bezier((24, 51), (24, 84), (72, 84), (72, 51))
outline += [(72, 23), (57, 23), (57, 51)]
outline += bezier((57, 51), (57, 64), (39, 64), (39, 51))
outline += [(39, 23)]
draw.polygon([p(point) for point in outline], fill=(249, 247, 255, 255))
draw.rounded_rectangle(p((24, 21, 39, 31)), radius=3*SCALE, fill=(129, 240, 187, 255))
draw.rounded_rectangle(p((57, 21, 72, 31)), radius=3*SCALE, fill=(129, 240, 187, 255))
icon = image.resize((256, 256), Image.Resampling.LANCZOS)
icon.save(OUT / "umapyon.ico", sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
icon.save(OUT / "umapyon.png")
