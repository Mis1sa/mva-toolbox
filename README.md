# MVA Toolbox

`com.mis1sa.mva-toolbox`

MVA Toolbox 是一个 VRChat Avatar 工具包，用于处理一些繁琐耗时的工作，包含以下功能：

- **Avatar Quick Toggle**
  - 一键生成 Bool / Int / Float 类型的切换层。

- **Quick Animator Edit**
  - 状态模式：提供状态拆分、手动 Transition 调整、状态合并流程。
  - 过渡模式：批量创建或修改 Transition、条件、Exit Time 与默认过渡，支持条件增减。
  - 参数模式：批量扫描 / 添加 / 检查 / 调整参数，支持 Avatar / Animator 资产与 FX 默认选择，支持 MA Parameters 组件参数直接添加到 Parameters，并提供参数移除操作。
  - BlendTree 模式：快速定位状态下的 BlendTree，并支持移动、导出、创建父级等操作。

- **AnimFix Utility**
  - **查找动画**：按“Avatar/Animator 根 + 目标对象 + 属性”查找影响该属性的所有 AnimationClip。
  - **烘培默认值**：将 Avatar 当前默认姿态烘焙为 AnimationClip，用于在切换控制器或合并状态机前保存默认状态。
  - **动画重定向**：辅助修复动画缺失属性和追踪 Animator 曲线并批量重定向层级、组件与 BlendShape 变动导致的路径。

- **Sync Main Camera to Scene View**
  - 在播放模式下，将主摄像机的位置和旋转对齐到 Scene 视图相机

- **Bone Active Sync**
  - 录制/编辑时同步网格与独占骨骼的启用状态，可选写入/移除动画属性，支持最小化父级容器策略与独占骨骼模式。

- **Material Refit**
  - 批量替换材质属性，如从旧着色器迁移到新着色器时，将常用属性重新映射到新材质上，减少手工逐个材质调整的工作量。

- **Quick Remove Bones**
  - 拖入需要移除的 SkinnedMeshRenderer，将列出这些网格独占的骨骼，支持批量移除网格与对应骨骼。
 
- **Utilities**
  - Jump To Animator：在资产中选中 AnimationClip，右键菜单跳转到引用它的 Animator / Avatar。
  - Switch Anim：在 Animation 窗口中，通过菜单或快捷键在当前物体的 AnimationClip 列表中前后切换。

- **必需**
  - Unity **2022.3.22f1**
  - VRChat Avatars SDK：`com.vrchat.avatars`
  - NDMF：`nadena.dev.ndmf`  

## 安装与使用

1. 确保已安装最新版 VCC。
2. 在浏览器中点击下方链接，并同意打开 VCC：  
   `vcc://vpm/addRepo?url=https%3A%2F%2Fmis1sa.github.io%2Fmva-toolbox%2Findex.json`
3. 在 VCC 中确认添加仓库后，在该仓库下找到 `MVA Toolbox` 并安装。

若需要手动添加仓库，也可以在 VCC 的 Repositories 页面中使用以下 URL：

`https://mis1sa.github.io/mva-toolbox/index.json`

## 许可证

本项目基于 **MIT License** 开源，详见仓库中的 `LICENSE.md` 文件。
