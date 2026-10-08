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

Windows x64 便携版采用 **根目录真实 AsterPlay.exe + DLL，resources/ 专供语言资源** 的结构。未来安装版也应直接复用：

```text
AsterPlay/
├── AsterPlay.exe              # 直接启动的 WinUI 3 程序，不再额外包装
├── AsterPlay.Core.dll
├── LiquidGlassWinUI.dll
├── CustomEffectRuntimeNative.dll
├── libmpv-2.dll
├── *.dll                       # .NET / Windows App SDK 运行库
├── Assets/                     # 原生开屏资源与图标
├── Info/                       # 构建说明、依赖来源和校验信息
└── resources/
    ├── zh-Hans/
    ├── de-DE/
    └── ...                     # 语言专用 *.resources.dll
```

发布保持 `PublishSingleFile=false` 和自包含部署，不压缩、不生成额外启动器、不在首次运行时解包。纯托管卫星资源目录会归类到 `resources/{culture}/`；程序在 XAML 初始化前注册程序集解析器以读取这些目录。WinUI 原生 MUI/PRI 等需要 SDK 固定加载位置的文件不强制移动，确保兼容性。

`build-windows.cmd -Full` 会清理旧版 `resources/AsterPlay.exe` 和多余的启动器文件并重新生成目录。旧 `-Unpacked` 参数保留兼容。安装版未来只负责复制同一目录树并创建卸载和快捷方式。

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
