# MVA Toolbox

`com.misisa.mva-toolbox`

MVA Toolbox 是由 Misisa 编写的一组 Avatar 工具，目前包含 **Avatar Quick Toggle (AQT)**，用于快速为 VRChat Avatars 生成 FX Layer、参数和表情菜单开关。

## 主要特性（当前）

- Avatar Quick Toggle (AQT)
  - 快速为 Avatar 生成 Bool / Int / Float 类型的切换层
  - 自动创建 / 复用 FX Animator、Expression Parameters、Expressions Menu
  - 表情菜单分页：遵守 VRChat 每页 8 项限制，自动创建“下一页”子菜单
  - 支持 Direct Apply 和 NDMF 工作流
  - 支持与 Avatar Optimizer (AAO) 合作的 BlendShape 解析

## 依赖

- 必需：
  - Unity 2022.3 及以上
  - VRChat Avatars SDK (`com.vrchat.avatars`)

- 可选：
  - NDMF (`nadena.dev.ndmf`)：启用 NDMF 工作流，在构建阶段非破坏式生成切换层
  - Avatar Optimizer：若存在，将通过反射增强 BlendShape 解析体验

## 使用方式（开发中）

当前版本主要用于测试与开发：

1. 通过 Unity Package Manager 使用 **从磁盘添加 (Add package from disk)**，选择本包下的 `package.json`。
2. 在安装了 VRChat SDK 的项目中使用 Avatar Quick Toggle 窗口和组件。

后续计划：

- 在 GitHub 发布公共仓库
- 提供适配 VCC/VPM 的索引文件，支持通过 VCC 安装和更新

## 许可证

本项目基于 **MIT License** 开源，详见仓库中的 `LICENSE` 文件。
