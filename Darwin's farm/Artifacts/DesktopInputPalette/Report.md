# PC 输入恢复和建造栏文字

用户确认：点击 Game 游戏画面并切换英文输入法后，N / M 恢复。检查时原生 Keyboard 存在并启用，Input System 为 Dynamic Update，两个控制器启用且场景引用完整。Game View 已确认 PlayFocused。未修改快捷键绑定和移动端操作。

PC 建造栏预制体 Assets/Scripts/map/prefab/TileItem.prefab：文字由近黑色改纯白，字号 24 改 28，单行显示。保留原字体和卡片布局。

Unity Play Mode 中，12 个地块名称均确认颜色为白、字号 28、无文字溢出；Game View 截图见 BuildNames.png。自动输入检查通过 M 开关 / Esc 关闭地图；N 的输入事件经正常帧处理后确实切换建造模式。输入焦点恢复由用户的实际操作确认。未做独立包测试。

测试结束已停止 Play Mode，并清理截图导入和动态字体缓存产生的无关改动。
