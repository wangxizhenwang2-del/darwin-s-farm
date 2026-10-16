from PIL import Image, ImageDraw

CELL = 512
FLOOR = 452
guide = Image.new('RGB', (CELL * 8, CELL), (247, 247, 242))
poses = [
    # near foot x/y, far foot x/y, near knee x/y, far knee x/y
    ((210, FLOOR), (307, FLOOR), (236, 389), (278, 389)),
    ((210, FLOOR), (299, FLOOR - 7), (236, 389), (280, 389)),
    ((210, FLOOR), (264, FLOOR - 58), (236, 389), (279, 373)),
    ((210, FLOOR), (186, FLOOR - 20), (236, 389), (229, 386)),
    ((307, FLOOR), (210, FLOOR), (278, 389), (236, 389)),
    ((299, FLOOR - 7), (210, FLOOR), (280, 389), (236, 389)),
    ((264, FLOOR - 58), (210, FLOOR), (279, 373), (236, 389)),
    ((186, FLOOR - 20), (210, FLOOR), (229, 386), (236, 389)),
]
for i, (near, far, near_knee, far_knee) in enumerate(poses):
    x0 = i * CELL
    d = ImageDraw.Draw(guide)
    # Flat guide geometry; the character reference supplies all final styling.
    d.line((x0 + 76, FLOOR + 2, x0 + 436, FLOOR + 2), fill=(170, 170, 170), width=2)
    d.ellipse((x0 + 180, 252, x0 + 330, 369), fill=(95, 164, 207), outline=(37, 71, 103), width=3)
    d.line((x0 + 208, 285, x0 + 194, 231), fill=(37, 71, 103), width=11)
    d.ellipse((x0 + 182, 215, x0 + 211, 242), fill=(95, 164, 207), outline=(37, 71, 103), width=3)
    # Far leg: light ochre. Near leg: dark brown. Maintain identities across cells.
    for hip, knee, foot, color in (
        ((270, 348), far_knee, far, (210, 150, 59)),
        ((240, 349), near_knee, near, (107, 57, 24)),
    ):
        pts = [(x0 + hip[0], hip[1]), (x0 + knee[0], knee[1]), (x0 + foot[0], foot[1])]
        d.line(pts, fill=color, width=11, joint='curve')
        fx, fy = x0 + foot[0], foot[1]
        d.line((fx - 21, fy + 1, fx + 10, fy + 1), fill=color, width=7)
        d.ellipse((fx - 5, fy - 5, fx + 5, fy + 5), fill=color)

guide.save('Artifacts/sprite-gen/peacock-walk/revision-3/gait-guide.png')
