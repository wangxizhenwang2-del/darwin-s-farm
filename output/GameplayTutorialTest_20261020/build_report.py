import hashlib
import json
import subprocess
from pathlib import Path

OUT = Path(__file__).resolve().parent
ROOT = OUT.parent.parent

def read(name):
    return json.loads((OUT / name).read_text(encoding="utf-8-sig"))

runs = [read(p.name) for p in sorted(OUT.glob("round*-seed*.json")) if not p.name.endswith("-command.json")]
assert len(runs) == 12
for round_number in (1, 2):
    tutorial = read(f"round{round_number}-tutorial.json")
    controls = read(f"round{round_number}-controls.json")
    assert tutorial["pages"] == 26
    assert all(p["categoryHit"] and p["pageHit"] and p["content"] and p["reset"] and not p["clippedByContent"] for p in tutorial["checks"])
    assert all(c["pass"] for c in controls["checks"])
    assert all(tutorial[k] for k in ("titleTutorialPauses", "returnedTitle", "gameplay", "pauseTutorialPauses", "returnsPause", "resumes"))

revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
baselines = json.loads((ROOT / "output/TwoRoundTest_20261019/baseline-font-hashes.json").read_text(encoding="utf-8-sig"))
unchanged = all(hashlib.sha256(Path(b["Path"]).read_bytes()).hexdigest().upper() == b["Hash"] for b in baselines)
assert unchanged

labels = {
    "idle": "完全不操作",
    "climate-once": "初日升温小档、加湿小档各一次",
    "climate-maintained": "每150日尝试再升温、加湿小档",
    "neighbor-guided": "扩建已解锁邻地，在邻地升温小档、加湿大档",
    "wrong-food-removal": "初日植物增量、存量均降低大档",
    "neighbor-rainforest": "直接尝试建造雨林（未解锁）",
}
table = []
for policy in labels:
    cells = []
    for round_number in (1, 2):
        r = next(r for r in runs if r["round"] == round_number and r["policy"] == policy)
        evolved = [e for e in r["events"] if e["kind"] == "evolved"]
        extinct = [e for e in r["events"] if e["kind"] == "extinct"]
        if policy == "neighbor-rainforest":
            cell = "拒绝：地形未解锁；没有建成雨林"
        elif r["firstLoss"] >= 0:
            cell = f"第{r['firstLoss']}日全灭、触发失败"
        elif evolved:
            cell = f"第{evolved[0]['day']}日出现彩鸡；第{extinct[0]['day']}日彩鸡灭绝；3600日仅余初始种"
        else:
            cell = "存活至3600日，仍仅初始物种"
        cells.append(cell)
    table.append("| " + labels[policy] + " | " + " | ".join(cells) + " |")

summary = {
    "date": "2026-10-20", "timezone": "Asia/Singapore", "revision": revision,
    "unity": "6000.2.9f1", "scene": "Assets/Scenes/WhiteBox.unity",
    "rounds": 2, "difficultySeeds": [1, 17], "tutorialPagesPerRound": 26,
    "controlChecksPerRound": 8, "scenarioRecords": len(runs),
    "dailyStepsIncludingRejectedExpansionControls": sum(r["final"]["day"] for r in runs),
    "maxCoexistingSpeciesInTestedPolicies": max(r["maxSpecies"] for r in runs),
    "fiveSpeciesGoalVerified": False, "productionAssetsUnchangedFromBaseline": unchanged,
    "scenarios": [{k:r[k] for k in ("round","policy","seed","requestedDays","firstLoss","maxSpecies","actions","events")} for r in runs],
}
(OUT / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")

report = f"""# 教程、玩法理解与难度：两轮测试反馈

测试日期：2026-10-20（新加坡时间）。版本：`{revision[:12]}`，Unity 6000.2.9f1，WhiteBox 场景。

**结论：读完教程可以理解“改变环境，引导进化，再维持共存”的大方向，主要操作能用。但教程与当前进化线索界面存在差异，新开局缺少第一步任务；首次进化之后的反馈不足。开局存活容易，首次进化在正确操作下很快，五物种共存目标本次未验证通过。**

## 实际测试方式与边界

- 在运行中的 Unity Editor 中实测，两轮分别重新加载新世界。教程与操作检查各跑两次；难度对照使用种子 1、17，开局均为160只初始动物、荒漠、植物库存50000，1倍速每秒1模拟日，玩家费用关闭。
- 每轮遍历7章26页，验证导航按钮中心能接收射线、正文与所选页面一致、长页可滚动且切页回顶、内容容器没有裁掉正文。两轮均通过。短页不需要滚动，不将其滚动比例当作故障。
- 每轮8项操作检查通过：M开图、点击彩鸡头像查看线索、Esc关图、N打开/关闭建造、Alt左键查看信息、工具选择/目标/预览/应用/撤销、空格与4倍速。快捷键及头像点击通过输入事件验证；工具提交复用界面会话与正式玩家控制器。没有逐一验证所有地块拖放和所有物种。
- 长期观察用真实 `SimulationTime.NextDay()` 推进，保留正式生态、环境、进化与经济日结算；没有注入新物种、修改性状、修改余额或绕过地形解锁。此方式加速观察，不代表真人实际坐着试玩了同等时长。
- 每个长期场景观察至3600日或全灭。3600日在配置的4倍速下等价15分钟模拟进程，属于折算时间，不是本次测试的实测游玩时长。
- 这是自动交互、画面审阅与规则对照形成的可理解性评估，不是真人盲测或统计意义的通关率。只抽查了桌面画面，没有完成手机、独立构建、音频和性能验收。

## 两轮难度对照

| 策略 | 第一轮：种子1 | 第二轮：种子17 |
| --- | --- | --- |
{chr(10).join(table)}

“每150日再升温/加湿”是重复同方向操作的试探，不是将环境锁定在适宜值：每轮48次申请中有22次因“环境数值未发生有效变化”被拒绝，已保留原始操作回执。雨林场景的建造申请被正确拒绝，之后的3600日观察属于未扩建对照，不能当作成功使用雨林的策略。

这组结果说明：放着不管不容易死，但也没有自动向目标推进；正确的早期气候干预可以迅速得到彩鸡；继续使用简单策略不能保证后续进化或长期共存。本组策略最多同时出现两种生命，未出现孔雀、灵感菇狸、鹿。**不能据此断言五物种目标不可能完成，也不能将2个种子外推为玩家成功率。**

## 会影响玩家理解和难度判断的问题

| 优先级 | 复现与观察 | 对玩家的影响 | 建议 |
| --- | --- | --- | --- |
| 优先 | 教程“生物网与进化网”“选择进化目标”让玩家读取＋／－及重复符号；新开局点开彩鸡，实际是文字故事、随机高亮和五项“？？？” | 玩家按教程寻找差距符号时找不到，也难判断操作是否接近目标 | 将教程改成当前线索与逐步揭示机制；提供已满足/未满足条件或可观察的方向反馈 |
| 优先 | 新游戏直接进入地图；初始物种展示后没有自动打开教程、首个任务或下一步提示 | 不看标题页教程的人容易不知道目标；26页内容需要自己筛选 | 初次开局给出短任务链：查看初始种→看彩鸡线索→试用一次工具→观察性状变化，并允许跳过和重看 |
| 优先 | 两轮简单气候操作都能在24～25日得到彩鸡，但后续3600日内没推进到第三种；彩鸡随后灭绝 | 第一项奖励来得快，接着进入长时间缺少明确方向的探索 | 彩鸡出现后及时提示食物、邻地、下一路线与保持母群；让玩家知道卡在性状、数量、有效天数、猎物还是目标已在场 |
| 一般 | 教程说从“鸡”开始，当前初始动物名称在模型、进化图和灭绝通知中为“鸽鸽” | 目标中的“鸡”与眼前物种难对应 | 统一名称，或明确“鸽鸽（初始鸡）”的关系 |
| 一般 | 教程介绍五种工具和档位，但没解释每格两个持续名额、生效/维持/恢复阶段、替换关系；实际预览才显示名额和25日生效等待 | 一次改多项可能让旧干预恢复，也可能把等待误解成操作无效 | 在第一次应用和第三个持续工具提交前解释规则，预览保留影响提示 |
| 一般 | 降低植物增量大档并降低存量大档后，43～44日全灭 | 新手试错可能在不到一分钟的1倍速模拟窗口里损失全部种群 | 对最后食物来源的大幅削减加清晰的风险提示，衰退时给补给/撤销/读档建议；是否保护新手由设计决定 |
| 一般 | 全灭时中央地块库存条与旧样式失败提示发生重叠 | 失败原因不易读，反馈与其他界面风格不一致 | 全灭时隐藏地块浮层，并使用清晰的失败面板展示原因与重开/读档入口 |
| 较低 | 经济章写“当前测试版可能关闭”；实际工具预览费用为0，账户仍增长 | 玩家难确定是否需要规划支出 | 根据当前模式显示“本版操作免费”或真实启用状态 |
| 较低 | Console存在◆、✦、✿缺字替换警告 | 新闻通知的装饰图标变成方框 | 给字体补齐这些符号或替换为图标资源 |

## 画面证据

- [新开局：地图没有首步任务提示](round1-new-game.png)
- [教程中的五物种目标](round1-tutorial-0-2.png)
- [教程中的工具操作说明](round1-tutorial-2-0.png)
- [实际进化线索：文字与问号](round1-evolution-clue.png)
- [全灭：第44日，中央提示与库存条重叠](round1-wrong-food-removal-seed1.png)
- [第二轮全灭画面：第43日](round2-wrong-food-removal-seed17.png)

## 记录与复现

两轮逐页记录为 `round1-tutorial.json`、`round2-tutorial.json`，操作记录为 `round1-controls.json`、`round2-controls.json`。各策略的 `round*-seed*.json` 保存每次操作是否成功、每日事件、每25日种群与性状/资源采样、最终状态和模型哈希；`summary.json` 汇总本次结果。

在同一工程已进入Play、Unity CLI可连接时，从仓库根目录运行：

```powershell
python output/GameplayTutorialTest_20261020/drive.py tutorial 1
python output/GameplayTutorialTest_20261020/drive.py controls 1
python output/GameplayTutorialTest_20261020/drive.py difficulty 1 1 idle climate-once climate-maintained neighbor-guided wrong-food-removal neighbor-rainforest
python output/GameplayTutorialTest_20261020/drive.py tutorial 2
python output/GameplayTutorialTest_20261020/drive.py controls 2
python output/GameplayTutorialTest_20261020/drive.py difficulty 2 17 idle climate-once climate-maintained neighbor-guided wrong-food-removal neighbor-rainforest
```

测试脚本位于本目录，运行时在内存编译。测试没有改动游戏源码、场景和规则；原有两份字体修改的SHA256与测试前一致。已退出Play，临时导入的截图资源已清理，最终截图保留在本报告目录。

编译状态正常；读取到的Console中没有游戏代码异常，只有一条本次截图库路径参数错误（随后改正）以及字体缺字警告，原始输出在 `final-console-*.json`。此前范围不明时完成的地块UI24项检查和回放协议3项检查保留在 `../TwoRoundTest_20261019/`；另一个独立.NET环境审计因本机SDK不支持net10未运行成功，不计入本次通过项。
"""
(OUT / "REPORT.md").write_text(report, encoding="utf-8")

feedback = """我这边按教程、玩法理解和难度测了两轮。基础操作基本顺，读完教程能理解“调整环境→引导进化→维持共存”。主要卡点是教程和现界面有差异：教程说看进化需求的＋／－，实际看到的是文字线索和问号；新游戏也缺少第一步任务。

难度是开局生存容易，后续目标不够清楚。两个种子下，不操作都能活到3600日；一次升温、加湿分别在第25/24日产生彩鸡，但这些简单策略都没推进到第三种，彩鸡后来也消失了。误把植物增量和存量都调低，会在第44/43日全灭。这里只测了有限策略，不能据此说五物种目标一定做不到。

建议先统一教程和进化线索的说明，给第一次操作加短引导，再补“现在卡在哪个条件”的反馈和食物风险提示。这样更容易判断玩家是在研究策略，还是不知道下一步该做什么。详细记录和截图见同目录REPORT.md。
"""
(OUT / "可转发反馈.txt").write_text(feedback, encoding="utf-8")
print("Verified: 2 rounds, 52 tutorial page visits, 16 control checks, 12 scenario records; report written.")
