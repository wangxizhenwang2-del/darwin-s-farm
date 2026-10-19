"""Apply the approved one-based (column, row) layouts to the 15 species assets."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1] / "Assets" / "BiowebV46" / "Species"
POSITIONS = {
    "c60ed314": ((1, 2), (1, 2)),  # 虾兔
    "7f113739": ((1, 4), (4, 3)),  # 鸡
    "6b5c947b": ((2, 1), (2, 2)),  # 鲨母
    "b59a4a82": ((1, 5), (4, 4)),  # 彩鸡
    "761468a5": ((2, 4), (5, 2)),  # 灵感菇狸
    "103a527e": ((2, 2), (1, 3)),  # 鹿游
    "f0686f45": ((2, 5), (5, 5)),  # 孔雀
    "b2e8cdba": ((3, 5), (6, 4)),  # 哈激麟
    "fd1c574b": ((2, 3), (4, 1)),  # 鹿
    "a61486cd": ((3, 4), (6, 3)),  # 鹿飞
    "0143fd10": ((3, 1), (2, 4)),  # 大鲨母
    "080b0789": ((3, 2), (3, 3)),  # 鲨鹿
    "fb2d8345": ((4, 3), (6, 2)),  # 雪狼
    "b2afc73a": ((3, 3), (5, 3)),  # 鹿驯
    "ad3c3d0f": ((4, 4), (6, 1)),  # 雀狮
}

for prefix, (evolution, food) in POSITIONS.items():
    matches = list(ROOT.glob(f"*-{prefix}.asset"))
    if len(matches) != 1:
        raise RuntimeError(f"Expected one asset for {prefix}, found {len(matches)}")
    asset = matches[0]
    data = asset.read_text(encoding="utf-8")
    data = re.sub(r"^  (?:evolutionGridPos|foodGridPos):.*\n", "", data, flags=re.MULTILINE)
    anchor = re.search(r"^  baseEcologicalNiche:.*\n", data, re.MULTILINE)
    if not anchor:
        raise RuntimeError(f"Missing insertion point in {asset}")
    fields = (f"  evolutionGridPos: {{x: {evolution[0]}, y: {evolution[1]}}}\n"
              f"  foodGridPos: {{x: {food[0]}, y: {food[1]}}}\n")
    data = data[:anchor.end()] + fields + data[anchor.end():]
    asset.write_text(data, encoding="utf-8", newline="")
