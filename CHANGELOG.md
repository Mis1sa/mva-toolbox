# Changelog

## [0.6.0] - 2026-02-02

### Changed

- Sync Main Camera to Scene View：重构同步流程，新增编辑模式缓存、播放前一次性对齐与运行时启停逻辑，改善在切换 Game / Scene 视图、缺少 Scene 视窗以及退出播放模式时的相机体验。

## [0.6.0-beta.3] - 2026-01-26

### Fixed

- Avatar Quick Toggle：
  - 修复新建动画层的状态机与过渡无法持久化到控制器资产的问题。
  - 修复 Int / Float 模式在生成分页菜单时无法正确创建下一页控制的问题。
- AnimFix Utility：调整动画查找模式下 BlendShape 选项的排列，保持与原始 Mesh 顺序一致。
- Quick Animator Edit：优化参数检查界面，增加分组盒与“无用参数”全选按钮，改善批量处理效率。
- Bone Active Sync：
  - 避免与 Avatar Quick Toggle 预览功能发生冲突，支持在预览期间正确挂起同步。
  - 当关闭“仅独立骨骼”时，修复重新启用骨骼的逻辑，防止误开关父级骨骼。
- Quick Remove Bones：忽略普通 MeshRenderer，解决每次拖入都会弹出提示框的问题。

## [0.6.0-beta.2] - 2026-01-03

### Fixed

- 修复 AnimFixUtility 中各个功能的控制权选择范围的问题。

## [0.6.0-beta.1] - 2025-12-28

### Added

- Quick Animator Edit（过渡模式）：新增“增减 Conditions”批量编辑能力。
- Quick Animator Edit（参数模式）：支持 MA Parameters 组件直接添加到 Parameters；参数调整新增“移除”操作。
- AnimFix Utility：支持 MA Merge Animator 组件；查找动画支持精确指定 Blendshape。
- Bone Active Sync：新增网格与独占骨骼启用状态同步，可选写入/移除动画属性，支持最小化父级容器与独占模式。

### Fixed

- Sync Main Camera to Scene View：无 Scene 视图时不再重置主摄像机到原点。

## [0.5.3] - 2025-12-25

### Fixed

- Quick Remove Bones：优化独占骨骼清理逻辑，确保无论勾选何种子级处理选项，都能正确释放需保留的骨骼及其子物体，避免误删。

## [0.5.2] - 2025-12-23

### Changed

- Quick Remove Bones：移除 Avatar 锁定流程，改为直接对拖入的 SkinnedMeshRenderer 进行独占骨骼分析，可同时处理多个 Avatar。
- 调整工具菜单排序：AnimFix Utility、Material Refit、Quick Remove Bones 的优先级更新。

## [0.5.1] - 2025-12-22

### Changed

- 将 **Find Anim / Bake Default Anim / Anim Path Redirect** 三个旧窗口合并为统一的 **AnimFix Utility**，共享目标与控制器/层级上下文，分 Tab 使用。

### Fixed

- 修复 **Switch Anim** 必须选中动画控制器所在物体才能切换动画的问题，现在会自动读取 Animation 窗口的活动对象并在未选中场景对象时继续工作。

## [0.5.0] - 2025-12-21

### Added

- Quick Animator Edit：整合 **Quick Add Parameter / Quick State / Quick Transition / Animator Controller Rebuilt** 的主要功能于单一窗口，并新增参数与 BlendTree 相关的编辑流程，保持统一上下文与操作体验。

### Fixed

- 修复 Avatar Quick Toggle 在 Int Switch 模式下无法正确切换 WD 编辑模式的问题。

## [0.4.0] - 2025-12-01

### Added

- 新增 **Quick Remove Bones** 工具：在 Avatar 中自动分析 Renderer 与独占骨骼，支持批量移除骨骼与关联网格。

### Changed

- 将 **Avatar Quick Toggle** 组件移动到 `组件/MVA Toolbox/Avatar Quick Toggle`，便于在 Inspector 中查找。

### Fixed

- 修复 **Avatar Quick Toggle** 在 Float 切换层上的 ON/OFF 状态计算问题。
- 修复 **Avatar Quick Toggle** Int 层使用 “+” 按钮时写回配置项出错的问题。
- 修复 **Material Refit** 未额外创建材质实例导致引用丢失的问题，并修复其预览逻辑。
- 修复 **Anim Path Redirect** 在移除所有缺失条目后无法应用更改的问题。

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
