"""Write reports from Unity logs and arithmetic; never simulate ecology."""
import json
from pathlib import Path
import re
import statistics

B = Path(__file__).resolve().parent
S = json.loads((B / "RESULT_SUMMARY.json").read_text(encoding="utf8"))
D = json.loads((B / "DETERMINISM_VALIDATION.json").read_text(encoding="utf8"))
EXPECTED = json.loads((B / "FORMAL_CASES.json").read_text(encoding="utf8"))

def summary(name):
    return S.get(name + "-run1", {})

def rows(name):
    path = B / "runs" / (name + "-run1.jsonl")
    if not path.exists() or name + "-run1" not in S:
        return {}
    data = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if line.startswith("{"):
            row = json.loads(line); data[row["day"]] = row
    return data

def populations(row, species=None):
    return [p for t in row["tiles"] for p in t["populations"]
            if species is None or p["species"] == species]

def n(row, species):
    return sum(p["count"] for p in populations(row, species))

def det(name):
    x = D.get(name, {})
    return "PASS" if (x.get("dailyJsonIdentical") and
                      x.get("fullJsonlIdentical") and
                      x.get("allUnityReplaysCompleted")) else "NOT_RUN"

def result(name):
    x = summary(name)
    return "；".join(k + "=" + str(v) for k, v in x.get("final", {}).items()) or "无存活种群"

complete = sum(det(name) == "PASS" for name in EXPECTED)
formal_rows = "\n".join("| " + name + " | " + det(name) + " | " +
                        str(summary(name).get("days", "—")) + " | " +
                        (D.get(name, {}).get("sha256", ["—"])[0]) + " |"
                        for name in EXPECTED)
all_formal = [summary(name) for name in EXPECTED if summary(name)]
max_abs = max((x["maxAbsMaterialResidual"] for x in all_formal), default=0)
max_rel = max((x["maxRelativeMaterialResidual"] for x in all_formal), default=0)
violations = sum(len(x["unpaidBirthViolations"]) for x in all_formal)
plant_violations = sum(len(x["physicalPlantResourceViolations"]) for x in all_formal)

text = f"""# Task 5 人口验证报告

整体状态：**BLOCKED**。当前主工程仍保留冻结的逐日完整出生付款；富资源10鹿不能跨日积累出生资源，正式持续繁殖目标FAIL。未获用户确认前，独立储备原型不并入游戏。三物种长期稳定尚未证明，不能报Task5通过。

真实Unity正式回放：{complete}/{len(EXPECTED)}组已完成三次完整进程一致性。每组从第0日运行，不用Python代理。每项生态结果与确定性分开判断；三次一致的灭绝仍是生态FAIL。完整表在本文末和DETERMINISM_VALIDATION.json。

## 夹具范围

seed=240，Unity6000.2.9f1，同设备。机制夹具在3×3中用空原始地块和一个测试地块，明确标mechanismFixture；初始动物身体计入第0日总量。通过现有环境编辑M接口维持指定T/H/R，每次保留当前S，不按日补库存；真实生长、摄食、出生、死亡、捕食和濒危仍执行正式M代码。机制隔离时关闭变异/迁徙，正式旧路线保持默认开启。部分竞争/食物链夹具只在初始条件中将气候适应匹配环境，输入逐项明示；这些夹具不能声称是合法玩家进化路径。

## T1 单鸡：PASS（已运行项），正式确定性见末表

固定T55/H35/R50000，两种初值均运行1000日。

| 输入 | 最终结果 | 验证 |
| --- | --- | --- |
| formal-T1-chicken50 | {result("formal-T1-chicken50")} | 低人口先增长，消耗积累库存后接近长期资源约束 |
| formal-T1-chicken5000 | {result("formal-T1-chicken5000")} | 起始资源不足，真实衰退并接近同一人口区间 |

默认鸡成年成本33.132，K_food=1509，其成年总需求49996.188，距R=50000仅3.812，小于一只成年成本。它是成年维持上界；长期人口约1040还要支付真实幼体/身体和自然死亡，因此不能要求人口等于1509。按1040比较，K_food高45.1%反映维护上界与净增长平衡的区别，不是用K制造死亡。单日整数K_eff候选随余数相位改变，不能当作唯一稳定平衡。

## T2 共同竞争：PASS（已运行项）

固定森林R80000、鸡1000、鹿反事实0/50/200/500/800，所有反事实调用前后检查真实模型哈希不变。正式输出：

"""
text += "| 假设鹿数量 | 鸡可再生植物摄食 |\n| --- | --- |\n"
for line in summary("formal-T2-counterfactual").get("counterfactuals", []):
    match = re.search(r"proposedCount=(\d+) renewableIntake=([0-9.]+)", line)
    if match:
        text += f"| {match[1]} | {match[2]} |\n"
text += f"""

新增鹿需求不能令鸡无故多获资源。正反消费者遍历差异见逐地块plantAllocationAudit；没有按物种顺序先扣库存。

| 真实情景 | 最终结果 | 解释 |
| --- | --- | --- |
| formal-T2-chicken-favored | {result("formal-T2-chicken-favored")} | 默认鸡适合55/35，鹿承受真实资源与气候压力 |
| formal-T2-deer-favored | {result("formal-T2-deer-favored")} | 默认鹿适合30/70，竞争结果随获取效率改变 |

没有规定双方必须共存或预设胜者。

## T3 / 5D 小创始群：FAIL；正式修复BLOCKED

固定默认鹿T30/H70。富资源R80000/S900000；贫乏R1000/S5000。每个出生都须完整支付2070。

| 输入 | 初始鹿 | R | 首次出生 | 累计鹿出生 | 鹿首次消失日 | 日末结果 |
| --- | --- | --- | --- | --- | --- | --- |
"""
for count in (10, 20, 50, 100):
    for level, r in (("rich", 80000), ("poor", 1000)):
        name = f"formal-T3-deer{count}-{level}"; x = summary(name)
        text += (f"| {name} | {count} | {r} | {x.get('firstBirth',{}).get('鹿','无')} | "
                 f"{x.get('births',{}).get('鹿','—')} | "
                 f"{x.get('firstDisappearance',{}).get('鹿','未消失')} | {result(name)} |\n")
name = "formal-T3-deer10-chicken1000"; x = summary(name)
text += (f"| {name} | 10 | 80000 | {x.get('firstBirth',{}).get('鹿','无')} | "
         f"{x.get('births',{}).get('鹿','—')} | "
         f"{x.get('firstDisappearance',{}).get('鹿','未消失')} | {result(name)} |\n")
text += """

富资源10鹿第1日：成年651.9744全付，机会0.35，繁殖资源724.4999，单只完整成本2070，出生0。每一天机会有小数，资源却不跨日，N<29时即使资源无限也不能从当天分数需求中支付一只默认鹿。50鹿也会因每日整数付款封顶损失部分可用机会；100鹿可真付费增长。这不是用“数量少”替代计算。

原付款保留未用繁殖食物作为消费损耗；逐日字段unusedBirthFood显示去向。复用现有energyReserve的受控原型让10鹿第3日真付费出生、100日达27；贫乏仍灭绝、竞争仍可失败。原型只单次探索，未获确认、未正式合并。设计及守恒见BIRTH_ACCOUNTING_DESIGN.md，不能把原型结果算作当前游戏PASS。

## T4 食物链

合法捕食与真实共同竞争：已运行检查中未出现鹿被菇狸非法捕食，猎物身体仅移除一次，猎手能量/碎屑分配使用原HuntLevel。捕食者食品需求仍受真实猎物限制。

"""
for name in ("formal-T4-predator", "formal-T4-no-predator"):
    x = summary(name)
    text += (f"- {name}：{result(name)}；鸡捕食死亡合计"
             f"{x.get('predationDeaths',{}).get('鸡','—')}，鹿捕食死亡"
             f"{x.get('predationDeaths',{}).get('鹿','—')}。\n")
text += """

有捕食者时鸡减少，鹿的植物竞争得到释放；捕食者随后也会因鸡更新不足而崩溃。此项能说明因果与付款，**没有证明三物种长期稳定**。缺少猎物数量反馈的风险和已回退的饱和实验见参数账，未暗中保底猎物。

## T5 长期与环境变化

各项2000日真实运行，允许竞争淘汰与灭绝。

| 输入 | 日末结果 | 生态观察 |
| --- | --- | --- |
"""
for name in ("formal-T5-rich-civet20", "formal-T5-rich-civet50",
             "formal-T5-rich-civet100", "formal-T5-poor", "formal-T5-Rchange"):
    text += f"| {name} | {result(name)} | 见对应逐日曲线/灭绝事件 |\n"
text += f"""

Rchange在第600日结算后将R降1000，第1000日结算后恢复80000；每次保持当前S，后续生态从下一日响应。恢复资源不会复生已灭绝动物。

稳定三物种：**FAIL，未找到**。稳定三物种状态的人口扰动：**NOT_RUN**，其前提尚未成立。不能把仅有两种/一种的稳定人口当作三物种成功。

## K诊断与只读验证

植食K_food：反事实成年维护边界；不把高S当永久供给，也不是当日分配除以每只需求。捕食者新K_food：**NOT_RUN**，输出-1，原历史猎物估计不能冒充新承载力。

K_eff：当前只提供同一M层的一天整数G诊断。多个交点、快速群落变化返回不确定；LOCAL_ONE_DAY_CANDIDATE仅表示有界采样中的局部候选，不证明唯一平衡。整数付款在低人口造成多个零净增长平台，故K_eff整体仍为候选诊断，不作为正式长期承载估计，也不用于人口修正。

开启/关闭只读诊断的正式对照：formal-readonly-on/off，全部101条逐日JSON、模型哈希、环境哈希完全一致。新旧N/K选择输入A/B：formal-selection-legacyK/foodK，此seed的1000日人口/五Bar/身体/出生死亡轨迹相同，不能据一个种子证明全面收益；新K输入默认未启用。两组各自三次独立进程也一致，详见FINAL_VERIFICATION.json。

物质审计：已完成正式日志最大单日绝对残差{max_abs:.9g}，最大相对残差{max_rel:.3g}；完整出生支付违反记录{violations}，植物领取超过真实可用S的记录{plant_violations}。应结合单精度百万至亿量级账本解释末位误差，不能把所有损失都称为未知。显式损耗公式见RESOURCE_ACCOUNTING_AUDIT.md。

## 曲线

![真实Unity人口曲线](plots/population-tests.png)

![付款原型对照，尚未合并](plots/birth-account-ab.png)

## 三次独立进程证据

PASS要求完整JSONL、所有逐日JSON及双哈希一致，且三次都走到指定结束日。旧路线即使操作被拒绝，也必须单独记生态FAIL，确定性PASS不改变该结论。

| 输入 | 确定性 | 日数 | 三次相同的完整JSONL SHA256 |
| --- | --- | --- | --- |
{formal_rows}

每项输入在inputs，完整三次JSONL/Unity日志/进程证据在runs；run1另有actions.log、events.log、daily-hashes.tsv、population.csv。完整指纹见SHA256.json。

检查点恢复BLOCKED：复用检查点无，缓存命中0，节省模拟日数0，完整恢复对照NOT_RUN。单日K反事实复制不属于完整世界恢复。不同路线没有拼接为一局联合生态。
"""
(B / "POPULATION_TEST_REPORT.md").write_text(
    text.replace("](plots/population-tests.png)",
                 "](<" + (B / "plots/population-tests.png").as_posix() + ">)")
        .replace("](plots/birth-account-ab.png)",
                 "](<" + (B / "plots/birth-account-ab.png").as_posix() + ">)"),
    encoding="utf8")

old = {
    "basic120": ("基础120日", "无拒绝、确定性已通过"),
    "color500": ("鸡→彩鸡", "原第136日正式进化，续至500日"),
    "mushroom1000": ("鸡→灵感菇狸", "原第473日正式进化，续至1000日"),
    "deer1900": ("灵感菇狸→鹿", "原1789日分化，1805日灭绝"),
    "peacock900": ("彩鸡→孔雀", "原575–579五Bar达标，猎物门槛不足"),
}
reg = """# 旧进化路线回归

同一原始JSON逐字节复制为formal-regression输入，seed和动作顺序、初始1000鸡的身体支付均保留。输入操作继续按指定日结算后执行。当前公式改变后，不要求与旧公式哈希相等；只要求当前同版本三次一致。

旧对照取前阶段最新nextstage固定回放及next-deer/next-peacock正式失败回放。不同历史调参版本的final/tuned等文件不混作同一基线。

| 路线 | 原结果 | 当前结果 | 当前输入拒绝 | 三次确定性 |
| --- | --- | --- | --- | --- |
"""
for key, (label, outcome) in old.items():
    name = "formal-regression-" + key; x = summary(name)
    events = [e for e in x.get("events", []) if "kind=PresetEvolution" in e]
    rejected = x.get("execution", {}).get("actionRejections")
    desc = "；".join(events) if events else ("无正式进化；" + result(name))
    reg += f"| {label} | {outcome} | {desc} | {rejected} | {det(name)} |\n"
reg += """

**变化原因。** 在第一条分化发生前，世界只有鸡，不能把此阶段变化全归因于跨物种竞争。旧独立层可领取上限改为单资源权重分配，独处时正获取权重不会再单独扣减总可领取量；成年维持先领取、重复密度出生倍率取消，早期人口、摄食和候选性状收益均改变。后续相同环境脉冲作用在不同的数量与五Bar上，旧第136/473日不再必达。

彩鸡输入第136日鸡为1768只，五Bar=(67,47,9,8,78)，彩鸡模板=(70,49,15,14,75)，偏差=(3,2,6,6,3)。运动与体型各超容差1，因此该日首先失败的是正式五Bar条件，不能解释成仅缺猎物。

菇狸输入第340日仅39鸡、体型8；同一负植物脉冲令体型于350/360/370日变成6/2/1。第370日S=996118.69、R=71000，成年满足率1，仍出生1、死亡3；第380日S=1000000、R=121000，7鸡、出生0、死亡1并已濒危5日。因此后半段失败并非植物饥饿：极小体型的原有虚弱死亡、出生整数付款及错误时机的原操作序列共同造成衰退。恢复库存不能纠正已经不同的性状/付款轨迹。

菇狸/鹿原输入在共享候选下可能先导致鸡灭绝，本局结束后正式玩家工具会拒绝后续动作。拒绝保留原文，不跳过工具限制或暗中复活种群。无菇狸起点就不能声称验证了鹿后半段；应记路线FAIL。孔雀旧五Bar窗口也不能沿用成新模型已达标证据，须以当前逐日五Bar为准。

当前主工程仍有小群分数付款问题。捕食者规模不只由preyEnergyPerSize决定：真实猎物净更新、捕获/遭遇、繁殖整数付款与成本同时限制它。未获确认的储备原型仅证明小鹿可真付费出生，混合竞争与三物种长期稳定仍会失败，不能算旧路径修复。

候选长期收益函数仍使用理论繁殖活动，尚未完整纳入跨日付款的实际整数出生；它对小创始群可能高估收益。付款策略确认后需要同步回归这一预测，不能据理论收益声称新群已经可维持。孔雀/鹿输入未建立其前驱群时，独立后半段改善验证为NOT_RUN，整条旧路径结果为FAIL。

## 关键日期实际快照

以下数字直接取当前Unity输出，不是重新用Python预测。每组物种格式为N/(T,H,M,size,F)，括号T/H是群体适应性状。

| 输入 | 日期 | 当前快照 |
| --- | --- | --- |
"""
for key, days in [("color500", (100, 136, 236, 500)),
                  ("mushroom1000", (100, 340, 380, 390, 473, 1000)),
                  ("deer1900", (473, 1500, 1789, 1805, 1900)),
                  ("peacock900", (136, 575, 579, 900))]:
    name = "formal-regression-" + key; data = rows(name)
    for day in days:
        row = data.get(day)
        if row is None: continue
        values = []
        for tile in row["tiles"]:
            for p in tile["populations"]:
                values.append(f"({tile['x']},{tile['y']}){p['species']} "
                              f"{p['count']}/({p['fitT']},{p['fitH']},"
                              f"{p['movement']},{p['size']},{p['fertility']})")
        reg += f"| {name} | {day} | {'；'.join(values) or '无存活种群'} |\n"
reg += """

当前输入、逐项动作、拒绝理由、重大生态事件、逐日双哈希、Unity日志都在同名runs文件。三次验证是对真实成功或失败轨迹的重复，不把重复失败写成进化通过。

Task5F回归执行与确定性见上表，完整三次完成后该执行项为PASS；原成功路线当前生态复现结果FAIL。冻结付款正式修改仍BLOCKED。尚未恢复五物种联合培育。
"""
(B / "REGRESSION_REPORT.md").write_text(reg, encoding="utf8")

readme = f"""# Task 5 交付索引

整体 **BLOCKED**：当前主工程10鹿的跨日付款粒度问题仍在，修改冻结支付机制等待用户确认；长期稳定三物种未证明。已完成正式三次回放{complete}/{len(EXPECTED)}组。不能把确定性PASS当成生态目标PASS。

- MODEL_BASELINE.md：当前真实Unity配置；MODEL_BASELINE_BEFORE.md：继承的改前来源。
- DYNAMIC_K_FORMULA.md：共享S、成年优先和只读K。
- RESOURCE_ACCOUNTING_AUDIT.md：1789日鹿案例与改后收支。
- POPULATION_TEST_REPORT.md：T1–T5、曲线、失败与未运行项。
- BIRTH_ACCOUNTING_DESIGN.md：冻结付款诊断、守恒、独立原型A/B及待确认。
- PARAMETER_CHANGELOG.md：保留/回退的公式和参数。
- REGRESSION_REPORT.md：四条旧路线和基础回放。
- DETERMINISM_VALIDATION.json / SHA256.json：三次逐日与完整文件验证。
- inputs / runs：完整输入、每日JSONL、Unity日志、逐项操作与事件、人口CSV和逐日哈希。
- checkpoint-before / checkpoint-final-candidate：改前与当前回退点。
- experiment-encounter-v3 / birth-account-prototype：已回退或未批准的受控分支，不能冒充正式游戏版本。

运行正式测试：用本目录run_unity.py，指定已关闭交互编辑器的主工程。每次新Unity进程从第0日打开V05沙盒。示例：python run_unity.py formal-T3-deer10-rich --runs 3 --project 主工程绝对路径。启动器只启动Unity，没有生态公式。analyze_results.py和plot_results.py只读取真实日志。

历史实验输入含overridePreyEncounter时，必须在独立工程恢复experiment-encounter-v3源码/资源闭包；付款原型则恢复birth-account-prototype/Assets闭包。各自sha256.json与日志bindingSha256记录版本。不得覆盖其他协作者文件，也不得将实验分支的物种/库存直接拼成联合世界。

所有玩家路线保持真实工具接口；机制夹具的直接初始物种/气候设置明确标记，不算游戏内可执行培育路径。恢复缓存仍BLOCKED，命中0，节省模拟日数0。
"""
(B / "README.md").write_text(readme, encoding="utf8")

audit_path = B / "RESOURCE_ACCOUNTING_AUDIT.md"
audit = audit_path.read_text(encoding="utf8").split("## 5B 改后审计")[0]
audit += f"""## 5B 改后审计

正式候选共享植物账本在当前Unity工程运行。数值与公式见MODEL_BASELINE.md、DYNAMIC_K_FORMULA.md；不把下述机制夹具与上文1789日世界当成相同环境的A/B。

植物分配的总预算只有一份PlantFoodBudget(S)。通道只影响权重，成年阶段和繁殖阶段消耗同一个remaining；当日总摄食从S扣一次。可领取预算不超过真实S减既有保留线。完整出生继续支付身体与幼体成本。

每日日结闭合式：

M_after = M_before + 实际植物生长 − accountedBiomassSinkToday

M = S + 藻类库存 + 碎屑 + 所有存活身体 + energyReserve。固定环境夹具每天只保持T/H/R，保留当前S，因此没有把编辑器设置误算成每日免费生物量。工具操作发生在日结之后，其外部物质变化单列materialDelta；下一日从操作后状态核对。

显式损耗=分给消费者的食物−新增幼体身体+猎食储备截断/正式转换储备截断。它包含成年能耗、幼体能耗和原规则未保存的繁殖剩余；不是只看总量下降就把它统称自然死亡。自然死亡身体进碎屑；捕食移除身体后只向猎手转一次处理后能量，其余进碎屑；注销把残存身体与储备回碎屑。

正式已完成记录：最大绝对闭合误差{max_abs:.9g}，最大相对误差{max_rel:.3g}；逐项出生预算检查发现{violations}项未付足、{plant_violations}项植物超额领取。原始JSONL逐地块包含plantGrowth、consumedPlants、stock、detritus，逐群包含adultDemand/adultPaid/birthBudget/newbornCost/body/reserve等。百万至亿量级float账本的末位舍入要按相对误差解释。

### 10鹿富资源的真实日账

该夹具T30/H70/R80000/S900000，默认鹿模板。各项全来自本版本Unity。

| 日 | 数量 | 成年需求/支付 | 食物领取 | 繁殖预算 | 完整幼体成本 | 出生 | 未用繁殖食物 | 日末储备 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
"""
rich = rows("formal-T3-deer10-rich")
for day in (1, 2, 3, 4, 10, 12):
    if day not in rich: continue
    ps = populations(rich[day], "鹿")
    if not ps: continue
    p = ps[0]
    audit += (f"| {day} | {p['count']} | {p['adultDemand']:.4f}/{p['adultPaid']:.4f} | "
              f"{p['allocatedFood']:.4f} | {p['birthBudget']:.4f} | "
              f"{p['newbornCost']:.4f} | {p['births']} | "
              f"{p['unusedBirthFood']:.4f} | {p['reserve']:.4f} |\n")
audit += """

因此富资源时K_food很高、成年也确实付足，并不能绕过原分数出生资源不跨日保存的付款缺陷。未用繁殖资源在当前冻结规则中作为消费损耗离开可用账本；这是明示的机制问题，不能把它藏起来或改称已完成出生。

### 竞争与捕食

T2只读反事实显示鸡的植物实得随鹿需求增加保持或下降；正反遍历审计不按名单顺序决定胜负。T4鹿的捕食死亡应为0，菇狸仅捕食源数据合法的鸡；有捕食者使鸡减少并释放鹿竞争压力，也会令菇狸自身失去可持续猎物供给。人口曲线与食物链最终灭绝是实际结果，不是验收故障。

捕食者新K_food尚NOT_RUN；旧AvailableSustainablePreyFood基于历史净更新，不能当成本轮已通过的新反事实K。K_eff的一天整数候选不能证明整个食物链长期稳定。饱和遭遇实验已回退、跨日出生原型待确认，均详见参数账和付款设计。

资源/出生检测的0.1单位浮点容差只用于日志审计，不改变M层严格整数出生付款。JSONL是逐日观测而非完整存档；数量已归零但尚待注销的内部记录仍计入CanonicalModelHash和TotalMaterial，不能从仅存活种群列表反推完整恢复状态。
"""
audit_path.write_text(audit, encoding="utf8")
print("reports updated;", complete, "/", len(EXPECTED), "triple groups complete")
