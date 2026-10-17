<#
  host.ps1 — 解锁动画宿主（PowerShell 兜底版）
  登录时由 HKCU Run 键或计划任务启动：Edge kiosk 全屏播放 UEG 授权界面（ueg/unlock.html）。
  流程结束（页面派发 ueg:done）后播放"中点点亮·圆形水波纹扩散"收尾过渡，
  过渡期间整窗透明度渐降到 0——实时桌面（图标/任务栏/鼠标）从窗口后面透出来，
  过渡结束（或按键跳过、或兜底秒数）后关闭窗口。
  素材缺失时静默退出。零下载、零安装、不动系统文件、不动启动链。
  整窗淡出靠启动阶段编译的一段 Win32 P/Invoke（Add-Type）；编译被拦时自动退回立即关窗。
  每次运行在 <本目录>\run.log 留诊断记录。
  注意：本文件含中文，必须保持 UTF-8 with BOM（PowerShell 5.1 对无 BOM 文件按 GBK 读）。
#>
$ErrorActionPreference = 'SilentlyContinue'

$base    = $PSScriptRoot
if ($base -eq '' -or $null -eq $base) { $base = (Get-Location).Path }
$cfgPath = Join-Path $base 'config.txt'
$page    = Join-Path $base 'ueg\unlock.html'
$edge    = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$profile = Join-Path $base 'edge-profile'

$logFile = Join-Path $base 'run.log'
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

# ---------- 1. 读取 config.txt（显式 UTF-8：中文系统上默认按 GBK 读会乱码）----------
$cfg = @{}
if (Test-Path $cfgPath) {
  foreach ($line in (Get-Content -LiteralPath $cfgPath -Encoding UTF8)) {
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
if ((Cfg 'voice' 'male') -eq 'female') { AddParam 'voice' 'female' } else { AddParam 'voice' 'male' }
if ((Cfg 'audio' 'on') -eq 'off')    { AddParam 'audio' '0' }
if ((Cfg 'autoplay' 'on') -eq 'off') { AddParam 'auto'  '0' }

$fallbackSeconds = 10
$fb = Cfg 'exit_fallback_seconds' ''
if ($fb -match '^\d+$') { $fallbackSeconds = [int]$fb }

$url = 'file:///' + $page.Replace('\', '/')
if ($q.Count -gt 0) { $url = $url + '?' + ($q -join '&') }
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
# 正常情况窗口就在启动进程上；若 Edge 把窗口交给同 profile 的既有浏览器进程
# （singleton 交接），按命令行里的 profile 路径也能找到它。
function Get-SameProfileProcesses {
  try {
    return @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
      Where-Object { $_.CommandLine -like ('*' + $profile + '*') })
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
