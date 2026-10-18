# 进化网 / 食物网界面调整

最终版本采用用户逐项确认的 4 张纸张、植物手绘素材。内置 image_gen 生成，无外部 API 脚本。素材位于 Assets/Resources/GameUI/SpeciesMaps，原始生成图片保留。

按最后反馈，取消 Viewport 纯黄色遮挡，隐藏旧 SpeciesMapBackground 正方形底图。两页图表直接显示在长方形纸张背景上。图表宽 2080、高 1060，节点横向跨度 1820；只调整节点和标题坐标，头像不拉伸。连线读取原节点坐标和原拓扑数据，关系不变。

物种说明框改为 720×480 起、自适应内容和屏幕，恢复纸张比例，增加内边距，文字自动在 22–28 Canvas 字号间调整。屏幕变化时同步重新约束说明框。

进化网作为排序 300 的全屏模态界面覆盖菜单、工具栏和手机 HUD；经营操作的旧 OnGUI 面板在地图打开时隐藏。关闭恢复原模拟暂停状态。Esc 只关闭图表，不同时打开菜单。主菜单、暂停菜单和发现卡显示时不允许图表抢占。

手机在建造地块下方增加进化网按钮。依照用户提供的“一父三子”树状图标生成 UI 网格，去掉参考图的深色底。单指拖动空白处，双指缩放；双指操作后抑制误点物种。

## 验证

Unity 6000.2.9f1 Editor 实际 Play Mode，确认帧数推进后检查。

- 1920×1080、1280×720、720×1280：手机入口、图标网格、按钮位置、排序、两页共 30 个节点的说明文字、页面切换、关闭和暂停状态恢复均通过。
- Input System 注入 Touchscreen 事件：单指拖动、双指缩放、双指误点抑制、释放状态均通过（以上三种尺寸）。
- Input System 注入 Keyboard 事件：M 打开 / 关闭、Esc 仅关闭图表、菜单维持游戏状态，通过。
- 主菜单/暂停菜单抢占保护：实际验证暂停菜单打开时图表不能抢占，恢复游戏后可打开。
- 两页 Viewport alpha=0、旧背景对象 inactive、节点横向跨度 1820，确认通过。
- 点击原菜单所在屏幕位置，UI 射线首先命中地图 Viewport，避免穿透。
- 编译通过。最终运行检查无新增游戏脚本错误；过程中出现过 Unity Inspector 的 SerializedObject/UIElements 内部异常，刷新 Inspector 后未重现。
- 尚未验证：独立 Windows 包、安卓真机、TapPlay。

自动检查入口：Assets/Scripts/UI/Editor/SpeciesMapsPresentationChecks.cs，Run / TouchGestures / DesktopKeys。Run 在 Play Mode、进入游戏并关闭发现卡后执行。SetResolution 仅供 Editor 验证。

截图：Desktop.png、FoodWeb.png、SpeciesClue.png、MobileEntry.png、Portrait.png，均来自 Unity Game View。竖屏图包含放大、平移后的状态；可双指缩小查看整体。

## 图片生成提示词

OuterFrame.png:
A single 16:9 wide landscape opaque game UI outer backing panel, fills image edge to edge, no surrounding scene. Antique naturalist field journal aesthetic, watercolor pale warm parchment, subtle fibrous grain, hand-inked muted olive double outline close to perimeter, a thin muted moss-green outer binding, tiny botanical leaves limited to the four corners. Very restrained decorations, center entirely empty pale cream with no objects. This is the outer backing behind an existing botanical evolution chart and behind another panel. Flat orthographic 2D, crisp usable UI texture, no perspective, no words, no letters, no graphs, no icons, no blue, no black field, no modern neon.

InnerPanel.png:
A single wide 16:9 landscape opaque game UI inner background panel filling image edge to edge. Same antique naturalist field journal art direction: pale ivory aged paper, fine paper fibers, extremely light tea stains, fine hand-drawn warm olive border inset just 2 percent, little simple leaf sprigs at corners only. Top 10 percent a blank slightly warmer cream title strip, remaining center nearly blank for a botanical chart overlay. Restrained hand painted watercolor and ink, flat orthographic 2D. No text, no typography, no graph, no icon, no black, no blue, no glossy shading.

SwitchButton.png (transparent background):
One single horizontal game UI button background centered and large on a truly transparent canvas. Wide rounded rectangular antique cream paper label, aspect 3 to 1, thin double irregular hand inked olive green outline, subtle paper grain, tiny leaf flourish at left and right ends, broad clean empty center for game-generated Chinese text. Matches a cozy illustrated naturalist botanical field journal, watercolor and fine ink, understated warm beige and sage, flat 2D view, no words or letters, no icons, no shadow outside silhouette, generous transparent margin.

CloseButton.png (transparent background):
One single small square game UI close-button BACKGROUND with softly chamfered rounded corners, centered and large on a truly transparent canvas. Antique cream parchment surface, thin double imperfect dark olive ink outline, tiny restrained curled leaf at upper left and lower right corners, center empty to later overlay a close glyph in Unity. Same cozy hand painted botanical naturalist field journal style, beige paper and sage green only. Flat 2D, no text, NO X symbol, no letters, no icons, no outer drop shadow, transparent surrounding margin.
