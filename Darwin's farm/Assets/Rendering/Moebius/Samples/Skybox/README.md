# Moebius Clear Sky

现在只保留一套晴朗天空：`Content/Skybox/Materials/ClearSky.mat`。Shader 仍为 `Darwin/Moebius Skybox`，使用 Unity URP 标准天空盒路径。

以参考中的清透蓝青天空、淡奶油色积云、细轮廓线和少量水平云带为方向。已移除上一版的暖色 / 阴天 / 暮色预设、预设选择器和程序远山。

## 使用

- 打开本目录 `MoebiusSkyboxDemo.unity` 查看森林与晴空。
- 在 `Window > Rendering > Lighting > Environment > Skybox Material` 选择 `ClearSky`，相机 Background Type 设为 Skybox。
- 也可点击材质面板底部 **Apply to Active Scene and Main Camera**，只设置场景天空和相机背景类型。
- 新建同款材质：`Assets > Create > Rendering > Moebius Clear Sky Material`。
- **Reset Clear Sky Defaults** 恢复这一套晴空参数，没有其他风格模式。
- Scene View 需开启天空显示；持续预览云运动可开启 Effects / Always Refresh。

## 当前画法

天空使用大面积蓝青色，向地平线过渡为淡奶油色；下半球保持浅青色，不再形成灰色底部。默认关闭颜料颗粒、纸纹与色阶。

四组云沿天空方向排布，顶部留出大量空白。云形由偏向一侧的高云冠、较低的侧肩、小云瓣和拖尾组成，取代等高圆顶排成的宽云带。每组云的云冠位置和高度由固定种子决定，部分云组镜像；底部由不同大小的云瓣相接，不再用直线整体裁平。主体内部保留连续的连接区域，避免云瓣间出现空洞。

两层淡暖色云影与前后云团的形状对应；少量局部细线提示前部云团的上缘，不逐个描出完整椭圆。小幅连续坐标扰动和不等距的径向起伏只修饰轮廓，主形状由云团构图决定。变化跟随云自身的坐标，没有逐帧随机。

太阳改成较大的淡奶油色圆盘，默认角半径 7.5 度。一条薄云横向经过圆盘下部，圆盘下半部向当前天空颜色淡化，保留完整但克制的圆形轮廓。默认不发光晕，不增加第二个太阳。

晴空底色保持不变。为了便于构图，太阳默认采用手动方向；Follow Main Directional Light 仍可开启，开启后才跟随场景主光，不会改变场景灯光本身。云移动直接使用连续 GPU 时间。

## 常用参数

| 参数 | 默认与用途 |
| --- | --- |
| Sky Colors | 蓝青天顶 / 中层，浅青下层，淡奶油地平线。可微调颜色，不必切换预设。 |
| Cloud Density | 1，最多四组云分布在完整天空球上，不是每个视野都塞满云。 |
| Cloud Scale / Coverage | 1.1 / 0.65，调整云的角尺寸和占据面积。 |
| Cloud Elevation Range | 3–12 度，主要让云出现在低空，保持天顶开阔。 |
| Cloud Ink Strength / Width | 0.3 / 0.65 渲染像素，细外缘和少量有渐隐端点的局部折线。 |
| Cloud Shape Variation | 0.5，调整云冠偏移和细小边缘起伏，0 也保留设计好的不对称云团。 |
| Cloud Shade Strength | 0.6，控制两层淡暖色云影，不做球体式光照。 |
| Cloud Drift | 每秒 0.025 度，连续缓慢移动；0 表示静止。 |
| Follow Main Directional Light | 默认关闭，便于安排画面构图；可开启以同步场景主光。 |
| Sun Angular Radius | 默认 7.5 度，控制大圆盘的角半径。 |
| Sun Cloud-Shelf Strength | 默认 0.7，控制横向薄云和下半部圆盘的淡化程度；0 恢复完整圆盘。 |
| Sun Cloud-Shelf Height | 默认 -0.25，薄云位于太阳中心下方四分之一半径处。 |
| Sun Cloud-Shelf Width | 默认 0.012，控制薄云厚度，以太阳半径为比例。 |
| Exposure / Saturation | 0 / 1，保持明亮晴空。 |

原地块材质、主光、环境光、反射和雾的逻辑不变。天空本身不会给 3D 物体添加距离雾，也不自动烘焙环境光或刷新反射探针。主光被相机剔除时，太阳使用手动方向后备值。

## 文件与验证

- `Shaders/Materials/MoebiusSkybox.shader`：天空入口和参数。
- `Shaders/Includes/Inputs/MoebiusSkyboxInput.hlsl`：材质常量缓冲。
- `Shaders/Includes/Features/MoebiusSkyboxAtmosphere.hlsl`：天空颜色和太阳。
- `Shaders/Includes/Features/MoebiusSkyboxClouds.hlsl`：云形、线条和动画。
- `Editor/Inspector/MoebiusSkyboxShaderGUI.cs`：英语材质面板。
- `Editor/Inspector/MoebiusSkyboxPresets.cs`：单一晴空默认值与创建入口，保留原脚本文件名。
- `Content/Skybox/Materials/ClearSky.mat`：唯一交付材质；保留之前日光材质 GUID。

当前云形的实际 Unity 截图与验证结果见项目根目录 `Artifacts/Skybox/CloudShape`。`CloudSun` 保存上一轮太阳与云的实现记录；其他早期配色图片也属于历史记录。
