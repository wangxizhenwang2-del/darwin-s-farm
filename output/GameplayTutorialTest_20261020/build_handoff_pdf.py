from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import json
from xml.sax.saxutils import escape

from PIL import Image
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas
from reportlab.platypus import Paragraph, Table, TableStyle
from pypdf import PdfReader

SOURCE = Path(__file__).resolve().parent
ROOT = SOURCE.parent.parent
OUT = ROOT / "output/pdf"
OUT.mkdir(parents=True, exist_ok=True)
PDF = OUT / "达尔文农场_教程与难度测试报告.pdf"
ZIP = OUT / "达尔文农场_测试反馈_可直接转发.zip"
SUMMARY = json.loads((SOURCE / "summary.json").read_text(encoding="utf-8"))
W, H = A4
M = 42
CW = W - 2*M
GREEN = colors.HexColor("#254B3E")
INK = colors.HexColor("#263B33")
MUTED = colors.HexColor("#66766D")
PALE = colors.HexColor("#F0F4EE")
LINE = colors.HexColor("#D5DED3")
ORANGE = colors.HexColor("#A95B35")
CREAM = colors.HexColor("#FBFAF5")

pdfmetrics.registerFont(TTFont("Hei", "C:/Windows/Fonts/simhei.ttf"))
pdfmetrics.registerFont(TTFont("Song", "C:/Windows/Fonts/simsun.ttc", subfontIndex=0))
c = canvas.Canvas(str(PDF), pagesize=A4, pageCompression=1)
c.setTitle("达尔文农场：教程、玩法理解与难度测试反馈")
c.setAuthor("本地测试反馈")
c.setSubject("两轮自动交互、画面检查与模拟观察；含真实截图证据")

def para(text, x, top, width, size=10.5, color=INK, font="Song", leading=None):
    style = ParagraphStyle("text", fontName=font, fontSize=size, leading=leading or size*1.55,
                           textColor=color, wordWrap="CJK", spaceAfter=0)
    p = Paragraph(text, style)
    _, height = p.wrap(width, H)
    assert top + height < H - 30, (text[:30], top, height)
    p.drawOn(c, x, H-top-height)
    return height

def label(text, x, top, size=10, color=GREEN):
    c.setFillColor(color)
    c.setFont("Hei", size)
    c.drawString(x, H-top-size, text)

def page(number, title, subtitle):
    c.setFillColor(colors.white)
    c.rect(0, 0, W, H, fill=1, stroke=0)
    c.setFillColor(GREEN)
    c.rect(0, H-10, W, 10, fill=1, stroke=0)
    label("达尔文农场  /  测试反馈", M, 28, 10)
    label("2026.10.20", W-M-70, 28, 10, MUTED)
    label(title, M, 61, 23)
    para(subtitle, M, 96, CW, 10.5, MUTED)
    c.setStrokeColor(LINE)
    c.line(M, 41, W-M, 41)
    label("WhiteBox · 两轮自动测试与画面检查 · 版本 " + SUMMARY["revision"][:7], M, H-30, 8, MUTED)
    label(f"{number} / 4", W-M-30, H-30, 8, MUTED)

def image(name, x, top, width, crop=None):
    path = SOURCE/name
    with Image.open(path) as im:
        iw, ih = im.size
    crop = crop or (0, 0, iw, ih)
    sx, sy, sw, sh = crop
    scale = width/sw
    height = sh*scale
    c.saveState()
    clip = c.beginPath()
    clip.rect(x, H-top-height, width, height)
    c.clipPath(clip, stroke=0, fill=0)
    c.drawImage(str(path), x-sx*scale, H-top-(ih-sy)*scale,
                width=iw*scale, height=ih*scale, mask="auto")
    c.restoreState()
    c.setStrokeColor(LINE)
    c.setLineWidth(.65)
    c.rect(x, H-top-height, width, height, fill=0, stroke=1)
    return lambda box: (x+(box[0]-sx)*scale, H-top-(box[1]+box[3]-sy)*scale,
                        box[2]*scale, box[3]*scale)

def note(top, text, height=48):
    c.setFillColor(PALE)
    c.roundRect(M, H-top-height, CW, height, 6, stroke=0, fill=1)
    para(text, M+13, top+10, CW-26, 10, GREEN, "Hei")

def cell(text, header=False):
    return Paragraph(escape(text).replace("\n", "<br/>"), ParagraphStyle("cell", fontName="Hei" if header else "Song",
                fontSize=10, leading=15.5, textColor=colors.white if header else INK, wordWrap="CJK"))

# 1. A stand-alone executive page: the receiver can understand the result here.
page(1, "玩法能理解，后续指引需要补齐", "教程、玩法理解与游戏难度  |  新加坡时间 2026-10-20")
para("读完教程后，能理解“调整环境 → 引导进化 → 维持共存”的大方向。"
     "主要操作可用；但新开局缺少首步任务，教程与实际线索界面存在差异，首次进化后的推进方向不够清楚。",
     M, 132, CW, 12, INK, "Hei", 20)
card_top = 219
card_w = (CW-20)/3
for i, (big, small) in enumerate([("2 轮", "重新开始并独立检查"), ("26 页 / 轮", "教程导航与正文通过"), ("8 项 / 轮", "主要操作检查通过")]):
    x=M+i*(card_w+10)
    c.setFillColor(PALE)
    c.roundRect(x, H-card_top-72, card_w, 72, 6, fill=1, stroke=0)
    label(big, x+12, card_top+12, 19)
    label(small, x+12, card_top+44, 9, MUTED)
label("难度观察：同样策略，换种子复测", M, 315, 13)
rows = [[cell("策略", True), cell("第一轮 / 种子1", True), cell("第二轮 / 种子17", True)],
        [cell("完全不操作"), cell("3600日仍存活\n仅初始物种"), cell("3600日仍存活\n仅初始物种")],
        [cell("初日升温、加湿\n各小档一次"), cell("25日出现彩鸡\n3325日彩鸡灭绝"), cell("24日出现彩鸡\n3256日彩鸡灭绝")],
        [cell("每150日尝试再\n升温、加湿"), cell("25日出现彩鸡\n2651日彩鸡灭绝"), cell("24日出现彩鸡\n2593日彩鸡灭绝")],
        [cell("扩邻地并调整\n邻地气候"), cell("3600日仍存活\n未出现新物种"), cell("3600日仍存活\n未出现新物种")],
        [cell("植物增量、存量\n均降低大档"), cell("44日全灭\n触发失败"), cell("43日全灭\n触发失败")]]
t=Table(rows, colWidths=[CW*.34, CW*.33, CW*.33], rowHeights=[29]+[47]*5)
t.setStyle(TableStyle([("BACKGROUND",(0,0),(-1,0),GREEN),("VALIGN",(0,0),(-1,-1),"MIDDLE"),
                      ("LEFTPADDING",(0,0),(-1,-1),10),("RIGHTPADDING",(0,0),(-1,-1),8),
                      ("ROWBACKGROUNDS",(0,1),(-1,-1),[colors.white,CREAM]),
                      ("LINEBELOW",(0,0),(-1,-1),.5,LINE)]))
t.wrapOn(c,CW,H)
t.drawOn(c,M,H-345-264)
para("说明：重复升温/加湿不是锁定适宜值；每轮48次申请中有22次因数值不再变化被拒绝。"
     "另测试了直接造雨林：两轮均提示“地形未解锁”。", M, 621, CW, 9.2, MUTED)
note(675, "优先建议：先统一教程与实际界面，再补首步任务和进化条件反馈。", 45)
para("范围：Unity 6000.2.9f1，WhiteBox。自动交互与画面审阅；长期观察用正式日结算加速推进。"
     "本次策略最多达到两物种共存，未验证五物种目标；这不等于证明目标无法完成，也不代表真人通关率。",
     M, 736, CW, 9.5, MUTED)
c.showPage()

# 2. Evidence comparing the tutorial instruction with the current UI.
page(2, "问题 01  教程与进化线索不一致", "玩家按教程寻找“＋／－”时，实际看到的是故事线索和问号。")
label("A  教程：认识生命 > 生物网与进化网", M, 137, 11)
image("tutorial-evolution-symbols.png",M,161,CW,(630,580,1000,355))
para("教程举例写着“温度 ++、湿度 -”，并解释加减符号表示调整方向。上图为真实界面的局部放大。",
     M, 360, CW, 10, MUTED)
label("B  实际：按 M，再点开彩鸡头像", M, 410, 11)
image("round1-evolution-clue.png",M,434,CW,(712,650,700,380))
para("实际弹窗为培育故事与五项“？？？”；并没有教程描述的差距符号。两轮点击均可正常打开。",
     M, 723, CW, 10, MUTED)
note(762,"建议：教程改成当前的线索与逐步揭示规则，并提供可观察的达标反馈。",40)
c.showPage()

# 3. Show the gap between the first world view and the manual's final objective.
page(3, "问题 02  开局没有明确的第一步", "最终目标写在手册里，新玩家需要自己找到入口、筛选章节，再决定操作。")
label("A  新游戏直接进入地图", M, 137, 11)
image("round1-new-game.png",M,161,CW)
para("两轮新游戏都没有自动展示教程或首步任务。初始物种展示结束后，玩家仍需自己查找下一步。",
     M, 459, CW, 10, MUTED)
label("B  手册写明“五种生命在同一个世界共存”", M, 508, 11)
image("tutorial-goal-bottom.png",M,532,CW,(635,625,1000,320))
para("教程从“鸡”开始，当前游戏里初始种显示为“鸽鸽”；称呼也需要统一。", M, 747, CW, 10, MUTED)
note(775,"建议：查看初始种 → 看彩鸡线索 → 应用一次工具 → 观察变化。",29)
c.showPage()

# 4. Failure evidence and recommendations, with a clearly stated limit on inference.
page(4, "问题 03  试错风险与后续反馈", "开局能长期存活，但错误的食物操作会快速全灭；后续进化缺少清晰的定位反馈。")
label("证据：降低植物增量与存量后，第44日全灭", M, 137, 11)
mapping=image("round1-wrong-food-removal-seed1.png",M,161,CW,(470,25,980,590))
c.setStrokeColor(ORANGE)
c.setLineWidth(1.8)
c.rect(*mapping((800,475,320,110)),fill=0,stroke=1)
para("红框：地块库存条与失败提示重叠。第二轮同策略在第43日全灭，原图也已附入交付包。",
     M, 480, CW, 10, MUTED)
label("这次能确认什么", M, 530, 12)
para("• 本次两轮不操作均活到3600日，期间未出现新物种。<br/>"
     "• 正确的早期气候干预可在24～25日得到彩鸡。<br/>"
     "• 这组简单策略没有推进到孔雀、灵感菇狸或鹿，彩鸡也未长期保住。",
     M, 553, CW, 10.5, INK, "Song", 18)
label("建议按这个顺序改进", M, 622, 12)
para("1. 大幅削减最后食物来源前，提示风险；衰退时提示补给、撤销或读档。<br/>"
     "2. 新物种出现后，告诉玩家目前卡在性状、数量、有效天数、猎物还是其他条件。<br/>"
     "3. 讲清两个持续名额与生效、维持、恢复阶段；全灭时隐藏冲突的地图浮层。",
     M, 645, CW, 10.5, INK, "Song", 18)
note(718,"本轮未验证五物种通关。应先补足反馈，再区分理解成本与数值难度。",40)
para("证据来源：本地运行截图、两轮逐页/操作记录、种子1与17的场景记录。"
     "截图均为实机原图；本报告只作局部放大与红框标注。未完成手机、音频、性能和真人盲测。",
     M, 771, CW, 8.5, MUTED)
c.showPage()
c.save()

reader=PdfReader(str(PDF))
assert len(reader.pages)==4
text="\n".join(p.extract_text() or "" for p in reader.pages)
for word in ["26", "3600", "3325", "3256", "43", "44", "五物种", "问号"]:
    assert word in text, word

raws=[
    ("tutorial-evolution-symbols.png","01_教程要求读取加减符号.png"),
    ("round1-evolution-clue.png","02_实际进化线索与问号.png"),
    ("round1-new-game.png","03_新游戏开局画面.png"),
    ("tutorial-goal-bottom.png","04_教程中的五物种目标.png"),
    ("round1-wrong-food-removal-seed1.png","05_第一轮第44日全灭.png"),
    ("round2-wrong-food-removal-seed17.png","06_第二轮第43日全灭.png"),
]
feedback=(SOURCE/"可转发反馈.txt").read_text(encoding="utf-8").replace("详细记录和截图见同目录REPORT.md。","详细结果和截图见附件中的图文测试报告。")
readme="""达尔文农场：教程、玩法理解与难度测试反馈

建议先看《图文测试报告.pdf》，共4页，已放入关键问题截图。
《简短反馈.txt》可以直接复制到聊天里。
“原始截图”目录包含6张完整原图，便于放大或转发。

测试日期：2026-10-20（新加坡时间）
版本：7a413d9，Unity 6000.2.9f1，WhiteBox场景。
方式：两轮自动交互、画面审阅及正式日结算的加速观察。
难度种子：1、17。初始动物160只。未修改游戏规则、性状或余额。
结论范围：基本操作可用；教程与实际线索有差异，首步与后续目标反馈需要补齐；本次策略未验证五物种目标，不能认定目标不可能完成。

截图标注：报告中的截图为实机原图局部放大；第4页红框标出失败提示与库存条的重叠位置。原始截图保持完整。
"""
with ZipFile(ZIP,"w",ZIP_DEFLATED) as z:
    prefix="达尔文农场_测试反馈/"
    z.write(PDF,prefix+"图文测试报告.pdf")
    z.writestr(prefix+"简短反馈.txt",feedback)
    z.writestr(prefix+"阅读说明.txt",readme)
    for original,name in raws:
        z.write(SOURCE/original,prefix+"原始截图/"+name)
with ZipFile(ZIP) as z:
    assert z.testzip() is None
    assert len(z.namelist())==9
print(str(PDF))
print(str(ZIP))
print(f"4 PDF pages; 6 full screenshots; {ZIP.stat().st_size} bytes; verified archive.")
