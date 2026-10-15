"""Apply approved evolution edge deletions while preserving other DOCX parts."""
import json
import os
import re
import tempfile
import zipfile
from pathlib import Path
from lxml import etree

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "游戏策划案v4.6.docx"
NS = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}


def text(node):
    return "".join(node.xpath(".//w:t/text()", namespaces=NS))


with zipfile.ZipFile(PATH) as z:
    root = etree.fromstring(z.read("word/document.xml"))
tables = [t for t in root.xpath(".//w:tbl", namespaces=NS)
          if "进化连接" in text(t.xpath("./w:tr", namespaces=NS)[0])]
assert len(tables) == 1
table = tables[0]
removed = 0
for row in table.xpath("./w:tr", namespaces=NS)[1:]:
    cells = row.xpath("./w:tc", namespaces=NS)
    label = re.sub(r"\s+", "", text(cells[1]))
    if label in ("鸡↔孔雀", "孔雀↔鸡", "鲨母↔虾兔", "虾兔↔鲨母"):
        table.remove(row)
        removed += 1
assert removed in (0, 1, 2)
for i, row in enumerate(table.xpath("./w:tr", namespaces=NS)[1:], 1):
    nodes = row.xpath("./w:tc[1]//w:t", namespaces=NS)
    nodes[0].text = str(i)
    for node in nodes[1:]:
        node.text = ""

for p in root.xpath(".//w:p", namespaces=NS):
    nodes = p.xpath(".//w:t", namespaces=NS)
    full = text(p)
    if "进化" in full or "生物网" in full:
        # Count replacement has equal length, preserving run formatting even
        # when the two digits are split across Word runs.
        for match in reversed(list(re.finditer(r"(?:22|21)(?=\s*条)", full))):
            at = match.start() + 1
            cursor = 0
            for node in nodes:
                value = node.text or ""
                if cursor <= at < cursor + len(value):
                    index = at - cursor
                    node.text = value[:index] + "0" + value[index + 1:]
                    break
                cursor += len(value)
    for node in nodes:
        if node.text:
            node.text = node.text.replace("删除鸡 ↔ 鹿连接", "删除鸡 ↔ 鹿及鸡 ↔ 孔雀连接")
            node.text = node.text.replace("删除鸡 ↔ 鹿及鸡 ↔ 孔雀连接", "删除鸡 ↔ 鹿、鸡 ↔ 孔雀及鲨母 ↔ 虾兔连接")
            node.text = node.text.replace("两者不再互为预设进化候选", "上述连接的两端不再互为预设进化候选")

data = json.loads((ROOT / "Assets/BiowebV46/bioweb.json").read_text(encoding="utf-8"))
names = {row["id"]: row["name"] for row in data["species"]}
expected = {frozenset((names[e["from"]], names[e["to"]])) for e in data["evolutionLinks"]}
actual = set()
for row in table.xpath("./w:tr", namespaces=NS)[1:]:
    actual.add(frozenset(re.sub(r"\s+", "", text(row.xpath("./w:tc", namespaces=NS)[1])).split("↔")))
assert actual == expected and len(actual) == 20
backup = Path(tempfile.mkdtemp(prefix="darwin-v46-edge-edit-")) / PATH.name
backup.write_bytes(PATH.read_bytes())
updated = etree.tostring(root, encoding="UTF-8", xml_declaration=True, standalone=True)
temp_path = PATH.with_name(PATH.stem + ".edge-edit.tmp.docx")
with zipfile.ZipFile(PATH) as src, zipfile.ZipFile(temp_path, "w") as dst:
    for item in src.infolist():
        dst.writestr(item, updated if item.filename == "word/document.xml" else src.read(item.filename))
os.replace(temp_path, PATH)
print(f"Removed {removed} row; document has 20 edges. Backup: {backup}")
