# AsterPlay 实现文档

> 本文档用于记录 AsterPlay 从当前基础框架继续推进到可稳定使用版本的实施路线。
>
> 基线来源：
> - 项目交接文档 `HANDOFF.md`
> - GitHub 仓库当前 `main` 实际代码
> - 当前基线提交：`087ddb11d836efc6d2d797d42d2772b2f5fa5c8d`

---

## 1. 当前阶段

AsterPlay 当前处于：

```text
基础框架完成，开始完善播放器和完整媒体浏览功能
```

当前已经具备：

- Emby 登录与会话恢复
- 媒体库获取
- Hero 首页
- 继续观看
- 最新媒体
- 收藏
- 基础详情窗口
- libmpv 集成
- 播放 / 暂停 / Seek / 音量
- 音轨循环切换
- 字幕循环切换
- 全屏
- 断点续播
- Emby 播放状态同步
- Windows x64 自包含构建
- 自动准备 .NET SDK
- 自动准备 libmpv
- 无边框主窗口
- Hero 响应式布局

当前尚未形成完整闭环的主要区域：

- 首页卡片操作
- 完整播放器控制 UI
- 可明确选择的音轨 / 字幕
- 完整电影详情
- 剧集季 / 集详情
- 完整媒体库浏览
- 搜索 / 筛选 / 排序
- DirectPlay / DirectStream / Transcode 能力协商
- 图片缓存
- Token 安全
- 弹幕系统
- 发布前稳定性与性能优化

---

## 2. 实施原则

后续继续遵循：

```text
功能可用 > 播放可靠 > Emby 状态同步 > UI 美化
```

开发时尽量保持：

1. 每一步都可以独立验证。
2. 每完成一步，应用都比上一版更可用。
3. 播放器核心未稳定前，不进行大规模 UI 重构。
4. 不再向旧 qEmby 仓库开发新功能。
5. 所有新功能直接基于 AsterPlay `main` 推进。
6. 大功能先解决数据和行为，再处理视觉效果。
7. 播放器已迁移到 libmpv Render API；后续弹幕直接使用 WPF Overlay，不再受 HwndHost Airspace 限制。

---

# 3. 实施步骤

## Step 0：建立真实播放基线

### 目标

先确认当前播放器和 Emby 状态同步在真实服务器上的实际表现。

### 主要内容

真实 Emby 环境逐项测试：

- 登录
- 自动恢复登录
- Hero 加载
- 继续观看加载
- 最新媒体加载
- 视频播放
- 断点续播
- 暂停 / 恢复
- 前进 / 后退
- Slider Seek
- 音量
- 音轨切换
- 字幕切换
- F11 全屏
- ESC 退出全屏
- 播放中进度上报
- 暂停状态上报
- 关闭播放器后的停止上报
- Emby 继续观看位置是否更新

### 主要涉及

```text
src/AsterPlay/Services/EmbyClient.cs
src/AsterPlay/Views/PlayerWindow.xaml
src/AsterPlay/Views/PlayerWindow.xaml.cs
src/AsterPlay/Services/Mpv/MpvRenderContext.cs
src/AsterPlay/Services/Mpv/MpvClient.cs
```

### 完成标准

- 当前已有功能能够在真实 Emby 服务端稳定复现。
- 已知播放问题可以明确归类到 UI、mpv、Emby API 或流媒体兼容性。
- 不在未知播放问题存在时直接进行播放器大重构。

---

## Step 1：首页卡片真正可操作

> 状态：已实现（2026-10-04）

### 目标

把当前主要用于展示的首页变成可直接使用的入口。

### 主要内容

#### 继续观看

点击卡片：

```text
继续观看 -> GetPlayableStreamAsync(item) -> PlayerWindow
```

直接播放当前具体电影或剧集 Episode。

#### 最新媒体

点击卡片：

```text
最新媒体 -> DetailsWindow
```

进入对应媒体详情。

#### 代码整理

建议统一抽出：

```csharp
PlayItemAsync(MediaCardViewModel item)
ShowDetails(MediaCardViewModel item)
```

不要让播放逻辑只绑定到 `CurrentHero`。

### 主要涉及

```text
src/AsterPlay/Views/HomeView.xaml
src/AsterPlay/Views/HomeView.xaml.cs
src/AsterPlay/ViewModels/HomeViewModel.cs
```

### 完成标准

首页三个主要入口均可操作：

- Hero：播放 / 收藏 / 详情
- 继续观看：点击直接播放
- 最新媒体：点击进入详情

---

## Step 2：播放器控制层第二版

> 状态：已实现（2026-10-04）

### 目标

从当前固定占据底部空间的第一版控制栏，升级为正常视频播放器使用方式。

### 主要内容

播放器布局改成：

```text
视频
└─ Overlay Controls
```

控制栏覆盖在视频底部，而不是单独占一个 Grid Row。

加入：

- 鼠标移动显示控制栏
- 鼠标静止后自动隐藏
- 底部半透明渐变层
- 全屏状态下相同逻辑
- 双击视频切换全屏
- 静音按钮
- 播放速度入口
- 控制栏隐藏时隐藏鼠标指针
- 鼠标重新移动后恢复

保留当前快捷键：

- Space
- Left
- Right
- Up
- Down
- F11
- ESC

### 主要涉及

```text
src/AsterPlay/Views/PlayerWindow.xaml
src/AsterPlay/Views/PlayerWindow.xaml.cs
```

### 完成标准

- 播放器控制栏不永久占据视频高度。
- 普通窗口和全屏模式均能自动显示 / 隐藏。
- 双击可以进入 / 退出全屏。
- 原有快捷键行为不回退。

### 实现说明

播放器已从 `wid + HwndHost` 迁移到 libmpv Render API。mpv 通过 OpenGL 渲染到 `OpenTK.GLWpfControl` 提供的 FBO，再由 OpenGL/DirectX interop 进入 WPF 视觉树。因此视频、控制栏和后续弹幕可以在同一个 WPF Grid 中正常叠加，鼠标输入也直接使用 WPF 事件。

已实现：

- 视频占满播放器客户区
- 底部渐变 Overlay 控制栏
- 鼠标移动显示控制栏
- 3 秒静止自动隐藏
- 控制栏隐藏时隐藏鼠标指针
- 双击视频切换全屏
- 普通窗口 / 全屏共用同一 WPF Overlay 控制层
- libmpv Render API + OpenTK.GLWpfControl
- 删除旧 HwndHost / wid / 独立控制窗口 / 鼠标轮询
- 静音按钮
- 0.5× / 0.75× / 1× / 1.25× / 1.5× / 2× 播放速度
- Space / Left / Right / Up / Down / M / F11 / ESC 快捷键

---

## Step 3：音轨 / 字幕从循环切换升级为明确选择

> 状态：已实现（2026-10-04）

### 目标

替换当前：

```text
cycle audio
cycle sub
```

这种用户不知道当前选择的方式。

### 主要内容

从 mpv 获取：

```text
track-list
aid
sid
```

建立播放器 Track 模型，例如：

```csharp
PlayerTrack
{
    Id
    Type
    Language
    Title
    Codec
    Selected
    External
}
```

UI 中加入：

### 音轨菜单

显示：

- 当前选中状态
- 语言
- 标题
- Codec
- Channel 信息（可获取时）

### 字幕菜单

显示：

- 关闭字幕
- 当前选中状态
- 语言
- 标题
- 内封 / 外挂信息

播放器控制栏同时显示当前音轨、当前字幕。

### 主要涉及

```text
src/AsterPlay/Services/Mpv/MpvClient.cs
src/AsterPlay/Services/Mpv/MpvRenderContext.cs
src/AsterPlay/Views/PlayerWindow.xaml
src/AsterPlay/Views/PlayerWindow.xaml.cs
```

### 完成标准

用户不再通过“反复点击直到猜中”为方式切换音轨和字幕。

### 实现说明

已实现：

- 基于 mpv `track-list/N/*` 读取音轨 / 字幕轨道
- 读取轨道 ID、语言、标题、Codec、外挂状态、默认 / 强制标记和声道信息
- 音轨按钮显示当前选择
- 字幕按钮显示当前选择或“关闭”
- 左键弹出明确轨道菜单
- 当前轨道勾选
- 字幕菜单提供“关闭字幕”
- 使用 `aid` / `sid` 直接选择目标轨道
- 切换后发送 `AudioTrackChange` / `SubtitleTrackChange` 播放进度事件
- 轨道菜单打开时暂停控制栏自动隐藏

---

## Step 4：扩充 Emby 详情 API 层

> 状态：已实现（2026-10-04）

### 目标

先补齐详情页所需数据能力，再重做详情 UI。

### 需要增加的 API

至少需要：

- Item 详细信息
- Series 信息
- Season 列表
- Episode 列表
- People / Cast
- Directors
- Studios
- Genres
- Tags
- MediaSources
- MediaStreams
- UserData

建议增加类似：

```csharp
GetItemAsync(string itemId)
GetSeasonsAsync(string seriesId)
GetEpisodesAsync(string seriesId, string seasonId)
```

必要时根据 Emby 实际返回继续拆分。

### Models

扩展 `EmbyModels.cs`，覆盖：

- People
- MediaSources
- MediaStreams
- VideoCodec
- AudioCodec
- Width / Height
- BitRate
- Channels
- Language
- DisplayTitle
- IndexNumber
- ParentIndexNumber

### ViewModel

不要继续让完整详情页只依赖：

```text
MediaCardViewModel
```

建议新增：

```text
DetailsViewModel
SeasonViewModel
EpisodeViewModel
MediaStreamViewModel
```

### 完成标准

给定一个 ItemId，可以取得构建电影或剧集完整详情页所需的数据。

### 实现说明

已实现：

- 单 Item 使用官方 `GET /Users/{UserId}/Items/{Id}` 获取完整 BaseItemDto
- Series Seasons 使用官方 `GET /Shows/{Id}/Seasons`
- Season Episodes 使用官方 `GET /Shows/{Id}/Episodes?SeasonId=...`
- Season / Episode 列表请求官方支持的扩展 Fields
- 扩展 People / Studios / Tags / Taglines / ProviderIds
- 扩展 MediaSources / MediaStreams
- 扩展视频 Codec、音频 Codec、容器、分辨率、码率、声道、HDR 相关色彩字段
- 扩展 SeriesId / SeasonId / SeriesName / SeasonName
- 扩展 Played / PlayCount / LastPlayedDate
- 新增人物图片 URL 构建
- 新增 `DetailsViewModel`
- 新增 `SeasonViewModel`
- 新增 `EpisodeViewModel`
- 新增 `MediaSourceViewModel`
- 新增 `MediaStreamViewModel`

Windows x64 self-contained publish 已通过。真实服务器字段展示将在 Step 5/6 页面接入时验证。

---

## Step 5：完整电影详情页

> 状态：已实现，待真实服务器验收（2026-10-04）

### 目标

让电影详情页成为真正的操作页面。

### 内容

详情页至少加入：

- 背景图
- Poster
- 标题
- 年份
- 社区评分
- 时长
- 类型
- 简介
- 播放 / 继续播放
- 收藏
- 演员
- 导演
- 标签
- 媒体源
- 视频 Codec
- 分辨率
- HDR 信息（可获取时）
- 音频轨道
- 字幕轨道

### 播放按钮

如果存在：

```text
PlaybackPositionTicks > 0
```

按钮文案建议区分：

```text
继续播放
从头播放
```

### 完成标准

用户可以在电影详情页完成：

```text
了解影片 -> 查看媒体信息 -> 收藏 -> 播放
```

### 实现说明

已实现：

- 打开详情页时重新请求完整 Emby Item，不依赖首页卡片裁剪数据
- 背景图 / Poster / 标题 / 原标题 / Tagline
- 年份 / 社区评分 / 时长 / 分级 / 类型
- 完整简介
- 演员横向列表及人物图片
- 导演 / 制作公司
- Genres / Tags / ProviderIds
- MediaSources
- Video / Audio / Subtitle 媒体流摘要
- 视频 Codec / 分辨率 / HDR/色彩字段（服务器提供时）
- 音频语言 / Codec / 声道
- 字幕语言 / Codec / 外挂 / 默认 / 强制状态
- 收藏 / 取消收藏，并重新拉取 Item 确认状态
- 无进度时显示“播放”
- 有进度时显示“继续播放”以及独立“从头播放”
- `GetPlayableStreamAsync(..., restart: true)` 明确以 0 ticks 启动，不影响首页默认续播行为
- Series 暂保留通用详情展示，季 / 集浏览进入 Step 6

Windows x64 self-contained publish 已通过。Step 5 需使用真实电影数据验收字段完整性、图片和操作。

---

## Step 6：剧集详情、季和集

> 状态：已实现，待真实服务器验收（2026-10-04）

### 目标

建立完整的 Series -> Season -> Episode 浏览与播放链路。

### 内容

剧集详情：

- Series 海报 / Backdrop
- 标题
- 简介
- 收藏
- Season 切换
- Episode 列表

每个 Episode 显示：

- SxxExx
- Episode 标题
- 缩略图
- 简介
- 时长
- 播放进度
- 已播放状态
- 继续播放入口

### 播放

点击 Episode 后直接传具体 Episode 给：

```text
GetPlayableStreamAsync()
```

不要再经过 Series 的 NextUp 推断。

### 完成标准

用户可以：

```text
剧集 -> 选择季 -> 选择集 -> 播放
```

### 实现说明

已实现：

- Series 详情页直接调用 `GetSeasonsAsync(seriesId)`
- Season 下拉切换
- 默认优先选择第一个正常季度，避免 Specials/Season 0 抢占默认选择
- 切换季度后调用 `GetEpisodesAsync(seriesId, seasonId)`
- Episode 列表显示 SxxExx、标题、缩略图、简介和时长
- Episode 显示播放进度、已播放状态
- 有续播位置时按钮显示“继续播放”
- 已播放 Episode 进度显示为 100%
- Series 顶部隐藏旧的通用播放/从头播放按钮，避免走 Series -> NextUp 推断
- Episode 播放直接把具体 Episode Item 传给 `GetPlayableStreamAsync()`
- 保留 Series 收藏、海报、Backdrop、简介、演员、标签等通用详情
- Windows x64 self-contained publish 已通过

真实服务器验收重点：Season 顺序、Specials、Episode 编号、缩略图、播放进度和具体 Episode 播放。

---

## Step 7：完整媒体库浏览

> 状态：核心功能已实现，待真实服务器验收（2026-10-04）

### 目标

让用户不再依赖 Hero、继续观看和最新媒体寻找内容。

### 建议新增

```text
Views/LibraryView.xaml
Views/LibraryView.xaml.cs
ViewModels/LibraryViewModel.cs
```

### 内容

第一阶段：

- 电影库
- 剧集库
- Poster Grid
- 分页或增量加载
- 点击进入详情

第二阶段：

- 搜索
- 类型筛选
- 年份筛选
- 排序

排序至少考虑：

- 名称
- 添加时间
- 上映年份
- 社区评分
- 最近播放

第三阶段：

- 收藏页面
- 播放历史页面

### 性能注意

媒体库数量较大时不要一次性请求全部高分辨率图片。

### 完成标准

用户可以浏览服务器完整电影 / 剧集资源，并能搜索、筛选、排序。

### 实现说明

已实现：

- 新增独立 `LibraryView.xaml / LibraryView.xaml.cs`
- 新增 `LibraryViewModel`
- 主窗口标题栏新增“首页 / 媒体库”导航
- 支持“全部媒体库”以及单个 Emby 媒体库选择
- 支持 Movie / Series / Movie+Series 类型筛选
- 支持关键词搜索（Enter 或“应用筛选”）
- 支持精确年份筛选
- 支持“仅收藏”筛选
- 支持名称、添加时间、上映年份、社区评分、最近播放排序
- 每页 40 项，使用服务端 `StartIndex / Limit` 分页
- Poster Grid 使用受限宽度图片，不一次请求全部原图
- 点击 Poster 直接进入现有电影 / 剧集详情页
- Windows x64 self-contained publish 已通过

未伪装实现：

- 独立“完整播放历史”页面暂未加入。官方 Items 查询的 `IsPlayed` 仅表示已看完，`IsResumable` 仅表示可续播，不能准确代表完整历史；后续若实现将基于可靠的历史语义单独处理。

---

## Step 8：完整播放能力协商

> 状态：已实现，待真实媒体组合验收（2026-10-04）

### 目标

替换当前较简单的：

```text
PlaybackInfo -> 第一 MediaSource -> DirectStream / static stream
```

播放策略。

### 需要设计

Emby：

- PlaybackInfo
- DeviceProfile
- MediaSource
- MediaStream
- DirectPlay
- DirectStream
- TranscodingUrl

播放策略建议顺序：

```text
DirectPlay
    ↓ 不支持
DirectStream
    ↓ 不支持
Transcode
```

### 需要考虑

- H.264
- HEVC / H.265
- AV1
- HDR
- Dolby Vision
- AAC
- AC3
- EAC3
- TrueHD
- DTS
- DTS-HD
- 字幕格式
- 非兼容字幕
- 码率限制
- 容器兼容性

### 建议架构

当逻辑明显增大后，考虑从 `EmbyClient` 中拆出：

```text
PlaybackService
PlaybackProfile
PlaybackDecision
```

### 完成标准

常见视频 / 音频 / 字幕组合能够正确选择 DirectPlay、DirectStream 或 Transcode，而不是强行直放。

### 实现说明

已实现：

- 新增独立 `PlaybackProfile`
- 新增独立 `PlaybackDecision / PlaybackDecisionSelector`
- PlaybackInfo 请求正式携带 Emby DeviceProfile
- DeviceProfile 显式声明常见 mpv DirectPlay 容器与 Codec：
  - MKV / WebM
  - MP4 / M4V / MOV
  - MPEG-TS / M2TS
  - AVI
  - MPEG / VOB
  - OGG / OGV
  - H.264 / HEVC(H.265) / AV1 / VP9 / VP8 / MPEG-2 / MPEG-4 / VC-1
  - AAC / AC3 / EAC3 / TrueHD / DTS(DCA) / MP2 / MP3 / Opus / Vorbis / FLAC / ALAC / PCM
- 转码 fallback 使用 HLS + TS + H.264 + AAC/AC3
- 转码音频最多声明 8 声道，实际下混由 mpv 处理
- 字幕声明 Embed，不虚报 AsterPlay 尚未实现的 External sidecar DeliveryUrl 能力
- 不人为设置 MaxStreamingBitrate；在没有用户带宽设置前，不因客户端自设上限触发不必要转码
- PlaybackInfo 返回后不再固定使用第一 MediaSource
- 非续播按服务器结果选择：
  `DirectPlay -> DirectStream -> Transcode`
- DirectStream 优先使用服务器返回的 `DirectStreamUrl`
- Transcode 使用服务器返回的 `TranscodingUrl`
- 保留旧服务器 Supports* 标志不完整时的兼容 fallback
- 播放决策写入 `PlaybackDecision` 日志
- MediaSource 日志记录 DirectStream / Transcode 元数据并继续脱敏 token
- 续播仍保持已验证架构：
  `Fresh UserData -> PlaybackInfo(StartTimeTicks) -> server-offset stream -> mpv local timeline 0 -> absolute Emby timeline`
- 续播不会退回 mpv 本地初始 seek
- Windows x64 self-contained publish 已通过

已明确限制：

- 官方 Emby 4.10.1 DeviceProfile 的 ProfileConditionValue 没有 `VideoRangeType`，因此不使用 Jellyfin 扩展字段强制区分 Dolby Vision。
- HDR / Dolby Vision 当前依赖 mpv/FFmpeg 实际解码能力；如果后续需要“DOVI 一律服务端转码”，应单独做可配置策略。
- External sidecar 字幕尚未通过 `DeliveryUrl -> mpv sub-add` 接入，因此 profile 不声明 External 字幕能力。

真实服务器验收建议覆盖：H.264/AAC、HEVC/EAC3、AV1、TrueHD/DTS、多字幕、需要转码的异常组合，以及续播。

---

## Step 9：播放器状态与诊断信息

> 状态：已实现，待运行时验收（2026-10-04）

### 目标

方便开发和后续用户诊断播放问题。

### 从 mpv 获取

可以逐步加入：

- 视频分辨率
- 视频 Codec
- 音频 Codec
- 当前 FPS
- 当前码率
- Cache 状态
- Buffer 状态
- hwdec-current
- 视频输出
- 当前 aid
- 当前 sid

### UI

提供一个简洁的“播放信息”面板，不需要默认长期显示。

### 完成标准

遇到播放异常时，不需要只靠猜测即可确认：

- 是否硬件解码
- 当前 Codec
- 当前分辨率
- 当前音轨
- 当前字幕
- 是否正在缓冲

### 实现说明

已实现：

- 播放控制栏新增“信息”按钮
- 支持 `I` 键快速显示 / 隐藏诊断面板
- 诊断面板默认隐藏，不参与鼠标命中，不影响播放器输入
- 面板每秒刷新一次，不额外创建高频诊断线程
- 展示 Emby 协商结果：
  - DirectPlay / DirectStream / Transcode
  - negotiated container / protocol
  - 服务器播放决策原因
  - MediaSourceId
  - 原始源容器
  - 原始视频 Codec / 音频 Codec
  - 原始分辨率
  - 是否使用服务器续播偏移
- 展示 mpv 实际播放状态：
  - 实际视频 Codec
  - 实际音频 Codec
  - 实际视频分辨率
  - estimated-vf-fps / container-fps
  - 视频 / 音频实际码率（mpv packet 级估算）
  - `hwdec-current`
  - `current-vo`
  - 当前 aid / sid
  - 当前选中音轨 / 字幕名称
  - `demuxer-cache-duration`
  - `cache-buffering-state`
  - `paused-for-cache`
- 硬解状态直接显示为“软件解码”或具体硬解后端，例如 `d3d11va-copy`
- Step 8 的播放协商结果现在可直接从 UI 验收，不必只看 playback.log
- 打开诊断面板时会写入 `PlayerDiagnostics` 日志
- Windows x64 self-contained publish 已通过

运行时验收重点：分别播放 DirectPlay / DirectStream / Transcode 内容，确认 UI 与 playback.log 中的 PlaybackDecision 一致，并确认 hwdec、Codec、音轨、字幕、缓冲状态实时变化。

---

## Step 10：图片缓存与加载体系

### 目标

解决大型媒体库中的重复图片请求和滚动性能问题。

### 建议新增

```text
Services/ImageCacheService.cs
```

至少支持：

- Memory Cache
- Disk Cache
- 请求超时
- 加载占位图
- 失败占位图
- Lazy Loading
- 缓存清理
- 同一 URL 请求合并

### Disk Cache

建议存储于：

```text
%LOCALAPPDATA%\AsterPlay\cache\images
```

### 完成标准

多次进入同一页面时，不会重复下载大量相同 Emby 图片。

---

## Step 11：Session Token 安全

### 目标

取消 `session.json` 中的 AccessToken 明文存储。

### 实现

普通 JSON 保留：

- ServerUrl
- UserId
- DeviceId
- UserName

AccessToken 使用 Windows DPAPI：

```csharp
ProtectedData.Protect()
ProtectedData.Unprotect()
```

### 兼容旧数据

需要支持一次性迁移：

```text
发现旧明文 AccessToken
        ↓
正常读取
        ↓
DPAPI 加密
        ↓
覆盖 session.json
```

### 完成标准

磁盘上的 `session.json` 不再出现可直接读取的 AccessToken 明文。

---

## Step 12：弹幕渲染技术 PoC

### 目标

验证基于当前 Render API 架构的 WPF 弹幕 Overlay 在真实播放中的性能和同步表现。

### 当前技术基础

播放器已经使用：

```text
libmpv Render API
        ↓
OpenGL FBO
        ↓
OpenTK.GLWpfControl / DirectX interop
        ↓
WPF Visual Tree
```

视频不再由 `HwndHost` 原生子窗口覆盖，因此普通 WPF `Canvas / Grid / UserControl` 可以直接叠加在视频上方。

### PoC 需要验证

- 普通窗口
- 最大化
- 全屏
- Resize
- DPI
- 多显示器
- Alt+Tab
- Overlay 与视频时间轴同步
- 大量弹幕时的 WPF 渲染性能
- 鼠标穿透模式

### 完成标准

可以在视频 WPF Surface 上方持续稳定渲染测试文字，不出现明显错位、闪烁或播放卡顿。

---

## Step 13：完整弹幕系统

### 前提

只有 Step 12 的渲染方案稳定后才开始。

### 建议模块

```text
DanmakuService
DanmakuRenderer
DanmakuOverlay
DanmakuSettings
```

后续可继续拆：

```text
DanmakuParser
DanmakuTimeline
DanmakuScheduler
DanmakuFilter
```

### 功能

基础：

- 弹幕加载
- 时间轴同步
- 滚动弹幕
- 顶部弹幕
- 底部弹幕
- 字号
- 速度
- 透明度
- 开关

后续：

- 屏蔽词
- 用户屏蔽
- 密度控制
- 防重叠
- 性能限制
- 高 DPI

### 完成标准

弹幕与 mpv 播放时间同步，并且不会明显影响视频播放性能。

---

## Step 14：发布前收尾

### libmpv

当前开发阶段使用 mpv git-release。

正式发布前：

- 选择经过真实测试的固定版本
- 构建脚本固定版本
- 不再每次自动拉取最新 development build

### 稳定性

补充：

- 网络异常提示
- Emby 请求超时
- 登录失效
- Token 失效
- 媒体不存在
- PlaybackInfo 失败
- mpv 初始化失败
- 视频源失效
- 转码失败

### UI

播放器和功能链路稳定后再统一处理：

- Hero 缩略图限制到 5～6 个
- Hero 缩略图 Overlay
- VisualStateManager
- Hover 动画
- Hover 信息
- 右键菜单
- 键盘导航
- 焦点状态
- 空状态页面
- 加载 Skeleton

### 性能

检查：

- 首页首次加载
- 大媒体库滚动
- 图片内存占用
- 页面切换
- 长时间播放
- 播放状态上报
- Overlay 性能
- 应用关闭资源释放

### 发布验证

最终验证：

```text
git clone
bootstrap-dotnet.cmd
build-windows.cmd
dist\AsterPlay\AsterPlay.exe
```

确保干净 Windows x64 环境中：

- 不需要用户安装 .NET
- mpv 依赖完整
- 应用能启动
- 登录正常
- 播放正常
- 状态同步正常

---

# 4. 推荐连续开发主线

当前最建议连续完成：

```text
Step 0 真实播放验证
    ↓
Step 1 首页卡片操作
    ↓
Step 2 播放器控制层
    ↓
Step 3 音轨 / 字幕选择
    ↓
Step 4 Emby 详情 API
    ↓
Step 5 电影详情
    ↓
Step 6 剧集季 / 集
    ↓
Step 7 完整媒体库 / 搜索
```

完成这一段后，AsterPlay 才算真正形成一个完整 Emby 客户端的基础使用闭环。

之后继续：

```text
Step 8 播放能力协商
    ↓
Step 9 播放诊断
    ↓
Step 10 图片缓存
    ↓
Step 11 Token 安全
    ↓
Step 12 弹幕 PoC
    ↓
Step 13 完整弹幕
    ↓
Step 14 发布收尾
```

---

# 5. 建议 Git 提交粒度

不要把多个大阶段堆在一次提交中。

建议按功能拆，例如：

```text
feat(home): make resume cards playable
feat(home): open details from latest cards

feat(player): add overlay controls
feat(player): auto-hide controls
feat(player): add double-click fullscreen
feat(player): add mute and playback speed

feat(player): expose mpv track list
feat(player): add audio track picker
feat(player): add subtitle picker

feat(emby): add item details API
feat(emby): add seasons and episodes API
feat(details): add movie details
feat(details): add series seasons and episodes

feat(library): add media library browser
feat(search): add media search
feat(library): add filters and sorting

feat(playback): add device profile
feat(playback): support transcoding decisions

feat(cache): add image cache service
fix(security): protect access token with DPAPI

feat(danmaku): add overlay proof of concept
feat(danmaku): add timeline renderer
```

每个提交都应尽量满足：

- 可以单独构建
- 不留下明显半成品编译错误
- 有明确行为变化
- 易于回退

---

# 6. 当前最近目标

Step 1 已完成：

- 继续观看点击直接播放。
- 最新媒体点击进入详情。
- 已抽出统一 Item 播放入口。
- Hero 播放 / 详情入口继续复用统一逻辑。

Step 2 已完成。
Step 3 已完成。
Step 4 已完成。
Step 5 已实现，待真实服务器验收。
Step 6 已实现，待真实服务器验收。
Step 7 核心功能已实现，待真实服务器验收。
Step 8 已实现，待真实媒体组合验收。
Step 9 已实现，待运行时验收。

当前下一项实际开发任务：

```text
Step 10：图片缓存
```
