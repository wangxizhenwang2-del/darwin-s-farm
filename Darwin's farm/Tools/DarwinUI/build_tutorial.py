"""Rebuild the seven-chapter Unity tutorial asset from the supplied handbook."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[2]
source = root / "Tools/DarwinUI/HandbookSource.md"
destination = root / "Assets/Resources/DarwinJournal/TutorialDatabase.asset"
illustrations = destination.parent / "Illustrations"
sprites = {}
for name in ("FiveSpecies", "NeighborTiles", "FiveTools", "FiveTraits", "FoodWeb"):
    meta = (illustrations / (name + ".png.meta")).read_text(encoding="utf-8-sig")
    sprites[name] = re.search(r"(?m)^guid: ([a-f0-9]+)$", meta).group(1)

text = source.read_text(encoding="utf-8-sig")
text = text.split("## Unity 教程接入说明", 1)[0] # Developer notes are not player content.
text = text.replace("±5,000 / 游戏日", "±25,000 / 游戏日")
text = text.replace("±15,000 / 游戏日", "±50,000 / 游戏日")
text = text.replace("±10%", "±25%").replace("±30%", "±50%")
text = text.replace("档位数值来自当前策划候选配置，正式发布前请与 Unity 实际数值核对。", "档位数值已按当前游戏配置核对。")
text = text.replace("海拔相差一级会阻碍通行，相差两级则无法直接迁徙。", "海拔差会影响通行；高差越大，迁徙越困难。")

def quote(value):
    return json.dumps(value, ensure_ascii=False)

def rich(markdown):
    lines = []
    for line in markdown.strip().splitlines():
        line = line.strip()
        if line == "---" or re.match(r"^\|[-:| ]+\|$", line):
            continue
        if line.startswith("### "):
            line = "<size=112%><b>" + line[4:] + "</b></size>"
        elif line.startswith("> "):
            line = "<color=#557653>✎ " + line[2:] + "</color>"
        elif line.startswith("| "):
            line = " · ".join(part.strip() for part in line.strip("| ").split("|"))
        line = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", line)
        lines.append(line)
    return "\n".join(lines).strip()

def mobile_copy(body):
    replacements = {
        "按住 <b>Alt 并单击鼠标左键</b> 点击地块": "在触屏上长按地块",
        "按住鼠标中键拖动以平移镜头，滚动鼠标滚轮放大或缩小画面。": "单指拖动平移视角，双指捏合缩放画面。",
        "按 <b>N</b> 打开土地建造栏，从下方栏拖动土地到目标格子进行建造。": "点击「建造地块」，选择土地，再在目标格子确认建造。",
        "画面左侧有五种环境工具": "点击「环境工具」可选择五种工具",
        "按住 <b>Alt + 鼠标左键</b> 点击地块上的生物": "轻点地块上的生物",
        "按 <b>M</b> 打开生物网与进化网。": "触控版可在生物信息面板观察种群；生物网与进化网当前需在桌面版按 M 查看。",
        "在生物网中<b>左键点击某种生物</b>": "在桌面版生物网中点击某种生物",
        "按 <b>M</b> 打开进化网，选择想培育的物种": "在生物信息中观察性状；桌面版可按 M 打开进化网，选择想培育的物种",
    }
    for source_text, mobile_text in replacements.items():
        body = body.replace(source_text, mobile_text)
    return body

chapter_pattern = re.compile(r"(?m)^# 第[一二三四五六七]章 · (.+)$")
section_pattern = re.compile(r"(?m)^## ([1-7])\.([1-5]) (.+)$")
chapters = list(chapter_pattern.finditer(text))
out = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:", "--- !u!114 &11400000", "MonoBehaviour:", "  m_ObjectHideFlags: 0", "  m_CorrespondingSourceObject: {fileID: 0}", "  m_PrefabInstance: {fileID: 0}", "  m_PrefabAsset: {fileID: 0}", "  m_GameObject: {fileID: 0}", "  m_Enabled: 1", "  m_EditorHideFlags: 0", "  m_Script: {fileID: 11500000, guid: ce9a353891ca7754f8f032105200a284, type: 3}", "  m_Name: TutorialDatabase", "  m_EditorClassIdentifier: Assembly-CSharp::TutorialDatabase", "  categories:"]
for chapter_index, chapter in enumerate(chapters):
    chunk = text[chapter.end():chapters[chapter_index + 1].start() if chapter_index + 1 < len(chapters) else len(text)]
    sections = list(section_pattern.finditer(chunk))
    out += [f"  - id: chapter{chapter_index+1}", f"    title: {quote(chapter.group(1))}", f"    sortOrder: {chapter_index}", "    pages:"]
    for section_index, section in enumerate(sections):
        body = chunk[section.end():sections[section_index+1].start() if section_index+1 < len(sections) else len(chunk)]
        if chapter_index == 6:
            body = "<color=#9A694A>规划玩法：当前测试版可能关闭经济系统。</color>\n\n" + rich(body)
        else:
            body = rich(body)
        art = {"1.3": "FiveSpecies", "2.2": "NeighborTiles", "2.3": "NeighborTiles",
               "3.2": "FiveTools", "3.3": "FiveTools", "4.2": "FiveTraits",
               "4.3": "FoodWeb", "5.1": "FoodWeb", "5.2": "FoodWeb",
               "6.4": "FiveSpecies"}.get(section.group(1)+"."+section.group(2))
        reference = "{fileID: 21300000, guid: " + sprites[art] + ", type: 3}" if art else "{fileID: 0}"
        out += [f"    - id: {section.group(1)}_{section.group(2)}", f"      title: {quote(section.group(3))}", f"      content: {quote(body)}", f"      mobileContent: {quote(mobile_copy(body))}", f"      illustration: {reference}", f"      sortOrder: {section_index}"]
destination.write_text("\n".join(out)+"\n", encoding="utf-8")
print(f"Wrote {len(chapters)} chapters and {sum(len(section_pattern.findall(text[c.end():chapters[i+1].start() if i+1 < len(chapters) else len(text)])) for i,c in enumerate(chapters))} pages")
