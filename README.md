# 550C / UEG 登录解锁动画

![License](https://img.shields.io/badge/License-Apache--2.0-blue) ![Platform](https://img.shields.io/badge/platform-Windows_10%2F11-0078D6) ![Dependencies](https://img.shields.io/badge/dependencies-0-brightgreen) ![Privacy](https://img.shields.io/badge/privacy-100%25_local-brightgreen) ![AI](https://img.shields.io/badge/AI-assisted-blueviolet) [![Release](https://img.shields.io/github/v/release/belightest/ueg-unlock-anim?label=release&color=blue)](https://github.com/belightest/ueg-unlock-anim/releases/latest)

**[⬇ 下载 exe 版（免编译，开箱即用）](https://github.com/belightest/ueg-unlock-anim/releases/latest)**

开机登录后全屏播放一段 UEG 授权动画：终端扫描 → 550C 身份核验 → 550C 标志化终幕，
收尾以一滴透明水波纹荡开、整窗渐隐，实时桌面从窗口后面透出来。
动画播完进程立即退出，**不留后台、不占内存**。

```
欢迎界面 ──┐
           ├─ Edge 冷启动与桌面加载并行（窗口全程置顶）
桌面加载 ──┘
                    ↓ 桌面"看得见"且"安顿好"才放行
        ┌───────────┴───────────┐
        │  终端第 0 帧（预载待命） │
        └───────────┬───────────┘
                    ↓ postMessage 起拍，零加载等待
              三幕动画 25s
                    ↓ ueg:done
        水波纹扩散 + 整窗 alpha 渐隐 1.5s
                    ↓
              露出完整桌面
```

## 特性

- **原生宿主**：`UEGHost.exe`（C# / winexe），没有 PowerShell 冷启动、没有运行时编译、没有控制台窗口闪现
- **时序是核心**：Edge 提前起（冷启动与桌面加载重叠），但动画必须等桌面真正可见、且壁纸/图标/任务栏安顿好才开播——既不等桌面、也不偷跑
- **动画预载**：登录期间页面已完全就绪，放行到开播只有一条 `postMessage` 的距离
- **三幕动画**：终端扫描 → 550C 核验（身份卡/旋转地球/进度条）→ 2200 粒子拼出 550C 标志
- **三套主题**：联合政府公文白（默认）/ 磷光终端暗绿 / 自定义主色（取色器）
- **收尾过渡**：透明水波纹（不是白光）+ 整窗分层渐隐，实时桌面逐帧透出
- **一体化控制台**：照片导入（竖拍 EXIF 自动转正）、名片编辑、主题取色、播放开关、触发安装/卸载、`--selftest` 自检
- **零依赖**：只用系统自带 .NET Framework 的 `csc.exe` 编译；不下载、不安装任何东西；不碰注册表启动项以外的系统配置

## 环境要求

- Windows 10 / 11（x64）
- Microsoft Edge（任意近版本）
- .NET Framework 4.x（系统自带，仅编译时需要）

## 快速开始（最终用户：下载一个 zip，解压即用）

到 [Releases](https://github.com/belightest/ueg-unlock-anim/releases/latest) 下载
`ueg-unlock-anim-<版本>-win64.zip`，解压到任意目录（别放 Program Files 等只读位置）：

1. 运行 `UEGUnlockConfig.exe` → 设名片/照片/主题 →「安装触发」（免管理员，写当前用户的 Run 键）
2. **注销重新登录**即可看到（纯锁屏解锁不会触发，见下文说明）

> **首次运行稍慢属正常**：新解压目录的 Edge 配置文件（`edge-profile`）是空的，
> Edge 第一次使用它会自我重启一次（换进程，约 1 秒内完成，不是崩溃）。
> 宿主会自动识别并接管新进程，起播约在启动后 1–2 秒；之后即是老配置文件，
> 没有这一步。

不想用控制台也可以手动装触发（二选一，别并用，会播两遍）：

```powershell
New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
  -Name 'UEGUnlockAnim' -Value '"<zip 解压路径>\UEGHost.exe"' -PropertyType String -Force
```

## 自己编译（开发者）

```powershell
src\build.cmd
```

用系统自带 csc.exe 零依赖编译，产出两个 exe；同时在 `dist\` 打出与 release 相同的
完整可分发包 `ueg-unlock-anim-<版本>-win64.zip`（白名单打包，天然排除
photo.jpg / config.txt / run.log / edge-profile 等个人文件，源码与文档随 zip 分发）。
版本号在 build.cmd 的 `set VER=` 一行，发新版时改那里。

## 目录结构

```
├── host.ps1                 PowerShell 兜底宿主（与 UEGHost.exe 行为同构，含开播闸门）
├── config.example.txt       配置模板（复制为 config.txt）
├── src/
│   ├── UEGHost.cs           原生宿主：起 Edge / 桌面判定 / 置顶 / 渐隐收尾
│   ├── UEGUnlockConfig.cs   一体化控制台（WinForms 全自绘，黑白灰单色系）
│   └── build.cmd            用系统 csc.exe 编译上面两个，并打出 dist\ 完整可分发包
└── ueg/
    ├── unlock.html          外层：iframe 容器 + 开播闸门 + 收尾水波纹
    ├── ueg.html             动画本体（三幕 + 终幕粒子 + 配音）
    ├── assets/              徽记 / logo / 点云数据（见 CREDITS.md）
    └── voice/               5 句旁白 × 男女两声
```

## 配置项（config.txt）

| 键 | 取值 | 说明 |
|---|---|---|
| `theme` | `ueg` / `550c` / `custom` | 主题；`custom` 配合 `accent` |
| `accent` | `#rrggbb` | 自定义主色 |
| `voice` | `male` / `female`（默认 `female`） | 配音音色 |
| `audio` | `on` / `off` | 音频总开关 |
| `autoplay` | `on` / `off` | 关 = 每幕等待点击/回车 |
| `enabled` | `on` / `off` | 总开关，`off` 静默跳过（不用卸载触发） |
| `exit_fallback_seconds` | 秒 | 收尾过渡超时强关 |
| `operator_name` / `issuer` / `photo` … | — | 身份卡内容；`photo` 指向 `ueg/assets/` 下的图 |

配置文件为 UTF-8。

## 时序设计（本项目最核心的部分）

登录动画的难点不在动画本身，而在**"什么时候开始播"**。本项目的方案演进了四代，
每一代都踩过一个真实的坑，最终方案如下：

| 代 | 方案 | 结局 |
|---|---|---|
| 1 | PowerShell 宿主，等桌面就绪再起 Edge | 安全但慢：桌面出现后还要 1–3s 才见画面 |
| 2 | 原生宿主，t+35ms 就起 Edge | **动画全程在跑但用户什么都没看见**——窗口抢在 shell 前创建，被后创建的桌面按 z 序盖住 |
| 3 | 立刻起 Edge，无闸门 | 动画连同音频在欢迎界面背后开跑，用户进桌面时已播过一半 |
| 4（现行） | 并行起 Edge + 桌面可见且安顿才放行 + 动画预载 | 桌面亮起与动画起拍几乎同时，且完整播放 |

现行方案的四个要点：

1. **置顶而非拼创建顺序**：kiosk 窗口用 `SetWindowPos(HWND_TOPMOST)` 压在桌面之上，
   与"谁先建"无关，这才敢把 Edge 提前到桌面之前启动。
2. **"Explorer 窗口建好" ≠ "桌面显示出来"**：登录期间用户桌面早已创建，屏幕却还指在
   Winlogon 桌面（锁屏/欢迎界面）。判定用 `OpenInputDesktop()` 取当前输入桌面名，
   与本进程桌面名比对——只有相等时用户才真的看得见。
3. **安顿一拍再放行**：桌面刚切过来往往还没画完（壁纸仍黑、图标未落位）。
   等任务栏建出后再留 `SettleMs`（默认 700ms），保证收尾渐隐透出的是完整桌面。
4. **细粒度轮询**：宿主 50ms、页面 50ms。放行链路上每一级轮询间隔都直接变成
   用户可见的黑屏时长，两级串联时最坏值是相加的。

动画侧的开播闸门在 `ueg/unlock.html`：页面一上线就把 `ueg.html?...&hold=1`
预载进 iframe（JS/图片/canvas 在登录期间全部就绪），轮询 `ueg/go.txt`，
宿主放行时写该文件，页面收到后 `postMessage('ueg:go')` 起拍。
**双击直接打开 `unlock.html`（不带 `ueg=1`）会立即开播**，方便单独调试动画。

## 收尾过渡

页面派发 `ueg:done` → 标题换 `UEG-WATER`，同时宿主对窗口加 `WS_EX_LAYERED`、
alpha 分 30 步降到 0（约 1.5s）：滴心 → 半透明冷水色水膜 → 三道涟漪环匀速扩散 →
停一拍 → 退潮，实时桌面在窗口后面逐帧透出。水波纹必须 `linear`
（cubic-bezier 前段过冲会让 39% 进度就缩到 0.85，扩散感全无）。
任意键可立即跳过。

## 触发方式说明

- **HKCU Run 键**（推荐，控制台按钮即此）：免管理员，任务管理器启动页可开关。
  语义是"新建登录会话时执行"——开机、重启、**注销再登录**、切换用户都会触发；
  **纯 Win+L 锁屏→解锁不触发**（当初就是为了"按解锁太频繁"选了这个语义）。
- **计划任务 AtLogon**：也可以，但普通 PowerShell 窗口即使账户是管理员也跑在
  UAC 过滤令牌下，必须右键"以管理员身份运行"再注册。
- **两者别并用**，否则会播两遍。

## 控制台自检

```powershell
.\UEGUnlockConfig.exe --selftest <一个空目录>
```

会在该目录放一份配置副本跑完整读写回归，结果写入 `selftest_result.txt`，
退出码 0 = 全过。自检**不碰正式 config.txt**。

## 常见问题

- **播两遍** → Run 键和计划任务并用了，卸掉一个。
- **没声音** → `audio=off`？或 Edge 自动播放策略（宿主已带
  `--autoplay-policy=no-user-gesture-required`）。
- **想改"等一下"的时长** → `src/UEGHost.cs` 里的 `SettleMs`（默认 700ms）。
- **file:// 下 canvas 报 SecurityError** → Edge 必须带
  `--allow-file-access-from-files`（宿主已带）。这是 file:// 图片
  `drawImage` 进 canvas 会污染画布导致的。
- **换电脑/换路径** → 全部用相对路径与 `AppDomain.CurrentDomain.BaseDirectory`，
  整个目录拷走即可；只需重新跑一次 `build.cmd`。

## 授权

- 原创代码：**Apache License 2.0**（见 `LICENSE`）
- 550C 视觉素材衍生自 [dsh-550c-boot](https://github.com/yannicksong0106/dsh-550c-boot)
  （**MIT**，Copyright (c) 2026 Ziyang Song；动画与 HTML 原稿由 Voidpocket 提供），
  按其许可保留版权声明，详见 `NOTICE` 与 [CREDITS.md](CREDITS.md)
- 配音为 TTS 生成，非影视原声
- 本项目为**非官方**作品，与相关作品的权利人无任何关联，亦未获得其授权或背书；
  "UEG""550C"等名称仅用于描述界面风格
- 本项目由 AI 辅助创建
