"""Update the V4.5 Word plan with the approved V4.6 habitat and bioweb data."""
import json
import os
import pathlib
import re

from docx import Document
from docx.shared import Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_CELL_VERTICAL_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets" / "BiowebV46" / "bioweb.json"
CODE = ROOT / "Assets" / "Scripts" / "Simulation" / "Environment" / "EnvironmentRules.cs"
OUT = ROOT / "游戏策划案v4.6.docx"
OLD = ROOT / "游戏策划案v4.5.docx"


def shade(cell, color):
    tc_pr = cell._tc.get_or_add_tcPr()
    shd = OxmlElement("w:shd")
    shd.set(qn("w:fill"), color)
    tc_pr.append(shd)


def border(cell):
    tc_pr = cell._tc.get_or_add_tcPr()
    borders = OxmlElement("w:tcBorders")
    for side in ("top", "left", "bottom", "right"):
        edge = OxmlElement("w:" + side)
        edge.set(qn("w:val"), "single")
        edge.set(qn("w:sz"), "4")
        edge.set(qn("w:color"), "D9D9D9")
        borders.append(edge)
    tc_pr.append(borders)


def make_table(doc, after, rows, widths=None):
    table = doc.add_table(rows=0, cols=len(rows[0]))
    table.autofit = False
    table.style = "Table Grid"
    for ri, row in enumerate(rows):
        cells = table.add_row().cells
        for ci, value in enumerate(row):
            cells[ci].text = str(value)
            cells[ci].vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
            if widths:
                cells[ci].width = widths[ci]
            shade(cells[ci], "E7EEF4" if ri == 0 else "FFFFFF" if ri % 2 else "F7F9FB")
            border(cells[ci])
            for paragraph in cells[ci].paragraphs:
                paragraph.paragraph_format.space_after = Pt(2)
                paragraph.paragraph_format.space_before = Pt(2)
                for run in paragraph.runs:
                    run.font.size = Pt(8.5 if len(rows[0]) >= 7 else 9)
                    if ri == 0:
                        run.bold = True
    table.rows[0]._tr.get_or_add_trPr().append(OxmlElement("w:tblHeader"))
    after._p.addnext(table._tbl)
    return table


def insert_paragraph_after(doc, after, text, style=None):
    p = doc.add_paragraph(text, style=style)
    after._p.addnext(p._p)
    return p


doc = Document(OLD)
ps = list(doc.paragraphs)
old_scenarios = doc.tables[18]
old_overview = doc.tables[0]
old_variables = doc.tables[4]
old_climate_example = doc.tables[9]
replace = {
    0: "达尔文农场游戏策划案 V4.6",
    2: "2026 年 10 月 12 日  ·  面向策划、程序、美术与界面协作",
    3: "V4.6 确定 27 种生境状态、八种视觉地形，以及正式版 15 物种进化网和食物链。玩家从干燥的荒漠和鸡群出发，通过扩张、环境工程与食物供给影响生态。本文将正式数据、运行规则和现阶段代码边界分开说明。",
    4: "V4.6 变更摘要　陆地生境只由海拔、温度和湿度确定；当前生境负责温湿度与植被日增量的恢复目标。旧地形网取消。正式生物网包含 15 个物种、20 条双向进化连接和 25 条猎物到捕食者的食物关系，物种数值以随附 JSON 为准。",
    5: "实现口径　八种视觉地形、27 种生境状态及正式物种资产已接入 WhiteBox。当前逐日生命循环仍只处理陆地；水域、天空和跨生态位进化连接已进入正式数据，但其完整生态结算与路线尚待接入和实机验收。旧版五物种离线结果只作为历史测试，不代表 V4.6 平衡结论。",
    7: "当前 WhiteBox 开局只生成一块干燥的荒漠地块和 160 只鸡；地块使用海拔 0、T=55、H=35、R=50,000 的放置预设。其他地块、种群和环境操作可由编辑模式配置。策划目标保留 2,000 初始生物能，正式存档入口尚待设计。",
    10: "二  看植物库存 S 与实际每日恢复量 R。S 决定今天能吃多少，R 决定后续生产。温湿度与海拔决定生境状态，当前生境决定 R 的自然恢复目标；植物工具可直接改变当时的 R 和 S。操作前看实际值、目标、生境名称、抢断名额和剩余时间。",
    15: "每日按物种汇总全图野外存活数，按各物种价值与数量的自然对数发放收入；仓内备份不计。每个物种首次解锁另奖：植食者 200、一级捕食者 500、二级捕食者 1,000。正式网的物种价值为 1～30，逐项见第 7 章。",
    26: "第 1 至 6 章说明地图、生境状态、环境工程、季风和植物；第 7 至 11 章说明正式物种数据、食物网、进化与迁徙；第 12 至 15 章说明经济、事件、界面、实现边界与验收。",
    34: "待开发或待确认　正式换貌、季风表现和完整经济界面仍需实机检查；水域与天空生态和跨生态位进化尚未进入当前陆地日循环。旧版四物种 15 分钟目标需按新网络重新设定和验证。",
    39: "计算与设计　海拔和植物操作即时，温湿度用 25 天渐入，种群逐日结算。界面同时显示实际值、当前生境、恢复目标、操作槽位、过渡日数和预计生境。",
    41: "2 地块地图与陆地生境",
    48: "规则　陆地生境状态仅由实际海拔 z、有效温度 T 和有效湿度 H 判定，共 27 种；每种状态映射八种视觉地形之一。实际植被日增量 R、植物库存 S 与今日增长 g 均不参与判定。陆地单格容量 B=1,000,000，R 限制在 0～150,000。",
    50: "计算与设计　T、H 是 0～100 的游戏指数，不等同摄氏度或现实相对湿度。当前生境的默认值与实际值分别保存；切换生境时实际 T、H、R、S 不瞬间跳变，后续恢复目标改用新生境默认值。",
    51: "玩家影响与示例　两格都剩 50 万库存，R=5 万的一格长期供给低于 R=12 万的一格。湿度跨档可使生境切换并改变未来 R 的恢复目标；直接削减库存或 R 不会单独改变生境。",
    53: "通俗解释　八种视觉地形各有一个 ※ 放置预设。放置时取该格对应海拔、温度、湿度和植被日增量，初始库存 S₀=默认 R；之后由当前生境状态提供恢复目标。",
    54: "规则　下表是 V4.6 完整生境矩阵。行列括号内分别是该海拔温湿档的默认温度与湿度；格内括号是默认植被日增量，w=10,000。※ 为八种视觉地形的放置预设；方括号内为指定生物的适温／适湿，不表示新地块自动生成生物。",
    56: "计算与设计　三个海拔的默认温湿度分别为 35／55／75、30／50／70、25／45／65。边界归属固定为 0≤值≤40、40＜值＜60、60≤值≤100；40 属冷或干，60 属热或湿。每格默认值都落在自身判定区间内。",
    57: "玩家影响与示例　海拔 1 的湿冷森林目标为 T=30、H=70、R=80,000；当温度进入中档且湿度仍为湿档，生境变为湿润森林，视觉仍是森林，后续恢复目标改为 T=50、H=70、R=100,000。",
    58: "陆地 27 种生境状态的判定",
    59: "通俗解释　每块陆格同时拥有一个生境状态和一种视觉地形。跨温湿档或海拔变化时直接查矩阵；只在对应视觉地形发生变化时换貌。已建陆格不能通过菜单直接换生境。",
    60: "规则　生境状态 = f(z,T,H)。不再按地形网的邻边移动，也不按 R 分档分类。海拔为 0、1、2；温湿度分别按冷／中／热、干／中／湿分档，所有合法组合恰好对应矩阵中的一格。",
    62: "计算与设计　模型保留温湿度小数并以实际有效值判定，不先四舍五入。温湿度、季风或海拔操作跨界时重算生境；只有生境状态变化后才更新视觉映射和恢复目标。植物工具不触发生境切换。",
    63: "玩家影响与示例　z=0、T=40、H=60 对应冷湿的阴冷森林；T 刚超过 40 即进入中湿的湿润雨林。z=1 的湿冷森林变为湿润森林时，视觉地形保持森林，R 目标从 8w 改为 10w。",
    64: "旧 TerrainTransitionGraph 地形网不再提供玩法路线或转换门槛；科技预览直接计算目标海拔和温湿度对应的生境状态。",
    73: "玩家影响与示例　炎热草原 T=75，增温目标 100、D=25；第 10 天为 85，第 25 天为 100。若 T=85 时主动撤销，退出从 85 开始，不瞬间跳回 75。",
    77: "规则　玩家命令在推进日前按提交顺序生效；环境时间线与生境状态先更新，再长植物、捕食、植物竞争、出生死亡、单项性状选择、濒危注销、原地分化、正式网中的物种转化与自然迁徙，最后发布快照。当前日循环只覆盖陆地生态。",
    83: "陆地原地分化与已接入的陆地同生态位进化逐日检查；普通自然迁徙按既定检查日进行。正式网中的水域、天空和跨生态位连接保留为待接入路线，不能视为已被陆地日循环执行。",
    89: "计算与设计　单格温湿目标=clamp(投放时当前生境默认值±25或50,0,100)，从实际 T／H 用 25 天进入；季风在覆盖成员上即时加 ±20。植物投放使 S←clamp(S×倍率,0,B)，R←clamp(当前实际 R±25,000或50,000,0,150,000)。海拔 z←z±1，越界拒绝。",
    93: "规则　温湿度提交后 25 天线性进入目标，之后维持 150 天，再用 150 天回到届时生境状态的默认值。换目标从当前实际值重新开始；失去名额或主动撤销时从当前值恢复。生境切换不会改写仍处于进入或维持阶段的操作目标。",
    94: "计算与设计　恢复期不占两个生效名额；恢复目标随当前生境状态变化。若恢复中生境再次切换，从切换当天实际值向新默认值重新计 150 天。季风偏移与仍生效的温湿工具并行。",
    96: "玩家影响与示例　炎热草原默认 T=75，高 1 档温度目标为 100。若经过另一生境边界，工具本次目标和剩余时长仍保留；恢复阶段以当时生境默认温度为终点。",
    99: "通俗解释　植物工具同时改变短期存粮 S 和长期供给 R。工具结束后 R 回到届时生境状态的默认植被日增量；温湿度通过切换生境改变这个目标。",
    100: "规则　高 1／2 档使当前 S×1.25／1.50、当前 R+25,000／50,000；低 1／2 档使当前 S×0.75／0.50、当前 R−25,000／50,000。S 裁剪在 0～B，R 裁剪在 0～150,000。R 维持 50 天，再用 50 天线性回到届时生境默认值；库存一次性变化不自动撤回。",
    104: "R(a)=R₀+(R_生境默认−R₀)×clamp(a/50,0,1)。恢复开始和恢复中生境切换时，均从当日实际 R₀ 向当前生境默认值重新计时。",
    105: "R 的自然恢复目标直接读取当前生境矩阵的格内数值。温湿度变化本身不叠加另一套连续气候修正公式；只有跨档导致生境变化时，目标才更新。",
    106: "玩家影响与示例　炎热草原默认 R=90,000、S=90,000，高 2 档植物提交后 R=140,000、S=135,000；生境仍是炎热草原，恢复目标为 90,000。",
    107: "玩家影响与示例　炎热草原低 2 档植物提交后 R=40,000、S=45,000；再次投放从当时实际库存再乘 0.5。即使库存之后增长，降低 R 的作用仍按工具时序维持。",
    111: "规则　海拔有效范围 0～2，越界拒绝。海拔操作提交后按当时有效 T、H 立即重算生境、水路、普通迁徙和季风覆盖。视觉地形仅按新生境映射更新；海拔本身永久保留。季风锚点和重叠的规则沿用第 5 章。",
    114: "式 5　生境状态分类输入",
    115: "生境=f(z,T,H)；T、H 使用实际有效值，R 与植物库存 S 不参与。",
    116: "式 6　植被日增量恢复目标",
    117: "R_目标=当前生境状态的格内默认值；生境切换时重定向恢复，不瞬时覆盖实际 R。",
    118: "玩家影响与示例　草原持续降湿，湿度跨入干档后可变为荒漠；恢复终点按当时荒漠生境确定。反向增湿可回到草原。海拔变化也可换貌，但不会重新生成库存。",
    166: "正式物种模板　V4.6 使用随附 bioweb JSON 的 15 个物种名称、营养级、基础运动、体型、繁殖、生物能价值和基础生态位。下表完整列出这些数值。十种陆地／空中生物的适温适湿由第 2 章矩阵方括号补齐；五种水域生物的适温适湿未在 JSON 或矩阵中给定，资产暂用中性 50／50 占位，不作为已定策划数值。",
    181: "正式食物链只使用 JSON 中的 25 条定向关系，方向为“猎物→捕食者”。营养级限定捕食层次，具体可吃对象仍须列在关系中；共享猎物只扣一次活体。完整关系按捕食者汇总如下。",
    204: "正式进化网包含 15 个物种与 20 条连接。JSON 的 from／to 用于绘图，游戏把每条连接按双向候选读取；连接只给候选资格，性状、食物、位置和时序门槛仍须满足。当前陆地日循环仅执行符合陆地位置与生态位的转化；水域、天空及跨生态位连接已登记但尚未完整运行。完整边表如下。",
    217: "规则　每个物种有独立价值 V；正式网的取值为 1～30，逐项见第 7 章。每日按物种汇总全图野外存活数 N_s；仓内个体不计。N_s=0 时该物种收入为 0，不能计算 ln 0。",
    249: "界面应显示当前生境名称、视觉地形、实际 T／H／R／z、当前生境默认值、库存 S、玩家目标、两个生效名额、恢复阶段与剩余时间，并给出预计生境。水格专项界面待接入。",
    250: "预览不提交操作，正式提交重验合法性。科技预览区分当前值、投放目标、即刻库存乘算、可能被挤出的工具、恢复终点及预计生境；预计生境只查目标海拔与温湿度。Visual 只读快照，不通过动画回写生态。",
    257: "14 实现边界与 V4.6 接入契约",
    259: "模型负责环境、植物、人口、食物网、性状和转化；控制器提交操作与同步地图；界面读取快照。V4.6 新增生境状态与完整 15 物种资产，20 条进化连接和 25 条食物关系已写入资产。当前陆地日循环和跨生态位路径的实施范围见下段。",
    262: "第 1～6 章的生境分类与恢复目标已接入环境模型；八种视觉地形继续复用既有表现。正式物种数值与关系已进入资产和 WhiteBox 注册。当前生命日循环仍跳过水格，天空及跨生态位预设转化未由陆地循环完成；相关规则和长链平衡需专项实现及验收。",
    269: "正式目标：从一群鸡开始，允许玩家使用环境、植物、迁徙和定向进化操作，验证正式网中可达的陆地物种、食物供给与数量稳定性。旧版鹿、狸猫、猛鹿、雀狮四物种 15 分钟验收项因模板与网络被替换，须以 V4.6 的新路径重新设计，不再作为本版通过条件。",
    270: "已验证：27 种生境默认值均落在自身区间，边界与植物不参与分类的规则通过独立组合审计；15 个物种、20 条进化连接及 25 条食物关系已生成资源。尚未验证：Unity 实机场景中的全部素材引用、陆地长期平衡、正式网的玩家可达性，以及水域、天空与跨生态位生命循环。",
}
for index, content in replace.items():
    ps[index].text = content

old_overview.rows[1].cells[1].text = "温度、湿度、海拔、植物库存与日增量；27 种生境状态映射八类视觉地形。"
old_variables.rows[3].cells[2].text = "每日植被日增量；实际 R 受植物工具和恢复时间线影响"
old_variables.rows[3].cells[3].text = "生境切换更新恢复目标；植物工具即时增减，50 天维持、50 天恢复"
for ri, value in enumerate(("75", "85", "100", "100", "100", "75"), 1):
    old_climate_example.rows[ri].cells[1].text = value
old_climate_example.rows[-1].cells[2].text = "恢复完成；按当前生境默认值"
for section in doc.sections:
    for part in (section.header, section.footer):
        for paragraph in part.paragraphs:
            for run in paragraph.runs:
                run.text = run.text.replace("V4.5", "V4.6")

for style_name in ("Title", "Subtitle", "Heading 1", "Heading 2", "Heading 3"):
    if style_name in doc.styles:
        doc.styles[style_name].font.color.rgb = RGBColor(0, 0, 0)

# Remove both V4.5 terrain tables. The new matrix is the classification source.
for old_table in (doc.tables[5], doc.tables[6]):
    old_table._tbl.getparent().remove(old_table._tbl)

profiles = re.findall(
    r'new HabitatProfile\("([^\"]+)", TerrainKind\.\w+, (\d), (\d+), (\d+), (\d+)\)',
    CODE.read_text(encoding="utf-8"))
assert len(profiles) == 27
marks = {3, 7, 8, 11, 16, 18, 19, 25}
annotations = {
    3: "鸡 55/35", 4: "孔雀 58/58", 8: "灵感菇狸 75/80",
    9: "雪狼 28/28", 11: "鹿 30/70", 14: "雀狮 52/66",
    16: "彩鸡 70/49", 18: "鹿驯 25/20", 22: "鹿飞 40/45",
    24: "哈激麟 67/20",
}
defaults = [(35, 55, 75), (30, 50, 70), (25, 45, 65)]
matrix = []
for z in range(3):
    dry, middle, wet = defaults[z]
    matrix.append([f"海拔 {z}", f"干 0–40（{dry}）", f"中 40–60（{middle}）", f"湿 60–100（{wet}）"])
    for ti, label in enumerate(("冷", "中", "热")):
        row = [f"{label} {('0–40','40–60','60–100')[ti]}（{defaults[z][ti]}）"]
        for hi in range(3):
            index = z * 9 + ti * 3 + hi
            name, _, _, _, recovery = profiles[index]
            text = ("※" if index in marks else "") + name + f"（{int(recovery)//10000}w）"
            if index in annotations:
                text += "\n[" + annotations[index] + "]"
            row.append(text)
        matrix.append(row)
matrix_table = make_table(doc, ps[54], matrix)
# The table contains its own header for each elevation; repeating elevation 0
# at the next page would mislabel elevation 1/2 rows.
tr_pr = matrix_table.rows[0]._tr.get_or_add_trPr()
for node in tr_pr.findall(qn("w:tblHeader")):
    tr_pr.remove(node)

data = json.loads(SOURCE.read_text(encoding="utf-8"))
species = data["species"]
by_id = {row["id"]: row for row in species}
species_rows = [["物种", "生态位", "营养级", "运动", "体型", "繁殖", "价值", "适温／适湿"]]
for row in species:
    name = row["name"]
    niche = {"Land": "陆", "Water": "水", "Air": "空"}[row["baseEcologicalNiche"]]
    fit = next((note.split(" ", 1)[1] for note in annotations.values()
                if note.startswith(name + " ")), "—")
    species_rows.append([name, niche, row["trophicLevel"], row["baseMovementAbility"],
                         row["baseSize"], row["baseFertility"], row["value"], fit])
make_table(doc, ps[166], species_rows)

prey_by_predator = {}
for edge in data["foodLinks"]:
    predator = by_id[edge["to"]]["name"]
    prey_by_predator.setdefault(predator, []).append(by_id[edge["from"]]["name"])
food_rows = [["捕食者", "营养级", "合法猎物"]]
for row in species:
    if row["name"] in prey_by_predator:
        food_rows.append([row["name"], row["trophicLevel"],
                          "、".join(prey_by_predator[row["name"]])])
make_table(doc, ps[181], food_rows)
insert_paragraph_after(doc, ps[181], "表中猎物共对应 25 条关系；植食者从植物预算取食，不列为 foodLinks 边。")

evolution_rows = [["序号", "进化连接（双向）"]]
for number, edge in enumerate(data["evolutionLinks"], 1):
    evolution_rows.append([number, by_id[edge["from"]]["name"] + " ↔ " +
                           by_id[edge["to"]]["name"]])
make_table(doc, ps[204], evolution_rows)
insert_paragraph_after(doc, ps[204], "V4.6 修订：删除鸡 ↔ 鹿、鸡 ↔ 孔雀及鲨母 ↔ 虾兔连接；上述连接的两端不再互为预设进化候选。其余进化连接及 25 条食物关系保持不变。")

# Replace V4.5 scenario claims with V4.6 acceptance cases.
for row in old_scenarios.rows[1:]:
    row._tr.getparent().remove(row._tr)
for scenario in [
    ("生境边界", "40 属冷／干，60 属热／湿；小数直接按真实值判定。", "独立组合审计通过；Unity 预览待验收。"),
    ("同视觉换生境", "湿冷森林升温到中档后仍显示森林，R 目标由 8w 转为 10w。", "模型审计通过；视觉过渡待实机检查。"),
    ("植物单独变化", "R 与库存变化不触发生境或视觉变化，R 按当前生境默认值恢复。", "模型审计通过；工具交叠待检查。"),
    ("正式生物网", "15 物种、20 条双向进化连接、25 条定向食物关系完整导入。", "资源静态校验完成；长期生态曲线待验收。"),
    ("跨生态位连接", "陆水空路径应有合法位置、食物预算与生命结算。", "正式数据已登记；运行规则尚待接入。"),
]:
    cells = old_scenarios.add_row().cells
    for cell, value in zip(cells, scenario):
        cell.text = value

doc.save(OUT)
print(OUT)
