// =====================================================================
//  UEGUnlockConfig.cs —「UEG 解锁动画」一体化控制台
// ---------------------------------------------------------------------
//  功能：编辑个人名片 / 导入身份卡照片 / 主题配色（公文白·磷光终端·自定义主色）/
//        播放设置 / 总开关 / 安装·卸载登录触发（HKCU Run 键，免管理员）/ 立即预览
//  视觉：黑白灰单色系（纯黑 #0B0B0C 底 · 深灰卡片 · 浅灰文字 · 白色主操作），
//        按钮/卡片/输入槽/复选框/下拉/页签全部自绘，无第三方依赖。
//  编译：双击同目录 build.cmd（系统自带 csc.exe，零依赖、零下载）
//  运行：.NET Framework 4.x（Win10/11 自带），双击 exe 即用
//  自检：UEGUnlockConfig.exe --selftest <目录>   （无界面，写结果文件，
//         目录里放一份 config.txt 副本即可，不会碰正式配置）
// =====================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UEGUnlockConfig
{
    // ============ 设计变量：黑白灰单色系 ============
    static class Pal
    {
        public static readonly Color Bg0 = ColorTranslator.FromHtml("#0B0B0C");     // 窗体底（近黑）
        public static readonly Color Surface = ColorTranslator.FromHtml("#121214"); // 卡片面（深灰）
        public static readonly Color Field = ColorTranslator.FromHtml("#0E0E10");   // 输入槽
        public static readonly Color Line = ColorTranslator.FromHtml("#26262A");    // 细边
        public static readonly Color Line2 = ColorTranslator.FromHtml("#3C3C44");   // 强边 / 悬停
        public static readonly Color Focus = ColorTranslator.FromHtml("#55555E");   // 聚焦边
        public static readonly Color Ink = ColorTranslator.FromHtml("#F2F2F4");     // 主文字（白）
        public static readonly Color Ink2 = ColorTranslator.FromHtml("#9C9CA4");    // 次文字（浅灰）
        public static readonly Color Ink3 = ColorTranslator.FromHtml("#5E5E66");    // 弱文字（中灰）
        public static readonly Color Hover = ColorTranslator.FromHtml("#1C1C20");   // 悬停填充
        public static readonly Color Pressed = ColorTranslator.FromHtml("#242429"); // 按下填充
        public static readonly Color White = Color.FromArgb(255, 255, 255);         // 主操作 / 强调
        public static readonly Color PrimaryHover = ColorTranslator.FromHtml("#E4E4E8");   // 主操作悬停（白 → 浅灰）
        public static readonly Color PrimaryPressed = ColorTranslator.FromHtml("#C9C9D0"); // 主操作按下（浅灰 → 中灰）
    }

    static class Fnt
    {
        public static readonly Font Base = new Font("Microsoft YaHei UI", 9f);
        public static readonly Font BaseBold = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        public static readonly Font Title = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
        public static readonly Font Small = new Font("Microsoft YaHei UI", 8f);
        public static readonly Font CardTitle = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        public static readonly Font Field = new Font("Microsoft YaHei UI", 9.5f);
    }

    // ============ 键盘焦点提示开关：仅键盘导航时画焦点框（鼠标操作不画，保持界面干净）============
    static class FocusCue
    {
        public static bool Keyboard = false;
    }

    // ============ 自绘按钮：主操作（白底黑字）/ 幽灵（描边）/ 页签 ============
    class FlatButton : Button
    {
        public enum BtnStyle { Primary, Ghost, Tab }
        public BtnStyle Style = BtnStyle.Ghost;
        public bool Active = false;   // 仅页签用：当前停靠页
        private bool hover = false;
        private bool pressed = false;

        public FlatButton()
        {
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = Fnt.Base;
            ForeColor = Pal.Ink;
            BackColor = Pal.Bg0;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = BackColor;
            Color fore = ForeColor;
            Color border = back;
            if (Style == BtnStyle.Primary)
            {
                if (Enabled)
                {
                    if (pressed) { back = Pal.PrimaryPressed; }
                    else if (hover) { back = Pal.PrimaryHover; }
                    else { back = Pal.White; }
                    fore = Pal.Bg0;
                    border = back;
                }
                else { back = Pal.Surface; fore = Pal.Ink3; border = Pal.Line; }
            }
            else if (Style == BtnStyle.Tab)
            {
                back = Pal.Bg0;
                fore = Active ? Pal.Ink : Pal.Ink3;
                if (hover && !Active) { fore = Pal.Ink2; }
            }
            else
            {
                if (!Enabled) { fore = Pal.Ink3; border = Pal.Line; }
                else if (pressed) { back = Pal.Pressed; border = Pal.Line2; }
                else if (hover) { back = Pal.Hover; border = Pal.Line2; }
                else { border = Pal.Line; }
            }
            using (SolidBrush b = new SolidBrush(back)) { g.FillRectangle(b, ClientRectangle); }
            if (border != back)
            {
                using (Pen p = new Pen(border)) { g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1); }
            }
            if (Focused && FocusCue.Keyboard)
            {
                using (Pen p = new Pen(Pal.Focus))
                {
                    g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                }
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Style == BtnStyle.Tab && Active)
            {
                using (SolidBrush b = new SolidBrush(Pal.White))
                {
                    g.FillRectangle(b, new Rectangle(0, ClientSize.Height - 2, ClientSize.Width, 2));
                }
            }
        }
    }

    // ============ 卡片：深灰面 + 1px 细边 + 内嵌小标题 ============
    class Card : Panel
    {
        public string Title = "";
        public Card()
        {
            BackColor = Pal.Surface;
            ForeColor = Pal.Ink;
            Font = Fnt.Base;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Pal.Line))
            {
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
            if (!string.IsNullOrEmpty(Title))
            {
                TextRenderer.DrawText(e.Graphics, Title, Fnt.CardTitle, new Point(14, 10), Pal.Ink3);
            }
        }
    }

    // ============ 输入槽：深色底 + 细边（悬停加深 / 聚焦提亮），内嵌无边框文本框 ============
    class FieldBox : Panel
    {
        private bool hover = false;
        private bool focused = false;
        public FieldBox()
        {
            BackColor = Pal.Field;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }
        public void Attach(Control inner)
        {
            inner.Left = 8;
            inner.Top = (ClientSize.Height - inner.Height) / 2;
            inner.Width = ClientSize.Width - 16;
            inner.GotFocus += delegate { focused = true; Invalidate(); };
            inner.LostFocus += delegate { focused = false; Invalidate(); };
            inner.MouseEnter += delegate { hover = true; Invalidate(); };
            inner.MouseLeave += delegate { hover = false; Invalidate(); };
            Controls.Add(inner);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Color c = focused ? Pal.Focus : (hover ? Pal.Line2 : Pal.Line);
            using (Pen p = new Pen(c))
            {
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }
    }

    // ============ 自绘下拉：暗色槽 + 暗色列表项 ============
    class FlatCombo : ComboBox
    {
        public FlatCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            BackColor = Pal.Field;
            ForeColor = Pal.Ink;
            Font = Fnt.Field;
            Cursor = Cursors.Hand;
            ItemHeight = 20;
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Focused && FocusCue.Keyboard)
            {
                using (Pen p = new Pen(Pal.Focus))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                }
            }
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) { return; }
            Color back = ((e.State & DrawItemState.Selected) == DrawItemState.Selected) ? Pal.Line2 : Pal.Field;
            using (SolidBrush b = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(b, e.Bounds);
            }
            string t = (Items[e.Index] == null) ? "" : Items[e.Index].ToString();
            Rectangle r = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, t, Font, r, Pal.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }
    }

    // ============ 自绘复选框：暗色方框 + 勾选白底黑勾 ============
    class FlatCheck : CheckBox
    {
        private bool hover = false;
        public FlatCheck()
        {
            Appearance = Appearance.Normal;
            TextAlign = ContentAlignment.MiddleLeft;
            AutoSize = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = Fnt.Base;
            ForeColor = Pal.Ink;
            BackColor = Pal.Surface;
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(BackColor)) { g.FillRectangle(b, ClientRectangle); }
            int box = 14;
            int by = (ClientSize.Height - box) / 2;
            Rectangle r = new Rectangle(0, by, box, box);
            if (Checked)
            {
                using (SolidBrush b = new SolidBrush(Pal.White)) { g.FillRectangle(b, r); }
                using (Pen p = new Pen(Pal.Bg0))
                {
                    p.Width = 2f;
                    g.DrawLine(p, r.X + 3, r.Y + 8, r.X + 6, r.Y + 11);
                    g.DrawLine(p, r.X + 6, r.Y + 11, r.X + 11, r.Y + 4);
                }
            }
            else
            {
                using (SolidBrush b = new SolidBrush(Pal.Field)) { g.FillRectangle(b, r); }
                using (Pen p = new Pen(hover ? Pal.Line2 : Pal.Line))
                {
                    g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                }
            }
            Rectangle tr = new Rectangle(box + 10, 0, ClientSize.Width - box - 10, ClientSize.Height);
            TextRenderer.DrawText(g, Text, Font, tr, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }
    }

    // ============ 描边框（照片预览用） ============
    class Frame : Panel
    {
        public Frame()
        {
            BackColor = Pal.Field;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Pal.Line))
            {
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }
    }

    // ============ config.txt 模型：host.ps1 读同一份文件，UTF-8 with BOM ============
    public class AnimConfig
    {
        public string BaseDir;

        public string OperatorName = "";
        public string OperatorId = "";
        public string Issuer = "";
        public string ClearanceLevel = "";
        public string ValidUntil = "";
        public string Scope = "";
        public string Biometric = "";
        public string Photo = "assets/photo.jpg";
        public string Theme = "ueg";
        public string Accent = "";
        public string Voice = "male";
        public string Audio = "on";
        public string Autoplay = "on";
        public string Enabled = "on";
        public int FallbackSeconds = 10;

        public AnimConfig(string baseDir)
        {
            this.BaseDir = baseDir;
        }

        public string FilePath { get { return Path.Combine(BaseDir, "config.txt"); } }
        public string HostScript { get { return Path.Combine(BaseDir, "host.ps1"); } }

        public void Load()
        {
            string p = FilePath;
            if (!File.Exists(p)) { return; }
            foreach (string raw in File.ReadAllLines(p, Encoding.UTF8))
            {
                string t = raw.Trim();
                if (t.Length == 0 || t.StartsWith("#") || t.StartsWith("[")) { continue; }
                int i = t.IndexOf('=');
                if (i < 1) { continue; }
                string k = t.Substring(0, i).Trim();
                string v = t.Substring(i + 1).Trim();
                switch (k)
                {
                    case "operator_name": OperatorName = v; break;
                    case "operator_id": OperatorId = v; break;
                    case "issuer": Issuer = v; break;
                    case "clearance_level": ClearanceLevel = v; break;
                    case "valid_until": ValidUntil = v; break;
                    case "scope": Scope = v; break;
                    case "biometric": Biometric = v; break;
                    case "photo": Photo = v; break;
                    case "theme": Theme = v; break;
                    case "accent": Accent = v; break;
                    case "voice": Voice = v; break;
                    case "audio": Audio = v; break;
                    case "autoplay": Autoplay = v; break;
                    case "enabled": Enabled = v; break;
                    case "exit_fallback_seconds":
                        int n;
                        if (int.TryParse(v, out n)) { FallbackSeconds = n; }
                        break;
                }
            }
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# ===== UEG 解锁动画配置（由控制台生成，也可手工修改）=====");
            sb.AppendLine("# 保存后下次登录播放时生效。");
            sb.AppendLine();
            sb.AppendLine("# 主题：ueg = 联合政府公文白（默认）；550c = 磷光终端暗绿；");
            sb.AppendLine("#       custom = 自定义主色（配合下面 accent=#rrggbb）");
            sb.AppendLine("theme=" + Theme);
            sb.AppendLine();
            sb.AppendLine("# 自定义主色（仅 theme=custom 时生效）：# 开头的 6 位十六进制");
            sb.AppendLine("accent=" + Accent);
            sb.AppendLine();
            sb.AppendLine("# 配音：male = 男声；female = 女声");
            sb.AppendLine("voice=" + Voice);
            sb.AppendLine();
            sb.AppendLine("# 音频总开关：on = 播放配音；off = 静音播放");
            sb.AppendLine("audio=" + Audio);
            sb.AppendLine();
            sb.AppendLine("# 自动播放：on = 三幕自动往下走；off = 每一步等待点击/回车");
            sb.AppendLine("autoplay=" + Autoplay);
            sb.AppendLine();
            sb.AppendLine("# 总开关：on = 登录时播放；off = 静默跳过（不卸载触发也能停播）");
            sb.AppendLine("enabled=" + Enabled);
            sb.AppendLine();
            sb.AppendLine("# 收尾过渡多少秒还没结束就强制关窗（秒）");
            sb.AppendLine("exit_fallback_seconds=" + FallbackSeconds.ToString());
            sb.AppendLine();
            sb.AppendLine("# ===== 身份卡（显示在授权界面，留空则用页面默认值）=====");
            sb.AppendLine("operator_name=" + OperatorName);
            sb.AppendLine("operator_id=" + OperatorId);
            sb.AppendLine("issuer=" + Issuer);
            sb.AppendLine("clearance_level=" + ClearanceLevel);
            sb.AppendLine("valid_until=" + ValidUntil);
            sb.AppendLine("scope=" + Scope);
            sb.AppendLine("biometric=" + Biometric);
            sb.AppendLine("photo=" + Photo);
            File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
        }
    }

    // ============ 登录触发：写 HKCU Run 键，不需要管理员 ============
    public static class Trigger
    {
        public const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ValueName = "UEGUnlockAnim";
        public const string TaskName = "UEG Unlock Animation";

        public static string PowerShellPath()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\WindowsPowerShell\v1.0\powershell.exe";
        }

        public static string CommandLine(string hostPs1)
        {
            // 优先用原生宿主 UEGHost.exe（无控制台窗口闪现、无运行时编译，冷启动最快）；
            // 找不到就退回 PowerShell 跑 host.ps1
            string dir = Path.GetDirectoryName(hostPs1);
            if (string.IsNullOrEmpty(dir)) { dir = hostPs1; }
            string hostExe = Path.Combine(dir, "UEGHost.exe");
            if (File.Exists(hostExe))
            {
                return "\"" + hostExe + "\"";
            }
            return "\"" + PowerShellPath() + "\" -ExecutionPolicy Bypass -WindowStyle Hidden -NoProfile -NonInteractive -File \"" + hostPs1 + "\"";
        }

        public static bool IsInstalled()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunPath, false))
            {
                if (k == null) { return false; }
                return k.GetValue(ValueName) != null;
            }
        }

        public static void Install(string hostPs1)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunPath, true))
            {
                if (k == null) { throw new InvalidOperationException("无法打开注册表 Run 键：HKCU\\" + RunPath); }
                k.SetValue(ValueName, CommandLine(hostPs1), RegistryValueKind.String);
            }
        }

        public static void Uninstall()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunPath, true))
            {
                if (k == null) { return; }
                if (k.GetValue(ValueName) != null) { k.DeleteValue(ValueName, false); }
            }
        }

        // 旧版计划任务是否仍在（两种触发并存会登录播两遍，需要提醒）
        public static bool OldTaskExists()
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = "schtasks.exe";
                psi.Arguments = "/query /tn \"" + TaskName + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi);
                using (p)
                {
                    if (!p.WaitForExit(5000))
                    {
                        try { p.Kill(); }
                        catch { }
                        return false;
                    }
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    // ============ 照片导入：校验是真图片 → 限尺寸 → 统一存 ueg/assets/photo.jpg ============
    public static class PhotoImporter
    {
        public static string Import(string srcPath, string baseDir)
        {
            if (!File.Exists(srcPath)) { throw new FileNotFoundException("找不到文件：" + srcPath); }
            string outDir = Path.Combine(baseDir, "ueg", "assets");
            if (!Directory.Exists(outDir)) { Directory.CreateDirectory(outDir); }
            string outPath = Path.Combine(outDir, "photo.jpg");

            using (FileStream fs = new FileStream(srcPath, FileMode.Open, FileAccess.Read))
            using (Image img = Image.FromStream(fs, false, false))
            {
                ApplyExifOrientation(img);   // 先修正 EXIF 方向，再按修正后的尺寸算缩放
                int maxEdge = 2048;
                int w = img.Width;
                int h = img.Height;
                double scale = 1.0;
                if (Math.Max(w, h) > maxEdge)
                {
                    scale = (double)maxEdge / (double)Math.Max(w, h);
                }
                int nw = (int)Math.Round((double)w * scale);
                int nh = (int)Math.Round((double)h * scale);
                if (nw < 1) { nw = 1; }
                if (nh < 1) { nh = 1; }
                using (Bitmap bmp = new Bitmap(nw, nh, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.FromArgb(14, 16, 20));   // 深底：透明 PNG 压成 JPEG 不会出黑块
                    g.DrawImage(img, new Rectangle(0, 0, nw, nh));
                    ImageCodecInfo jpg = null;
                    foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                    {
                        if (c.FormatID.Equals(ImageFormat.Jpeg.Guid)) { jpg = c; }
                    }
                    if (jpg == null)
                    {
                        bmp.Save(outPath, ImageFormat.Jpeg);
                    }
                    else
                    {
                        EncoderParameters eps = new EncoderParameters(1);
                        eps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                        bmp.Save(outPath, jpg, eps);
                    }
                }
            }
            return outPath;
        }

        // EXIF 方向标签（PropertyTagOrientation = 0x0112）
        // .NET Framework 的 Image 不会自动应用方向，手机竖拍/侧拍导入后会躺倒，需手动翻转
        private const int ExifOrientationId = 0x0112;

        private static void ApplyExifOrientation(Image img)
        {
            try
            {
                PropertyItem pi = img.GetPropertyItem(ExifOrientationId);
                if (pi == null || pi.Value == null || pi.Value.Length < 1) { return; }
                switch (pi.Value[0])
                {
                    case 2: img.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
                    case 3: img.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                    case 4: img.RotateFlip(RotateFlipType.Rotate180FlipX); break;
                    case 5: img.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                    case 6: img.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                    case 7: img.RotateFlip(RotateFlipType.Rotate270FlipX); break;
                    case 8: img.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
                    default: break;   // 1 = 正常，或标注异常：都不动
                }
            }
            catch
            {
                // 读不到方向（PNG/BMP/EXIF 已被剥离）就按原样导入，不阻断流程
            }
        }
    }

    // ============ 主界面（黑白灰单色系 · 全自绘） ============
    public class MainForm : Form
    {
        private AnimConfig cfg;
        private bool dirty = false;

        private TextBox tbName, tbId, tbIssuer, tbLevel, tbValid, tbScope, tbBio;
        private PictureBox pbPhoto;
        private Label lblPhotoInfo;

        private FlatCombo cbVoice;
        private FlatCombo cbTheme;
        private FlatButton btnAccent;
        private Label lblAccent;
        private Panel swatch;
        private FlatCheck chkAudio, chkAuto, chkEnabled;
        private TextBox txtFallback;
        private Label lblTrigger;

        private Panel pageA, pageB;
        private FlatButton tabA, tabB;

        private Label lblStatus;

        public MainForm()
        {
            cfg = new AnimConfig(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
            cfg.Load();
            BuildUi();
            LoadToUi();
            RefreshTriggerLabel();
        }

        // ---------------- 界面搭建（绝对定位，不靠设计器）----------------
        private void BuildUi()
        {
            Text = "UEG 解锁动画 · 控制台";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(660, 610);
            BackColor = Pal.Bg0;
            Font = Fnt.Base;
            ForeColor = Pal.Ink;
            KeyPreview = true;                                        // 只在键盘操作时显示焦点框
            KeyDown += delegate { FocusCue.Keyboard = true; };
            MouseDown += delegate { FocusCue.Keyboard = false; };

            // 顶部 2px 品牌线（白）
            Panel bar = new Panel();
            bar.Left = 0; bar.Top = 0; bar.Width = 660; bar.Height = 2;
            bar.BackColor = Pal.White;
            Controls.Add(bar);

            // 页眉：标题 + 副标题 + 右侧状态，底部分隔线
            Panel header = new Panel();
            header.Left = 0; header.Top = 2; header.Width = 660; header.Height = 50;
            header.BackColor = Pal.Bg0;
            header.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Pal.Line))
                {
                    e.Graphics.DrawLine(p, 16, header.Height - 1, 644, header.Height - 1);
                }
            };
            Controls.Add(header);

            Label lblTitle = new Label();
            lblTitle.Text = "UEG 解锁动画";
            lblTitle.Font = Fnt.Title;
            lblTitle.ForeColor = Pal.Ink;
            lblTitle.AutoSize = false;
            lblTitle.Left = 24; lblTitle.Top = 10; lblTitle.Width = 320; lblTitle.Height = 26;
            header.Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = "配置控制台 · CONFIGURATION CONSOLE";
            lblSub.Font = Fnt.Small;
            lblSub.ForeColor = Pal.Ink3;
            lblSub.AutoSize = false;
            lblSub.Left = 25; lblSub.Top = 37; lblSub.Width = 380; lblSub.Height = 14;
            header.Controls.Add(lblSub);

            Label lblRight = new Label();
            lblRight.Text = "本地配置 · 无需管理员";
            lblRight.Font = Fnt.Small;
            lblRight.ForeColor = Pal.Ink2;
            lblRight.TextAlign = ContentAlignment.MiddleRight;
            lblRight.AutoSize = false;
            lblRight.Left = 420; lblRight.Top = 21; lblRight.Width = 216; lblRight.Height = 16;
            header.Controls.Add(lblRight);

            // 页签条
            tabA = new FlatButton();
            tabA.Style = FlatButton.BtnStyle.Tab;
            tabA.Text = "个人名片";
            tabA.Active = true;
            tabA.Left = 16; tabA.Top = 66; tabA.Width = 116; tabA.Height = 30;
            tabA.Click += delegate { ShowPage(0); };
            Controls.Add(tabA);

            tabB = new FlatButton();
            tabB.Style = FlatButton.BtnStyle.Tab;
            tabB.Text = "播放与触发";
            tabB.Left = 136; tabB.Top = 66; tabB.Width = 116; tabB.Height = 30;
            tabB.Click += delegate { ShowPage(1); };
            Controls.Add(tabB);

            // 两个页面容器（互斥可见）
            pageA = new Panel();
            pageA.BackColor = Pal.Bg0;
            pageA.Left = 0; pageA.Top = 102; pageA.Width = 660; pageA.Height = 448;
            Controls.Add(pageA);

            pageB = new Panel();
            pageB.BackColor = Pal.Bg0;
            pageB.Left = 0; pageB.Top = 102; pageB.Width = 660; pageB.Height = 448;
            pageB.Visible = false;
            Controls.Add(pageB);

            BuildPageId();
            BuildPagePlay();

            // 底部分隔线 + 操作按钮 + 状态
            Panel sep = new Panel();
            sep.Left = 16; sep.Top = 556; sep.Width = 628; sep.Height = 1;
            sep.BackColor = Pal.Line;
            Controls.Add(sep);

            FlatButton btnSave = new FlatButton();
            btnSave.Style = FlatButton.BtnStyle.Primary;
            btnSave.Text = "保存设置";
            btnSave.Left = 16; btnSave.Top = 566; btnSave.Width = 110; btnSave.Height = 32;
            btnSave.Click += new EventHandler(OnSave);
            Controls.Add(btnSave);

            FlatButton btnPreview = new FlatButton();
            btnPreview.Style = FlatButton.BtnStyle.Ghost;
            btnPreview.Text = "立即预览一次";
            btnPreview.Left = 134; btnPreview.Top = 566; btnPreview.Width = 120; btnPreview.Height = 32;
            btnPreview.Click += new EventHandler(OnPreview);
            Controls.Add(btnPreview);

            FlatButton btnClose = new FlatButton();
            btnClose.Style = FlatButton.BtnStyle.Ghost;
            btnClose.Text = "关闭";
            btnClose.Left = 262; btnClose.Top = 566; btnClose.Width = 80; btnClose.Height = 32;
            btnClose.Click += new EventHandler(OnClose);
            Controls.Add(btnClose);

            lblStatus = new Label();
            lblStatus.Font = Fnt.Small;
            lblStatus.ForeColor = Pal.Ink2;
            lblStatus.AutoSize = false;
            lblStatus.Left = 358; lblStatus.Top = 575; lblStatus.Width = 286; lblStatus.Height = 18;
            Controls.Add(lblStatus);

            AcceptButton = btnSave;
            CancelButton = btnClose;
        }

        // ---------- 页面一：个人名片 ----------
        private void BuildPageId()
        {
            Card cardId = new Card();
            cardId.Title = "身份卡 · 显示在授权界面";
            cardId.Left = 16; cardId.Top = 0; cardId.Width = 628; cardId.Height = 272;
            pageA.Controls.Add(cardId);

            string[] labels = new string[] { "操作员姓名", "证件编号", "签发机构", "密级", "有效期至", "权限范围", "生物特征" };
            TextBox[] boxes = new TextBox[7];
            int y = 32;
            for (int r = 0; r < 7; r++)
            {
                Label lb = new Label();
                lb.Text = labels[r];
                lb.Font = Fnt.Base;
                lb.ForeColor = Pal.Ink2;
                lb.AutoSize = false;
                lb.Left = 28; lb.Top = y + 4; lb.Width = 84; lb.Height = 20;
                cardId.Controls.Add(lb);
                TextBox tb = MakeField(116, y, 492, 26, cardId);
                tb.TextChanged += new EventHandler(OnFieldChanged);
                boxes[r] = tb;
                y += 32;
            }
            tbName = boxes[0];
            tbId = boxes[1];
            tbIssuer = boxes[2];
            tbLevel = boxes[3];
            tbValid = boxes[4];
            tbScope = boxes[5];
            tbBio = boxes[6];

            Card cardPhoto = new Card();
            cardPhoto.Title = "身份卡照片";
            cardPhoto.Left = 16; cardPhoto.Top = 282; cardPhoto.Width = 628; cardPhoto.Height = 160;
            pageA.Controls.Add(cardPhoto);

            Frame frm = new Frame();
            frm.Left = 28; frm.Top = 32; frm.Width = 116; frm.Height = 116;
            cardPhoto.Controls.Add(frm);

            pbPhoto = new PictureBox();
            pbPhoto.Left = 4; pbPhoto.Top = 4; pbPhoto.Width = 108; pbPhoto.Height = 108;
            pbPhoto.SizeMode = PictureBoxSizeMode.Zoom;
            pbPhoto.BorderStyle = BorderStyle.None;
            pbPhoto.BackColor = Pal.Field;
            frm.Controls.Add(pbPhoto);

            FlatButton btnPick = new FlatButton();
            btnPick.Style = FlatButton.BtnStyle.Ghost;
            btnPick.Text = "选择图片并导入…";
            btnPick.Left = 168; btnPick.Top = 36; btnPick.Width = 180; btnPick.Height = 30;
            btnPick.Click += new EventHandler(OnPickPhoto);
            cardPhoto.Controls.Add(btnPick);

            lblPhotoInfo = new Label();
            lblPhotoInfo.Font = Fnt.Small;
            lblPhotoInfo.ForeColor = Pal.Ink2;
            lblPhotoInfo.AutoSize = false;
            lblPhotoInfo.Left = 168; lblPhotoInfo.Top = 78; lblPhotoInfo.Width = 440; lblPhotoInfo.Height = 60;
            cardPhoto.Controls.Add(lblPhotoInfo);
        }

        // ---------- 页面二：播放与触发 ----------
        private void BuildPagePlay()
        {
            Card cardPlay = new Card();
            cardPlay.Title = "播放设置";
            cardPlay.Left = 16; cardPlay.Top = 0; cardPlay.Width = 628; cardPlay.Height = 236;
            pageB.Controls.Add(cardPlay);

            Label lbVoice = MkLabel("配音", 28, 38, cardPlay);
            cbVoice = new FlatCombo();
            cbVoice.Left = 96; cbVoice.Top = 32; cbVoice.Width = 150;
            cbVoice.Items.AddRange(new object[] { "男声", "女声" });
            cbVoice.SelectedIndexChanged += new EventHandler(OnFieldChanged);
            cardPlay.Controls.Add(cbVoice);

            Label lbTheme = MkLabel("主题", 28, 72, cardPlay);
            cbTheme = new FlatCombo();
            cbTheme.Left = 96; cbTheme.Top = 66; cbTheme.Width = 240;
            cbTheme.Items.AddRange(new object[] { "联合政府公文白（UEG）", "550C 磷光终端（暗绿）", "自定义主色…" });
            cbTheme.SelectedIndexChanged += new EventHandler(OnThemeChanged);
            cardPlay.Controls.Add(cbTheme);

            btnAccent = new FlatButton();
            btnAccent.Style = FlatButton.BtnStyle.Ghost;
            btnAccent.Text = "取色…";
            btnAccent.Left = 348; btnAccent.Top = 66; btnAccent.Width = 72; btnAccent.Height = 26;
            btnAccent.Click += new EventHandler(OnPickAccent);
            cardPlay.Controls.Add(btnAccent);

            swatch = new Panel();
            swatch.Left = 434; swatch.Top = 71; swatch.Width = 22; swatch.Height = 16;
            swatch.BackColor = Pal.Field;
            swatch.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Pal.Line2))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, swatch.Width - 1, swatch.Height - 1);
                }
            };
            cardPlay.Controls.Add(swatch);

            lblAccent = new Label();
            lblAccent.Font = Fnt.Small;
            lblAccent.ForeColor = Pal.Ink2;
            lblAccent.AutoSize = false;
            lblAccent.Left = 464; lblAccent.Top = 70; lblAccent.Width = 150; lblAccent.Height = 20;
            cardPlay.Controls.Add(lblAccent);

            chkAudio = MkCheck("播放配音（关闭则静音播放）", 28, 102, 460, cardPlay);
            chkAuto = MkCheck("三幕自动播放（关闭则每一步等待点击/回车）", 28, 132, 500, cardPlay);
            chkEnabled = MkCheck("登录时播放动画（关闭 = 静默跳过，不用卸载触发）", 28, 162, 520, cardPlay);
            chkEnabled.Font = Fnt.BaseBold;

            Label lbFb = MkLabel("兜底秒数", 28, 200, cardPlay);
            txtFallback = MakeField(96, 194, 64, 26, cardPlay);
            txtFallback.TextChanged += new EventHandler(OnFieldChanged);
            txtFallback.KeyPress += new KeyPressEventHandler(OnDigitsOnly);

            Label lbFbHint = new Label();
            lbFbHint.Text = "收尾过渡超过这个秒数还没结束就强制关窗";
            lbFbHint.Font = Fnt.Small;
            lbFbHint.ForeColor = Pal.Ink3;
            lbFbHint.AutoSize = false;
            lbFbHint.Left = 176; lbFbHint.Top = 200; lbFbHint.Width = 420; lbFbHint.Height = 18;
            cardPlay.Controls.Add(lbFbHint);

            Card cardTrig = new Card();
            cardTrig.Title = "登录触发";
            cardTrig.Left = 16; cardTrig.Top = 248; cardTrig.Width = 628; cardTrig.Height = 190;
            pageB.Controls.Add(cardTrig);

            lblTrigger = new Label();
            lblTrigger.Font = Fnt.Base;
            lblTrigger.ForeColor = Pal.Ink;
            lblTrigger.AutoSize = false;
            lblTrigger.Left = 28; lblTrigger.Top = 32; lblTrigger.Width = 572; lblTrigger.Height = 44;
            cardTrig.Controls.Add(lblTrigger);

            FlatButton btnInstall = new FlatButton();
            btnInstall.Style = FlatButton.BtnStyle.Ghost;
            btnInstall.Text = "安装登录触发";
            btnInstall.Left = 28; btnInstall.Top = 86; btnInstall.Width = 150; btnInstall.Height = 32;
            btnInstall.Click += new EventHandler(OnInstall);
            cardTrig.Controls.Add(btnInstall);

            FlatButton btnUninstall = new FlatButton();
            btnUninstall.Style = FlatButton.BtnStyle.Ghost;
            btnUninstall.Text = "卸载登录触发";
            btnUninstall.Left = 186; btnUninstall.Top = 86; btnUninstall.Width = 150; btnUninstall.Height = 32;
            btnUninstall.Click += new EventHandler(OnUninstall);
            cardTrig.Controls.Add(btnUninstall);

            Label lbTrigHint = new Label();
            lbTrigHint.Text = "触发写入当前用户注册表（HKCU Run 键），不需要管理员权限；\n安装后也可以在 任务管理器 → 启动应用 里随时开关。";
            lbTrigHint.Font = Fnt.Small;
            lbTrigHint.ForeColor = Pal.Ink3;
            lbTrigHint.AutoSize = false;
            lbTrigHint.Left = 28; lbTrigHint.Top = 132; lbTrigHint.Width = 572; lbTrigHint.Height = 48;
            cardTrig.Controls.Add(lbTrigHint);
        }

        // ---------------- 小件工厂 ----------------
        private Label MkLabel(string text, int x, int y, Control parent)
        {
            Label lb = new Label();
            lb.Text = text;
            lb.Font = Fnt.Base;
            lb.ForeColor = Pal.Ink2;
            lb.AutoSize = false;
            lb.Left = x; lb.Top = y; lb.Width = 64; lb.Height = 20;
            parent.Controls.Add(lb);
            return lb;
        }

        private FlatCheck MkCheck(string text, int x, int y, int w, Control parent)
        {
            FlatCheck c = new FlatCheck();
            c.Text = text;
            c.Left = x; c.Top = y; c.Width = w; c.Height = 22;
            c.CheckedChanged += new EventHandler(OnFieldChanged);
            parent.Controls.Add(c);
            return c;
        }

        private TextBox MakeField(int x, int y, int w, int h, Control parent)
        {
            FieldBox fb = new FieldBox();
            fb.Left = x; fb.Top = y; fb.Width = w; fb.Height = h;
            TextBox tb = new TextBox();
            tb.BorderStyle = BorderStyle.None;
            tb.BackColor = Pal.Field;
            tb.ForeColor = Pal.Ink;
            tb.Font = Fnt.Field;
            tb.Height = h - 6;
            fb.Attach(tb);
            parent.Controls.Add(fb);
            return tb;
        }

        private void OnDigitsOnly(object sender, KeyPressEventArgs e)
        {
            if (char.IsDigit(e.KeyChar) || e.KeyChar == '\b') { return; }
            e.Handled = true;
        }

        private void ShowPage(int idx)
        {
            bool a = (idx == 0);
            pageA.Visible = a;
            pageB.Visible = !a;
            tabA.Active = a;
            tabB.Active = !a;
            tabA.Invalidate();
            tabB.Invalidate();
        }

        // ---------------- 配置 ↔ 界面 ----------------
        private void LoadToUi()
        {
            tbName.Text = cfg.OperatorName;
            tbId.Text = cfg.OperatorId;
            tbIssuer.Text = cfg.Issuer;
            tbLevel.Text = cfg.ClearanceLevel;
            tbValid.Text = cfg.ValidUntil;
            tbScope.Text = cfg.Scope;
            tbBio.Text = cfg.Biometric;
            cbVoice.SelectedItem = (cfg.Voice == "female") ? "女声" : "男声";
            lblAccent.Tag = (cfg.Accent == null) ? "" : cfg.Accent.Trim();
            string th = (cfg.Theme == null) ? "ueg" : cfg.Theme.Trim().ToLower();
            if (th == "550c") { cbTheme.SelectedItem = "550C 磷光终端（暗绿）"; }
            else if (th == "custom") { cbTheme.SelectedItem = "自定义主色…"; }
            else { cbTheme.SelectedItem = "联合政府公文白（UEG）"; }
            RefreshAccentLabel();
            chkAudio.Checked = (cfg.Audio != "off");
            chkAuto.Checked = (cfg.Autoplay != "off");
            chkEnabled.Checked = (cfg.Enabled != "off");
            int fb = cfg.FallbackSeconds;
            if (fb < 1) { fb = 1; }
            if (fb > 120) { fb = 120; }
            txtFallback.Text = fb.ToString();
            RefreshPhotoPreview();
            dirty = false;
        }

        private void SaveFromUi()
        {
            cfg.OperatorName = tbName.Text.Trim();
            cfg.OperatorId = tbId.Text.Trim();
            cfg.Issuer = tbIssuer.Text.Trim();
            cfg.ClearanceLevel = tbLevel.Text.Trim();
            cfg.ValidUntil = tbValid.Text.Trim();
            cfg.Scope = tbScope.Text.Trim();
            cfg.Biometric = tbBio.Text.Trim();
            object vo = cbVoice.SelectedItem;
            cfg.Voice = ((vo != null && vo.ToString() == "女声")) ? "female" : "male";
            object to = cbTheme.SelectedItem;
            string ts = (to == null) ? "" : to.ToString();
            if (ts == "550C 磷光终端（暗绿）") { cfg.Theme = "550c"; }
            else if (ts == "自定义主色…") { cfg.Theme = "custom"; }
            else { cfg.Theme = "ueg"; }
            cfg.Accent = (lblAccent.Tag == null) ? "" : lblAccent.Tag.ToString();
            cfg.Audio = chkAudio.Checked ? "on" : "off";
            cfg.Autoplay = chkAuto.Checked ? "on" : "off";
            cfg.Enabled = chkEnabled.Checked ? "on" : "off";
            int fb;
            if (!int.TryParse(txtFallback.Text.Trim(), out fb)) { fb = 10; }
            if (fb < 1) { fb = 1; }
            if (fb > 120) { fb = 120; }
            cfg.FallbackSeconds = fb;
            cfg.Save();
        }

        private void RefreshPhotoPreview()
        {
            try
            {
                string rel = (cfg.Photo == null) ? "" : cfg.Photo;
                string p = ResolvePhotoPath(rel);
                if (!File.Exists(p))
                {
                    lblPhotoInfo.Text = "未找到照片文件：\n" + p + "\n（授权界面将使用占位样式）";
                    Image old0 = pbPhoto.Image;
                    pbPhoto.Image = null;
                    if (old0 != null) { old0.Dispose(); }
                    return;
                }
                byte[] bytes = File.ReadAllBytes(p);
                Image old = pbPhoto.Image;
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Image tmp = Image.FromStream(ms))
                {
                    pbPhoto.Image = new Bitmap(tmp);
                }
                if (old != null) { old.Dispose(); }
                lblPhotoInfo.Text = "当前照片：" + p + "\n像素 " + pbPhoto.Image.Width + " × " + pbPhoto.Image.Height +
                    "\n（换图：选一张新图片导入，自动压缩到长边 2048 以内）";
            }
            catch (Exception ex)
            {
                lblPhotoInfo.Text = "照片预览失败：" + ex.Message;
            }
        }

        // 动画页面在 ueg\ 里，CSS url() 的相对路径按"文档基准"解析 → 实际指 ueg\ 下
        // 控制台预览必须用同一基准，否则会指到不存在的 <安装目录>\assets\photo.jpg
        // 注意：配置里的存储值 assets/photo.jpg 是对的，不要改成 ueg/assets/photo.jpg
        private string ResolvePhotoPath(string rel)
        {
            if (string.IsNullOrEmpty(rel)) { return ""; }
            string r = rel.Replace('/', '\\');
            if (Path.IsPathRooted(r)) { return r; }
            string inUeg = Path.Combine(cfg.BaseDir, "ueg", r);
            if (File.Exists(inUeg)) { return inUeg; }
            string inBase = Path.Combine(cfg.BaseDir, r);
            return File.Exists(inBase) ? inBase : inUeg;   // 都不存在时返回主路径用于提示
        }

        private void RefreshTriggerLabel()
        {
            string s = Trigger.IsInstalled()
                ? "登录触发：已安装（HKCU Run 键，免管理员）"
                : "登录触发：未安装";
            if (Trigger.OldTaskExists())
            {
                s += "\n※ 检测到旧版计划任务「UEG Unlock Animation」仍在：两种触发并存会播放两遍，请卸载其一。";
            }
            lblTrigger.Text = s;
        }

        private void SetStatus(string s)
        {
            lblStatus.Text = s;
        }

        // ---------------- 事件 ----------------
        private void OnFieldChanged(object sender, EventArgs e)
        {
            dirty = true;
        }

        // ---------------- 主题 / 自定义主色 ----------------
        private bool ThemeIsCustom()
        {
            return (cbTheme.SelectedItem != null && cbTheme.SelectedItem.ToString() == "自定义主色…");
        }

        private string AccentHex()
        {
            return (lblAccent.Tag == null) ? "" : lblAccent.Tag.ToString();
        }

        private void RefreshAccentLabel()
        {
            string hex = AccentHex();
            if (hex == "")
            {
                lblAccent.Text = "未选择";
                lblAccent.ForeColor = Pal.Ink3;
                swatch.BackColor = Pal.Field;
                return;
            }
            lblAccent.Text = hex;
            lblAccent.ForeColor = Pal.Ink2;
            try { swatch.BackColor = ColorTranslator.FromHtml(hex); }
            catch { swatch.BackColor = Pal.Field; }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            dirty = true;
            bool custom = ThemeIsCustom();
            btnAccent.Enabled = custom;
            lblAccent.Enabled = custom;
            if (custom && AccentHex() == "")
            {
                lblAccent.Text = "未选择（点「取色…」）";
                lblAccent.ForeColor = Pal.Ink3;
                swatch.BackColor = Pal.Field;
            }
            else
            {
                RefreshAccentLabel();
            }
        }

        private void OnPickAccent(object sender, EventArgs e)
        {
            using (ColorDialog dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.AnyColor = true;
                string cur = AccentHex();
                if (cur.Length == 7)
                {
                    try { dlg.Color = ColorTranslator.FromHtml(cur); }
                    catch { }
                }
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                lblAccent.Tag = "#" + dlg.Color.R.ToString("X2") + dlg.Color.G.ToString("X2") + dlg.Color.B.ToString("X2");
                RefreshAccentLabel();
                dirty = true;
            }
        }

        // 主题选了 custom 但没取色：拦住保存，避免存下一份"看起来没变化"的配置
        private bool ThemeAccentOk()
        {
            if (!ThemeIsCustom()) { return true; }
            return Regex.IsMatch(AccentHex(), "^#[0-9a-fA-F]{6}$");
        }

        private void OnPickPhoto(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择身份卡照片";
                dlg.Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                // 主题是「自定义主色」但还没取色：不再拦导入，改为把主题回落成公文白并提示，
                // 这样写进配置的永远是有效组合（否则页面会静默回退成默认主题，用户根本看不出来）
                bool themeFallback = !ThemeAccentOk();
                if (themeFallback) { cbTheme.SelectedItem = "联合政府公文白（UEG）"; }
                try
                {
                    string outPath = PhotoImporter.Import(dlg.FileName, cfg.BaseDir);   // 先导入：失败则配置一字不动
                    cfg.Photo = "assets/photo.jpg";
                    SaveFromUi();                                                       // 后保存：统一写盘
                    dirty = false;
                    RefreshPhotoPreview();
                    SetStatus(themeFallback ? "照片已导入 · 主题未取色已回落为「公文白」" : "照片已导入");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导入失败：\n" + ex.Message, "UEG 控制台", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnInstall(object sender, EventArgs e)
        {
            try
            {
                Trigger.Install(cfg.HostScript);
                RefreshTriggerLabel();
                SetStatus("已安装：下次登录自动播放");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "安装失败：\n" + ex.Message, "UEG 控制台", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnUninstall(object sender, EventArgs e)
        {
            Trigger.Uninstall();
            RefreshTriggerLabel();
            SetStatus("已卸载：登录时不再播放");
        }

        private void OnPreview(object sender, EventArgs e)
        {
            if (!File.Exists(cfg.HostScript))
            {
                MessageBox.Show(this, "找不到 host.ps1：\n" + cfg.HostScript, "UEG 控制台", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                string hostExe = Path.Combine(cfg.BaseDir, "UEGHost.exe");
                if (File.Exists(hostExe))
                {
                    psi.FileName = hostExe;        // 原生宿主：无控制台窗口
                }
                else
                {
                    psi.FileName = Trigger.PowerShellPath();
                    psi.Arguments = "-ExecutionPolicy Bypass -WindowStyle Hidden -NoProfile -NonInteractive -File \"" + cfg.HostScript + "\"";
                }
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (System.Diagnostics.Process.Start(psi)) { }
                SetStatus("已启动一次预览（Esc 可跳过）");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动失败：\n" + ex.Message, "UEG 控制台", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSave(object sender, EventArgs e)
        {
            try
            {
                if (!ThemeAccentOk())
                {
                    MessageBox.Show(this, "主题是「自定义主色」，但还没有选主色。\n请先点「取色…」选一个颜色。", "UEG 控制台", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SaveFromUi();
                dirty = false;
                SetStatus("已保存到 config.txt");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：\n" + ex.Message, "UEG 控制台", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnClose(object sender, EventArgs e)
        {
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!dirty) { return; }
            DialogResult r = MessageBox.Show(this, "有未保存的更改，要保存吗？", "UEG 控制台",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (r == DialogResult.Yes)
            {
                if (!ThemeAccentOk())
                {
                    MessageBox.Show(this, "主题是「自定义主色」，但还没有选主色。\n请先点「取色…」选一个颜色。", "UEG 控制台", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                    return;
                }
                try { SaveFromUi(); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败：\n" + ex.Message, "UEG 控制台", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    e.Cancel = true;
                }
            }
            base.OnFormClosing(e);
        }
    }

    // ============ 入口：默认图形界面；--selftest <目录> 无界面自检 ============
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args != null && args.Length >= 2 && args[0] == "--selftest")
            {
                SelfTest.Run(args[1]);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    static class SelfTest
    {
        public static void Run(string dir)
        {
            StringBuilder log = new StringBuilder();
            int fails = 0;
            try
            {
                if (!File.Exists(Path.Combine(dir, "config.txt")))
                {
                    log.AppendLine("FAIL: 目录里没有 config.txt 副本");
                    WriteResult(dir, log.ToString());
                    Environment.ExitCode = 1;
                    return;
                }

                // 1) 配置往返：Load → 改 → Save → 重载 → 比对
            AnimConfig c = new AnimConfig(dir);
            c.Load();
            string before = c.Issuer;
            string beforeTheme = c.Theme;
            string beforeAccent = c.Accent;
            string beforeVoice = c.Voice;
            int beforeFb = c.FallbackSeconds;
                c.Issuer = "自检-" + DateTime.Now.ToString("HHmmss");
                c.FallbackSeconds = 7;
                c.Voice = "female";
                c.Theme = "550c";
                c.Accent = "#123456";
                c.Save();
                AnimConfig c2 = new AnimConfig(dir);
                c2.Load();
                bool okIssuer = c2.Issuer == c.Issuer;
                bool okFb = c2.FallbackSeconds == 7;
                bool okVoice = c2.Voice == "female";
                bool okTheme = c2.Theme == "550c";
                bool okAccent = c2.Accent == "#123456";
                log.AppendLine("roundtrip issuer : " + (okIssuer ? "PASS" : "FAIL"));
                log.AppendLine("roundtrip fallback: " + (okFb ? "PASS" : "FAIL"));
                log.AppendLine("roundtrip voice  : " + (okVoice ? "PASS" : "FAIL"));
                log.AppendLine("roundtrip theme  : " + (okTheme ? "PASS" : "FAIL"));
                log.AppendLine("roundtrip accent : " + (okAccent ? "PASS" : "FAIL"));
                if (!okIssuer) { fails++; }
                if (!okFb) { fails++; }
                if (!okVoice) { fails++; }
                if (!okTheme) { fails++; }
                if (!okAccent) { fails++; }

                // 2) BOM 校验（PS 5.1 中文系统按 BOM 认 UTF-8，没有就按 GBK 读崩）
                byte[] head = new byte[3];
                using (FileStream fs = new FileStream(Path.Combine(dir, "config.txt"), FileMode.Open, FileAccess.Read))
                {
                    fs.Read(head, 0, 3);
                }
                bool bom = (head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF);
                log.AppendLine("utf8 bom         : " + (bom ? "PASS" : "FAIL"));
                if (!bom) { fails++; }

                // 3) 触发命令串构造
                string cmd = Trigger.CommandLine(Path.Combine(dir, "host.ps1"));
                // 该目录没有 UEGHost.exe → 退回 PowerShell 脚本；两种形态都算通过
                bool cmdOk = cmd.Contains("UEGHost.exe") ||
                    (cmd.Contains("powershell.exe") && cmd.Contains("host.ps1") && cmd.Contains("-ExecutionPolicy Bypass"));
                log.AppendLine("trigger cmd      : " + (cmdOk ? "PASS" : "FAIL"));
                log.AppendLine("  -> " + cmd);
                if (!cmdOk) { fails++; }

                // 还原测试改过的值
                c2.Issuer = before;
                c2.Theme = beforeTheme;
                c2.Accent = beforeAccent;
                c2.Voice = beforeVoice;
                c2.FallbackSeconds = beforeFb;
                c2.Save();
            }
            catch (Exception ex)
            {
                log.AppendLine("EXCEPTION: " + ex.ToString());
                fails++;
            }
            log.AppendLine("RESULT: " + (fails == 0 ? "ALL PASS" : (fails.ToString() + " FAIL")));
            WriteResult(dir, log.ToString());
            Environment.ExitCode = (fails == 0 ? 0 : 1);
        }

        // 结果文件写入：目录不可写/不存在时不能让自检自己崩掉，退回 stderr
        private static void WriteResult(string dir, string text)
        {
            try
            {
                File.WriteAllText(Path.Combine(dir, "selftest_result.txt"), text, new UTF8Encoding(true));
            }
            catch
            {
                try { Console.Error.WriteLine(text); }
                catch { }
            }
        }
    }
}
