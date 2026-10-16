# Task 5 人口验证报告

整体状态：**BLOCKED**。当前主工程仍保留冻结的逐日完整出生付款；富资源10鹿不能跨日积累出生资源，正式持续繁殖目标FAIL。未获用户确认前，独立储备原型不并入游戏。三物种长期稳定尚未证明，不能报Task5通过。

真实Unity正式回放：30/30组已完成三次完整进程一致性。每组从第0日运行，不用Python代理。每项生态结果与确定性分开判断；三次一致的灭绝仍是生态FAIL。完整表在本文末和DETERMINISM_VALIDATION.json。

## 夹具范围

seed=240，Unity6000.2.9f1，同设备。机制夹具在3×3中用空原始地块和一个测试地块，明确标mechanismFixture；初始动物身体计入第0日总量。通过现有环境编辑M接口维持指定T/H/R，每次保留当前S，不按日补库存；真实生长、摄食、出生、死亡、捕食和濒危仍执行正式M代码。机制隔离时关闭变异/迁徙，正式旧路线保持默认开启。部分竞争/食物链夹具只在初始条件中将气候适应匹配环境，输入逐项明示；这些夹具不能声称是合法玩家进化路径。

## T1 单鸡：PASS（已运行项），正式确定性见末表

固定T55/H35/R50000，两种初值均运行1000日。

| 输入 | 最终结果 | 验证 |
| --- | --- | --- |
| formal-T1-chicken50 | 鸡=1040 | 低人口先增长，消耗积累库存后接近长期资源约束 |
| formal-T1-chicken5000 | 鸡=1040 | 起始资源不足，真实衰退并接近同一人口区间 |

默认鸡成年成本33.132，K_food=1509，其成年总需求49996.188，距R=50000仅3.812，小于一只成年成本。它是成年维持上界；长期人口约1040还要支付真实幼体/身体和自然死亡，因此不能要求人口等于1509。按1040比较，K_food高45.1%反映维护上界与净增长平衡的区别，不是用K制造死亡。单日整数K_eff候选随余数相位改变，不能当作唯一稳定平衡。

## T2 共同竞争：PASS（已运行项）

固定森林R80000、鸡1000、鹿反事实0/50/200/500/800，所有反事实调用前后检查真实模型哈希不变。正式输出：

| 假设鹿数量 | 鸡可再生植物摄食 |
| --- | --- |
| 0 | 56170.08 |
| 50 | 56170.08 |
| 200 | 56170.08 |
| 500 | 41196.1367 |
| 800 | 33132 |


新增鹿需求不能令鸡无故多获资源。正反消费者遍历差异见逐地块plantAllocationAudit；没有按物种顺序先扣库存。

| 真实情景 | 最终结果 | 解释 |
| --- | --- | --- |
| formal-T2-chicken-favored | 鸡=1040 | 默认鸡适合55/35，鹿承受真实资源与气候压力 |
| formal-T2-deer-favored | 鹿=417 | 默认鹿适合30/70，竞争结果随获取效率改变 |

没有规定双方必须共存或预设胜者。

## T3 / 5D 小创始群：FAIL；正式修复BLOCKED

固定默认鹿T30/H70。富资源R80000/S900000；贫乏R1000/S5000。每个出生都须完整支付2070。

| 输入 | 初始鹿 | R | 首次出生 | 累计鹿出生 | 鹿首次消失日 | 日末结果 |
| --- | --- | --- | --- | --- | --- | --- |
| formal-T3-deer10-rich | 10 | 80000 | 无 | 0 | 13 | 无存活种群 |
| formal-T3-deer10-poor | 10 | 1000 | 无 | 0 | 13 | 无存活种群 |
| formal-T3-deer20-rich | 20 | 80000 | 无 | 0 | 40 | 无存活种群 |
| formal-T3-deer20-poor | 20 | 1000 | 无 | 0 | 40 | 无存活种群 |
| formal-T3-deer50-rich | 50 | 80000 | 1 | 100 | 未消失 | 鹿=41 |
| formal-T3-deer50-poor | 50 | 1000 | 无 | 0 | 43 | 无存活种群 |
| formal-T3-deer100-rich | 100 | 80000 | 1 | 357 | 未消失 | 鹿=154 |
| formal-T3-deer100-poor | 100 | 1000 | 无 | 0 | 43 | 无存活种群 |
| formal-T3-deer10-chicken1000 | 10 | 80000 | 无 | 0 | 13 | 鸡=1641 |


富资源10鹿第1日：成年651.9744全付，机会0.35，繁殖资源724.4999，单只完整成本2070，出生0。每一天机会有小数，资源却不跨日，N<29时即使资源无限也不能从当天分数需求中支付一只默认鹿。50鹿也会因每日整数付款封顶损失部分可用机会；100鹿可真付费增长。这不是用“数量少”替代计算。

原付款保留未用繁殖食物作为消费损耗；逐日字段unusedBirthFood显示去向。复用现有energyReserve的受控原型让10鹿第3日真付费出生、100日达27；贫乏仍灭绝、竞争仍可失败。原型只单次探索，未获确认、未正式合并。设计及守恒见BIRTH_ACCOUNTING_DESIGN.md，不能把原型结果算作当前游戏PASS。

## T4 食物链

合法捕食与真实共同竞争：已运行检查中未出现鹿被菇狸非法捕食，猎物身体仅移除一次，猎手能量/碎屑分配使用原HuntLevel。捕食者食品需求仍受真实猎物限制。

- formal-T4-predator：鹿=80；鸡捕食死亡合计4300，鹿捕食死亡0。
- formal-T4-no-predator：鸡=1640；鸡捕食死亡合计0，鹿捕食死亡0。


有捕食者时鸡减少，鹿的植物竞争得到释放；捕食者随后也会因鸡更新不足而崩溃。此项能说明因果与付款，**没有证明三物种长期稳定**。缺少猎物数量反馈的风险和已回退的饱和实验见参数账，未暗中保底猎物。

## T5 长期与环境变化

各项2000日真实运行，允许竞争淘汰与灭绝。

| 输入 | 日末结果 | 生态观察 |
| --- | --- | --- |
| formal-T5-rich-civet20 | 灵感菇狸=21；鸡=1607 | 见对应逐日曲线/灭绝事件 |
| formal-T5-rich-civet50 | 鹿=80 | 见对应逐日曲线/灭绝事件 |
| formal-T5-rich-civet100 | 鹿=678 | 见对应逐日曲线/灭绝事件 |
| formal-T5-poor | 无存活种群 | 见对应逐日曲线/灭绝事件 |
| formal-T5-Rchange | 无存活种群 | 见对应逐日曲线/灭绝事件 |


Rchange在第600日结算后将R降1000，第1000日结算后恢复80000；每次保持当前S，后续生态从下一日响应。恢复资源不会复生已灭绝动物。

稳定三物种：**FAIL，未找到**。稳定三物种状态的人口扰动：**NOT_RUN**，其前提尚未成立。不能把仅有两种/一种的稳定人口当作三物种成功。

## K诊断与只读验证

植食K_food：反事实成年维护边界；不把高S当永久供给，也不是当日分配除以每只需求。捕食者新K_food：**NOT_RUN**，输出-1，原历史猎物估计不能冒充新承载力。

K_eff：当前只提供同一M层的一天整数G诊断。多个交点、快速群落变化返回不确定；LOCAL_ONE_DAY_CANDIDATE仅表示有界采样中的局部候选，不证明唯一平衡。整数付款在低人口造成多个零净增长平台，故K_eff整体仍为候选诊断，不作为正式长期承载估计，也不用于人口修正。

开启/关闭只读诊断的正式对照：formal-readonly-on/off，全部101条逐日JSON、模型哈希、环境哈希完全一致。新旧N/K选择输入A/B：formal-selection-legacyK/foodK，此seed的1000日人口/五Bar/身体/出生死亡轨迹相同，不能据一个种子证明全面收益；新K输入默认未启用。两组各自三次独立进程也一致，详见FINAL_VERIFICATION.json。

物质审计：已完成正式日志最大单日绝对残差2，最大相对残差5.77e-08；完整出生支付违反记录0，植物领取超过真实可用S的记录0。应结合单精度百万至亿量级账本解释末位误差，不能把所有损失都称为未知。显式损耗公式见RESOURCE_ACCOUNTING_AUDIT.md。

## 曲线

![真实Unity人口曲线](<G:/unity/darwin-s-farm/Darwin's farm/Artifacts/V05DynamicK_20261016/plots/population-tests.png>)

![付款原型对照，尚未合并](<G:/unity/darwin-s-farm/Darwin's farm/Artifacts/V05DynamicK_20261016/plots/birth-account-ab.png>)

## 三次独立进程证据

PASS要求完整JSONL、所有逐日JSON及双哈希一致，且三次都走到指定结束日。旧路线即使操作被拒绝，也必须单独记生态FAIL，确定性PASS不改变该结论。

| 输入 | 确定性 | 日数 | 三次相同的完整JSONL SHA256 |
| --- | --- | --- | --- |
| formal-T1-chicken50 | PASS | 1000 | 32e19cf15913bea698fa83517a17fa207b23151f88a63e02d5b80fed022ac6dd |
| formal-T1-chicken5000 | PASS | 1000 | 7bfb1bd42915187e40f5fb5b589756d8999b04b51d57d844a24f5b7424ea7cb9 |
| formal-T2-chicken-favored | PASS | 1000 | 7dcbf0a37bf3c040fb052481d22d86001fec197b608225ec1094dbbb4562a833 |
| formal-T2-counterfactual | PASS | 1 | 86f7ddb2474901b0d04a1d8f77ad4451226435889204e7cec1654a32fe78a4d0 |
| formal-T2-deer-favored | PASS | 1000 | dd401d3e31b5b0aa3ceb13e6d94971c0e51a2665a861485b9f50c0b8e5515ec5 |
| formal-T3-deer10-chicken1000 | PASS | 100 | cfaf02bafde30cc14341400e40b44918a09260e4115a266e13b7c6cac3009d59 |
| formal-T3-deer10-poor | PASS | 100 | 361d17d1d28491ac7199ca403210b5dbe728f40bd95816cea7963d0d74138659 |
| formal-T3-deer10-rich | PASS | 100 | 7888441c27e2d6af743eda808340ac273fc3591f28ae856150c1495810017ee2 |
| formal-T3-deer100-poor | PASS | 100 | a3551504e8fdff82362b90e37ad5207c4fd38d9abbdfff592d5f5486c1be1015 |
| formal-T3-deer100-rich | PASS | 100 | 623c13bf1941b01287ea024e5d4b0d9b368cab4d5c700b08708acabbbd80e38c |
| formal-T3-deer20-poor | PASS | 100 | e67ef556785ef3348c642d7b012fa19259dd946ec593313d20a7d07a365cb08a |
| formal-T3-deer20-rich | PASS | 100 | 96a60dff7da78f47a57962ef7ddf9b692eb658814e863462fd85d677b99dfd05 |
| formal-T3-deer50-poor | PASS | 100 | 3ae876bd387032150bb3ade9cbeb2b5c2ceb554a252866ae722c30e75bc95d2a |
| formal-T3-deer50-rich | PASS | 100 | 52d9d86aca1f07a5fed21e04ca0de6541d0a9540a9a3ffd2b357eb891fbc81cd |
| formal-T4-no-predator | PASS | 1000 | aa29ce2befd631ec4fbe3f3ae9bdb8e2431ed19f0513ed3524a6d4c021a37ab9 |
| formal-T4-predator | PASS | 1000 | b735f8e3101af0cfa7c726e9f30c000f845b803109c0dd33a702788808b4326d |
| formal-T5-poor | PASS | 2000 | 8657c8c4352be40a014c62e6ff90d91d4ed1c8c7fa9a4022e571ef41f3c2ebfb |
| formal-T5-rich-civet100 | PASS | 2000 | 0b43cc3e05ffba45b0d18cea46090e066d836a9541dc37c76136b1ba2a42ab54 |
| formal-T5-rich-civet20 | PASS | 2000 | 379d3d42ef789fc90ccf0c1add774272131b1f19fe147be06f12c8da79e747b3 |
| formal-T5-rich-civet50 | PASS | 2000 | f932546387b695eca569e6fe6caf5c887f3dc5d2e3991d343bfe55355b2c18ab |
| formal-T5-Rchange | PASS | 2000 | bf8425de37f6ada6ec74f3feadaaba92e1964205e5b38393c1a5bf4e6b6a6d69 |
| formal-regression-basic120 | PASS | 120 | efbac4fe18ae195aad39532bb1b2be5622c2b784615410f8bbb3ca3b11c4a448 |
| formal-regression-color500 | PASS | 500 | 6c560ee56e1fe696f7739cfdc613c02375b9a98cff97450dbfa829ce6442c6b6 |
| formal-regression-mushroom1000 | PASS | 1000 | 710a09eadd601f93105ff191ff702b1b7e120605fd4cbba432c6331aa5c33b3d |
| formal-regression-deer1900 | PASS | 1900 | 00855da667a1b70191499e2f01db60a9f2bfea6174a0f1aab7451e37a127da3f |
| formal-regression-peacock900 | PASS | 900 | d63d50c6972e1b05eeb57c357228dd3bbe372cd800ea4587cd712f3fd3dfaa12 |
| formal-selection-legacyK | PASS | 1000 | 82694683ef725b619873beaae405c51a1ed12ca312e0d9127d8abd048162272a |
| formal-selection-foodK | PASS | 1000 | a9cea262d454d66709b4f2f3d46a5973be09197b1ff9e44e319a0fba0add0a20 |
| formal-readonly-on | PASS | 100 | 93165ecc25ef62c2ddfefe75b8228027680586119a5ab89f12439893f7c5e89c |
| formal-readonly-off | PASS | 100 | 9010a78ca356198b4b49b3f5d6d7cc8be5b5e35fc5ab27861747a5efc6c8c1da |

每项输入在inputs，完整三次JSONL/Unity日志/进程证据在runs；run1另有actions.log、events.log、daily-hashes.tsv、population.csv。完整指纹见SHA256.json。

检查点恢复BLOCKED：复用检查点无，缓存命中0，节省模拟日数0，完整恢复对照NOT_RUN。单日K反事实复制不属于完整世界恢复。不同路线没有拼接为一局联合生态。
