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

> 状态：已实现并验收通过（2026-10-04）

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

> 状态：已实现，待运行时验收（2026-10-04）

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

### 实现说明

已实现：

- 新增 `Services/ImageCacheService.cs`
- 新增 `Controls/CachedImage.cs`
- Memory Cache 使用弱引用，避免长期固定占用大量图片内存
- Disk Cache 存储于：
  `%LOCALAPPDATA%\AsterPlay\cache\images`
- 图片请求超时 12 秒
- 单图最大缓存 32 MB
- 同一 URL 并发请求合并，只执行一次实际下载
- 最多 6 个图片下载并发
- URL 缓存键会移除 `api_key / X-Emby-Token`，token 刷新不会导致同图生成重复缓存
- 加载中占位图
- 加载失败占位图
- `CachedImage` 仅进入最近 ScrollViewer 可视区域后才开始加载
- 控件卸载或 URL 变化时取消当前 UI 等待，不影响其它共享请求
- 磁盘缓存自动清理：
  - 最长保留 60 天
  - 总容量最多约 512 MB
  - 优先保留最近使用图片
- 首页 Hero / 缩略图 / 海报全部接入缓存
- 媒体库 Poster Grid 接入缓存
- 详情页 Backdrop / Poster / Episode 缩略图 / 演员图片接入缓存
- Windows x64 self-contained publish 已通过

运行时验收重点：

1. 第一次进入页面允许产生图片下载。
2. 返回同一页面后应大量出现磁盘/内存命中，而不是重新下载。
3. 媒体库滚动时屏幕外海报不应提前全部请求。
4. 断网或单图失败时不应拖死页面，应显示失败占位。
5. 缓存目录长期使用后不会无限增长。

---

## Step 11：Session Token 安全

> 状态：已实现并验收通过（2026-10-04）

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

### 实现说明

已实现：

- 新增 Windows DPAPI 依赖 `System.Security.Cryptography.ProtectedData`
- 运行时 `EmbySession` 不再直接序列化到磁盘
- 新磁盘格式只保存：
  - `FormatVersion`
  - `ServerUrl`
  - `UserId`
  - `DeviceId`
  - `UserName`
  - `AccessTokenProtected`
- AccessToken 使用：
  `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)`
- 加密结果以 Base64 写入 `AccessTokenProtected`
- Load 时使用 `ProtectedData.Unprotect` 恢复运行时 token
- 加入固定应用 entropy：`AsterPlay.Session.v1`
- 写入 `session.json` 使用临时文件 + 原子替换，减少写一半导致损坏的风险
- 支持旧版明文 `AccessToken` 自动迁移：
  1. 正常读取旧 session
  2. 恢复 token
  3. 立即重新 DPAPI 加密
  4. 原子覆盖旧 `session.json`
- DPAPI 解密失败时不会把密文当明文继续使用
- Windows x64 self-contained publish 已通过
- Windows CI 运行时 smoke test 已通过：
  - 新保存的 `session.json` 不包含明文 token
  - 新加密 session 可以正常 Load
  - 旧明文 session 可以正常 Load
  - 旧明文 session Load 后会自动改写成 `AccessTokenProtected`

本机验收重点：

1. 用已有旧版本 session 启动新版 AsterPlay，不应要求重新登录。
2. 启动后检查 `%LOCALAPPDATA%\AsterPlay\session.json`，不应再存在 `"AccessToken": "明文..."`。
3. 应看到 `AccessTokenProtected`，其值为不可直接读取的 Base64 密文。
4. 关闭并重新启动 AsterPlay，自动登录仍应正常。
5. 登出后 `session.json` 应继续被删除。

---

## Step 12：弹幕渲染技术 PoC

> 状态：已实现并完成真实播放验收（2026-10-04）

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

### 实现说明

已实现：

- PoC 阶段新增 `Controls/DanmakuPocOverlay.cs`；进入 Step 13 后已演进为正式 `Controls/DanmakuOverlay.cs`
- Overlay 直接位于视频 WPF Visual Tree 上方，不使用额外 HWND
- Overlay `IsHitTestVisible=false`，验证鼠标穿透路径
- 播放器控制栏新增“弹幕 PoC”按钮
- 支持 `D` 键快速开关 PoC
- 使用单一 `CompositionTarget.Rendering` 渲染循环，不为每条测试弹幕创建 Timer / WPF Animation
- 测试弹幕由播放时间轴确定位置，暂停时冻结，倍速时按媒体时间加速
- 每 250ms 使用 mpv `time-pos` 重新校时；Seek 期间隐藏弹幕，待目标时间附近恢复连续播放后再重新显示，避免校时抖动
- 使用确定性的时间槽生成约 50 条并发测试文字，用于持续压力观察
- Resize / 最大化 / 全屏时直接根据当前 WPF Surface 尺寸重新计算轨道与位置
- 每帧读取当前 DPI；DPI 改变时清理 FormattedText 缓存并按新 `PixelsPerDip` 重建
- Alt+Tab 或 WPF Composition 暂停后，下一次 mpv 校时不会累积长期时间漂移
- 播放信息面板新增“弹幕 PoC”诊断行，显示：
  - 当前活动弹幕数
  - 最近一帧 Overlay 绘制耗时
  - 峰值 Overlay 绘制耗时
  - Overlay 当前尺寸
  - 当前 DPI
- `playback.log` 每 5 秒记录一次 PoC 指标，便于运行时比对卡顿
- Windows x64 self-contained publish 已通过临时 CI 验证
- 临时 Step 12 CI 已在验证后删除

真实播放验收已通过：

1. 普通窗口持续播放正常，测试文字无明显闪烁、错位或播放卡顿。
2. Resize、最大化、F11 全屏 / 退出全屏时弹幕轨道可正常适配。
3. 暂停 / 恢复、10 秒 Seek、Slider Seek、0.5× / 2× 倍速均可正常工作。
4. Seek 期间弹幕会暂时隐藏；待目标时间附近恢复连续播放后重新显示，不再暴露中间校时抖动。
5. DPI / 多显示器、Alt+Tab 场景未出现长期漂移或明显跳变。
6. Overlay 不截获鼠标，控制栏与双击全屏交互正常。
7. 播放信息与 playback.log 中的 PoC 指标未发现对视频播放有明显影响。

Step 12 验收完成，允许进入 Step 13。

---

## Step 13：完整弹幕系统

> 状态：已完成并通过真实运行验收（2026-10-04）

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

### 当前实现进度

第一阶段已完成：

- 新增正式弹幕数据模型：
  - `DanmakuComment`
  - `DanmakuMode`
  - `DanmakuDocument`
  - `DanmakuContext`
  - `DanmakuSettings`
- 新增 `IDanmakuSource` 数据源抽象，真实弹幕来源可以独立接入，不与播放器或渲染器耦合。
- 新增 `DanmakuService`，当前生产数据源只保留 `LogVarDanmakuSource`。
- 已移除：
  - 弹弹play数据源及 AppId / AppSecret 配置。
  - 内置测试 / 本地弹幕源。
- 播放器“LogVar”配置窗口只保留：
  - 服务器地址，例如 `http://192.168.1.10:9321`
  - 可选 Access Token
- 配置方式参考 qEmby 的 LogVar / `danmu_api` 逻辑：
  - 服务器基础地址与 Access Token 分开保存。
  - 客户端负责把 Token 作为服务器路径段插入，再追加 `/api/v2/...`。
  - 用户不需要手写具体 API 路径。
- Access Token 使用 Windows DPAPI（CurrentUser）加密持久化。
- LogVar 配置保存于：
  `%LOCALAPPDATA%\AsterPlay\danmaku-source.json`
- Emby 会把标题、原始标题、剧集名、季 / 集、ItemType、媒体 Path 与真实文件名传入弹幕匹配上下文。
- 自动匹配流程参考 qEmby：
  1. 优先使用真实媒体文件名调用 `POST /api/v2/match`。
  2. 如果文件名匹配失败或置信度不足，则使用剧名 / 季 / 集调用 `GET /api/v2/search/episodes`。
  3. 对候选进行标题、季、集评分，剧集阈值 72，电影阈值 62。
  4. 选中 `episodeId` 后调用 `GET /api/v2/comment/{episodeId}?format=json&duration=true`。
- LogVar 弹幕解析兼容：
  - `p`：时间、模式、颜色。
  - `m` / `text` / `content`：弹幕正文。
  - `t` / `time`：没有 `p` 时的时间字段。
  - 模式 1 / 4 / 5 映射为滚动 / 底部 / 顶部。
- PoC Overlay 已升级为正式 `DanmakuOverlay`，旧 `DanmakuPocOverlay.cs` 已移除。
- 正式 Overlay 支持：
  - 滚动弹幕
  - 顶部固定弹幕
  - 底部固定弹幕
  - 字号
  - 滚动速度
  - 固定弹幕持续时间
  - 透明度
  - 屏幕高度占比
  - 开关
- 时间轴使用有序弹幕列表 + 二分范围查询，不再每帧扫描完整弹幕集合。
- 保留 Step 12 已验收的 Seek 行为：
  - Seek 开始立即隐藏弹幕
  - 跳转期间不暴露中间校时
  - 目标附近恢复连续播放后重新同步并显示
- 播放信息面板现在显示正式弹幕源、已加载数量、活动数量以及渲染耗时。
- `D` 快捷键和播放器“弹幕”按钮继续作为开关。
- 新增播放器“弹幕设置”菜单，可即时调整：
  - 字号：18 / 22 / 28 / 34
  - 速度：0.75× / 1.0× / 1.25× / 1.5×
  - 透明度：50% / 70% / 85% / 100%
  - 显示区域：50% / 72% / 100%
  - 一键恢复默认设置
- 弹幕设置持久化到：
  `%LOCALAPPDATA%\AsterPlay\danmaku-settings.json`
- 设置在播放器启动时自动读取，修改后立即应用并保存。
- 播放信息面板会同时显示当前字号、速度和透明度。
- 新增播放器“匹配”按钮和两级 LogVar 手动匹配窗口：
  1. 先搜索并选择 LogVar 中的整部剧 / 季度条目。
  2. 再从该剧集下面选择当前 Emby 视频对应的集数。
- 剧集搜索调用 `GET /api/v2/search/episodes`，不限制单集，保留每个 anime 下的完整 episode 列表。
- 左侧显示剧集 / 季度候选，右侧显示所选剧集的全部集数；当前 Emby 集数会自动定位到对应 LogVar 集。
- 手动绑定粒度改为“LogVar 服务器 + Emby SeriesId + Season”，不再按单个 Emby Item 保存。
- 保存剧集绑定时同时记录当前集的集数偏移：
  - 例如 Emby E01 对应 LogVar E13，则保存偏移 +12。
  - 同一季后续 Emby E02 会自动映射到 LogVar E14。
- 手动剧集绑定保存于：
  `%LOCALAPPDATA%\AsterPlay\danmaku-series-overrides.json`
- 已保存的剧集绑定优先于自动匹配；如果绑定的剧集无法解析当前集，会回退到自动匹配。
- “恢复自动匹配”会清除当前 Emby Series + Season 的剧集绑定。
- 新增弹幕过滤与性能控制：
  - LogVar 解析会保存发送者标识（`p` 第 4 段或 `sender / user / userId / uid`）。
  - “弹幕设置 -> 过滤设置”支持屏蔽词和发送者屏蔽，每行一项。
  - 屏蔽词使用不区分大小写的包含匹配。
  - 发送者使用不区分大小写的精确匹配。
  - 过滤列表随 `danmaku-settings.json` 持久化，单类最多保存 200 项。
  - 密度支持低 35% / 中 65% / 高 100%，使用稳定哈希采样，同一条弹幕不会随帧随机闪现。
  - 同时显示上限支持 40 / 80 / 140，避免弹幕峰值把 WPF Overlay 压满。
  - 防重叠默认开启。
- 防重叠轨道调度不再仅按 CommentId 固定散列：
  - 滚动弹幕会根据前一条弹幕宽度、进入时间和两条弹幕速度判断是否会追尾。
  - 顶部 / 底部固定弹幕按轨道占用时间分配。
  - 没有安全轨道时直接跳过该条，不在画面上强行叠字。
  - Resize / DPI / 字号 / 速度 / 显示区域变化时重新计算轨道计划。
- 弹幕诊断增加：
  - 原始载入数量
  - 过滤 / 密度后的可见数量
  - 当前活动数量
  - 防重叠调度丢弃数量
  - 当前密度和同时显示上限
- FormattedText 缓存超过 1024 项时定期清理，配合活动数量上限控制长时间播放的 Overlay 内存与单帧压力。
- LogVar 配置窗口新增“测试连接”：
  - 使用当前服务器地址与 Access Token 直接调用标准 `POST /api/v2/match` 链路。
  - 15 秒超时。
  - 可区分无 Token 成功、Token 成功以及 HTTP / 网络失败。
  - 不需要保存配置即可先验证 Token 路径是否可用。
- 超大弹幕文档增加最后一道保护：
  - 单个视频最多保留 200,000 条有效弹幕进入 Overlay 调度。
  - 超过上限时按整个时间轴均匀采样，不只保留开头部分。
  - 播放信息 / playback.log 显示原始数量、保留数量与文档保护丢弃数量。
  - 该保护只针对异常超大文档，正常弹幕量不受影响。
- Windows x64 self-contained publish 已通过。

真实运行验收：

- 已使用实际 LogVar 服务器地址成功加载并显示真实弹幕。
- 已确认 LogVar 配置、自动匹配、episodeId 获取、comment 下载、解析、渲染这一基础链路可工作。
- 第一版“按单集绑定”的手动匹配已完成真实运行测试，但已根据实际体验改为“先匹配整部剧，再选择当前集”的两级逻辑。
- 剧集级手动匹配已完成真实运行验收：
  - 剧集 / 季度候选可正常显示。
  - 所选剧集的集数列表可正常显示。
  - 当前 Emby 集数可正确定位到 LogVar 集。
  - 同一季后续集可自动沿用剧集绑定。
  - 集数偏移可继续映射后续集。
  - 手动绑定可持久化。
  - “恢复自动匹配”可正确清除当前季绑定。
- 过滤 / 密度 / 防重叠 / 性能限制已完成真实运行验收：
  - 屏蔽词可即时生效并持久化。
  - LogVar 提供发送者信息时，发送者屏蔽可正常生效。
  - 35% / 65% / 100% 密度切换稳定，无随机闪烁。
  - 高密度场景下滚动弹幕追尾 / 重叠明显减少。
  - 顶部 / 底部固定弹幕轨道分配正常。
  - 40 / 80 / 140 同时显示上限可正确限制峰值。
- LogVar Access Token 已完成真实运行验收：
  - “测试连接”可正确验证实际 Token。
  - 保存后自动匹配 / 手动匹配 / 弹幕加载均正常。
- 长时间 / 大弹幕量播放已完成真实运行验收：
  - 连续播放期间视频无明显卡顿。
  - Overlay 单帧 / 峰值耗时未发现持续异常。
  - active 数量受配置上限约束。
  - 大文档保护与诊断链路可用。

Step 13 最终验收完成，允许进入 Step 14。

### 完成标准

弹幕与 mpv 播放时间同步，并且不会明显影响视频播放性能。

---

## Step 14：发布前收尾

### 2026-10-04 实现进度

已完成代码侧发布收尾：

- libmpv 默认构建已从滚动 `mpv-player/mpv git-release` 改为固定 Windows x64 LGPL 包：
  - release: `2026-09-29-b4b5d69a44`
  - mpv commit: `b4b5d69a44e240e4a95c230bb7f018c381f0c5ae`
  - asset: `mpv-dev-lgpl-x86_64-20260929-git-b4b5d69a44.7z`
  - SHA-256: `8c80c506cf95f403d8a2b9d672d5f88d965885666f25c850f711a06b9510dfc8`
- `bootstrap-mpv.ps1` 会验证固定包 SHA-256；默认构建发现旧/非固定 runtime 时会替换，不再静默跟随最新 development build。
- `build-windows.ps1` 增加 self-contained 发布自检，要求 `AsterPlay.exe / libmpv-2.dll / coreclr.dll / hostfxr.dll / hostpolicy.dll` 全部存在，并输出 `BUILD-INFO.txt`、`RUNTIME-SOURCE.txt`。
- 新增 Windows release GitHub Actions：真实执行 `dotnet publish`、固定 mpv 下载/校验、发布目录验证并上传 `AsterPlay-win-x64` artifact。
- Emby 错误提示统一区分：网络不可达、请求超时、401/403 登录或 Token 失效、404 媒体不存在、5xx 服务端错误、PlaybackInfo 失败、转码地址缺失。
- 启动时恢复 session：仅明确 401/403 才清除本地 session；临时网络故障不再误删登录状态。
- mpv 的 `END_FILE` 错误已回传播放器 UI，可区分普通视频源失败与 Transcode 流失败，同时保留 `playback.log` 诊断路径。
- 首页 Hero 候选数量限制为 6，避免大屏侧边缩略图无限扩张。

仍需真实 Windows x64 人工验收后才能把 Step 14 标记为最终完成：

- 干净机器从 `git clone -> bootstrap-dotnet.cmd -> build-windows.cmd` 的完整路径。
- 实际启动、登录、DirectPlay / DirectStream / Transcode、续播/拖动、状态同步、弹幕长时间播放。
- UI 的 VisualState / hover / 焦点 / 空状态 / Skeleton 等纯视觉收尾，以及大媒体库滚动和长时间播放的人工性能观察。


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
Step 8 已实现，已完成 DirectStream / 硬解等实机验证。
Step 9 已实现，诊断 UI 已完成实机验证。
Step 10 已实现并完成运行时缓存效果验收。
Step 11 已实现并完成本机 session 迁移 / 自动登录验收。
Step 12 已实现并完成真实播放验收。
Step 13 已实现并完成真实运行验收。

当前下一项实际任务：

```text
Step 14：发布前收尾
```

优先从稳定性与发布验证开始，不回动已经完成真实验收的播放 / 弹幕主链。
