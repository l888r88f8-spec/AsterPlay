# AsterPlay 实现文档

## 当前架构

AsterPlay 当前为纯 WinUI 3 桌面应用，使用 .NET 8、Windows App SDK、libmpv 与 D3D11 Composition。

源码结构：

```text
src/
├─ AsterPlay.Core/
│  ├─ Models/
│  ├─ Services/
│  │  ├─ Danmaku/
│  │  ├─ Mpv/
│  │  └─ Playback/
│  └─ ViewModels/
└─ AsterPlay.WinUI/
   ├─ Controls/
   ├─ Interop/
   ├─ Services/
   └─ Views/
```

职责划分：

- `AsterPlay.Core`：Emby API、会话、设置、播放协商、libmpv 客户端、弹幕数据与服务、详情/媒体库 ViewModel。
- `AsterPlay.WinUI`：WinUI 3 应用壳、页面、播放器 UI、D3D11 SwapChainPanel 合成、液态玻璃、图片缓存与懒加载。
- 默认发布入口：`src/AsterPlay.WinUI/AsterPlay.WinUI.csproj`。
- 默认构建入口：`build-windows.cmd`（自动检测依赖，增量或全量编译）。

## 已实现功能

### 登录与账户

- Emby 登录
- 会话恢复
- 已保存服务器管理
- 登出
- Access Token 使用 Windows DPAPI 保护
- 应用设置持久化

### 首页与媒体库

- Hero
- 继续播放
- 媒体库入口与分区
- 搜索
- 类型 / 年份 / 收藏筛选
- 排序
- 分页
- 右键详情

### 详情页

- 电影详情
- 剧集详情
- Season / Episode 浏览
- 演员 / 导演 / Studio / Genre / Tag
- MediaSource / MediaStream 信息
- 收藏
- 播放 / 继续播放 / 从头播放

### 播放器

- libmpv
- D3D11 Composition
- SwapChainPanel
- DirectPlay / DirectStream / Transcode 协商
- 续播
- 客户端 Seek
- 服务端 reopen/seek
- 播放 / 暂停
- 音量 / 静音
- 倍速
- 音轨选择
- 字幕选择 / 关闭
- 上一集 / 下一集 / 剧集列表
- 全屏
- 播放诊断
- Emby Playing / Progress / Pause / Unpause / TrackChange / Stopped 上报

### 弹幕

- LogVar 数据源
- 自动匹配
- 手动匹配
- 时间轴同步
- Seek 抑制与重同步
- 滚动 / 顶部 / 底部弹幕
- 字号、速度、透明度、显示区域
- 密度限制
- 活动数量限制
- 防重叠
- 屏蔽词
- 用户屏蔽
- DPAPI 保护数据源 Token

### 图片加载

- 内存缓存
- 磁盘缓存
- 同 URL 请求合并
- 下载并发限制
- 超时
- 缓存大小 / 年龄清理
- viewport-based lazy loading
- 首页 / 媒体库 / 详情页统一使用缓存图片控件

## 构建与发布

运行：

```bat
build-windows.cmd
```

输出：

```text
dist/AsterPlay/
```

发布包为 Windows x64 self-contained 便携版，默认使用 `PublishSingleFile=true` 和 `IncludeAllContentForSelfExtract=true` 整合 .NET / WinUI 运行依赖。实际输出包含：

- `AsterPlay.exe`（应用和可合并的运行依赖）
- `CustomEffectRuntimeNative.dll`（Liquid Glass 原生桥接）
- `libmpv-2.dll`（以及 mpv 需要的同目录依赖）
- `Assets/AsterPlay.AppIcon.png`、`Assets/AsterPlay.ico`（原生开屏资源）
- `Info/BUILD-INFO.txt`、`Info/LIQUIDGLASS-COMPAT.txt`、`Info/RUNTIME-SOURCE.txt`（构建与授权来源信息）

单文件便携版首次运行时会解包部分依赖到用户临时目录，不需要安装 .NET / Windows App SDK。可用 `build-windows.cmd -Unpacked` 恢复完整目录结构进行故障排查，`-Full` 强制全量构建。

CI 会验证：

1. 构建环境自动检测和缺失依赖安装
2. 自动选择增量或完整 Windows x64 Release 发布
3. 必需运行时文件校验
4. 固定 libmpv 版本与校验信息
5. 应用启动 smoke test
6. Release artifact 上传

## 维护原则

- UI 代码只放在 `AsterPlay.WinUI`。
- 与 UI 框架无关的业务逻辑只放在 `AsterPlay.Core`。
- 不在 Core 中引用 WinUI/XAML 类型。
- 播放器渲染路径保持 libmpv + D3D11 Composition。
- 图片网络请求统一经过 WinUI 图片缓存服务。
- 新功能优先复用现有 Emby / playback / danmaku 服务，不在页面中复制协议逻辑。
- 默认分支 `main` 始终保持可构建、可启动。
