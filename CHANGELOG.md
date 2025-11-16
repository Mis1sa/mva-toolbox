# Changelog

## [0.1.0-beta.1] - 2025-11-16

### Added

- 新增 **Find Anim** 窗口与 **Jump To Animator** 工具：支持按对象与属性查找动画剪辑，可从 AnimationClip 右键跳转到场景中的 Avatar / Animator。
- Find Anim 集成 **Avatar Quick Toggle (AQT)**：在结果下方显示影响当前物体的 AQT 配置，并区分 GameObject 激活与 BlendShape；支持“全部属性”模式与搜索范围过滤。
- Find Anim / AQT 支持与 **Avatar Optimizer (AAO)** 的 MSM / RenameBlendShape 联动，能够识别合并网格和预改名后的 BlendShape 目标。
- 新增 **Switch Anim** 工具：在 Animation 窗口中通过菜单或快捷键在同一物体的 AnimationClip 列表中前后切换。
- 新增 **Sync Main Camera to Scene View** 工具：在播放模式下将主摄像机对齐到 Scene 视图相机。

## [0.0.1] - 2025-11-16

### Added

- 初始打包 MVA Toolbox 为 Unity Package：`com.misisa.mva-toolbox`。
- 引入 Avatar Quick Toggle (AQT) 相关运行时与编辑器脚本。
- 增加菜单分页逻辑，自动处理 VRChat Expressions Menu 的 8 项限制并创建“下一页”子菜单。
- 支持 Direct Apply 与 NDMF 工作流，避免在 NDMF 模式下写入临时资产到磁盘。
- 提供基础文档（README）与 MIT 许可证。
