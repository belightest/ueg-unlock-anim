# 素材来源与授权说明（CREDITS）

本仓库**代码部分**（`host.ps1`、`src/*.cs`、`src/build.cmd`、`ueg/*.html`）为原创，
按根目录 `LICENSE`（Apache License 2.0）授权。
下列内容有第三方来源，**按其原始许可保留版权声明与许可文本**——
这不是礼节性署名，而是那些许可的生效条件。

## 550C 片头动画与 HTML 原稿 —— Voidpocket / Ziyang Song

本项目的 550C 视觉风格不是从零画起的，它衍生自：

- **dsh-550c-boot** — Full-Screen 550C Boot Splash Intro（DSH 插件）
  <https://github.com/yannicksong0106/dsh-550c-boot>
  **MIT License，Copyright (c) 2026 Ziyang Song**
  - 550C 片头动画与 HTML 原稿由 **Voidpocket**（[@Voidpocket](https://github.com/Voidpocket)）提供
  - 插件工程由 **Ziyang Song**（[@yannicksong0106](https://github.com/yannicksong0106)）完成

MIT 许可允许再分发与制作衍生作品，条件是保留上述版权声明与许可文本
（已一并收在根目录 `NOTICE` 中）。

| 本仓库文件 | 与上游的关系 |
|---|---|
| `ueg/assets/ueg-seal-white.png`<br>`ueg/assets/ueg-lockup-*.png` | 上游徽记/lockup 的再处理版（纯白图形 + 透明背景） |
| `ueg/assets/logo-550c.png` | 同上（终幕 550C 标志化用） |
| `ueg/assets/ueg-land.js` | 由上游徽记中的地球部分**离线烘培**出的点云数据 |
| `ueg/ueg.html` 终幕段落 | 视觉与节奏参照上游 550C 片头，代码为本项目独立编写 |

> 若你是上游作者且希望改用其他署名方式或许可条款，开个 issue 即可，这里按你的意思改。

## 登录授权阶段的交互思路 —— dsh-startup-screen

交互式授权分步推进、由语音时长驱动节奏、首次点击门等**思路**受下面项目启发：

- **dsh-startup-screen** <https://github.com/6shenhonghong9/dsh-startup-screen>
  **MIT License，Copyright (c) 沈宏宏**

只借鉴思路，**未使用**其代码，也**未使用**其任何素材。

## 配音

`ueg/voice/voice-{male,female}-{1..5}.wav` 共 10 个文件，为 **TTS 生成**的 5 句旁白
（男声/女声两版），非任何影视作品原声。删除整个 `voice/` 目录后页面会静默跳过音频，
动画照常播放（`audio=off` 亦可）。

## 代码

- `host.ps1`：PowerShell 5.1，原生 cmdlet + 启动期 `Add-Type` 编译一段 Win32 P/Invoke，零第三方依赖。
- `src/UEGHost.cs`、`src/UEGUnlockConfig.cs`：C# 5，系统自带 `csc.exe` 编译，零第三方依赖。
- `ueg/unlock.html`、`ueg/ueg.html`：原生 HTML/CSS/JS + Canvas，无框架、无构建步骤。

## 第三方素材：不包含

本仓库不包含任何影视作品的画面、声音、音乐、配音、官方标识或字体文件。
"UEG""550C"等名称与术语仅用于描述界面风格，相关作品与名称的权利归其权利人所有。
本项目为非官方作品，与相关作品的权利人无任何关联，亦未获得其授权或背书。
