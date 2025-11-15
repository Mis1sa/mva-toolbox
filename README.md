# MVA Toolbox

`com.mis1sa.mva-toolbox`

MVA Toolbox 是由 Mis1sa 编写的一组 VRChat Avatar 工具包，目前主要包含 **Avatar Quick Toggle (AQT)**，用于快速为 Avatar 生成 FX Layer、参数和表情菜单开关。

## 功能概览

- **Avatar Quick Toggle (AQT)**
  - 一键生成 Bool / Int / Float 类型的切换层
  - 自动创建 / 复用 FX Animator、Expression Parameters、Expressions Menu
  - 菜单分页：遵守 VRChat 每页最多 8 项的限制，自动创建“下一页”子菜单
  - 支持两种工作流：
    - **Direct Apply**：直接修改项目中的控制器 / 菜单 / 参数资源
    - **NDMF 工作流**：在构建阶段通过 NDMF 非破坏式生成切换层
  - 支持与 Avatar Optimizer (AAO) 协作的 BlendShape 解析（存在 AAO 时自动增强）

## 依赖

- **必需**
  - Unity **2022.3** 及以上
  - VRChat Avatars SDK：`com.vrchat.avatars`
  - NDMF：`nadena.dev.ndmf`  
    > 通过 VCC 安装本包时，会自动一并安装其依赖（包含 NDMF），请优先使用 VCC 安装。

- **可选**
  - Avatar Optimizer：若存在，将通过反射增强 BlendShape 解析体验

## 安装与使用

### 通过 Unity Package Manager（本地开发）

1. 将本仓库克隆到本地，例如 `D:/Data/Backup/Git/MVA Toolbox`。
2. 在 Unity 中打开 **Window → Package Manager**。
3. 点击左上角 `+` 按钮，选择 **Add package from disk...**。
4. 选择本包根目录下的 `package.json` (`com.mis1sa.mva-toolbox`)。

## 许可证

本项目基于 **MIT License** 开源，详见仓库中的 `LICENSE.md` 文件。
