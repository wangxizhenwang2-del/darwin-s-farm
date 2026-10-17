# Vincent Moebius 效果还原

参考提交：`origin/Vincent-Branch@c06e92a551dc386b00371cfb629c377de52a8423`。

原版 shader、材质、贴图、模型和示例场景逐文件校验，531 个文件完全一致。
主体直接复用原网格和材质，保留分离法线、全部 UV、切线、顶点遮罩、多材质与完整起伏。
取消原先“简化地面 + 普通 PBR 混色”的主体渲染方式。
一米连接带使用独立适配 shader，光照、排线、纸面、高光、点描等算法复用 Vincent 原代码。
雪山、火山使用该提交中的正式 Prefab，保留各自专用 shader、描边、岩石遮罩和熔岩材质。

高地绘制网格的源资产禁止 CPU 读写；编辑器生成可读副本，逐通道核对其数据，保持源资源不变。
地面碰撞合并原网格形状和连接带，适配导航、选中与粒子碰撞。
六套环境粒子继续使用技美原生材质、贴图、运动与风控制器。

地图中的地块共用同一套主光。独立示例 Scene 各自摆放的灯位不同时叠加到地图上；
截图对照时应使用相同的光照、相机和渲染配置。这里验证原版 shader、材质和网格数据一致，
不把不同灯位的两张截图当作像素一致的证明。

## 画面与验证

最终结果：21 组实机回归通过，观察 27844 个切换帧；涵盖 32 种地块/旋转组合、
56 个有序地形对、16 次连续打断及八类地形的模拟和实机移动。
八块原生地面、12 个原始材质槽、200 个可见地面/碰撞采样全部一致。
当前 Play 会话无编译或运行错误，退出后运行时网格全部释放。
连接带 shader 位于 Resources，保留其排线、三平面与点描变体。

[总览](Overview.png)；[森林](Forest.png)、[沙漠](Desert.png)、[草原](Grassland.png)、
[高地](Highland.png)、[雨林](Rainforest.png)、[苔原](Tundra.png)、[雪山](SnowMountain.png)、[火山](Volcano.png)。

- [源资源比对](source-asset-parity.json)
- [原生地面通道、材质与碰撞比对](native-ground-parity.txt)
- [切换、旋转、移动回归](integration-results.md)
- [高地可读副本通道比对](highland-readable-copy.txt)
- [资源释放](cleanup.txt)与[外观回退](fallback.txt)

Play 验证地图不保存到 WhiteBox 场景。
