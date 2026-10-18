# WhiteBox 水路接入

当前效果以 `Assets/Rendering/Moebius/Samples/RiverConnections/Scenes` 下四个技术美术场景为参考。原场景、原模型和原始预制体没有改动。

## 正式放置栏

| 水地块 | 默认海拔 | 使用资源 |
| --- | --- | --- |
| 横向水道 | 0 | 已有 River 模型中的 I 河道 |
| 弯曲水道 | 1 | 场景中的 L_New 河道 |
| 三叉水道 | 2 | 已有 River 模型中的 T 河道 |
| 十字水道 | 1 | 场景中的 Cross_River 河道 |

第四项替换了之前额外生成的方形水面。为保留现有引用和存档资源标识，原 `湖泊.asset` 的路径与 GUID 保持不变，放置栏名称和形状改为“十字水道”。

I/T 源模型带一米高的河床底座；适配预制体将河床对齐到本地 Y=0、岸高约 Y=1、水面 Y=.25。适配只作用于水路派生资源，源模型不变。`Assets/Resources/Water/MapWaterAssets.asset` 引用这些正式水路资源；重新生成可使用 `Tools > Water > Install map assets`。

## 与原有陆地的边界

`MapWaterController` 将地块位置交给已有 `MoebiusRiverNetwork` 处理河口、圆弧封口和瀑布。正式地图将 `generateLandFoundations` 关闭：陆地由原有地形系统完整绘制，水路系统不再给陆地生成底座、侧壁或顶面。陆地对应的节点只提供邻接位置，不包含渲染器。

没有测试布局或测试地块定义写入 WhiteBox，正式放置栏只有原有八类陆地和上述四种水路。

## 连接和动物

- 同级、相对河口匹配：水面连接，并视为同一水域；动物可在整片水域内自由选点和游动，不等待七天迁徙检查。
- 海拔差一级：从高到低形成瀑布，迁徙沿用原上下游概率。
- 海拔差两级：仍形成瀑布，禁止直接迁徙；存在合法中间海拔水路时允许绕行。
- 未对接的河口按参考场景封口，动物不得穿岸。

动物浸水比例可在 `PopulationMovementController > Settings > Water populations > submergedFraction` 调整，默认 .25。水域路径查询使用连接后的实际水面网格，并考虑动物轮廓和体型。地图修改会刷新受影响连接并取消失效的表现路径。

日常游动沿用 `WaterBodyTopology` 的同级水域划分，并缓存到地图连接版本；目标点、整条路径和每一步移动都受该范围约束。路径不能借道不同海拔水域绕回，也不能跨越同块内不连通的水面。代表图像可以游离其账本地块，种群 ID、数量、资源归属保持不变；表现同步不会将它重建回原块。连接断开时取消旧路线，必要时将表现位置恢复到原属水域的安全水面。

自动迁徙只检查源水域通往其他海拔水域的边界，包含远离账本地块的河口。差一级继续使用原来的上下游概率、食物条件、迁徙比例与冷却；差两级禁止直接通过。同级游动不再触发迁徙事件或改变冷却。瀑布动画从动物实际游到的位置出发。

PC 与移动端倍速按钮统一调用 `SimulationTime.CycleSpeed()`：`1X → 2X → 4X → 暂停 → 1X`。空格仍暂停/恢复原速度；菜单的强制暂停不会被倍速按钮解除。

同级自由游动与倍速回归：在初始单块地形的新 WhiteBox Play 会话中运行 `Tools > Water > Check shared water roaming and speed controls (fresh Play Mode)`。使用正式横向水道资产创建仅本次运行有效的布局，不写入 WhiteBox 场景。结果见 [回归报告](Artifacts/WaterRoaming/Report.txt) 和 [运行截图](Artifacts/WaterRoaming/SharedWater.png)。移动端按钮回调在 Editor 验证，未做手机真机测试。

## 本次纠正与验收

已纠正：I/T 河道水面埋入河床、额外方形水面、对原有陆地重复生成几何导致闪烁。正式水路配置海拔为 0/1/2/1。

验收直接读取 WhiteBox 放置栏使用的正式资产，不使用临时水地块定义代替：

- 直河 105、三叉 146、弯河 87、十字 156，共 494 个实际水面位置对照岸体碰撞几何，埋入河床的样本为 0。
- 正式默认海拔下，同级对接与三个高低瀑布正确生成，两级直接迁徙保持阻断。
- 检查八类既有陆地：水路系统生成的陆地渲染器为 0。
- 运行检查：`Tools > Water > Check actual WhiteBox palette (fresh Play Mode)`。只在初始单块地形的新 Play 会话中运行，临时布局退出 Play 即移除。
- 动物与迁徙检查：`Tools > Water > Run integration checks (fresh WhiteBox Play Mode)`。

报告与实际截图：[正式放置栏验收](Artifacts/WaterCorrection/ActualPalette.txt)、[正式地块总览](Artifacts/WaterCorrection/ActualPalette.png)、[直河水面](Artifacts/WaterCorrection/StraightWater.png)、[高低瀑布](Artifacts/WaterCorrection/ActualWaterfall.png)、[陆地](Artifacts/WaterCorrection/Land.png)、[既有回归](Artifacts/WaterCorrection/Regression.txt)。这些纠正后的图和报告取代上次的视觉验收结论。
