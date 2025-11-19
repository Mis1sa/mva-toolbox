# MVA Toolbox

`com.mis1sa.mva-toolbox`

MVA Toolbox 是一个 VRChat Avatar 工具包，用于处理一些繁琐耗时的工作，目前包含以下功能：

- **Avatar Quick Toggle**
  - 一键生成 Bool / Int / Float 类型的切换层

- **Animator Controller Rebuilt**
  - 重构 AnimatorController：复制层、状态机、状态、BlendTree 与 StateMachineBehaviour，用于修复 AnimatorController 的状态机与转换结构。

- **Find Anim**
  - 按“Avatar/Animator 根 + 目标对象 + 属性”查找影响该属性的所有 AnimationClip
  - **Jump To Animator**
    - 在资产中选中 AnimationClip，右键菜单跳转到引用它的 Animator / Avatar

- **Switch Anim**
  - 在 Animation 窗口中，通过菜单或快捷键在当前物体的 AnimationClip 列表中前后切换

- **Sync Main Camera to Scene View**
  - 在播放模式下，将主摄像机的位置和旋转对齐到 Scene 视图相机

- **Material Refit**
  - 批量替换材质属性，如从旧着色器迁移到新着色器时，将常用属性重新映射到新材质上，减少手工逐个材质调整的工作量。

- **Anim Path Redirect**
  - 对 Animator 中的曲线路径进行追踪与重定向，支持批量修复层级变更、组件缺失与 BlendShape 名称变更导致的动画丢失问题。

- **Bake Default Anim**
  - 将 Avatar 当前默认姿态（Transform / SkinnedMeshRenderer / Renderer 等）烘焙为 AnimationClip，用于在切换控制器或合并状态机前保存默认状态。

- **Quick Add Parameter**
  - 在 AnimatorController 中快速添加参数，并可针对 Avatar / Animator 资产批量补齐常用参数，避免手动反复打开 Animator 窗口进行参数维护。

- **Quick State**
  - 提供 Animator 状态的拆分与合并工具，可在同一层内将状态拆成“头/尾”两段，或将两个状态合并为一个，并自动调整相关 Transition 结构。

- **Quick Transition**
  - 批量查看和编辑某一层或某个状态机中的 Transition 设置，用于快速统一过渡时间、Exit Time 或条件配置。

- **必需**
  - Unity **2022.3** 及以上
  - VRChat Avatars SDK：`com.vrchat.avatars`
  - NDMF：`nadena.dev.ndmf`  
    > 通过 VCC 安装本包时，会自动一并安装其依赖，请优先使用 VCC 安装。

- **可选**
  - Avatar Optimizer：兼容其BlendShape重命名相关功能

## 安装与使用

1. 确保已安装最新版 VCC。
2. 在浏览器中点击下方链接，并同意打开 VCC：  
   `vcc://vpm/addRepo?url=https%3A%2F%2Fmis1sa.github.io%2Fmva-toolbox%2Findex.json`
3. 在 VCC 中确认添加仓库后，在该仓库下找到 `MVA Toolbox` 并安装。

若需要手动添加仓库，也可以在 VCC 的 Repositories 页面中使用以下 URL：

[https://mis1sa.github.io/mva-toolbox/index.json](https://mis1sa.github.io/mva-toolbox/index.json)

## 许可证

本项目基于 **MIT License** 开源，详见仓库中的 `LICENSE.md` 文件。
