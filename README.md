# AsterPlay

AsterPlay 是一款基于 **.NET 8 与 WinUI 3** 开发的 Windows 原生 Emby 客户端，使用 **libmpv** 进行媒体播放，支持 Liquid Glass 界面效果。

## 特性

- **Emby 连接**：添加和管理服务器、账户登录、会话恢复、退出登录，使用 Windows DPAPI 保护访问令牌。
- **媒体浏览**：首页 Hero 展示、继续观看、最新媒体、媒体库分类、搜索、排序、筛选及分页。
- **内容详情**：电影、剧集、季与集数信息，演职人员、媒体源及音视频轨道信息，支持收藏。
- **视频播放**：通过 libmpv 与 D3D11 / SwapChainPanel 实现原生播放；支持 Direct Play、Direct Stream 和转码。
- **播放控制**：断点续播、进度跳转、音量与倍速调整、音轨与字幕切换、上一集 / 下一集、全屏播放及播放状态同步。
- **弹幕功能**：集成 LogVar 弹幕源，支持自动 / 手动匹配、时间同步、过滤、密度控制及防重叠。
- **界面体验**：支持系统深浅色模式、Liquid Glass 视觉效果、开屏画面、媒体图片缓存与按需加载。
- **便携运行**：提供 Windows x64 自包含版本，无须单独安装 .NET 8 或 Windows App SDK 运行时。

## 依赖

| 组件 | 用途 |
| --- | --- |
| Windows 10（19041+）/ Windows 11，x64 | 运行平台 |
| .NET 8 | 应用运行环境与开发 SDK |
| WinUI 3 / Windows App SDK 2.5.1 | 原生 Windows 界面 |
| libmpv | 视频解码与播放 |
| LiquidGlassWinUI 与原生兼容运行库 | Liquid Glass 视觉效果 |
| Microsoft C++ v145 Build Tools | 仅在需要重新编译 Liquid Glass 原生运行库时使用 |

普通构建会自动检测并准备所需的 .NET SDK 和 libmpv 依赖。项目内已提供兼容的 Liquid Glass 原生 DLL，正常构建不要求安装完整 Visual Studio IDE。

## 构建方法

在 **Windows x64** 系统中克隆仓库，并在项目根目录执行：

```powershell
git clone https://github.com/l888r88f8-spec/AsterPlay.git
cd AsterPlay
.\build-windows.cmd
```

需要清理缓存并完整重新构建时执行：

```powershell
.\build-windows.cmd -Full
```

构建脚本会自动检查依赖、执行 Release 发布，并优先复用已有的构建缓存；首次构建需要网络连接。

输出目录为 `dist/AsterPlay/`，可直接运行其中的 `AsterPlay.exe`。便携版采用**非单文件、自包含**发布方式：主程序与运行 DLL 位于根目录，`resources/` 收纳可安全重定位的托管语言资源，`Assets/` 存放应用资源，`Info/` 存放构建与依赖来源信息。部分 WinUI 原生语言文件因加载要求可能保留在其原有位置。

## 许可

AsterPlay 采用 **MIT License** 开源，详细条款见 [LICENSE](LICENSE)。

Copyright © 2026 [l888r88f8-spec](https://github.com/l888r88f8-spec)。

项目使用的第三方代码、库及二进制组件遵循各自的许可证；AsterPlay 的 MIT 许可不替代第三方组件的许可要求。

## 引用公告

- **[LiquidGlassWinUI](https://github.com/luckyelysia/LiquidGlassWinUI)**：Liquid Glass 效果相关实现参考及适配来源；相关原始许可见 [LICENSE.upstream.txt](Native/LiquidGlassCompat/LICENSE.upstream.txt)。
- **[mpv](https://github.com/mpv-player/mpv)**：媒体播放核心。Windows 版 libmpv 使用 [zhongfly/mpv-winbuild](https://github.com/zhongfly/mpv-winbuild) 提供的构建产物，并遵循相应 LGPL 许可要求；具体版本和来源记录于构建产物的 `Info/RUNTIME-SOURCE.txt`。

以上引用用于注明第三方技术、组件及设计参考，不代表上述项目与 AsterPlay 存在官方关联或背书。
