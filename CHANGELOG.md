# Changelog

## [0.3.2] - 2025-11-21

### Changed

- 为 **Anim Path Redirect** 增强路径与组件级别的变更检测逻辑，引入约束/组件快照服务，统一追踪组件移除与 SMR BlendShape 相关的缺失曲线。

### Fixed

- 修复 **Anim Path Redirect** 在部分情况下无法正确识别组件缺失的问题（例如约束组件、VRC Constraint / VRC Contact / SkinnedMeshRenderer 等），现在会将这类缺失统一归入“缺失绑定 / 路径变更/移除”区域进行展示与修复。

## [0.3.1] - 2025-11-20

### Changed

- 统一各工具窗口的滚动实现，改用 `ToolboxUtils.ScrollView`，在保持原有 UI 结构与布局的前提下简化维护。
- 调整 **Quick State** 分割模式中“手动调整”及“头/尾 Transition” 区域的布局，使其在窗口缩放时更好填充剩余空间。

### Fixed

- 修复 **Quick Add Parameter** 等窗口中部分列表无法正常滚动的问题。
- 修复 **Quick State** 中“尾部 Transition” 水平滚动条固定不随内容移动的问题。
- 修复 **Anim Path Redirect** 中“缺失”项可选择 Avatar 根物体作为修复目标的问题。
- 调整 **Anim Path Redirect** 中“未处理 / 待修复” 状态文本的字号与对齐方式，使其更易阅读。

## [0.3.0] - 2025-11-19

### Added

- 新增 **Anim Path Redirect** 工具：对 Animator 中的曲线路径进行追踪与重定向，支持批量修复层级变更、组件缺失与 BlendShape 名称变更导致的动画丢失问题。
- 新增 **Bake Default Anim** 工具：将 Avatar 当前默认姿态（Transform / SkinnedMeshRenderer / Renderer 等）烘焙为 AnimationClip，用于在切换控制器或合并状态机前保存默认状态。

## [0.2.0] - 2025-11-18

### Added

- 集成 **Anim Controller Rebuilt**工具：支持深度复制 AnimatorController、状态机、状态、BlendTree 与 VRC Avatar Parameter Driver 组件。
- 新增 **Material Refit** 工具：支持批量替换材质属性，例如从旧着色器迁移到新着色器时，将常用属性重新映射到新材质上。
- 新增 **Quick Add Parameter** 工具：在 AnimatorController 中快速添加常用参数，并支持针对 Avatar / Animator 资产批量补齐参数。
- 新增 **Quick State** 工具：提供 Animator 状态的拆分与合并功能，自动调整相关 Transition 结构。
- 新增 **Quick Transition** 工具：批量查看和编辑某层或状态机中的 Transition，用于统一过渡时间、Exit Time 与条件配置。

## [0.1.0] - 2025-11-17

### Changed

- 将 **MVA Toolbox** 版本提升为 `0.1.0`，更新 Unity 包版本与 VPM 索引配置，并调整发布包名为 `mva-toolbox-0.1.0.zip`。

### Fixed

- 修复 **Avatar Quick Toggle (AQT)** 在 **NDMF 工作流** 下直接修改原始 FX Animator Controller 的问题，改为在构建过程中克隆控制器并重定向到克隆资产，避免污染原始资源与参数。
- 为 `QuickToggleConfig` 增加 `IEditorOnly` 标记，确保在 VRChat Avatar 构建与上传过程中自动剥离，避免“未知组件”警告和上传阻塞。
- 改进 **Sync Main Camera to Scene View** 工具逻辑，使其在已经处于播放模式时也可以通过菜单即时启用或禁用同步，无需重新进入 Play 模式。

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
