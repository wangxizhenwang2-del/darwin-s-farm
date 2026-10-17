# 空中种群巡航与迁徙

本阶段将 `EcologicalNiche.Air` 接入 WhiteBox 的种群显示与移动。目前使用现有物种插画作为飞行代表；不新增翅膀逐帧动画。每个代表仍代表整个种群，数量标签沿用现有规则。飞行插画使用独立的实体镂空材质，身体写入深度与法线，仅轮廓外保持透明，避免后方景物与 Moebius 描边穿透。

## 活动范围

- 每个地块最多容纳一个存活的飞行物种。同种迁入可合并，其他飞行物种会被拒绝。
- 飞行代表在所属地块中部沿椭圆巡航，默认长半径 4.5 米，并有轻微高度起伏；边缘预留代表图像和活动安全距离。
- 巡航高度取实际地面上方约 1.7 米，配合 ±0.12 米起伏表现低空飞行；树木、仙人掌等装饰不再抬高整块地的巡航高度。支持地块旋转、高度变化和水面地块。
- 普通巡航不修改种群所属地块、身份或数量，不触发迁徙；不依赖地面 NavMesh，也不阻挡玩家和陆生动物。

## 跨地块行为

- 自然空中迁徙接入既有每日迁徙调度，沿用迁徙间隔、冷却、适应度、食物压力与拆分数量规则，忽略地块海拔差。
- 同一轮多个飞行物种争用目的地时，迁徙计划预留天空槽位；提交阶段再次检查，防止冲突并保留个体数量。
- 手动迁移支持空中种群在陆地与水面上方转移，使用同一天空槽位检查；生态仓投放也复用该检查。
- 迁徙事件播放空中跨地块路径，到达后才显示新目的地的巡航代表。部分迁徙保留原地剩余种群，同种抵达合并数量。
- 地块移除、种群消失或目标失效时，沿用现有显示与过渡生命周期清理对象。
- 社区编辑入口拒绝同块同时写入两个不同的飞行物种，并合并同种空中记录。

## 参数与维护

`PopulationMovementController` 的 `Settings → Air populations` 可调飞行速度、相对地面高度、高度起伏、边缘距离和巡航半径。默认速度参数为 3，高度为地面上方 1.7 米，起伏幅度为 0.12 米，巡航半径为 4.5 米。WhiteBox 场景已保存这些参数。基准取 Ground 层地面碰撞体最高处，避免进入地形；较高植被可能正常遮挡低空飞行代表。

主要实现：

- `Assets/Scripts/Controller/PopulationFlight.cs`：地块局部巡航、边界和高度。
- `WhiteboxPopulation.cs`、`PopulationMovementController.cs`：显示、计数、移动更新与陆空隔离。
- `Assets/Scripts/Simulation/PopulationVisualPresenter*.cs`：空中显示及迁徙过渡。
- `SimulationController.Migration.cs`、`SimulationController.cs`、`Economy/PlayerEconomyController.cs`：迁徙、天空占用与编辑检查。

## 验证

直接观测入口：打开 WhiteBox，进入 Play，执行 `Tools → Darwin's Farm → Population → Preview air population (Play Mode)`。菜单会在可用地块放入 80 只鹿飞的代表并暂停生态计时；飞行继续。保留当前游戏镜头的位置、角度、投影、缩放和玩家跟随设置。已有空中种群时复用，重复点击不会叠加。退出 Play 恢复地图。

正常镜头检查：[相机参数与重复预览检查](normal-camera-check.txt)。原先高于整块树冠的巡航规则已移除；使用正常游戏镜头检查低空飞行与数量标签，不通过修改相机扩大视野。早期 `initial-flight.png`、`preview.png` 为旧高度／旧预览截图，当前效果以 [正常视角低空截图](low-flight-normal-camera.png) 为准。

在全新的 WhiteBox Play 会话中执行 `AirPopulationFlightChecks.Run()`，读取 `AirPopulationFlightChecks.Result`。此检查会改动运行中的测试地图与种群，结束后退出 Play 恢复原场景。

- [空中飞行实机回归记录](integration-results.md)
- [原有生态事件与陆生移动回归记录](existing-regressions.txt)
- [身体透明度与背景描边穿透渲染检查](sprite-opacity-check.txt)：真实鹿飞插画通过 URP 在不同背景颜色、背景深度边缘开关下渲染，5,441 个身体内部采样的颜色差异均为 0。旧透明材质的背景深度边缘差异最大为 229/255。
- [两块地形上空的飞行代表截图](initial-flight.png)

最终通过 9 组空中实机检查，含 12 种巡航／旋转组合、36,000 次边界与净空采样，以及巡航／迁徙材质检查。原有生态事件 15/15、移动检查 10/10 及 6,000 次代表移动回归通过。最终 Play 会话无编译或运行错误，保留原版 shader Inspector 的两条枚举绘制警告。

验证覆盖低空巡航、旋转、水面、地面净空、正常镜头可见性、无地面导航时的实际移动、自然迁徙、手动迁移、同种合并、天空冲突、身份与数量守恒、海拔变化和地块删除。验证环境为 Unity 6000.2.9f1 Editor Play Mode；尚未执行独立 Player 打包。
