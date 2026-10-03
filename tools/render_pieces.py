#!/usr/bin/env python3
"""Renders the 12 chess piece images from the macOS Apple Symbols font into Assets/Resources/Pieces."""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Resources", "Pieces")
FONT = "/System/Library/Fonts/Apple Symbols.ttf"
SIZE = 256

# Outline glyphs (white set) and solid glyphs (black set) per piece letter.
OUTLINE = {"K": "♔", "Q": "♕", "R": "♖", "B": "♗", "N": "♘", "P": "♙"}
SOLID = {"K": "♚", "Q": "♛", "R": "♜", "B": "♝", "N": "♞", "P": "♟"}


def draw_glyph(img, ch, font, fill):
    d = ImageDraw.Draw(img)
    l, t, r, b = d.textbbox((0, 0), ch, font=font)
    x = (SIZE - (r - l)) / 2 - l
    y = (SIZE - (b - t)) / 2 - t
    d.text((x, y), ch, font=font, fill=fill)


def silhouette(p, font):
    """Mask of everything inside the piece's outer contour (holes filled)."""
    mask = Image.new("L", (SIZE, SIZE), 0)
    draw_glyph(mask, SOLID[p], font, 255)
    draw_glyph(mask, OUTLINE[p], font, 255)
    mask = mask.point(lambda v: 255 if v > 40 else 0)
    ImageDraw.floodfill(mask, (0, 0), 128)
    return mask.point(lambda v: 0 if v == 128 else 255)


def main():
    os.makedirs(OUT, exist_ok=True)
    font = ImageFont.truetype(FONT, int(SIZE * 0.86))
    for p in "KQRBNP":
        sil = silhouette(p, font)

        # White piece: filled white silhouette, outline glyph on top in near-black.
        img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        img.paste((250, 250, 245, 255), (0, 0), sil)
        draw_glyph(img, OUTLINE[p], font, (25, 25, 25, 255))
        img.save(os.path.join(OUT, f"w{p}.png"))

        # Black piece: light detail lines inside, solid glyph in dark grey, black outline.
        img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        img.paste((225, 225, 225, 255), (0, 0), sil)
        draw_glyph(img, SOLID[p], font, (35, 35, 35, 255))
        draw_glyph(img, OUTLINE[p], font, (0, 0, 0, 255))
        img.save(os.path.join(OUT, f"b{p}.png"))
    print("wrote", OUT)


if __name__ == "__main__":
    main()
