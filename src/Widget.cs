// The taskbar widget: a layered child window of the Windows taskbar, drawn with per-pixel alpha.
// It docks just left of the Claude Code Usage Monitor when that is running, otherwise just left
// of the notification area. A cross-process child window shares its input queue with Explorer,
// so the UI thread never blocks: the key read, the network call and every file write or shell
// launch run on background threads.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OpenRouterBar
{
    static class Native
    {
        public const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
        public const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        public const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
        public const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
        public const int ULW_ALPHA = 2;

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int w, int h) { cx = w; cy = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        public static uint ProcessOf(IntPtr h) { uint pid; GetWindowThreadProcessId(h, out pid); return pid; }

        // GetDpiForWindow needs Windows 10 1607+; fall back to 100% scaling without it.
        public static float ScaleOf(IntPtr h)
        {
            try { return Math.Max(1f, GetDpiForWindow(h) / 96f); }
            catch (EntryPointNotFoundException) { return 1f; }
        }
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dstDc, IntPtr pptDst, ref SIZE size,
            IntPtr srcDc, ref POINT ptSrc, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    }

    class Widget : NativeWindow
    {
        readonly App app;
        public readonly IntPtr Taskbar;

        public Widget(App app, IntPtr taskbar)
        {
            this.app = app;
            Taskbar = taskbar;
            CreateParams cp = new CreateParams();
            cp.Caption = "OpenRouterBar";
            cp.Parent = taskbar;
            cp.Style = Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS;
            cp.ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            cp.Width = 1;
            cp.Height = 1;
            CreateHandle(cp);
        }

        public bool Alive { get { return Handle != IntPtr.Zero && Native.IsWindow(Handle) && Native.IsWindow(Taskbar); } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            if (m.Msg == Native.WM_LBUTTONUP) { app.RefreshNow(); return; }
            if (m.Msg == Native.WM_RBUTTONUP) { app.ShowMenu(); return; }
            base.WndProc(ref m);
        }

        public void Present(Bitmap bmp, Rectangle screenRect)
        {
            Native.POINT p = new Native.POINT(screenRect.X, screenRect.Y);
            Native.ScreenToClient(Taskbar, ref p);
            Native.SetWindowPos(Handle, IntPtr.Zero, p.X, p.Y, screenRect.Width, screenRect.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(memDc, hBmp);
            try
            {
                Native.SIZE size = new Native.SIZE(bmp.Width, bmp.Height);
                Native.POINT src = new Native.POINT(0, 0);
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = 1; // AC_SRC_ALPHA
                Native.UpdateLayeredWindow(Handle, screenDc, IntPtr.Zero, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(memDc, old);
                Native.DeleteObject(hBmp);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    class App : ApplicationContext
    {
        const int ManualRefreshCooldownSec = 30, FetchGiveUpSec = 120;

        readonly string dir;
        readonly Settings settings;
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly object gate = new object();
        Widget widget;
        Snapshot last;            // last successful poll
        Snapshot lastAttempt;     // last poll, successful or not
        Snapshot pending;         // handed over by the fetch thread
        bool inFlight;
        int generation;           // a fetch that outlives FetchGiveUpSec is abandoned; its late result is dropped
        DateTime pollStartedUtc;
        DateTime nextPollUtc = DateTime.MinValue;
        DateTime lastManualUtc = DateTime.MinValue;
        int failures;
        string lastRenderKey = "";

        public App()
        {
            dir = Path.GetDirectoryName(Application.ExecutablePath);
            settings = Settings.Load(Path.Combine(dir, "settings.ini"));
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288; }
            catch (NotSupportedException) { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
            tick.Interval = 1000;
            tick.Tick += delegate { OnTick(); };
            tick.Start();
            OnTick();
        }

        string ReadKey()
        {
            string path = Path.Combine(dir, "key.dat");
            if (!File.Exists(path)) return null;
            try
            {
                byte[] blob = Convert.FromBase64String(File.ReadAllText(path).Trim());
                string key = Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser)).Trim();
                return key.Length > 0 ? key : null;
            }
            catch (Exception) { return null; }
        }

        void OnTick()
        {
            try { Tick(); }
            catch (Exception ex) { RecordInternalError(ex); }
        }

        void Tick()
        {
            DateTime now = DateTime.UtcNow;
            Snapshot done = null;
            lock (gate) { if (pending != null) { done = pending; pending = null; inFlight = false; } }
            if (done == null && inFlight && (now - pollStartedUtc).TotalSeconds > FetchGiveUpSec)
            {
                lock (gate) { generation++; inFlight = false; }
                done = new Snapshot();
                done.CheckedUtc = now;
                done.Error = "request timed out";
            }
            if (done != null)
            {
                lastAttempt = done;
                if (done.Ok) { last = done; failures = 0; nextPollUtc = now.AddSeconds(settings.IntervalSec); }
                else if (done.Error.StartsWith("no key")) nextPollUtc = now.AddSeconds(Settings.MinIntervalSec);
                else
                {
                    failures++;
                    nextPollUtc = now.AddSeconds(Math.Min(Settings.MaxIntervalSec, settings.IntervalSec * Math.Pow(2, Math.Min(failures, 3))));
                }
                WriteStatus();
                lastRenderKey = "";
            }
            if (!inFlight && now >= nextPollUtc) StartPoll();
            Render(now);
        }

        void StartPoll()
        {
            int gen;
            lock (gate) { inFlight = true; gen = generation; }
            pollStartedUtc = DateTime.UtcNow;
            Thread t = new Thread(delegate ()
            {
                Snapshot s;
                string key = ReadKey();
                if (key == null)
                {
                    s = new Snapshot();
                    s.CheckedUtc = DateTime.UtcNow;
                    s.Error = "no key (run set-key.ps1)";
                }
                else s = Api.Fetch(key);
                lock (gate) { if (gen == generation) pending = s; }
            });
            t.IsBackground = true;
            t.Start();
        }

        public void RecordInternalError(Exception ex)
        {
            Snapshot s = new Snapshot();
            s.CheckedUtc = DateTime.UtcNow;
            s.Error = "internal error: " + ex.GetType().Name;
            lastAttempt = s;
            lastRenderKey = "";
            WriteStatus();
        }

        public void RefreshNow()
        {
            DateTime now = DateTime.UtcNow;
            if (inFlight || (now - lastManualUtc).TotalSeconds < ManualRefreshCooldownSec) return;
            lastManualUtc = now;
            nextPollUtc = now;
            OnTick();
        }

        void EnsureWidget()
        {
            IntPtr taskbar = Native.FindWindow("Shell_TrayWnd", null);
            IntPtr shell = Native.GetShellWindow();
            if (taskbar == IntPtr.Zero || shell == IntPtr.Zero || Native.ProcessOf(taskbar) != Native.ProcessOf(shell)) return;
            if (widget != null && widget.Alive && widget.Taskbar == taskbar) return;
            if (widget != null) { try { widget.DestroyHandle(); } catch (Exception) { } }
            widget = new Widget(this, taskbar);
            lastRenderKey = "";
        }

        static bool LightTaskbar()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                return v is int && (int)v == 1;
            }
            catch (Exception) { return false; }
        }

        void Render(DateTime now)
        {
            EnsureWidget();
            if (widget == null) return;
            IntPtr tb = widget.Taskbar;

            Native.RECT tbr;
            if (!Native.GetWindowRect(tb, out tbr)) return;
            float s = Native.ScaleOf(tb);
            if (tbr.Bottom - tbr.Top <= 0) return;
            bool light = LightTaskbar();

            double pct = 0;
            string value;
            bool dim = false;
            if (last != null && last.HasLimit)
            {
                pct = last.PercentUsed;
                value = ((int)Math.Round(pct)).ToString(CultureInfo.InvariantCulture) + "%";
                DateTime? next = Reset.NextUtc(last.LimitReset, now);
                if (next.HasValue) value += " \u00B7 " + Reset.Countdown(next.Value, now);
                dim = lastAttempt != null && !lastAttempt.Ok;
            }
            else if (last != null) { value = "no cap"; dim = true; }
            else if (lastAttempt != null && !lastAttempt.Ok) { value = lastAttempt.Error.StartsWith("no key") ? "no key" : "--"; dim = true; }
            else { value = "..."; dim = true; }

            int right;
            Native.RECT ar;
            IntPtr claude = Native.FindWindowEx(tb, IntPtr.Zero, "ClaudeCodeUsageMonitor", null);
            if (claude != IntPtr.Zero && Native.IsWindowVisible(claude) && Native.GetWindowRect(claude, out ar))
                right = ar.Left - (int)Math.Round(settings.GapPx * s);
            else
            {
                IntPtr notify = Native.FindWindowEx(tb, IntPtr.Zero, "TrayNotifyWnd", null);
                if (notify == IntPtr.Zero || !Native.GetWindowRect(notify, out ar)) ar.Left = tbr.Right - (int)(280 * s);
                right = ar.Left - (int)Math.Round(4 * s);
            }

            string renderKey = string.Join("|", new string[] {
                value, pct.ToString("0.0", CultureInfo.InvariantCulture), dim.ToString(), light.ToString(),
                s.ToString(CultureInfo.InvariantCulture), right.ToString(), tbr.Top.ToString(), tbr.Bottom.ToString() });
            if (renderKey == lastRenderKey) return;

            using (Font font = new Font("Segoe UI", 12f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                int seg = (int)Math.Round(10 * s), gap = Math.Max(1, (int)Math.Round(s));
                int barW = seg * 10 + gap * 9, barH = (int)Math.Round(13 * s);
                int padL = (int)Math.Round(6 * s), gapLabel = (int)Math.Round(8 * s), gapValue = (int)Math.Round(6 * s), padR = (int)Math.Round(4 * s);
                SizeF labelSz, valueSz;
                using (Bitmap probe = new Bitmap(1, 1))
                using (Graphics pg = Graphics.FromImage(probe))
                {
                    labelSz = pg.MeasureString(settings.Label, font, PointF.Empty, StringFormat.GenericTypographic);
                    valueSz = pg.MeasureString(value, font, PointF.Empty, StringFormat.GenericTypographic);
                }
                int w = padL + (int)Math.Ceiling(labelSz.Width) + gapLabel + barW + gapValue + (int)Math.Ceiling(valueSz.Width) + padR;
                int h = tbr.Bottom - tbr.Top;

                Color labelC = light ? Color.FromArgb(0x5A, 0x5A, 0x5A) : Color.FromArgb(0xAA, 0xAA, 0xAA);
                Color emptyC = light ? Color.FromArgb(0xC8, 0xC8, 0xC8) : Color.FromArgb(0x44, 0x44, 0x44);
                Color textC = dim ? labelC : (light ? ControlPaint.Dark(settings.Fill, 0.2f) : settings.Text);

                using (Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(1, 0, 0, 0)); // alpha 1 keeps the whole rectangle clickable
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    int barX = padL + (int)Math.Ceiling(labelSz.Width) + gapLabel;
                    int barY = (h - barH) / 2;
                    using (SolidBrush b = new SolidBrush(labelC))
                        g.DrawString(settings.Label, font, b, padL, (h - labelSz.Height) / 2f, StringFormat.GenericTypographic);

                    using (GraphicsPath clip = Rounded(new Rectangle(barX, barY, barW, barH), Math.Max(1, (int)Math.Round(2 * s))))
                    using (SolidBrush eb = new SolidBrush(emptyC))
                    using (SolidBrush fb = new SolidBrush(dim ? Color.FromArgb(160, settings.Fill) : settings.Fill))
                    {
                        g.SetClip(clip);
                        int fillEnd = barX + (int)Math.Round(barW * pct / 100.0);
                        for (int i = 0; i < 10; i++)
                        {
                            int x = barX + i * (seg + gap);
                            g.FillRectangle(eb, x, barY, seg, barH);
                            int fw = Math.Max(0, Math.Min(seg, fillEnd - x));
                            if (fw > 0) g.FillRectangle(fb, x, barY, fw, barH);
                        }
                        g.ResetClip();
                    }

                    using (SolidBrush vb = new SolidBrush(textC))
                        g.DrawString(value, font, vb, barX + barW + gapValue, (h - valueSz.Height) / 2f, StringFormat.GenericTypographic);

                    widget.Present(bmp, new Rectangle(right - w, tbr.Top, w, h));
                }
            }
            lastRenderKey = renderKey;
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static string Money(double v) { return "$" + v.ToString("0.00", CultureInfo.InvariantCulture); }

        static string Ago(DateTime utc)
        {
            TimeSpan t = DateTime.UtcNow - utc;
            if (t.TotalMinutes < 1) return "just now";
            if (t.TotalHours < 1) return ((int)t.TotalMinutes) + " min ago";
            return ((int)t.TotalHours) + " h ago";
        }

        public void ShowMenu()
        {
            menu.Items.Clear();
            DateTime now = DateTime.UtcNow;
            if (last != null && last.HasLimit)
            {
                AddInfo(Money(last.Used) + " of " + Money(last.Limit) + " used (" + ((int)Math.Round(last.PercentUsed)) + "%)");
                DateTime? next = Reset.NextUtc(last.LimitReset, now);
                AddInfo(Money(Math.Max(0, last.Remaining)) + " left" + (next.HasValue
                    ? " \u00B7 resets " + last.LimitReset + ", " + next.Value.ToLocalTime().ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture)
                    : ""));
                AddInfo("Today: " + Money(last.UsageDaily) + " \u00B7 checked " + Ago(last.CheckedUtc));
            }
            else if (last != null) AddInfo("This key has no credit limit set");
            if (lastAttempt != null && !lastAttempt.Ok) AddInfo("Last check failed: " + lastAttempt.Error);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Refresh now", null, delegate { lastManualUtc = DateTime.MinValue; RefreshNow(); });
            menu.Items.Add("Open OpenRouter keys page", null, delegate { Open("https://openrouter.ai/settings/keys"); });
            menu.Items.Add("Open OpenRouter activity", null, delegate { Open("https://openrouter.ai/activity"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { Quit(); });
            menu.Show(Cursor.Position);
        }

        void AddInfo(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Enabled = false;
            menu.Items.Add(item);
        }

        static void Open(string url)
        {
            ThreadPool.QueueUserWorkItem(delegate { try { System.Diagnostics.Process.Start(url); } catch (Exception) { } });
        }

        // status.txt never contains the key; it exists so a user can see why the widget shows "--".
        void WriteStatus()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                Snapshot a = lastAttempt;
                sb.AppendLine("last_attempt_utc=" + (a != null ? a.CheckedUtc.ToString("s") : ""));
                sb.AppendLine("last_attempt_ok=" + (a != null && a.Ok));
                sb.AppendLine("last_error=" + (a != null ? a.Error : ""));
                sb.AppendLine("consecutive_failures=" + failures);
                if (last != null)
                {
                    sb.AppendLine("last_ok_utc=" + last.CheckedUtc.ToString("s"));
                    sb.AppendLine("limit=" + last.Limit.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("limit_remaining=" + last.Remaining.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("percent_used=" + last.PercentUsed.ToString("0.00", CultureInfo.InvariantCulture));
                    sb.AppendLine("usage_daily=" + last.UsageDaily.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("limit_reset=" + last.LimitReset);
                    DateTime? next = Reset.NextUtc(last.LimitReset, DateTime.UtcNow);
                    sb.AppendLine("next_reset_utc=" + (next.HasValue ? next.Value.ToString("s") : ""));
                }
                sb.AppendLine("next_poll_utc=" + nextPollUtc.ToString("s"));
                sb.AppendLine("interval_sec=" + settings.IntervalSec);
                string text = sb.ToString(), path = Path.Combine(dir, "status.txt");
                ThreadPool.QueueUserWorkItem(delegate { try { File.WriteAllText(path, text); } catch (Exception) { } });
            }
            catch (Exception) { }
        }

        void Quit()
        {
            tick.Stop();
            if (widget != null) { try { widget.DestroyHandle(); } catch (Exception) { } }
            ExitThread();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Elevated, the menu's links would open the browser elevated too; it only ever needs the user's own rights.
            if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                MessageBox.Show("OpenRouterBar does not run as administrator. Start it normally.", "OpenRouterBar",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Mutex single;
            bool created;
            try { single = new Mutex(true, @"Local\OpenRouterBar", out created); }
            catch (Exception) { return; }
            using (single)
            {
                if (!created) return;
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                App app;
                try { app = new App(); }
                catch (Exception) { return; }
                Application.ThreadException += delegate (object sender, ThreadExceptionEventArgs e) { app.RecordInternalError(e.Exception); };
                Application.Run(app);
            }
        }
    }
}
