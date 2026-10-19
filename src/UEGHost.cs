// =====================================================================
//  UEGHost.cs —「UEG 解锁动画」原生宿主
// ---------------------------------------------------------------------
//  由来：登录触发原本走 powershell.exe -File host.ps1，开机冷启动时要付
//        PowerShell 冷启动（1–3s）+ Add-Type 运行时编译淡出用的 C#（1–3s），
//        动画因此明显晚于桌面出现，还会闪一下控制台窗口。
//  做法：把宿主职责搬进原生 winexe —— 无控制台子系统（永不闪窗口），
//        P/Invoke 随 exe 编译好（无运行时编译），冷启动开销 ≈ 0.2s。
//  流程：读 config.txt → 立刻起 Edge kiosk 全屏（不等桌面，让 Edge 约 1s 的冷启动
//        与桌面加载并行；窗口一律 HWND_TOPMOST 置顶，桌面一出现画面已在最上层）
//        → 页面先只留黑屏，等"Edge 窗口真的出现在屏上"才写 go.txt 放行开播
//        （判据 = WindowFromPoint 打中自己的窗口：打不中，就说明欢迎界面/锁屏
//        还占据着屏幕，动画连同音频会在用户看不见的桌面上悄悄跑掉 —— 2026-10-18
//        实测故障：输完密码后语音先响十几秒，桌面才慢悠悠画出来）
//        → 轮询窗口标题 → 页面进入 UEG-WATER（水波起手）时把整窗 alpha 渐降到 0，
//        实时桌面从窗口后面透出来 → 杀掉本次启动的 Edge → 进程退出。
//  收尾：进程结束即退出，不留后台、不占运存。
//  兼容：host.ps1 原样保留为兜底；没有 UEGHost.exe 时控制台自动退回 PowerShell。
//  编译：由同目录 build.cmd 用系统自带 csc.exe 编译（零依赖、零下载）
// =====================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UEGUnlockConfig
{
    static class Host
    {
        // ---------- Win32 ----------
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern IntPtr SetLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr OpenInputDesktop(uint dwFlags,
            [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDesktop(int dwThreadId);

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex,
            StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfn, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private const uint GA_ROOT = 2;
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x00080000;
        private const uint LWA_ALPHA = 0x00000002;

        // 置顶：HWND_TOPMOST 的窗口永远压在所有非置顶窗口（含桌面）之上，与创建顺序无关
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private const uint DESKTOP_READOBJECTS = 0x0001;
        private const int UOI_NAME = 2;

        // ---------- 运行参数 ----------
        // 放行前的轮询要快：桌面可见的发现延迟直接变成"黑屏等动画起播"的时间，
        // 250ms 一档太粗（叠加页面侧轮询最坏能到 450ms）；50ms 一档几乎无感。
        private const int PollMs = 50;
        private const int HardStopMin = 3;
        private const int FadeSteps = 30;
        private const int FadeStepMs = 50;
        private const int BootTimeoutSec = 60;     // 放行后迟迟不进 UEG-DONE = 动画坏了，别让全屏窗口赖着
        private const int ReleaseGraceMs = 90000;  // 桌面可见判定万一卡住，到点无条件放行（宁可早播也不能黑屏到底）
        private const int SettleMs = 700;          // 桌面可见后，等外壳把桌面安顿好再放行：壁纸/图标/任务栏画完，
                                                    // 动画收尾渐隐透出来的才是完整桌面，而不是半成品

        private static readonly List<IntPtr> EdgeWindows = new List<IntPtr>();
        private static readonly EnumWindowsProc EnumCb = new EnumWindowsProc(EnumWindowsCallback);
        private static readonly List<RECT> MonitorRects = new List<RECT>();
        private static readonly MonitorEnumProc MonCb = new MonitorEnumProc(MonitorEnumCallback);

        private static string baseDir = "";
        private static string logFile = "";
        private static string profileDir = "";
        private static string goFile = "";
        private static Process edgeProc = null;
        private static DateTime booted;

        [STAThread]
        static int Main(string[] args)
        {
            booted = DateTime.Now;
            try { return Run(); }
            catch (Exception ex) { Log("EXCEPTION: " + ex.ToString()); return 0; }
        }

        // ---------------- 主流程 ----------------
        static int Run()
        {
            baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(baseDir)) { baseDir = "D:\\BootAnim"; }
            baseDir = baseDir.TrimEnd('\\');
            logFile = Path.Combine(baseDir, "run.log");
            profileDir = Path.Combine(baseDir, "edge-profile");
            goFile = Path.Combine(baseDir, "ueg", "go.txt");

            // 日志轮转：超过 1MB 就按时间戳归档重来，防止长期登录累积把磁盘悄悄撑满。
            // 归档文件一律不删（删除类操作不擅自做），靠时间戳区分互不覆盖。
            try
            {
                FileInfo lf = new FileInfo(logFile);
                if (lf.Exists && lf.Length > 1048576)
                {
                    File.Move(logFile, logFile + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".old");
                }
            }
            catch { }

            string page = Path.Combine(baseDir, "ueg", "unlock.html");
            if (!File.Exists(page)) { Log("skipped: unlock.html missing"); return 0; }
            string edge = FindEdge();
            if (edge == "") { Log("skipped: msedge missing"); return 0; }

            g_cfg = LoadConfig(Path.Combine(baseDir, "config.txt"));
            if (Get("enabled", "on") == "off")
            {
                Log("skipped: disabled by config (enabled=off)");
                return 0;
            }

            int fallback = 10;
            string fbs = Get("exit_fallback_seconds", "");
            int fbn;
            if (int.TryParse(fbs, out fbn) && fbn > 0) { fallback = fbn; }
            if (fallback > 120) { fallback = 120; }

            List<string> q = new List<string>();
            AddParam(q, "name", Get("operator_name", ""));
            AddParam(q, "id", Get("operator_id", ""));
            AddParam(q, "issuer", Get("issuer", ""));
            AddParam(q, "level", Get("clearance_level", ""));
            AddParam(q, "valid", Get("valid_until", ""));
            AddParam(q, "scope", Get("scope", ""));
            AddParam(q, "bio", Get("biometric", ""));
            AddParam(q, "photo", Get("photo", ""));
            AddParam(q, "theme", Get("theme", "ueg"));
            AddParam(q, "accent", Get("accent", ""));
            AddParam(q, "voice", Get("voice", "female"));
            if (Get("audio", "on") == "off") { q.Add("audio=0"); }
            if (Get("autoplay", "on") == "off") { q.Add("auto=0"); }
            // 开播闸门的开关：页面只认带 ueg=1 的入口才等信号，
            // 直接双击打开 unlock.html（不带参数）时照常立即开播，方便单独查看
            q.Add("ueg=1");

            string url = "file:///" + page.Replace('\\', '/');
            if (q.Count > 0) { url = url + "?" + string.Join("&", q.ToArray()); }

            // 登录前的保险（正常路径下是零等待的空转）：等"用户桌面成为输入桌面"
            // 再起 Edge。注意 DesktopIsCurrent 读的是宿主自己窗口站的输入桌面，
            // 登录后它恒为 Default，挡不住"欢迎界面还挂着"的那十几秒 —— 真正
            // 的闸门在放行处的 EdgeOnScreen()。这里只在异常早到（登录前触发）
            // 时兜一下；等超过 ReleaseGraceMs 还没登录就退出，不往锁屏桌面起
            // 窗口 —— 用户真正登录时 Run 会再拉一次宿主。
            int deferMs = 0;
            DateTime deferDiag = DateTime.MinValue;
            while (!DesktopIsCurrent())
            {
                if (deferMs >= ReleaseGraceMs)
                {
                    Log("no user logon within " + ReleaseGraceMs + "ms, exit (Run will relaunch at logon)  [t+" + Ms() + "ms]");
                    return 0;
                }
                System.Threading.Thread.Sleep(PollMs);
                deferMs += PollMs;
                if ((DateTime.Now - deferDiag).TotalMilliseconds >= 500)
                {
                    deferDiag = DateTime.Now;
                    Diag();
                }
            }
            if (deferMs > 0)
            {
                Log("waited " + deferMs + "ms for user desktop, launching edge  [t+" + Ms() + "ms]");
            }

            // 起 Edge 前先清掉上一轮的开播信号，否则页面一上线就拿到旧信号、立刻开播
            ClearGo();

            // Edge 从建进程到首帧约 1 秒，让它跟桌面加载并行跑（登录后触发时零等待）。
            // 之前"抢在 shell 之前建窗会被桌面盖住"，是因为 z 序按创建顺序排（后建者在上）；
            // 现在改用 HWND_TOPMOST 置顶，压桌面与创建顺序无关，故可放心提前起 Edge。
            // 但页面只上线、不加载动画（见 unlock.html 的 iframe 闸门），
            // 等"用户桌面真的成为当前桌面"再放行 —— 否则动画在登录界面背后悄悄跑完一半。
            Log("launching: " + url + "  [t+" + Ms() + "ms]");

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = edge;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = baseDir;
                psi.Arguments =
                    "--kiosk \"" + url + "\" " +
                    "--user-data-dir=\"" + profileDir + "\" " +
                    "--allow-file-access-from-files " +
                    "--autoplay-policy=no-user-gesture-required " +
                    "--no-first-run " +
                    "--no-default-browser-check " +
                    "--disable-session-crashed-bubble " +
                    "--hide-crash-restore-bubble " +
                    // 砍掉与本次播放无关的后台初始化（组件更新/同步/网络/翻译等），首帧更快
                    "--disable-component-update " +
                    "--disable-background-networking " +
                    "--disable-sync " +
                    "--disable-default-apps " +
                    "--disable-client-side-phishing-detection " +
                    "--disable-features=Translate,OptimizationHints,MediaRouter " +
                    "--disable-renderer-backgrounding " +
                    "--disable-backgrounding-occluded-windows";
                edgeProc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log("FAILED to start Edge: " + ex.Message);
                return 0;
            }
            if (edgeProc == null) { Log("FAILED: Process.Start returned null"); return 0; }
            Log("edge pid=" + edgeProc.Id + "  [t+" + Ms() + "ms]");
            try { edgeProc.PriorityClass = ProcessPriorityClass.AboveNormal; }
            catch { }

            DateTime? doneAt = null;
            DateTime? relAt = null;
            DateTime hardStop = DateTime.Now.AddMinutes(HardStopMin);
            string lastTitle = null;
            bool shellUp = false;
            bool deskCurrent = false;
            DateTime deskCurrentAt = DateTime.MinValue;
            bool released = false;
            DateTime lastDiag = DateTime.MinValue;

            while (true)
            {
                IntPtr h = FindKiosk();
                // Edge 首启 relaunch 后 Process.Start 的进程号已失效（edgeProc 成
                // 了退出过的死进程）：kiosk 窗口一上线就把它所属的真正浏览器进程
                // 收养过来，屏上探针与收尾关窗随即恢复有效。详见 TryAdopt 注释。
                if (h != IntPtr.Zero && (edgeProc == null || edgeProc.HasExited))
                {
                    TryAdopt(h);
                }
                string title = (h == IntPtr.Zero) ? "" : TitleOf(h);

                if (title != "" && title != lastTitle)
                {
                    bool first = (lastTitle == null);
                    lastTitle = title;
                    if (first)
                    {
                        // 页面一上线就置顶：此刻桌面可能还没出现，先站到最上层等着
                        BringToFront(h);
                        Log("page up: " + title + "  [t+" + Ms() + "ms]");
                    }
                    else
                    {
                        Log("title: " + title + "  [t+" + Ms() + "ms]");
                    }
                }

                if (!shellUp && ShellIsUp())
                {
                    shellUp = true;
                    Log("shell up  [t+" + Ms() + "ms]");
                }

                if (!released)
                {
                    // "Explorer 窗口建好" ≠ "桌面显示出来了"：登录期间用户桌面早已存在，
                    // 只是屏幕当时还指在另一个桌面（Winlogon，锁屏/欢迎界面所在）上。
                    // 只有当用户桌面成为"输入桌面"时，用户才真的看得见。
                    //
                    // 而"输入桌面"这个名字也不能全信（2026-10-18 实测教训）：宿主读到
                    // 的永远是自己窗口站（WinSta0）的输入桌面，登录后它恒为 Default，
                    // 欢迎界面还挂着的十几秒里它也是 Default —— 上一版照这个放行，结果
                    // 语音比桌面早响十几秒。唯一不会撒谎的判据是"我们的窗口有没有真的
                    // 出现在屏上"：WindowFromPoint 只命中当前输入桌面上的窗口，欢迎
                    // 界面还挂着时打中的是那边的窗口；等输入桌面切到 Default，我们的
                    // 全屏置顶窗口才第一次被打中 —— 那一刻才是用户真正看得见我们的时候。
                    bool onScreen = EdgeOnScreen();
                    bool deskReady = shellUp && DesktopIsCurrent() && onScreen;
                    if (deskReady && !deskCurrent)
                    {
                        deskCurrent = true;
                        deskCurrentAt = DateTime.Now;
                        Log("desktop current (edge window on screen)  [t+" + Ms() + "ms]");
                    }
                    if (deskReady && DesktopSettled() &&
                        (DateTime.Now - deskCurrentAt).TotalMilliseconds >= SettleMs)
                    {
                        // 桌面不只要"看得见"，还要"安顿好"：等任务栏摆出来、再留一拍，
                        // 让壁纸和图标也画完 —— 动画起播时背后的桌面已经是完整的
                        Release();
                        released = true;
                        relAt = DateTime.Now;
                        Log("desktop visible, animation released  [t+" + Ms() + "ms]");
                    }
                    // 兜底放行：桌面检测卡住 90s 仍放行（宁可早播也不能黑屏到底）。
                    // 但外壳没起来（用户还没登录）绝不放行 —— 那正是登录前异常
                    // 触发的情形，放了就等于在锁屏上播动画。
                    else if (shellUp && (DateTime.Now - booted).TotalMilliseconds >= ReleaseGraceMs)
                    {
                        Release();
                        released = true;
                        relAt = DateTime.Now;
                        Log("released by grace (desktop detect stalled)  [t+" + Ms() + "ms]");
                    }
                    // 诊断：等放行期间每 500ms 记一行 —— 输入桌面叫什么、前台窗口是谁、
                    // 屏幕中心打中了谁。下次"语音比桌面先响"就不用再猜了。
                    if (!released && (DateTime.Now - lastDiag).TotalMilliseconds >= 500)
                    {
                        lastDiag = DateTime.Now;
                        Diag();
                    }
                    // 放行前持续重申置顶，桌面一出现就被我们压在下面
                    if (h != IntPtr.Zero) { BringToFront(h); }
                }

                if (title.IndexOf("UEG-WATER", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (doneAt == null)
                    {
                        doneAt = DateTime.Now;
                        Log("water transition started  [t+" + Ms() + "ms]");
                    }
                    int ownerPid = 0;
                    GetWindowThreadProcessId(h, out ownerPid);
                    FadeOut(h, FadeSteps, FadeStepMs);
                    StopKiosk(ownerPid);
                    Log("closed by window fade  [t+" + Ms() + "ms]");
                    return 0;
                }

                if (title.IndexOf("UEG-DONE-CLICKED", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    StopKiosk(0);
                    Log("closed by lightup end");
                    return 0;
                }

                if (title.IndexOf("UEG-DONE", StringComparison.OrdinalIgnoreCase) >= 0 && doneAt == null)
                {
                    doneAt = DateTime.Now;
                    Log("animation done, lightup transition (" + fallback.ToString() + "s fallback)  [t+" + Ms() + "ms]");
                }

                if (doneAt != null && (DateTime.Now - doneAt.Value).TotalSeconds >= fallback)
                {
                    StopKiosk(0);
                    Log("closed by fallback");
                    return 0;
                }

                // 放行后迟迟不进 UEG-DONE = 动画坏了，别让全屏窗口赖在屏上
                if (relAt != null && doneAt == null &&
                    (DateTime.Now - relAt.Value).TotalSeconds >= BootTimeoutSec)
                {
                    StopKiosk(0);
                    Log("closed by boot timeout (no UEG-DONE in " + BootTimeoutSec.ToString() + "s)");
                    return 0;
                }

                if (DateTime.Now > hardStop)
                {
                    StopKiosk(0);
                    Log("closed by 3min hard stop");
                    return 0;
                }

                System.Threading.Thread.Sleep(PollMs);
            }
        }

        // ---------------- 日志 ----------------
        private static string Ms() { return ((long)(DateTime.Now - booted).TotalMilliseconds).ToString(); }

        private static void Log(string msg)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(logFile, true, new UTF8Encoding(false)))
            {
                    sw.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg);
                }
            }
            catch { }
        }

        // ---------------- 配置 ----------------
        private static Dictionary<string, string> g_cfg = new Dictionary<string, string>();

        // 找 msedge.exe：先查 App Paths 注册表（只读，不走修改）—— 用户把 Edge
        // 装到别的盘、非默认位置，或被管家类软件改过安装路径时都能发现；
        // 注册表不可用（沙箱/权限异常）时回落两个默认安装位置。
        private static string FindEdge()
        {
            string[] hives = new string[]
            {
                "HKEY_LOCAL_MACHINE\\",
                "HKEY_CURRENT_USER\\"
            };
            string sub = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\msedge.exe";
            for (int i = 0; i < hives.Length; i++)
            {
                try
                {
                    object v = Microsoft.Win32.Registry.GetValue(hives[i] + sub, "", null);
                    if (v is string)
                    {
                        string p = (string)v;
                        if (p != "" && File.Exists(p)) { return p; }
                    }
                }
                catch { }
            }
            string p86 = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
            if (File.Exists(p86)) { return p86; }
            string p64 = @"C:\Program Files\Microsoft\Edge\Application\msedge.exe";
            if (File.Exists(p64)) { return p64; }
            return "";
        }

        private static Dictionary<string, string> LoadConfig(string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(path)) { return d; }
                // 编码自适应：记事本"另存为"默认写 ANSI(GBK) —— 含中文的配置被按
                // UTF-8 硬读会整片乱码（名片显示锟斤拷）。先严格 UTF-8 试解，
                // 字节不合法时按 GBK(936) 再解；两者都不可用才放弃。
                byte[] raw = File.ReadAllBytes(path);
                string text;
                try
                {
                    text = new UTF8Encoding(false, true).GetString(raw);
                }
                catch
                {
                    try { text = Encoding.GetEncoding(936).GetString(raw); }
                    catch { text = null; }
                }
                if (text == null) { return d; }
                text = text.TrimStart('\uFEFF');   // BOM 不参与键名
                text = text.Replace("\r\n", "\n").Replace("\r", "\n");
                foreach (string raw2 in text.Split('\n'))
                {
                    string t = raw2.Trim();
                    if (t == "" || t.StartsWith("#") || t.StartsWith("[")) { continue; }
                    int i = t.IndexOf('=');
                    if (i < 1) { continue; }
                    d[t.Substring(0, i).Trim()] = t.Substring(i + 1).Trim();
                }
            }
            catch { }
            return d;
        }

        private static string Get(string k, string def)
        {
            string v;
            if (g_cfg.TryGetValue(k, out v) && !string.IsNullOrEmpty(v)) { return v; }
            return def;
        }

        private static void AddParam(List<string> q, string k, string v)
        {
            if (string.IsNullOrEmpty(v)) { return; }
            q.Add(k + "=" + EscapeParam(v));
        }

        // 逐字节转义：保留 A-Za-z0-9-._~/ 与 : ，其余按 UTF-8 转 %XX
        // 不用 Uri.EscapeDataString：它会把 / 转成 %2F，file:// 下相对路径就解析不回来了
        private static string EscapeParam(string s)
        {
            StringBuilder sb = new StringBuilder();
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                char c = (char)b;
                bool keep = ((c >= 'A' && c <= 'Z') ||
                             (c >= 'a' && c <= 'z') ||
                             (c >= '0' && c <= '9') ||
                             c == '-' || c == '.' || c == '_' ||
                             c == '~' || c == '/' || c == ':');
                if (keep) { sb.Append(c); }
                else
                {
                    sb.Append('%');
                    sb.Append(b.ToString("X2"));
                }
            }
            return sb.ToString();
        }

        // ---------------- 找 kiosk 窗口 ----------------
        // 只认"标题带本项目标记"的 Edge 顶层窗口，绝不误伤用户自己的 Edge
        private static IntPtr FindKiosk()
        {
            EdgeWindows.Clear();
            try { EnumWindows(EnumCb, IntPtr.Zero); }
            catch { }
            for (int i = 0; i < EdgeWindows.Count; i++)
            {
                IntPtr h = EdgeWindows[i];
                string t = TitleOf(h);
                if (t.IndexOf("UEG-BOOT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("UEG-START", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("UEG-DONE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("UEG-WATER", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return h;
                }
            }
            return IntPtr.Zero;
        }

        // ---------------- 收养真正的 kiosk 进程 ----------------
        // Edge 首次使用一个全新 profile 时会"自我重启换进程"：启动器进程
        // （Process.Start 返回的那个）建出 FirstLaunchAfterInstallation 标记后
        // 立即退出，真正的浏览器进程以另一个 pid 接手（2026-10-20 实测：launcher
        // 33ms 内退出，Crashpad 无崩溃记录 = 不是崩溃，是首启正常流程）。
        // 后果：edgeProc 一开始就是死的 —— 屏上探针认不出我们的窗口、收尾也杀
        // 不掉它，全新目录（每台新机器、每个新解压目录）首次运行只能干等 90 秒
        // 兜底放行，用户看到的就是"双击后半天没反应，动画也没见"。
        // 修法：edgeProc 已退出而 kiosk 窗口已上线时，把窗口所属的真正 msedge
        // 进程收养为 edgeProc —— 后续所有判据恢复原有语义。
        // 安全性：FindKiosk 只收标题带 UEG-BOOT/START/DONE/WATER 的可见窗口，
        // 这里再校验 ① 进程名是 msedge ② 进程启动于宿主启动之后 —— 用户自己
        // 早就开着的 Edge（哪怕也开着我们的页面）绝不被收养、更不会被误杀。
        private static void TryAdopt(IntPtr kiosk)
        {
            if (kiosk == IntPtr.Zero) { return; }
            int pid = 0;
            GetWindowThreadProcessId(kiosk, out pid);
            if (pid <= 0) { return; }
            try
            {
                Process pr = Process.GetProcessById(pid);
                if (pr == null || pr.HasExited) { return; }
                if (!string.Equals(pr.ProcessName, "msedge", StringComparison.OrdinalIgnoreCase)) { return; }
                if (!IsOurEdge(pr)) { return; }
                edgeProc = pr;
                try { edgeProc.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
                Log("adopted kiosk edge pid=" + pid.ToString() +
                    " (launcher exited after first-launch relaunch)  [t+" + Ms() + "ms]");
            }
            catch { }
        }

        // 这个 msedge 进程是本次运行期间起来的吗？
        // 宿主启动之前就在跑的 Edge 一定是用户自己的（含用户手动打开我们页面的
        // 窗口），收养或关闭它们会闯祸；启动时间取不到时一律判"不是我们的"。
        private static bool IsOurEdge(Process pr)
        {
            if (pr == null || pr.HasExited) { return false; }
            try { return pr.StartTime >= booted; }
            catch { return false; }
        }

        private static bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam)
        {
            try
            {
                if (!IsWindowVisible(hWnd)) { return true; }
                StringBuilder cn = new StringBuilder(256);
                if (GetClassName(hWnd, cn, cn.Capacity) <= 0) { return true; }
                if (!string.Equals(cn.ToString(), "Chrome_WidgetWin_1", StringComparison.Ordinal)) { return true; }
                int pid = 0;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid <= 0) { return true; }
                Process pr = Process.GetProcessById(pid);
                if (pr == null) { return true; }
                bool isEdge = string.Equals(pr.ProcessName, "msedge", StringComparison.OrdinalIgnoreCase);
                if (!isEdge) { return true; }
                EdgeWindows.Add(hWnd);
            }
            catch { }
            return true;
        }

        private static string TitleOf(IntPtr hWnd)
        {
            try
            {
                StringBuilder sb = new StringBuilder(512);
                int len = GetWindowText(hWnd, sb, sb.Capacity);
                if (len <= 0) { return ""; }
                return sb.ToString(0, len);
            }
            catch { return ""; }
        }

        // 桌面就绪判断：有 shell 窗口，且 Progman 下的 SHELLDLL_DefView 已建立
        private static bool ShellIsUp()
        {
            try
            {
                IntPtr shell = GetShellWindow();
                if (shell == IntPtr.Zero) { return false; }
                IntPtr prog = FindWindow("Progman", null);
                if (prog == IntPtr.Zero) { prog = shell; }
                return FindWindowEx(prog, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
            }
            catch { return false; }
        }

        // 桌面"安顿好"了吗：任务栏窗口已建出 = 外壳把桌面摆完了（配合 SettleMs 再等一拍，
        // 让壁纸和图标也画完）。任务栏被设为自动隐藏时窗口依然存在，不影响判定。
        private static bool DesktopSettled()
        {
            try
            {
                return FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;
            }
            catch { return false; }
        }

        // 用户桌面现在是"屏幕真正显示的那个桌面"吗？
        // 登录期间锁屏/欢迎界面跑在另一个桌面（Winlogon）上，用户桌面虽已建好却还没被显示；
        // 只有它成为"输入桌面"时，用户才真的看得见。取不到信息一律放行，宁可早播。
        //
        // 注意（2026-10-18 纠正）：OpenInputDesktop 读的是**宿主自己窗口站**
        // （用户会话的 WinSta0）的输入桌面 —— 登录后它恒为 Default，欢迎界面还
        // 挂着的十几秒里它也是 Default。所以这个判定挡不住"输完密码、桌面还没
        // 画出来"那段时间，真正的闸门是 EdgeOnScreen()；本函数保留为辅助条件
        // （登录前异常触发、或输入桌面语义不同的系统上仍有一用）。
        //
        // 必须是绝对判定：输入桌面的名字就是用户交互桌面 Default。老逻辑拿
        // "输入桌面 == 宿主自己线程的桌面"做相对比较 —— 若宿主跑在 Winlogon 上
        // （登录前触发），两边都叫 Winlogon 也会相等，会误判放行：动画在密码框
        // 背后播放、用户一输密码就被桌面切换连窗销毁（2026-10-18 实测故障）。
        private static bool DesktopIsCurrent()
        {
            IntPtr input = IntPtr.Zero;
            try
            {
                input = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
                if (input == IntPtr.Zero) { return true; }
                string inName = ObjName(input);
                if (inName == "") { return true; }
                return string.Equals(inName, "Default", StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
            finally
            {
                if (input != IntPtr.Zero)
                {
                    try { CloseDesktop(input); }
                    catch { }
                }
            }
        }

        private static string ObjName(IntPtr hObj)
        {
            try
            {
                StringBuilder sb = new StringBuilder(256);
                int need = 0;
                if (GetUserObjectInformation(hObj, UOI_NAME, sb, sb.Capacity, out need) && need > 0)
                {
                    return sb.ToString();
                }
            }
            catch { }
            return "";
        }

        // ---------------- 屏上判定（放行闸门的真正判据） ----------------
        // "我们的 Edge 窗口真的出现在用户眼前了吗？"
        // 做法：在每个显示器的中心各打一枪 WindowFromPoint —— 该 API 只命中**当前
        // 输入桌面**（屏幕真正显示的那个桌面）上的窗口：欢迎界面/锁屏还挂着时，
        // 打中的是那边的窗口，沿父链爬到顶不是我们；等输入桌面切到 Default，
        // 我们的全屏置顶窗口才第一次被打中。判据取"命中的窗口爬到根是不是我们
        // 这次起的 Edge 进程"（Edge 的渲染子窗口同属这个进程，一起算）。
        // 多屏都要查：kiosk 全屏可能落在副屏（手动测试时焦点在副屏），只打主屏
        // 中心会永远打不中、白等 90 秒兜底。
        // 任何一步取不到信息都算"不在屏上"（fail-closed）：宁可晚播，不能早播。
        private static bool EdgeOnScreen()
        {
            if (edgeProc == null || edgeProc.HasExited) { return false; }
            try
            {
                List<POINT> pts = ScreenCenters();
                for (int i = 0; i < pts.Count; i++)
                {
                    IntPtr hit = WindowFromPoint(pts[i]);
                    if (hit == IntPtr.Zero) { continue; }
                    IntPtr root = GetAncestor(hit, GA_ROOT);
                    if (root == IntPtr.Zero) { root = hit; }
                    int pid = 0;
                    GetWindowThreadProcessId(root, out pid);
                    if (pid > 0 && pid == edgeProc.Id) { return true; }
                }
                return false;
            }
            catch { return false; }
        }

        // 所有显示器的中心点。枚举失败时退回主屏中心（老行为）。
        private static List<POINT> ScreenCenters()
        {
            List<POINT> pts = new List<POINT>();
            MonitorRects.Clear();
            try { EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonCb, IntPtr.Zero); }
            catch { }
            if (MonitorRects.Count == 0)
            {
                POINT only;
                only.X = GetSystemMetrics(SM_CXSCREEN) / 2;
                only.Y = GetSystemMetrics(SM_CYSCREEN) / 2;
                pts.Add(only);
                return pts;
            }
            for (int i = 0; i < MonitorRects.Count; i++)
            {
                POINT pt;
                pt.X = MonitorRects[i].Left + (MonitorRects[i].Right - MonitorRects[i].Left) / 2;
                pt.Y = MonitorRects[i].Top + (MonitorRects[i].Bottom - MonitorRects[i].Top) / 2;
                pts.Add(pt);
            }
            return pts;
        }

        private static bool MonitorEnumCallback(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData)
        {
            try
            {
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    MonitorRects.Add(mi.rcMonitor);
                }
            }
            catch { }
            return true;
        }

        // 屏幕中心点当前打中的窗口（命中不了返回 Zero）—— 诊断日志用，保持单屏语义
        private static IntPtr ProbeCenter()
        {
            POINT pt;
            pt.X = GetSystemMetrics(SM_CXSCREEN) / 2;
            pt.Y = GetSystemMetrics(SM_CYSCREEN) / 2;
            return WindowFromPoint(pt);
        }

        // ---------------- 诊断 ----------------
        // 等放行期间每 500ms 记一行：输入桌面名 / 自己桌面名 / 前台窗口 / 屏幕中心
        // 打中了谁。2026-10-18 的故障（"输完密码语音先响、桌面十几秒后才画出来"）
        // 就是靠这套日志定位的 —— 保留它，下次类似问题一眼看穿。
        private static void Diag()
        {
            try
            {
                string inName = InputDesktopName();
                string myName = SelfDesktopName();
                string fg = "-";
                IntPtr f = GetForegroundWindow();
                if (f != IntPtr.Zero)
                {
                    int fpid = 0;
                    GetWindowThreadProcessId(f, out fpid);
                    fg = ClassOf(f) + "(pid " + fpid.ToString() + ")";
                }
                string probe = "-";
                IntPtr hit = ProbeCenter();
                if (hit != IntPtr.Zero)
                {
                    int hpid = 0;
                    GetWindowThreadProcessId(hit, out hpid);
                    IntPtr root = GetAncestor(hit, GA_ROOT);
                    probe = ClassOf(hit) + "(pid " + hpid.ToString() +
                        ",root=" + (root == IntPtr.Zero ? "0" : root.ToString()) + ")";
                }
                Log("gate: inputdesk=" + (inName == "" ? "?" : inName) +
                    " selfdesk=" + (myName == "" ? "?" : myName) +
                    " fg=" + fg + " probe=" + probe +
                    " shell=" + (ShellIsUp() ? "1" : "0") +
                    " edge=" + (edgeProc == null || edgeProc.HasExited ? "dead" : edgeProc.Id.ToString()) +
                    "  [t+" + Ms() + "ms]");
            }
            catch { }
        }

        private static string InputDesktopName()
        {
            IntPtr d = IntPtr.Zero;
            try
            {
                d = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
                if (d == IntPtr.Zero) { return ""; }
                return ObjName(d);
            }
            catch { return ""; }
            finally
            {
                if (d != IntPtr.Zero) { try { CloseDesktop(d); } catch { } }
            }
        }

        private static string SelfDesktopName()
        {
            try
            {
                IntPtr d = GetThreadDesktop(GetCurrentThreadId());
                if (d == IntPtr.Zero) { return ""; }
                return ObjName(d);
            }
            catch { return ""; }
        }

        private static string ClassOf(IntPtr hWnd)
        {
            try
            {
                StringBuilder cn = new StringBuilder(256);
                if (GetClassName(hWnd, cn, cn.Capacity) > 0) { return cn.ToString(); }
            }
            catch { }
            return "?";
        }

        // ---------------- 开播信号 ----------------
        // 页面上线后只留黑屏，靠 go.txt 这个信号文件决定何时把动画装进 iframe：
        // 桌面可见 → 写文件；每次起 Edge 前先删掉旧的，防止上一轮残留让动画抢跑。
        private static void Release()
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(goFile, false, new UTF8Encoding(false)))
                {
                    sw.Write("1");
                }
            }
            catch (Exception ex) { Log("FAILED to release: " + ex.Message); }
        }

        private static void ClearGo()
        {
            try
            {
                if (File.Exists(goFile)) { File.Delete(goFile); }
            }
            catch { }
        }

        // 把 kiosk 窗口压到桌面之上。
        // 置顶（HWND_TOPMOST）与创建顺序无关 —— 这是"可以提前起 Edge"的前提：
        // 不必再等桌面建好才敢建窗，Edge 的冷启动因此能与桌面加载重叠。
        private static void BringToFront(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) { return; }
            try
            {
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                SetForegroundWindow(hWnd);
            }
            catch { }
        }

        // ---------------- 整窗淡出 ----------------
        // 页面没法让浏览器窗口透明，只能用 Win32 分层窗口属性：先加 WS_EX_LAYERED
        // 再把全局 alpha 从 255 逐级降到 0 —— 实时桌面从窗口后面逐帧透出来。
        private static void FadeOut(IntPtr hWnd, int steps, int stepMs)
        {
            if (hWnd == IntPtr.Zero) { return; }
            try
            {
                int style = GetLong(hWnd, GWL_EXSTYLE);
                IntPtr layered = new IntPtr(style | WS_EX_LAYERED);
                if (IntPtr.Size == 8) { SetLong64(hWnd, GWL_EXSTYLE, layered); }
                else { SetLong32(hWnd, GWL_EXSTYLE, layered); }
                for (int i = steps; i >= 0; i--)
                {
                    byte alpha = (byte)Math.Round(i * 255.0 / steps);
                    SetLayeredWindowAttributes(hWnd, 0, alpha, LWA_ALPHA);
                    System.Threading.Thread.Sleep(stepMs);
                }
            }
            catch { }
        }

        // ---------------- 关窗：只杀本次动画窗口所属的 Edge 进程 ----------------
        private static void StopKiosk(int pid)
        {
            if (pid > 0)
            {
                try
                {
                    Process p = Process.GetProcessById(pid);
                    if (p != null && !p.HasExited)
                    {
                        p.Kill();
                    }
                }
                catch { }
            }
            try
            {
                if (edgeProc != null && !edgeProc.HasExited)
                {
                    edgeProc.Kill();
                }
                else if (pid <= 0)
                {
                    // edgeProc 已退出（首启 relaunch 且没来得及收养）：按标题兜底
                    // 找窗口所属进程杀掉。窗口已被用户关掉时 FindKiosk 找不到，
                    // 无事可做也无害。同样只动本次运行期间起来的 Edge —— 用户
                    // 自己的 Edge 哪怕开着我们的页面也不能杀。
                    IntPtr kiosk = FindKiosk();
                    if (kiosk != IntPtr.Zero)
                    {
                        int kpid = 0;
                        GetWindowThreadProcessId(kiosk, out kpid);
                        if (kpid > 0)
                        {
                            try
                            {
                                Process p2 = Process.GetProcessById(kpid);
                                if (p2 != null && !p2.HasExited && IsOurEdge(p2)) { p2.Kill(); }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }
    }
}
