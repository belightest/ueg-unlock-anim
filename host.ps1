<#
  D:\BootAnim\host.ps1 — 解锁动画宿主
  登录时由计划任务启动：Edge kiosk 全屏播放 UEG 授权界面（ueg/unlock.html）。
  流程结束（页面派发 ueg:done）后播放"中点点亮·圆形水波纹扩散"收尾过渡，
  过渡期间整窗透明度渐降到 0——实时桌面（图标/任务栏/鼠标）从窗口后面透出来，
  过渡结束（或按键跳过、或兜底秒数）后关闭窗口。
  素材缺失时静默退出。零下载、零安装、不动系统文件、不动启动链。
  整窗淡出靠启动阶段编译的一段 Win32 P/Invoke（Add-Type）；编译被拦时自动退回立即关窗。
  每次运行在 D:\BootAnim\run.log 留诊断记录。
#>
$ErrorActionPreference = 'SilentlyContinue'

$base    = $PSScriptRoot
if ($base -eq '' -or $null -eq $base) { $base = 'D:\BootAnim' }
$cfgPath = Join-Path $base 'config.txt'
$page    = Join-Path $base 'ueg\unlock.html'
# Edge 路径：App Paths 注册表（只读）优先 —— 装在别的盘/非默认位置也能发现；
# 读不到再回落两个默认安装位置
$edge = $null
foreach ($hive in @('HKLM:', 'HKCU:')) {
  try {
    $v = (Get-ItemProperty -Path ($hive + '\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe') -ErrorAction Stop).'(default)'
    if ($v -and (Test-Path -LiteralPath $v)) { $edge = $v; break }
  } catch {}
}
if (-not $edge) {
  $edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
  if (-not (Test-Path -LiteralPath $edge)) { $edge = 'C:\Program Files\Microsoft\Edge\Application\msedge.exe' }
}
$profile = Join-Path $base 'edge-profile'

$logFile = Join-Path $base 'run.log'
$goFile  = Join-Path $base 'ueg\go.txt'
function Write-Log($msg) {
  try { ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $msg) | Out-File -LiteralPath $logFile -Append -Encoding utf8 } catch {}
}

if (-not (Test-Path $page)) { Write-Log 'skipped: unlock.html missing'; exit 0 }
if (-not (Test-Path $edge)) { Write-Log 'skipped: msedge missing'; exit 0 }

# ---------- 0. 整窗淡出的 P/Invoke（启动 Edge 之前编译一次）----------
# 收尾要把 Edge 窗口本身渐变成透明，透出实时桌面；浏览器页面做不到这一点，
# 只能用 Win32 分层窗口属性（WS_EX_LAYERED + SetLayeredWindowAttributes）。
# 入口名用实测存在的显式后缀（64 位 user32 无 SetWindowLongPtr 无后缀导出，
# 依赖 CLR 的 A/W 自动探测一旦不生效就是 EntryPointNotFoundException、淡出静默失效）。
# 编译放在启动 Edge 之前：万一所处环境禁止运行时编译 C#，失败发生在全屏窗口
# 出现之前，顶多是收尾退回立即关窗，绝不会留下没有看门狗的卡死全屏窗口。
$fadeOk = $false
try {
  Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WinFade {
  [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
  [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern IntPtr SetLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
  [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
  [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
  public static void Fade(IntPtr hWnd, int steps, int stepMs) {
    const int GWL_EXSTYLE = -20;
    const int WS_EX_LAYERED = 0x80000;
    const uint LWA_ALPHA = 0x2;
    int style = GetWindowLong(hWnd, GWL_EXSTYLE);
    IntPtr layered = new IntPtr(style | WS_EX_LAYERED);
    if (IntPtr.Size == 8) { SetLong64(hWnd, GWL_EXSTYLE, layered); } else { SetLong32(hWnd, GWL_EXSTYLE, layered); }
    for (int i = steps; i >= 0; i--) {
      byte alpha = (byte)Math.Round(i * 255.0 / steps);
      SetLayeredWindowAttributes(hWnd, 0, alpha, LWA_ALPHA);
      System.Threading.Thread.Sleep(stepMs);
    }
  }
}
'@ -ErrorAction Stop
  $fadeOk = $true
  Write-Log 'fade-out P/Invoke ready'
} catch {
  Write-Log ('fade-out unavailable: ' + $_.Exception.Message)
}

# ---------- 0.5 开播闸门的 P/Invoke（与 UEGHost.cs 第八版同构） ----------
# "桌面显示出来了"不能只靠 shell 起好判断：登录期间用户桌面早已建好，屏幕却还
# 指在 Winlogon 桌面（锁屏/欢迎界面）上，动画连同配音会在背后跑掉一半。
# 唯一不撒谎的判据是"我们的 Edge 窗口真的出现在当前输入桌面的屏上"：
# WindowFromPoint 只命中输入桌面上的窗口。编译同样放在启动 Edge 之前 ——
# 失败时闸门整体跳过、退回"起了就播"的老行为，绝不在等待上卡死。
$gateOk = $false
try {
  Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WinApi {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT Point);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
  [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);
  [DllImport("user32.dll")] public static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);
  [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr hDesktop);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, System.Text.StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);
  public const uint GA_ROOT = 2;
  public const int SM_CXSCREEN = 0;
  public const int SM_CYSCREEN = 1;
  public const uint DESKTOP_READOBJECTS = 0x0001;
  public const int UOI_NAME = 2;
}
'@ -ErrorAction Stop
  $gateOk = $true
  Write-Log 'gate P/Invoke ready'
} catch {
  Write-Log ('gate P/Invoke unavailable: ' + $_.Exception.Message)
}

# ---------- 0.6 开播闸门的判定函数（与 UEGHost.cs 第八版同构） ----------
function Clear-Go {
  try { if (Test-Path -LiteralPath $goFile) { Remove-Item -LiteralPath $goFile -Force } } catch {}
}
function Set-Go {
  try { Set-Content -LiteralPath $goFile -Value '1' -Encoding UTF8 -NoNewline } catch { Write-Log ('FAILED to release: ' + $_.Exception.Message) }
}
function Desktop-Is-Up {
  # shell 窗口建好，且 Progman 下 SHELLDLL_DefView 已建立
  try {
    if ([WinApi]::GetShellWindow() -eq [IntPtr]::Zero) { return $false }
    $prog = [WinApi]::FindWindow('Progman', $null)
    if ($prog -eq [IntPtr]::Zero) { $prog = [WinApi]::GetShellWindow() }
    return ([WinApi]::FindWindowEx($prog, [IntPtr]::Zero, 'SHELLDLL_DefView', $null) -ne [IntPtr]::Zero)
  } catch { return $false }
}
function Desktop-Is-Settled {
  # 任务栏窗口已建出 = 外壳把桌面摆完了（壁纸/图标随后画完，配合 SettleMs 再等一拍）
  try { return ([WinApi]::FindWindow('Shell_TrayWnd', $null) -ne [IntPtr]::Zero) } catch { return $false }
}
function Desktop-Is-Current {
  # 输入桌面的名字是不是 Default。这个判据本身不全信（宿主读到的是自己窗口站
  # 的输入桌面，登录后恒为 Default），取不到信息一律放行 —— 真闸门是 Edge-On-Screen。
  try {
    $d = [WinApi]::OpenInputDesktop(0, $false, [uint32][WinApi]::DESKTOP_READOBJECTS)
    if ($d -eq [IntPtr]::Zero) { return $true }
    try {
      $need = 0
      [void][WinApi]::GetUserObjectInformation($d, [WinApi]::UOI_NAME, $null, 0, [ref]$need)
      if ($need -le 0) { return $true }
      $sb = New-Object System.Text.StringBuilder ($need / 2)
      [void][WinApi]::GetUserObjectInformation($d, [WinApi]::UOI_NAME, $sb, $sb.Capacity, [ref]$need)
      return ($sb.ToString() -eq 'Default')
    } finally { [void][WinApi]::CloseDesktop($d) }
  } catch { return $true }
}
function Edge-On-Screen {
  # 主屏中心 WindowFromPoint → 根窗口 → pid 是不是我们的。同 profile 兜底覆盖
  # Edge 首启 relaunch 换进程（launcher 退出、浏览器进程另起 pid）的情形。
  # fail-closed：打不中 / 取不到信息一律判"不在屏上"，宁可晚播不能早播。
  if (-not $gateOk) { return $true }   # 探针不可用：外层会整体跳过闸门，这里不兜底
  try {
    $pt = New-Object 'WinApi+POINT'
    $pt.X = [WinApi]::GetSystemMetrics([WinApi]::SM_CXSCREEN) / 2
    $pt.Y = [WinApi]::GetSystemMetrics([WinApi]::SM_CYSCREEN) / 2
    $hit = [WinApi]::WindowFromPoint($pt)
    if ($hit -eq [IntPtr]::Zero) { return $false }
    $root = [WinApi]::GetAncestor($hit, [uint32][WinApi]::GA_ROOT)
    if ($root -eq [IntPtr]::Zero) { $root = $hit }
    $pid = 0
    [void][WinApi]::GetWindowThreadProcessId($root, [ref]$pid)
    if ($pid -le 0) { return $false }
    if ($pid -eq $proc.Id) { return $true }
    foreach ($o in (Get-SameProfileProcesses)) { if ($o.ProcessId -eq $pid) { return $true } }
    return $false
  } catch { return $false }
}

# ---------- 1. 读取 config.txt（编码自适应：记事本另存为 ANSI 的 GBK 文件不能按 UTF-8 硬读）----------
$cfg = @{}
if (Test-Path -LiteralPath $cfgPath) {
  # 控制台写的是 UTF-8 with BOM；但用户用记事本手动"另存为"会写成 ANSI(GBK)，
  # 按 UTF-8 硬读整片乱码（名片显示锟斤拷）。先严格 UTF-8 试解，字节不合法
  # 时按 GBK(936) 再解（PS 7 的 .NET Core 默认无 936 代码页，解不了就退回
  # 宽松 UTF-8，至少不崩）。
  $cfgLines = @()
  try {
    $bytes = [System.IO.File]::ReadAllBytes($cfgPath)
    $text = $null
    try { $text = (New-Object System.Text.UTF8Encoding($false, $true)).GetString($bytes) } catch {
      try { $text = [System.Text.Encoding]::GetEncoding(936).GetString($bytes) } catch { $text = $null }
    }
    if ($null -ne $text) {
      $text = $text.TrimStart([char]0xFEFF)
      $cfgLines = $text -split "\r?\n"
    }
  } catch {}
  foreach ($line in $cfgLines) {
    $t = $line.Trim()
    if ($t -eq '' -or $t.StartsWith('#') -or $t.StartsWith('[')) { continue }
    $i = $t.IndexOf('=')
    if ($i -lt 1) { continue }
    $cfg[$t.Substring(0, $i).Trim()] = $t.Substring($i + 1).Trim()
  }
}
function Cfg($k, $default) {
  if ($cfg.ContainsKey($k) -and $cfg[$k] -ne '') { return $cfg[$k] }
  return $default
}

# 控制台总开关：enabled=off 时静默退出（不卸载触发也能停播）
if ((Cfg 'enabled' 'on') -eq 'off') { Write-Log 'skipped: disabled by config (enabled=off)'; exit 0 }

# ---------- 2. 组装 URL 参数（unlock.html 会转给内层 ueg.html）----------
$q = New-Object System.Collections.Generic.List[string]
function AddParam($k, $v) { if ($v -ne '' -and $v -ne $null) { $q.Add($k + '=' + [Uri]::EscapeDataString($v)) } }
AddParam 'name'   (Cfg 'operator_name'   '')
AddParam 'id'     (Cfg 'operator_id'     '')
AddParam 'issuer' (Cfg 'issuer'          '')
AddParam 'level'  (Cfg 'clearance_level' '')
AddParam 'valid'  (Cfg 'valid_until'     '')
AddParam 'scope'  (Cfg 'scope'           '')
AddParam 'bio'    (Cfg 'biometric'       '')
AddParam 'photo'  (Cfg 'photo'           '')
# 主题：ueg（默认公文白）/ 550c（磷光终端暗绿）/ custom（自定义主色，配合 accent）
AddParam 'theme'  (Cfg 'theme'           'ueg')
$accent = Cfg 'accent' ''
if ($accent -ne '') { AddParam 'accent' $accent }
if ((Cfg 'voice' 'female') -eq 'male') { AddParam 'voice' 'male' } else { AddParam 'voice' 'female' }
if ((Cfg 'audio' 'on') -eq 'off')    { AddParam 'audio' '0' }
if ((Cfg 'autoplay' 'on') -eq 'off') { AddParam 'auto'  '0' }
# 开播闸门的开关：页面只认带 ueg=1 的入口才等宿主写 go.txt 信号，
# 直接双击打开 unlock.html（不带参数）时照常立即开播，方便单独查看
$q.Add('ueg=1')

$fallbackSeconds = 10
$fb = Cfg 'exit_fallback_seconds' ''
if ($fb -match '^\d+$') { $fallbackSeconds = [int]$fb }

$url = 'file:///' + $page.Replace('\', '/')
if ($q.Count -gt 0) { $url = $url + '?' + ($q -join '&') }
# 起 Edge 前先清掉上一轮的开播信号，否则页面一上线就拿到旧信号、立刻开播
Clear-Go
Write-Log ("launching: " + $url)

# ---------- 3. 启动 Edge kiosk（独立 profile，不干扰用户自己的 Edge 窗口）----------
# --allow-file-access-from-files 是终幕的必选项：file:// 页面把 file:// 图片画进
# canvas 会被判跨源、污染画布，getImageData 直接抛 SecurityError（粒子拼不成标志）。
# 加上它之后 file:// 图片视为同源；顺带 voice-cues.json 的 fetch 也不再被 CORS 拦。
$proc = Start-Process -FilePath $edge -PassThru -ArgumentList @(
  '--kiosk'
  $url
  ('--user-data-dir=' + $profile)
  '--allow-file-access-from-files'
  '--autoplay-policy=no-user-gesture-required'
  '--no-first-run'
  '--no-default-browser-check'
  '--disable-session-crashed-bubble'
  '--hide-crash-restore-bubble'
)
if ($null -eq $proc) { Write-Log 'FAILED: Start-Process returned null'; exit 0 }
Write-Log ("edge pid=" + $proc.Id)

# ---------- 4. 同 profile 的 Edge 进程工具函数 ----------
# 按命令行里的 profile 路径找进程 —— 这条命同时覆盖两种"窗口不在启动进程上"的
# 情形：① Edge 首启 relaunch（全新 profile 首次使用：启动器建出
# FirstLaunchAfterInstallation 后自我重启换 pid，2026-10-20 实测 33ms 内退出，
# 不是崩溃）；② 同 profile 已有实例时的 singleton 交接。
function Get-SameProfileProcesses {
  # PS 5.1 自带 Get-CimInstance；PS 7 默认不带（要装 WindowsCompatibility 模块），
  # 失败时退回 Get-WmiObject（两代都有，PS 7 里只是弃用警告）
  $procs = $null
  try { $procs = Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" -ErrorAction Stop } catch { $procs = $null }
  if ($null -eq $procs) {
    try { $procs = Get-WmiObject Win32_Process -Filter "Name='msedge.exe'" -ErrorAction SilentlyContinue } catch { $procs = $null }
  }
  if ($null -eq $procs) { return @() }
  try {
    return @($procs | Where-Object { $_.CommandLine -like ('*' + $profile + '*') })
  } catch { return @() }
}

function Get-KioskTitle {
  try { if ($proc.MainWindowTitle -ne '') { return $proc.MainWindowTitle } } catch {}
  foreach ($o in (Get-SameProfileProcesses)) {
    try {
      $pr = Get-Process -Id $o.ProcessId -ErrorAction SilentlyContinue
      if ($pr -and $pr.MainWindowTitle -ne '') { return $pr.MainWindowTitle }
    } catch {}
  }
  return ''
}

function Stop-Kiosk {
  foreach ($o in (Get-SameProfileProcesses)) {
    try { Stop-Process -Id $o.ProcessId -Force } catch {}
  }
  try { Stop-Process -Id $proc.Id -Force } catch {}
}

# ---------- 4.5 整窗淡出：把 Edge 窗口的透明度渐降到 0，露出实时桌面 ----------
# 页面没法让浏览器窗口透明，只能用 Win32 分层窗口属性：先给窗口加上 WS_EX_LAYERED，
# 再把全局 alpha 从 255 逐级降到 0——实时桌面（图标、任务栏、鼠标）就从窗口后面
# 逐帧透出来，水波也在同一批帧里一起透明。函数全程有界（步数×间隔），任何一步
# 失败都只是淡出无效，返回后照常关窗，绝不会卡住。P/Invoke 在脚本开头编译
# （结果见 $fadeOk）；编译被拦时本函数直接返回，收尾退回等页面信号或兜底秒数关窗。
function Fade-Out-Window {
  param([int]$Steps = 30, [int]$StepMs = 50)
  if (-not $fadeOk) { return }
  $hWnd = [IntPtr]::Zero
  try { if ($proc -ne $null -and $proc.MainWindowHandle -ne $null -and $proc.MainWindowHandle -ne 0) { $hWnd = $proc.MainWindowHandle } } catch {}
  if ($hWnd -eq [IntPtr]::Zero) {
    foreach ($o in (Get-SameProfileProcesses)) {
      try {
        $pr = Get-Process -Id $o.ProcessId -ErrorAction SilentlyContinue
        if ($pr -and $pr.MainWindowHandle -ne $null -and $pr.MainWindowHandle -ne 0) { $hWnd = $pr.MainWindowHandle; break }
      } catch {}
    }
  }
  if ($hWnd -eq [IntPtr]::Zero) { return }
  try { [void][WinFade]::Fade($hWnd, $Steps, $StepMs) } catch { }
}

# ---------- 4.7 等"用户桌面真的显示出来"才写开播信号 ----------
# "Explorer 窗口建好" ≠ "桌面显示出来"：登录期间用户桌面早已创建，屏幕却还
# 指在 Winlogon 桌面（锁屏/欢迎界面）上 —— 直接放行，动画连同配音会在背后
# 跑掉一半（2026-10-18 实测故障）。判据与 C# 宿主完全同构：
#   ShellIsUp && DesktopIsCurrent && EdgeOnScreen && DesktopSettled，
# 且距"首次在屏上"≥ 700ms（让壁纸/图标画完，收尾渐隐透出的是完整桌面）。
# 兜底：桌面检测卡住 90s 才放行，但外壳没起来（用户还没登录）绝不放行 ——
# 那正是登录前异常触发的情形，放了就等于在锁屏上播动画。
if (-not $gateOk) {
  # 探针 P/Invoke 编译失败：闸门整体跳过，退回"起了就播"的老行为
  Write-Log 'gate unavailable, release immediately (legacy behavior)'
  Set-Go
} else {
  $booted = Get-Date
  $shellUp = $false
  $deskCurrentAt = $null
  $released = $false
  while (-not $released) {
    if (-not $shellUp -and (Desktop-Is-Up)) { $shellUp = $true; Write-Log 'shell up' }
    $onScreen = Edge-On-Screen
    if ($shellUp -and (Desktop-Is-Current) -and $onScreen -and ($null -eq $deskCurrentAt)) {
      $deskCurrentAt = Get-Date
      Write-Log 'desktop current (edge window on screen)'
    }
    if (($null -ne $deskCurrentAt) -and (Desktop-Is-Settled) -and (((Get-Date) - $deskCurrentAt).TotalMilliseconds -ge 700)) {
      Set-Go
      $released = $true
      Write-Log 'desktop visible, animation released'
    } elseif ($shellUp -and (((Get-Date) - $booted).TotalMilliseconds -ge 90000)) {
      Set-Go
      $released = $true
      Write-Log 'released by grace (desktop detect stalled)'
    } elseif (((Get-Date) - $booted).TotalMilliseconds -ge 90000) {
      # 外壳 90s 还没起来 = 登录前异常触发（Run 早跑了）：退出，别在锁屏背后
      # 干等。真正的登录会再拉一次宿主（同 UEGHost.cs 起 Edge 前 defer 的语义）。
      Write-Log 'no user logon within 90s, exit (Run will relaunch at logon)'
      Stop-Kiosk
      exit 0
    }
    Start-Sleep -Milliseconds 50
  }
}

# ---------- 5. 轮询窗口标题：整窗淡出 / 兜底秒数 / 3 分钟硬上限 ----------
# 页面流程结束把 document.title 改成 UEG-DONE 并开始收尾过渡；水波起手那一刻
# 标题换成 UEG-WATER —— 宿主从这一刻起把整窗透明度渐降到 0（实时桌面透出来），
# 淡出结束即关窗。过渡放不完或按 Enter/空格/Esc 跳过则标题直接变
# UEG-DONE-CLICKED，宿主立即关窗（旧行为，也是淡出失败时的兜底）。
$doneAt     = $null
$hardStop   = (Get-Date).AddMinutes(3)
$firstTitle = $null
while ($true) {
  if ($proc.HasExited) {
    if ((Get-SameProfileProcesses).Count -eq 0) { Write-Log 'edge exited by itself'; exit 0 }
  }
  if ((Get-Date) -gt $hardStop) { Stop-Kiosk; Write-Log 'closed by 3min hard stop'; exit 0 }
  $title = Get-KioskTitle
  if ($title -ne '' -and $null -eq $firstTitle) {
    $firstTitle = $title
    Write-Log ("window title: " + $title)
  }
  if ($title -like '*UEG-WATER*') {
    # 水波起手：整窗淡出（约 1.5 秒，实时桌面逐帧透出）后关窗
    if ($null -eq $doneAt) { $doneAt = Get-Date; Write-Log 'water transition started' }
    if ($fadeOk) {
      Fade-Out-Window | Out-Null
      Stop-Kiosk
      Write-Log 'closed by window fade'
      exit 0
    }
    # 淡出不可用（编译被拦）：不打断水波，等页面自己的 UEG-DONE-CLICKED 信号
  }
  if ($title -like '*UEG-DONE-CLICKED*') { Stop-Kiosk; Write-Log 'closed by lightup end'; exit 0 }
  if ($title -like '*UEG-DONE*' -and $null -eq $doneAt) {
    $doneAt = Get-Date
    Write-Log ("animation done, lightup transition (" + $fallbackSeconds + "s fallback)")
  }
  # 兜底秒数：UEG-DONE / UEG-WATER 之后页面迟迟不派发关窗信号，到点强关
  # （放在标题判断外面：水波阶段标题是 UEG-WATER，不匹配 *UEG-DONE*，
  #   但兜底计时必须照走，否则页面失灵要干等 3 分钟硬上限）
  if ($null -ne $doneAt -and ((Get-Date) - $doneAt).TotalSeconds -ge $fallbackSeconds) {
    Stop-Kiosk; Write-Log 'closed by fallback'; exit 0
  }
  Start-Sleep -Milliseconds 250
}
