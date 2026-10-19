# 玩家手动测试指南

在 Unity `6000.2.9f1` 中打开 `Assets/Scenes/WhiteBox.unity`，进入 Play 模式。选择场景中的 `SimulationController`，确认 `presetEvolutionEnabled`、`migrationEnabled` 为开；通过 **Tools → Darwin Farm → Debug → Cross Habitat → Enable Water And Air** 打开两套生态。将 `BioenergyAccount.costsEnabled` 设为关、`SimulationController.enableRandomEvents` 设为关；植物库存与身体物质的生态付款仍会发生。建议打开 `Enable Ecology Console Logs`，在 Console 搜索 `preset_evolution`、`migration_arrival` 和 `formal_evolution`。`SimulationTime` 的天数应持续增加。

如果只想先确认工程能够运行，编辑器菜单 **Tools → Darwin Farm → Validation → Cross Habitat** 下有三项短程检查：`Water Land Short Smoke`、`Flight Short Smoke`、`Water Migration Short Smoke`。它们使用真实 `SimulateDay`，运行后在 Console 搜索 `SMOKE_PASS`；它们不替代以下玩家场景手测。

## 1. 陆→水与水→陆正式进化

准备一块陆格和与它四向直接相邻的纯水格，陆水高度与通路满足地图规则。将鹿母群放在陆格，数量建议 200 以上，五 Bar 调到鹿游目标附近：适温 50、适湿 50、运动 45、体型 30、繁殖 55（每项允许 ±5）。确保鹿与鹿游在当前进化网中相连，目标水格尚无鹿游，水域有实际植物库存和恢复量。连续运行至少 5 个合格日；若目标格已有 4 群，等待 10 日。成功后陆格鹿仍在，直接相邻水格出现鹿游，人数约为当时母群的 10%，并且物种、默认五 Bar、`bodyBiomass`、`energyReserve` 与植物付款都发生变化。库存不够时应拒绝创建。

反向测试将鹿游母群放在水格，五 Bar 调到鹿目标附近：适温 30、适湿 70、运动 72、体型 50、繁殖 35。目标陆格预留本地槽位、植物和食物支持，等待同样的合格天数。验证母群仍在原水格，真正的鹿出现在直接相邻的陆格。**反例：**让母群位于水格 `w3`，陆格 A 仅邻接同水域的 `w1`，此时 `w3→A` 不得成功；整群沿有效水路移到 `w1` 后，连续合格天数重新累计，才可尝试 A。

## 2. 水域迁徙与旋转

同水域换格：连接两个同高水格，使相向水路双向开放；在源格放一群水生种，目标格预留槽位。菜单 **Force Within Water Move** 将本次 Play 实例的每日换格机会调为 100%。观察一次成功后源格清空、目标格接收整群，人数、身体物质、储备随群移动。拆开并重连水路后继续观察，每个群应始终归属一个水格，水格植物库存总量不会因旋转而复制。

跨水域分群：两个相邻水格保留双向水路但海拔差设为 1，使它们属于不同水域；源侧种群及摄食压力较高，目标水域有充足食物。菜单 **Increase Cross Basin Move Opportunity** 提高上下游尝试系数。成功时源格保留母群、目标格出现同种分群；海拔差为 2、单向开口或斜角连接时应拒绝。菜单 **Restore Candidate Chances** 可恢复本轮候选概率。

## 3. 陆→空

在一块陆格放 3～4 个**地面**种群，其中鹿母群数量大于 50；天空不得已有飞行群。鹿的适温 40、适湿 45、体型 55、繁殖 40 各在鹿飞目标 ±5 内，运动严格大于 50。确保鹿↔鹿飞进化网边有效，并放入有足够库存且能够持续恢复的合法猎物（鹿飞资产登记的猎物包含彩鸡）。让地面生态先运行几日，形成猎物净恢复历史。可用菜单 **Force Land To Air Roll** 将合格后的抽卡设为 100%；若全部条件通过，下一日鹿母群减少固定 50 只，同格天空出现 50 只鹿飞，目标使用鹿飞默认五 Bar。检查植物/身体付款、猎物库存和母群保留。将运动改为恰好 50、地面群数改成 2 或 5、天空先占满，分别应拒绝。

## 4. 空→空迁徙

在飞行群上下左右相邻的一块陆格准备 3～4 个地面群、空天空，以及充足的鹿飞合法猎物；可把目标海拔差设为 2 来检查飞行不受海拔限制。菜单 **Increase Flight Move Opportunity** 提高迁徙尝试上限，继续推进天数。迁徙触发后可能整群搬到目标格、从原群分出 `min(50, floor(N/2))` 只建立同种群，或保持原状。分支上限 50 目前是候选值。观察 `migration_arrival`、两格飞行群人数、`bodyBiomass` 和 `energyReserve`；同日不得跨两格连续跳跃，斜角和已有其他飞行种的天空应拒绝。

## 5. 空→陆逆向分化

在一个**当地没有鹿**的陆格保留鹿飞母群、地面槽位和足够的植物与合法食物。将该飞行群的 `manualEvolutionTarget` 指向鹿资产，确保鹿飞↔鹿逆向网边存在。菜单 **Force Air To Land Roll** 将合格后的 0.5% 抽卡临时设为 100%。下一日应保留鹿飞母群，并在同格地面新增真正的鹿；目前按候选 10% 分化，可在 `SimulationController.flightReverseFounderFraction` 调整。若当地已有鹿、网边不合法、食物或身体付款不足，应无新增目标群。

结束后使用 **Restore Candidate Chances**，或退出 Play 模式以丢弃临时调试值。所有人数变化应结合当日出生、自然死亡和捕食观察；核对迁徙守恒时，以迁徙事件发生前后为准。
