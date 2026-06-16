# MVA Toolbox

`com.mis1sa.mva-toolbox`

MVA Toolbox 是一个 VRChat Avatar 工具包，用于处理一些繁琐耗时的工作。以下是主要功能模块：

## 功能

### 🧬 Avatar工具
- **开关生成**：一键生成 Bool / Int / Float 类型的快速切换层。
- **移除网格权重骨骼**：快速清理 SkinnedMeshRenderer 的独占骨骼。

### 🎮 动画控制器工具
- **状态工具**：提供状态拆分、合并、属性调整等编辑流程。
- **过渡工具**：批量创建或修改 Transition、条件、Exit Time 与默认过渡，支持条件增减。
- **参数工具**：
  - 扫描 / 添加 / 检查 / 调整参数
  - 参数的改名、替换和移除
  - 参数追踪：查看参数引用和写入位置
- **混合树工具**：快速定位状态下的 BlendTree，支持移动、导出、创建父级等操作。

### 🎬 动画工具
- **动画查询**：按"Avatar/Animator 根 + 目标对象 + 属性"查找影响该属性的所有 AnimationClip。
- **默认值烘培**：将 Avatar 当前默认姿态烘焙为 AnimationClip，用于切换控制器或合并状态机前保存默认状态。
- **动画重定向**：修复动画缺失属性，追踪 Animator 曲线并批量重定向层级、组件与 BlendShape 变动。
- **动画切换**：在 Animation 窗口中快速前后切换动画片段（快捷键：Shift+Alt+Z/X）。

### 🎨 材质工具
- **材质纹理替换**：批量替换材质属性，支持从旧着色器迁移到新着色器，减少手工调整工作量。

### 🔧 组件工具
- **Phys Bone 碰撞可视化**：在编辑器中可视化显示 VRChat Phys Bone 碰撞体。

### 📍 杂项
- **引用查询**：查找资产在项目和场景中的引用位置，支持引用链查看和跨资产搜索。
- **同步主摄像机**：在播放模式下将主摄像机的位置和旋转同步到 Scene 视图相机。
- **FBX替换**：在临时工作区中对齐源 FBX 与目标对象，按层级、材质与组件分阶段确认后导出替换结果。

## 必须

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
