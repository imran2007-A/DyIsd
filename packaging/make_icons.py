"""Generates the app icon (.ico), the Control Center header image and MSIX logos.
Run from the repo root: python packaging/make_icons.py"""
from PIL import Image, ImageDraw, ImageFilter


def rr(d, box, r, **kw):
    d.rounded_rectangle(box, radius=r, **kw)


def draw(size, tile=True):
    s = 4  # draw big, then shrink for smooth edges
    W = size * s
    im = Image.new("RGBA", (W, W), (0, 0, 0, 0))

    if tile:
        # dark rounded tile with a vertical gradient
        grad = Image.new("RGBA", (W, W))
        gd = ImageDraw.Draw(grad)
        for y in range(W):
            t = y / W
            c = int(42 * (1 - t) + 12 * t)
            gd.line([(0, y), (W, y)], fill=(c, c, c + 4, 255))
        mask = Image.new("L", (W, W), 0)
        rr(ImageDraw.Draw(mask), [0, 0, W - 1, W - 1], int(W * 0.23), fill=255)
        im.paste(grad, (0, 0), mask)
        # soft blue glow behind the pill
        glow = Image.new("RGBA", (W, W), (0, 0, 0, 0))
        ImageDraw.Draw(glow).ellipse([W * 0.12, W * 0.28, W * 0.88, W * 0.72], fill=(10, 132, 255, 120))
        glow = glow.filter(ImageFilter.GaussianBlur(W * 0.09))
        glow.putalpha(Image.composite(glow.getchannel("A"), Image.new("L", (W, W), 0), mask))
        im = Image.alpha_composite(im, glow)

    d = ImageDraw.Draw(im)
    pw, ph = W * (0.72 if tile else 0.94), W * (0.30 if tile else 0.44)
    px, py = (W - pw) / 2, (W - ph) / 2
    rr(d, [px, py, px + pw, py + ph], ph / 2, fill=(0, 0, 0, 255), outline=(255, 255, 255, 72), width=max(2, W // 64))
    # album art square
    a = ph * 0.56
    ax, ay = px + ph * 0.24, py + (ph - a) / 2
    art = Image.new("RGBA", (int(a) + 1, int(a) + 1))
    ad = ImageDraw.Draw(art)
    for y in range(art.height):
        t = y / art.height
        ad.line([(0, y), (art.width, y)], fill=(255, int(214 * (1 - t) + 159 * t), int(10 * (1 - t)), 255))
    am = Image.new("L", art.size, 0)
    rr(ImageDraw.Draw(am), [0, 0, art.width - 1, art.height - 1], int(a * 0.3), fill=255)
    im.paste(art, (int(ax), int(ay)), am)
    # equalizer bars
    bw, gap = W * (0.035 if tile else 0.05), W * (0.028 if tile else 0.035)
    x = px + pw - ph * 0.3 - (bw * 4 + gap * 3)
    for i, k in enumerate([0.35, 0.75, 0.5, 0.9]):
        bh = ph * 0.6 * k
        rr(d, [x, py + ph / 2 - bh / 2, x + bw, py + ph / 2 + bh / 2], bw / 2,
           fill=(10, 132, 255, 255) if i % 2 else (100, 210, 255, 255))
        x += bw + gap
    return im.resize((size, size), Image.LANCZOS)


# App/tray icon: the tile version reads well at every size.
draw(256).save("src/DyIsd/Assets/DyIsd.ico",
               sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
draw(128).save("src/DyIsd/Assets/icon.png")
for name, px in [("Square44x44Logo", 44), ("Square150x150Logo", 150), ("StoreLogo", 50)]:
    draw(px).save(f"packaging/Assets/{name}.png")
print("icons written")
