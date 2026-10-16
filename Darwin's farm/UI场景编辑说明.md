# 信息 UI 场景编辑

WhiteBox 场景已保存完整的信息 UI 层级，退出 Play Mode 后也可以编辑。

## Hierarchy 位置

- `Canvas/TileInformation`：地块面板背景 Image；`TileDetails` 是地块文字，`Close` 是关闭按钮。
- `Canvas/PopulationInformation`：种群面板背景 Image；展开 `Content` 可以编辑头像、名称、数值、简介、雷达图、食物关系和辅助文字。
- `Canvas/InformationHUD`：经济、时间、倍速、新闻显示。
- `Canvas/TileInformation/TileTrend`：新增地块趋势图，展开可编辑标题、坐标轴、日期和图例；`Chart` 组件可调整线宽、颜色、显示曲线数和植物轴范围。

选择文字组件调整 Font/Font Asset、字号、颜色和对齐；选择 Image 调整背景 Sprite；选择 RectTransform 调整位置和尺寸。改完按 Ctrl+S 保存场景。请在非 Play Mode 下进行长期修改。

编辑模式下两个面板保持可见，便于排版。运行时自动隐藏，仍通过原 Alt 点击机制打开；不会重复生成面板，也不会重置已配置的字体、字号、背景和默认位置。真实数值、物种名称、简介及头像仍由数据绑定刷新；物种头像和简介请在对应 SpeciesData 资产的 Portrait/Description 中设置。

## 实现改动

- `GameInformationUI`：序列化窗口、文本和按钮引用；复用已保存的对象，启动时绑定关闭事件和拖拽。
- `PopulationInfoPanel`：序列化子对象引用，有现成内容时不再创建和设置布局。
- `InformationTextDragHandle`：窗口引用可以随场景保存。
- `GameUITheme`：增加持久化 TMP 字体引用。
- `Assets/Scripts/UI/Editor/GameInformationUIInstaller.cs`：一次性创建可编辑层级，并保存背景切图和字体资源。
- `Assets/Resources/GameUI/Editable/`：保存五个背景 Sprite 和 TMP 字体资产。
- `Assets/Scenes/WhiteBox.unity`：保存面板、HUD 和各组件引用，原场景对象全部保留。

其他包含 GameInformationUI 的场景，可通过 `Tools → Darwin's Farm → UI → Install editable information UI` 接入，然后保存场景。工具保留已有布局，不会主动重建已保存的面板。

## 已验证

Unity 编译通过；保存并重开 WhiteBox 后面板及字体引用存在；Play Mode 不重复创建面板或 HUD；编辑模式临时将数量字号改为 27，运行时仍保持 27（测试后恢复原字号 22）；真实种群数量刷新正常；窗口打开、模拟指针拖拽及 X 关闭通过；运行时 Console 无错误。

摄像机和建造脚本的操作逻辑未修改，完整 Alt 鼠标与 N 建造流程仍需人工游玩复核。
