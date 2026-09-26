"""Generates the app icon (.ico) and MSIX logos. Run: python packaging/make_icons.py"""
from PIL import Image, ImageDraw

ACCENT = (76, 194, 255, 255)
PILL = (32, 32, 34, 255)
EDGE = (255, 255, 255, 70)

def draw(size):
    s = 4  # supersample for smooth edges
    W = size * s
    im = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    h = W * 0.46
    top = (W - h) / 2
    pad = W * 0.04
    d.rounded_rectangle([pad, top, W - pad, top + h], radius=h / 2, fill=PILL, outline=EDGE, width=max(1, W // 48))
    # album-art square on the left
    a = h * 0.52
    ax = pad + h * 0.26
    d.rounded_rectangle([ax, top + (h - a) / 2, ax + a, top + (h + a) / 2], radius=a * 0.28, fill=(247, 183, 51, 255))
    # equalizer bars on the right
    bw = W * 0.05
    gap = W * 0.03
    heights = [0.35, 0.7, 0.5, 0.85]
    x = W - pad - h * 0.3 - (bw * 4 + gap * 3)
    cy = top + h / 2
    for k in heights:
        bh = h * 0.62 * k
        d.rounded_rectangle([x, cy - bh / 2, x + bw, cy + bh / 2], radius=bw / 2, fill=ACCENT)
        x += bw + gap
    return im.resize((size, size), Image.LANCZOS)

base = draw(256)
base.save("src/DyIsd/Assets/DyIsd.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
for name, px in [("Square44x44Logo", 44), ("Square150x150Logo", 150), ("StoreLogo", 50)]:
    draw(px).save(f"packaging/Assets/{name}.png")
print("icons written")
