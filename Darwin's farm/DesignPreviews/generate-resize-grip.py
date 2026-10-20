"""Draw a small paper-and-ink corner grip to match the game's UI artwork."""

from pathlib import Path
from PIL import Image, ImageDraw

output = (Path(__file__).resolve().parents[1] /
          "Assets/Resources/GameUI/ToolArt/Generated/ResizeGrip.png")
size = 128
scale = 4
image = Image.new("RGBA", (size * scale, size * scale), (0, 0, 0, 0))
draw = ImageDraw.Draw(image, "RGBA")

def pt(points):
    return [(round(x * scale), round(y * scale)) for x, y in points]

def stroke(points, color, width):
    draw.line(pt(points), fill=color, width=round(width * scale), joint="curve")

# A quiet parchment corner, outlined twice like the existing hand-drawn frame.
draw.polygon(pt([(23, 113), (113, 23), (113, 113)]),
             fill=(221, 215, 185, 230))
stroke([(23, 111), (48, 89), (73, 63), (94, 42), (112, 23)],
       (56, 64, 47, 235), 4)
stroke([(24, 114), (70, 114), (113, 113), (113, 24)],
       (53, 60, 45, 235), 5)
stroke([(26, 108), (72, 62), (109, 27)],
       (135, 146, 112, 145), 2)

# Three diagonal carved marks give the standard bottom-right resize cue.
for start, end in [((54, 105), (105, 54)),
                   ((75, 105), (105, 75)),
                   ((94, 105), (105, 94))]:
    sx, sy = start
    ex, ey = end
    stroke([(sx + 2, sy + 2), (ex + 2, ey + 2)],
           (249, 246, 220, 200), 7)
    stroke([start, ((sx + ex) / 2 - 1, (sy + ey) / 2 + 1), end],
           (49, 62, 47, 245), 6)
    stroke([(sx + 1, sy - 2), (ex + 1, ey - 2)],
           (102, 122, 91, 110), 2)

image.resize((size, size), Image.Resampling.LANCZOS).save(output)
print(output)
