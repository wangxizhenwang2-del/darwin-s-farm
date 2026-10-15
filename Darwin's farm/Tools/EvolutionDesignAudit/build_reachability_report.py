"""Summarize bounded player-operation trials without claiming impossibility."""
import json
from collections import defaultdict, Counter
from pathlib import Path

OUT = Path(__file__).resolve().parent / "Results"
def read(name):
    return json.loads((OUT / name).read_text(encoding="utf-8"))

screen = read("land-direction-screen.json")
robust = read("land-direction-robustness.json")
gates = read("land-gate-diagnostics.json")
food = read("local-food-support.json")
groups, repeats = defaultdict(list), defaultdict(list)
for row in screen:
    groups[row["from"], row["to"]].append(row)
for row in robust:
    repeats[row["from"], row["to"]].append(row)
assert len(groups) == 18 and food["formalEvolutionEdges"] == 20
assert len(screen) == 2361 and len(robust) == 360

lines = ["# V4.6 陆地双向进化可通性设计检验", "",
"2026-10-12。正式网络已删除鸡↔鹿、鸡↔孔雀、鲨母↔虾兔，现有20条双向进化边及25条食物关系。原定10条陆地边在删除鸡↔孔雀后剩9条18方向；灵感菇狸↔飞狸及飞狸↔孔雀仍按约定排除。当地已有目标物种禁止转化的规则保留。", "",
"结论：全部18方向已做有限筛选。4方向出现正式直接转化，其中鸡↔彩鸡的两套候选流程通过本次全部扰动样本；鹿→鹿驯对初始数量和操作时机敏感；鸡→灵感菇狸成功少且新群未维持住。其余14方向尚未找到直接转化流程，不能据此判定绝对不可达。发现新的规则衔接问题后，暂停推荐其余流程及扩大复测，等待规则设计决策。", "",
"## 操作和验收定义", "",
"从已具备源物种A的当地配置开始；初始群体均使用正式模板，不手改性状。必要的源猎物和未来目标猎物列为场景前提，先排除目标B，再选合法陆地食物并补全生产者。初始库存采用放置预设的R，不人工补满。没有测试从白盒鸡开局走完整路线。", "",
"实际操作包括海拔单步调整、温湿度±25/±50、植物R±25000/±50000并同步S倍率、新建邻地；组合实验通过真实工具重施及撤销反馈调节，保留进入、维持、恢复、27生境切换和两个生效名额限制。没有玩家直接设定性状或凭空放入目标物种的步骤。", "",
"首要成功条件是事件日志中的直接A→B。其他初始物种或中间路径生成B不计成功。转化后另观察100日，目标全图总数每天至少10只才计通过维护检查；不等于证明永久稳定。", "",
"## 四阶段执行记录", "",
f"1. 结构检查：全网{len(food['rows'])}方向均检查非目标合法猎物及生产者路径；两处唯一猎物就是目标的冲突已删除。当地长期供给仍有未通过项，详见FOOD_SUPPORT.md。",
f"2. 单项实验：{Counter(r['stage'] for r in screen)['single']}次，基准源数量80/160/640，最接近源适温湿的8种放置预设之一；最多600日。",
f"3. 组合筛选：{Counter(r['stage'] for r in screen)['combined']}次，源数量20/160/640、源或目标附近的预设及雨林，8种反馈策略；最多1200日。",
"4. 容错复测：对每个已成功方向选一套较早转化流程，各90次，共360次。10个随机种子×源数量80%/100%/120%×首次操作延后0/5/10日。保持必要猎物数量不变。", "",
"采用无画面生产代码：筛选一次约8.5秒，容错复测约3.6秒；原有全网供给实验另约4秒。失败群消失即结束，达到直接转化即停止筛选，复测再观察100日。运行耗时依赖CPU。", "",
"## 每个方向的结果", "",
"性状差距列按适温、适湿、运动、体型、繁殖力排列，是某次记录中最接近目标的源群；范围外差距为各项超过±5的部分之和。瞬时符合不等于持续符合。", "",
"| 方向 | 筛选次数 | 直接转化次数 | 最接近的五项性状 | 范围外差距 | 容错直接转化／100日维护 |",
"|---|---:|---:|---|---:|---|"]
for key, rows in groups.items():
    best = min(rows, key=lambda r: r["bestDistanceOutsideTolerance"])
    rr = repeats[key]
    ok = sum(r["successDay"] > 0 for r in rr)
    maintain = sum(r["observed100Days"] and r["targetMinimumFirst100Days"] >= 10 for r in rr)
    status = f"{ok}/{len(rr)}；{maintain}/{len(rr)}" if rr else "未进入容错复测"
    lines.append(f"| {key[0]}→{key[1]} | {len(rows)} | {sum(r['successDay'] > 0 for r in rows)} | {'/'.join(map(str,best['bestTraits']))} | {best['bestDistanceOutsideTolerance']} | {status} |")
lines += ["", "复测成功占比是参数网格的覆盖结果，不是统计意义的独立成功概率。多个无迁徙案例对随机种子不敏感。本工具沿用System.Random桩，与Unity随机源不同；不能把90/90作为Unity实机高概率保证。", "",
"## 已出现直接转化的候选操作", ""]
terrain = {"Forest": "湿冷的森林", "Rainforest": "阴热的雨林", "Desert": "干燥的荒漠", "Tundra": "冰冻的苔原"}
for key, rr in repeats.items():
    if not rr:
        continue
    nominal = [r for r in rr if r["countScale"] == 1 and r["operationDelay"] == 0]
    example = nominal[0]
    lines += [f"### {key[0]}到{key[1]}", "",
        f"场景前提：{terrain.get(example['preset'],example['preset'])}，源群{example['initialSource']}只。源群和猎物均为模板性状。",
        f"实验策略：{example['operation']}，正常参数下种子1的转化日为{example['successDay']}。"]
    if key == ("鸡", "彩鸡"):
        lines += ["具体操作：在阴热雨林让20只鸡适应；每30日检查温湿度，使用最接近彩鸡70/49适温湿的合法±25/±50目标，当前值已在±3时不重施；若生境默认值更接近目标则撤销对应工具。没有植物操作。90个样本全在第40日转化，随后100日目标总数均不少于10。"]
    elif key == ("彩鸡", "鸡"):
        lines += ["具体操作：在干燥荒漠让20只彩鸡适应；同样每30日按鸡55/35的适温湿检查并施用或撤销工具。90个样本全在第31日转化，随后100日目标总数均不少于10。"]
    elif key == ("鸡", "灵感菇狸"):
        lines += ["具体操作：干燥荒漠640只鸡，在四向相邻位置新建阴热雨林，依靠自然迁徙。14/90出现直接转化，0/90通过100日维护，因此不能作为可靠推荐。"]
    else:
        lines += ["另有1600只模板鸡作为未来鹿驯的食物前提。每30日植物增加50000并同步库存×1.5；轮流检查温度、湿度，按鹿驯25/20适温湿选择最接近的合法工具目标或撤销。保持实际两个名额的挤出逻辑。标准数量640只、立即操作的10种子均第79日转化；全扰动网格仅30/90通过，仍需扩大适用范围。"]

lines += ["", "## 新发现的性状计时冲突", "",
"鹿→灵感菇狸案例在第431日五项性状75/75/34/25/69符合±5，当日捕食期望和猎物净补给也符合，连续计时开始。但第433日体型继续增大至26，超出目标体型20的±5范围，计时清零。正常流程没有转化。隔离实验仅在第431日之后暂停该源群的性状选择，其他群的性状、食物及环境继续正常结算，第435日直接转化。", "",
"鹿驯→雪狼案例第25—28日食物与五项性状同时达标，只有连续4日；随后体型降至64，低于目标70的允许下限65。当地4群且未整支转化，需要连续10日。隔离实验仅暂停该源群性状选择，第34日直接转化。", "",
"暂停性状选择是人工诊断，没有计入玩家可达性。它证明这两个案例的性状持续漂移打断了原本可完成的计时；不证明暂停选择能解决全网所有方向。", "",
"建议讨论一项局部规则：目标首次同时满足性状和食物门槛后进入进化准备期，仅该群暂缓普通性状选择，累计现有5/10日；每日继续检查食物、空间、数量及目标不存在等门槛，不满足即解除准备并清零。原有分化和迁徙如何中止准备需明确。玩家能看到候选、累计天数和中止原因。此建议尚未写入生产规则。", "",
"## 转化后捕食者维护缺口", "",
"鸡→灵感菇狸基准案例第226日生成10只灵感菇狸，第231日11只；第232日捕猎能量为0，储备仅覆盖约24%需求，当日死亡6只。第236日剩1只，后续当地注销；鸡猎物仍存在且增长。整只捕猎与连续能量预测的差别、储备及饥饿结算使新群不能维持，不能只看图上有食物关系。", "",
"已排除一个过于简单的数值修补：转化后仅把reserveHuntFraction从0.1改为0.5或1的实例实验，三者100日目标总数均降至0，因此不推荐直接提高这个值作为解决方案。实例改值仅用于诊断，生产参数未改。", "",
"下一项规则建议：捕食者以连续多日的能量缺口触发饥饿损失，并让进化验收用实际整只捕猎及储备账本前推一段时间，避免平均供给通过但新群迅速注销。缺口窗口、死亡强度、验证时段及对大群生态的影响还需单项实验；目前没有足够证据给出全网适用的新数值。", "",
"## 复现", "",
"运行以下命令。所有规则建议均未落实到游戏；游戏仅保留之前获批的植物预算修正及本次获批的进化边删除。", "",
"```powershell",
"dotnet run --project Tools/EvolutionDesignAudit/EvolutionDesignAudit.csproj -c Release -- --all-directions",
"dotnet run --project Tools/EvolutionDesignAudit/EvolutionDesignAudit.csproj -c Release -- --robust --evolution-only",
"dotnet run --project Tools/EvolutionDesignAudit/EvolutionDesignAudit.csproj -c Release -- --gate-diagnostics --evolution-only",
"python Tools/EvolutionDesignAudit/build_report.py",
"python Tools/EvolutionDesignAudit/build_reachability_report.py",
"```", "",
"原始证据：land-direction-screen.json、land-direction-robustness.json、land-gate-diagnostics.json、predator-reserve-diagnostics.json。当地供给详见FOOD_SUPPORT.md。历史鸡↔孔雀案例文件保持作溯源，不代表当前进化网。"]
(OUT / "LAND_SCREEN_HISTORY.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
print(OUT / "LAND_SCREEN_HISTORY.md")
