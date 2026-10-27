# v0.4.4 · 依附与扒边 / Edge Docking

## 中文

- 拖到屏幕底部任务栏或左右边缘后松开，桌宠自动变为双手扒边、半身探头姿态；拖回屏幕中间恢复全身。
- 新增右键“依附 / 扒边”菜单，可指定边缘、取消依附，以及开关拖动自动吸附。
- 支持依附最近使用窗口的底边或左右边，并跟随窗口移动和缩放；目标最小化、隐藏或关闭后回到屏幕边缘。
- 新增六帧透明扒边素材，保留眨眼、笑脸与轻呼吸；悬停任务气泡和点击固定仍可使用。
- 保存屏幕依附方向和沿边位置。窗口句柄不跨重启保存，重启后恢复对应的屏幕依附。

下载 `ProgressGlass-v0.4.4-windows.zip`，完整解压后运行 `ProgressGlass.exe`。升级前退出旧版，必须同时更新 `assets`；可复制旧目录的 `pet-settings.json` 保留个人设置。完整步骤见 [中文说明](https://github.com/D-admin/ProgressGlass/blob/v0.4.4/README.md)。

验证：88 项核心检查和进度更新脚本检查通过，包括三档尺寸、透明区域、贴边布局、真实测试窗口移动与缩放跟随、目标隐藏后回退。实际触摸屏未测试；当前支持底部及左右边缘，不含顶边。软件包附带源码、通用示例和许可证，不含对话、个人设置或运行记录。

## English

- Release the pet near the bottom taskbar or either screen side to switch to a half-body gripping pose. Drag toward the screen center to restore the full body.
- Adds a right-click docking submenu for choosing an edge, detaching and toggling automatic snapping.
- Attach to the bottom or sides of the most recently used window and follow its movement and resizing. A minimized, hidden or closed target falls back to the screen edge.
- Adds six transparent edge-gripping sprites with blinking, smiling and gentle breathing. Hover details and click-to-pin remain available.
- Saves screen docking direction and relative position. Window handles are not persisted across restarts; the corresponding screen edge is restored instead.

Download `ProgressGlass-v0.4.4-windows.zip`, extract the entire archive and run `ProgressGlass.exe`. Exit the old version first and update `assets` along with the EXE. Copy the old `pet-settings.json` to retain preferences. See the [English guide](https://github.com/D-admin/ProgressGlass/blob/v0.4.4/README.en.md).

Validation: 88 core checks and progress-updater checks passed, covering three sizes, transparency, edge layout, real test-window movement and resizing, and fallback when the target is hidden. Physical touch-screen interaction remains untested. Bottom and side edges are supported; the top edge is not. The archive includes source, generic examples and licenses, without conversations, personal preferences or runtime records.


# v0.4.3 · 灵动表情 / Expressive Animation

## 中文

- 新增十二种眼睛、嘴型与动作表情：自然眨眼、左右看、闭嘴微笑、惊讶、眨单眼、开心笑、招手、好奇和哈欠。
- 鼠标靠近时看向鼠标，悬停招手，点击开心回应并轻跳，拖动时惊讶，松开后回弹。
- 闲置时轻呼吸、摆动并偶尔变换表情；右键菜单新增轻柔模式，减少身体运动和装饰闪光。
- 保留原有任务气泡、跨项目活动、额度快照与连接检测；表情动画不代表工作进度或服务心跳。

下载 `ProgressGlass-v0.4.3-windows.zip`，完整解压后运行 `ProgressGlass.exe`。升级前退出旧版；必须同时更新 `assets`，不要只替换 EXE。可复制旧目录的 `pet-settings.json` 保留位置与大小。操作步骤见 [中文说明](README.md)。

验证：68 项核心检查及进度更新脚本检查通过；十二种表情在 96、128、160 三档尺寸下通过动态边界检查。当前 UI 为中文，文档提供中英文；触摸屏硬件尚未实测。软件包附带源码、通用示例和许可证，不含个人会话数据。

## English

- Adds twelve eye, mouth and gesture expressions: natural blinks, side glances, closed-mouth smiles, surprise, winks, laughter, waving, curiosity and yawning.
- Looks toward a nearby pointer, waves on hover, responds happily with a small bounce on click, looks surprised during dragging, and settles on release.
- Adds gentle breathing, swaying and occasional idle expressions. The new context-menu gentle mode reduces body movement and decorative sparkles.
- Retains the activity bubble, local cross-project activity, usage snapshots and connectivity checks. Animation does not indicate work completion or server health.

Download `ProgressGlass-v0.4.3-windows.zip`, extract the whole archive and run `ProgressGlass.exe`. Exit the old version before upgrading; update `assets` along with the EXE. Copy the old `pet-settings.json` if you want to preserve position and size. See the [English guide](README.en.md).

Validation: 68 core checks and updater checks passed; all twelve expressions passed animated-boundary checks at 96, 128 and 160 pixels. The UI is Chinese with bilingual documentation; physical touch-screen interaction remains untested. The archive includes corresponding source, generic examples and licenses, without personal session data.
