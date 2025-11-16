# MVA Toolbox

`com.mis1sa.mva-toolbox`

MVA Toolbox 是一个 VRChat Avatar 工具包，目前包含以下功能：

## 功能概览

- **Avatar Quick Toggle (AQT)**
  - 一键生成 Bool / Int / Float 类型的切换层
  - 自动创建 / 复用 FX Animator、Expression Parameters、Expressions Menu
  - 菜单分页：遵守 VRChat 每页最多 8 项的限制，自动创建“下一页”子菜单
  - 支持两种工作流：
    - **Direct 工作流**：直接修改项目中的控制器 / 菜单 / 参数资源
    - **NDMF 工作流**：在构建阶段通过 NDMF 非破坏式生成切换层
  - 支持与 Avatar Optimizer (AAO) 协作的 BlendShape 解析（存在 AAO 时自动增强）

- **Find Anim**
  - 按“Avatar/Animator 根 + 目标对象 + 属性”查找影响该属性的所有 AnimationClip
  - 集成 **AQT 配置提示**：在结果下方显示影响当前物体的 AQT Layer 配置
  - 支持识别 AAO MergeSkinnedMesh / RenameBlendShape 合并和改名后的 BlendShape 目标

- **Jump To Animator**
  - 在 Project 中选中 AnimationClip，右键菜单跳转到引用它的 Animator / Avatar

- **Switch Anim**
  - 在 Animation 窗口中，通过菜单或快捷键在当前物体的 AnimationClip 列表中前后切换

- **Sync Main Camera to Scene View**
  - 在播放模式下，将主摄像机的位置和旋转对齐到 Scene 视图相机

## 依赖

- **必需**
  - Unity **2022.3** 及以上
  - VRChat Avatars SDK：`com.vrchat.avatars`
  - NDMF：`nadena.dev.ndmf`  
    > 通过 VCC 安装本包时，会自动一并安装其依赖（包含 NDMF），请优先使用 VCC 安装。

- **可选**
  - Avatar Optimizer：兼容其BlendShape重命名相关功能

## 安装与使用

### 推荐方式：通过 VCC 安装

1. 确保已安装最新版 VCC。
2. 在浏览器中点击下方链接，并同意打开 VCC：  
   `vcc://vpm/addRepo?url=https%3A%2F%2Fmis1sa.github.io%2Fmva-toolbox%2Findex.json`
3. 在 VCC 中确认添加仓库后，在该仓库下找到 `MVA Toolbox` 并安装。

若需要手动添加仓库，也可以在 VCC 的 Repositories 页面中使用以下 URL：

[https://mis1sa.github.io/mva-toolbox/index.json](https://mis1sa.github.io/mva-toolbox/index.json)

## 许可证

本项目基于 **MIT License** 开源，详见仓库中的 `LICENSE.md` 文件。
