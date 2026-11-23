using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace AgentIsland
{
    // ==================== DirectComposition COM Interop ====================

    [ComImport]
    [Guid("c37ea93a-e7aa-450d-b16f-9746cb0407f3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDCompositionDevice
    {
        [PreserveSig]
        int Commit();

        [PreserveSig]
        int WaitForCommitCompletion();

        [PreserveSig]
        int GetFrameStatistics(IntPtr statistics);

        [PreserveSig]
        int CreateTargetForHwnd(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.Bool)] bool topmost,
            [MarshalAs(UnmanagedType.Interface)] out IDCompositionTarget target);

        [PreserveSig]
        int CreateVisual(
            [MarshalAs(UnmanagedType.Interface)] out IDCompositionVisual visual);
    }

    [ComImport]
    [Guid("eacdd04c-117e-4e17-88f4-d1b12b0e3d89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDCompositionTarget
    {
        [PreserveSig]
        int SetRoot([MarshalAs(UnmanagedType.Interface)] IDCompositionVisual visual);
    }

    [ComImport]
    [Guid("4d93059d-097b-4651-9a60-f0f25116e2f3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDCompositionVisual
    {
    }

    internal static class Program
    {
        private static Mutex _singleInstanceMutex;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        [STAThread]
        private static int Main(string[] args)
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00000000");

            string selfTestDir = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--self-test" && i + 1 < args.Length)
                {
                    selfTestDir = args[i + 1];
                    i++;
                }
            }

            try
            {
                if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                {
                    SetProcessDPIAware();
                }
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }

            string mutexName = string.IsNullOrEmpty(selfTestDir)
                ? "Local\\AgentIsland_v1"
                : "Local\\AgentIsland_SelfTest";

            bool createdNew;
            _singleInstanceMutex = new Mutex(true, mutexName, out createdNew);
            if (!createdNew)
            {
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new IslandForm(selfTestDir);
            Application.Run(form);

            GC.KeepAlive(_singleInstanceMutex);
            return form.SelfTestExitCode;
        }
    }

    internal sealed class SessionItem
    {
        public string ConversationId;
        public string Source; // antigravity | ClaudeCode | Codex | cli | ...
        public string SessionName; // Real conversation title (e.g. from annotations/*.pbtxt)
        public string Project;
        public string Kind; // working | needs_approval | needs_decision | needs_reply | error
        public int Level; // 0=silent running | 1=capsule ticker | 2=glance toast (8s) | 3=blocking urgent (30s)
        public string Title;
        public string Summary;
        public string LastUserInput;
        public string SearchQuery;
        public string TranscriptPath;
        public string AnnotationPath;
        public string ApprovalStepKey;
        public long Timestamp;
    }

    internal sealed class UserSettings
    {
        public int OffsetX = 0;
        public bool Muted = false;
        public int BreakIntervalMinutes = 50;
        public string TodayDate = "";
        public int TodayBreakCount = 0;
        public string Language = "auto"; // auto | zh | en
    }

    internal sealed class IslandForm : Form
    {
        // Win32 Styles & Messages
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
        private const uint LWA_ALPHA = 0x00000002;

        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_SETCURSOR = 0x0020;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSELEAVE = 0x02A3;
        private const int WM_CAPTURECHANGED = 0x0215;
        private const int WM_HOTKEY = 0x0312;

        private const int HTTRANSPARENT = -1;
        private const int HTCLIENT = 1;

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private const int DWMWA_NCRENDERING_ENABLED = 1;
        private const int DWMWA_NCRENDERING_POLICY = 2;
        private const int DWMNCRP_DISABLED = 1;
        private const int GWL_EXSTYLE = -20;
        private const uint TME_LEAVE = 0x00000002;

        // Global HotKeys: Alt+\ (toggle), Alt+Shift+\ (jump to latest AGY alert)
        private const int HOTKEY_TOGGLE_ID = 0x4101;
        private const int HOTKEY_FOCUS_AGY_ID = 0x4102;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_OEM_5 = 0xDC; // '\' key

        [StructLayout(LayoutKind.Sequential)]
        private struct TRACKMOUSEEVENT
        {
            public uint cbSize;
            public uint dwFlags;
            public IntPtr hwndTrack;
            public uint dwHoverTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_RETURN = 0x0D;
        private const ushort VK_K = 0x4B;
        private const ushort VK_V = 0x56;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("dcomp.dll", PreserveSig = true)]
        private static extern int DCompositionCreateDevice(
            IntPtr dxgiDevice,
            [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IDCompositionDevice dcompositionDevice);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point pt);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr hCursor);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;

        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        private readonly string _appDataDir;
        private readonly string _inboxDir;
        private readonly string _settingsPath;
        private readonly string _logPath;
        private readonly string _uiPath;
        private readonly string _actionsPath;
        private readonly string _webviewDataDir;
        private List<Dictionary<string, object>> _actions = new List<Dictionary<string, object>>();

        // DirectComposition + WebView2 Composition objects
        private IDCompositionDevice _dcompDevice;
        private IDCompositionTarget _dcompTarget;
        private IDCompositionVisual _dcompVisual;
        private CoreWebView2Environment _webEnv;
        private CoreWebView2CompositionController _compController;
        private CoreWebView2 _coreWebView;

        private readonly NotifyIcon _trayIcon;
        private readonly System.Windows.Forms.Timer _tickTimer;
        private readonly System.Windows.Forms.Timer _passthroughTimer;
        private FileSystemWatcher _inboxWatcher;

        private UserSettings _settings = new UserSettings();
        private bool _webReady = false;
        private bool _exiting = false;
        private bool _trackingMouseLeave = false;
        private bool _hotkeysRegistered = false;
        private bool _clickThrough = true;
        private bool _wasOutsideMouseDown = false;

        // Native smooth horizontal dragging
        private bool _nativeDragging = false;
        private int _dragStartMouseX = 0;
        private int _dragStartOffsetX = 0;

        // Health State
        private int _seatedSeconds = 0;
        private bool _healthAlertActive = false;
        private int _actionIndex = 0;
        private DateTime? _snoozedUntil = null;
        private DateTime? _pausedUntil = null;
        private readonly Random _rng = new Random();

        // AGY Sessions & Recent History (keep last 5 dismissed/focused alerts so nothing is forgotten)
        private readonly Dictionary<string, SessionItem> _sessions = new Dictionary<string, SessionItem>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SessionItem> _recentHistory = new List<SessionItem>();

        // Fixed invisible canvas (logical CSS px) - never resizes during morph (zero flicker/jump!)
        private const int FixedCanvasLogicalW = 520;
        private const int FixedCanvasLogicalH = 290;
        private int _logicalW = FixedCanvasLogicalW;
        private int _logicalH = FixedCanvasLogicalH;
        private double _webDpr = 0;
        private const int TopMarginLogical = 0;

        // Multi-rect hit regions reported by frontend (supports Split Pill + context menu)
        private List<RectangleF> _hitRects = new List<RectangleF>
        {
            new RectangleF(140, 14, 240, 40)
        };

        // Self-test state
        private readonly string _selfTestDir;
        public int SelfTestExitCode { get; private set; }
        private bool _dcompOk = false;
        private string _dcompHrSummary = "";
        private readonly List<string> _diagLogs = new List<string>();

        // Tray menu items
        private ToolStripMenuItem _muteMenuItem;
        private ToolStripMenuItem _pauseMenuItem;

        public IslandForm(string selfTestDir)
        {
            _selfTestDir = selfTestDir;
            SelfTestExitCode = 0;

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string rootDir = Path.GetFullPath(Path.Combine(exeDir, ".."));
            _uiPath = Path.Combine(rootDir, "ui", "index.html");
            if (!File.Exists(_uiPath))
            {
                _uiPath = Path.Combine(exeDir, "ui", "index.html");
            }
            _actionsPath = Path.Combine(rootDir, "config", "actions.json");
            if (!File.Exists(_actionsPath))
            {
                _actionsPath = Path.Combine(exeDir, "config", "actions.json");
            }

            _appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgentIsland"
            );
            _inboxDir = string.IsNullOrEmpty(_selfTestDir)
                ? Path.Combine(_appDataDir, "inbox")
                : Path.Combine(_selfTestDir, "inbox");
            _settingsPath = Path.Combine(_appDataDir, "settings.json");
            _logPath = string.IsNullOrEmpty(_selfTestDir)
                ? Path.Combine(_appDataDir, "app.log")
                : Path.Combine(_selfTestDir, "selftest.log");
            _webviewDataDir = string.IsNullOrEmpty(_selfTestDir)
                ? Path.Combine(_appDataDir, "webview2-data")
                : Path.Combine(_appDataDir, "webview2-selftest");

            Directory.CreateDirectory(_appDataDir);
            Directory.CreateDirectory(_inboxDir);
            LoadActions();
            if (!string.IsNullOrEmpty(_selfTestDir))
            {
                Directory.CreateDirectory(_selfTestDir);
                _settings.Muted = true;
            }
            else
            {
                LoadSettings();
            }
            EnsureTodayCounter();

            Log("Init AgentIsland | actions=" + _actions.Count + " | selfTest=" + (!string.IsNullOrEmpty(_selfTestDir)));

            this.Text = "AgentIsland";
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;

            if (string.IsNullOrEmpty(_selfTestDir))
            {
                _trayIcon = BuildTrayIcon();
                _tickTimer = new System.Windows.Forms.Timer { Interval = 3000 };
                _tickTimer.Tick += OnHealthAndRadarTick;
                _tickTimer.Start();

                _passthroughTimer = new System.Windows.Forms.Timer { Interval = 30 };
                _passthroughTimer.Tick += OnPassthroughPollTick;
                _passthroughTimer.Start();
            }

            this.Load += OnFormLoad;
            this.Shown += OnFormShown;
            this.FormClosing += OnFormClosing;
        }

        private void Log(string msg)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + msg;
                _diagLogs.Add(line);
                File.AppendAllText(_logPath, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_LAYERED + WS_EX_TRANSPARENT enables true cross-process click-through by default
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT;
                return cp;
            }
        }

        private void SetClickThrough(bool enable)
        {
            if (_clickThrough == enable) return;
            _clickThrough = enable;
            try
            {
                int ex = GetWindowLong(this.Handle, GWL_EXSTYLE);
                if (enable)
                    ex |= (WS_EX_LAYERED | WS_EX_TRANSPARENT);
                else
                    ex = (ex | WS_EX_LAYERED) & ~WS_EX_TRANSPARENT;
                SetWindowLong(this.Handle, GWL_EXSTYLE, ex);
            }
            catch { }
        }

        private void OnPassthroughPollTick(object sender, EventArgs e)
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            if (_nativeDragging)
            {
                SetClickThrough(false);
                return;
            }

            Point pt = Cursor.Position;
            bool inside = IsPointInsideAnyHitRect(pt.X, pt.Y);
            if (inside)
            {
                _wasOutsideMouseDown = false;
                if (_clickThrough)
                {
                    SetClickThrough(false);
                    // Synthesize immediate Move so WebView2 hover starts with 0ms lag
                    if (_compController != null)
                    {
                        try
                        {
                            _compController.SendMouseInput(
                                CoreWebView2MouseEventKind.Move,
                                CoreWebView2MouseEventVirtualKeys.None,
                                0,
                                this.PointToClient(pt));
                        }
                        catch { }
                    }
                }
            }
            else
            {
                if (!_clickThrough)
                {
                    // Notify WebView2 of leave before enabling pass-through so hover preview auto-collapses
                    if (_compController != null)
                    {
                        try
                        {
                            _compController.SendMouseInput(
                                CoreWebView2MouseEventKind.Leave,
                                CoreWebView2MouseEventVirtualKeys.None,
                                0,
                                Point.Empty);
                        }
                        catch { }
                    }
                    _trackingMouseLeave = false;
                    SetClickThrough(true);
                }

                // Detect mouse clicks anywhere outside the island so inline context menu closes like a native menu
                bool outsideDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
                                || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
                if (outsideDown && !_wasOutsideMouseDown)
                {
                    SendCommandToUi("outside_click");
                }
                _wasOutsideMouseDown = outsideDown;
            }
        }

        private void DisableNonClientRendering()
        {
            try
            {
                int policy = DWMNCRP_DISABLED;
                DwmSetWindowAttribute(this.Handle, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
            }
            catch { }
        }

        private bool InitDirectComposition()
        {
            try
            {
                Guid iid = typeof(IDCompositionDevice).GUID;
                int hr1 = DCompositionCreateDevice(IntPtr.Zero, iid, out _dcompDevice);
                if (hr1 != 0 || _dcompDevice == null) return false;

                int hr2 = _dcompDevice.CreateTargetForHwnd(this.Handle, true, out _dcompTarget);
                if (hr2 != 0 || _dcompTarget == null) return false;

                int hr3 = _dcompDevice.CreateVisual(out _dcompVisual);
                if (hr3 != 0 || _dcompVisual == null) return false;

                int hr4 = _dcompTarget.SetRoot(_dcompVisual);
                int hr5 = _dcompDevice.Commit();
                _dcompHrSummary = string.Format("({0:X},{1:X},{2:X},{3:X},{4:X})", hr1, hr2, hr3, hr4, hr5);
                _dcompOk = (hr1 == 0 && hr2 == 0 && hr3 == 0 && hr4 == 0 && hr5 == 0);
                Log("DirectComposition OK | hr=" + _dcompHrSummary);
                return _dcompOk;
            }
            catch (Exception ex)
            {
                Log("DirectComposition EXCEPTION: " + ex.ToString());
                return false;
            }
        }

        private async void OnFormLoad(object sender, EventArgs e)
        {
            SetLayeredWindowAttributes(this.Handle, 0, 255, LWA_ALPHA);
            DisableNonClientRendering();
            ApplyWindowBounds(FixedCanvasLogicalW, FixedCanvasLogicalH, true);
            StartInboxWatcher();

            if (string.IsNullOrEmpty(_selfTestDir))
            {
                bool hk1 = RegisterHotKey(this.Handle, HOTKEY_TOGGLE_ID, MOD_ALT | MOD_NOREPEAT, VK_OEM_5);
                bool hk2 = RegisterHotKey(this.Handle, HOTKEY_FOCUS_AGY_ID, MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, VK_OEM_5);
                _hotkeysRegistered = hk1 || hk2;
                Log(string.Format("Hotkeys registered: Alt+\\={0}, Alt+Shift+\\={1}", hk1, hk2));
            }

            if (!InitDirectComposition())
            {
                MessageBox.Show("DirectComposition 初始化失败，请查看 app.log", "Windows Dynamic Island");
                return;
            }

            try
            {
                _webEnv = await CoreWebView2Environment.CreateAsync(null, _webviewDataDir);
                _compController = await _webEnv.CreateCoreWebView2CompositionControllerAsync(this.Handle);
                _compController.DefaultBackgroundColor = Color.Transparent;
                _compController.RootVisualTarget = _dcompVisual;
                _compController.Bounds = new Rectangle(0, 0, this.ClientSize.Width, this.ClientSize.Height);
                _compController.IsVisible = true;
                _dcompDevice.Commit();

                _compController.CursorChanged += (s, args) =>
                {
                    try
                    {
                        if (_compController != null && _compController.Cursor != IntPtr.Zero)
                        {
                            SetCursor(_compController.Cursor);
                        }
                    }
                    catch { }
                };

                _coreWebView = _compController.CoreWebView2;
                var settings = _coreWebView.Settings;
                settings.AreDefaultContextMenusEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = false;

                if (!string.IsNullOrEmpty(_selfTestDir))
                {
                    await _coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(
                        "window.__diagErrors=[];window.addEventListener('error',e=>window.__diagErrors.push(String(e.message||e)));window.addEventListener('unhandledrejection',e=>window.__diagErrors.push(String(e.reason||e)));"
                    );
                }

                _coreWebView.WebMessageReceived += OnWebMessageReceived;
                _coreWebView.NavigationCompleted += async (s, args) =>
                {
                    _webReady = true;
                    Log("Composition WebView2 NavigationCompleted | IsSuccess=" + args.IsSuccess);
                    DrainInbox();
                    PushStateToUi(false, null);
                    EnsureTopMost();
                    if (!string.IsNullOrEmpty(_selfTestDir))
                    {
                        await RunSelfTestSuiteAsync();
                    }
                };

                if (File.Exists(_uiPath))
                {
                    _coreWebView.Navigate(new Uri(_uiPath).AbsoluteUri);
                }
            }
            catch (Exception ex)
            {
                Log("Composition WebView2 ERROR: " + ex.ToString());
                MessageBox.Show("WebView2 Composition 初始化失败：" + ex.Message, "Windows Dynamic Island");
            }
        }

        private void OnFormShown(object sender, EventArgs e)
        {
            DisableNonClientRendering();
            EnsureTopMost();
            Log(string.Format("FormShown | Handle=0x{0:X} Bounds={1} Scale={2:F2}",
                this.Handle.ToInt64(), this.Bounds, GetScaleFactor()));
        }

        private bool IsForegroundCaptureTool()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                if (pid == 0) return false;
                using (var p = Process.GetProcessById((int)pid))
                {
                    string name = (p.ProcessName ?? "").ToLowerInvariant();
                    return name.Contains("snippingtool")
                        || name.Contains("screenclipping")
                        || name.Contains("screencapture")
                        || name.Contains("sharex")
                        || name.Contains("snipaste");
                }
            }
            catch { return false; }
        }

        private void EnsureTopMost()
        {
            if (IsForegroundCaptureTool()) return;
            try
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
        }

        private double GetScaleFactor()
        {
            if (_webDpr >= 0.75 && _webDpr <= 4.0) return _webDpr;
            try
            {
                uint dpi = GetDpiForWindow(this.Handle);
                if (dpi >= 96) return dpi / 96.0;
            }
            catch { }
            using (Graphics g = this.CreateGraphics())
            {
                return g.DpiX / 96.0;
            }
        }

        private void ApplyWindowBounds(int logicalW, int logicalH, bool ensureTopMost)
        {
            _logicalW = Math.Max(240, Math.Min(760, logicalW));
            _logicalH = Math.Max(80, Math.Min(480, logicalH));

            double scale = GetScaleFactor();
            int physW = (int)Math.Round(_logicalW * scale);
            int physH = (int)Math.Round(_logicalH * scale);
            int topMargin = (int)Math.Round(TopMarginLogical * scale);

            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int centerX = screen.Left + (screen.Width - physW) / 2;
            int offsetPhys = (int)Math.Round(_settings.OffsetX * scale);
            int targetX = centerX + offsetPhys;

            int minX = screen.Left;
            int maxX = screen.Right - physW;
            if (targetX < minX) targetX = minX;
            if (targetX > maxX) targetX = maxX;
            _settings.OffsetX = (int)Math.Round((targetX - centerX) / scale);

            this.SetBounds(targetX, screen.Top + topMargin, physW, physH);

            if (_compController != null)
            {
                _compController.Bounds = new Rectangle(0, 0, physW, physH);
                if (_dcompDevice != null) _dcompDevice.Commit();
            }

            if (ensureTopMost) EnsureTopMost();
        }

        // ==================== Mouse, Hotkey & Multi-Rect Hit-Test ====================

        private static CoreWebView2MouseEventVirtualKeys ExtractVirtualKeys(IntPtr wParam)
        {
            int raw = unchecked((int)wParam.ToInt64()) & 0xFFFF;
            CoreWebView2MouseEventVirtualKeys keys = CoreWebView2MouseEventVirtualKeys.None;
            if ((raw & 0x0001) != 0) keys |= CoreWebView2MouseEventVirtualKeys.LeftButton;
            if ((raw & 0x0002) != 0) keys |= CoreWebView2MouseEventVirtualKeys.RightButton;
            if ((raw & 0x0004) != 0) keys |= CoreWebView2MouseEventVirtualKeys.Shift;
            if ((raw & 0x0008) != 0) keys |= CoreWebView2MouseEventVirtualKeys.Control;
            if ((raw & 0x0010) != 0) keys |= CoreWebView2MouseEventVirtualKeys.MiddleButton;
            return keys;
        }

        private static Point ExtractPoint(IntPtr lParam)
        {
            int v = unchecked((int)lParam.ToInt64());
            int x = (short)(v & 0xFFFF);
            int y = (short)((v >> 16) & 0xFFFF);
            return new Point(x, y);
        }

        private bool IsPointInsideAnyHitRect(int screenX, int screenY)
        {
            Point client = this.PointToClient(new Point(screenX, screenY));
            double scale = GetScaleFactor();
            float cx = (float)(client.X / scale);
            float cy = (float)(client.Y / scale);

            for (int i = 0; i < _hitRects.Count; i++)
            {
                RectangleF r = _hitRects[i];
                if (cx >= r.Left && cx <= r.Right && cy >= r.Top && cy <= r.Bottom)
                {
                    return true;
                }
            }
            return false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ERASEBKGND)
            {
                m.Result = new IntPtr(1);
                return;
            }

            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_TOGGLE_ID)
                {
                    EnsureTopMost();
                    SendCommandToUi("toggle_overview");
                }
                else if (id == HOTKEY_FOCUS_AGY_ID)
                {
                    var topAlert = GetSortedAlerts().FirstOrDefault();
                    if (topAlert != null)
                    {
                        HandleFocusAlert(topAlert.ConversationId);
                    }
                    else
                    {
                        EnsureTopMost();
                        SendCommandToUi("toggle_overview");
                    }
                }
                return;
            }

            if (m.Msg == WM_NCHITTEST)
            {
                if (_nativeDragging)
                {
                    m.Result = new IntPtr(HTCLIENT);
                    return;
                }
                Point screenPt = ExtractPoint(m.LParam);
                m.Result = IsPointInsideAnyHitRect(screenPt.X, screenPt.Y)
                    ? new IntPtr(HTCLIENT)
                    : new IntPtr(HTTRANSPARENT);
                return;
            }

            if (m.Msg == WM_SETCURSOR && _compController != null)
            {
                if (_compController.Cursor != IntPtr.Zero)
                {
                    SetCursor(_compController.Cursor);
                    m.Result = new IntPtr(1);
                    return;
                }
            }

            if (_nativeDragging)
            {
                if (m.Msg == WM_MOUSEMOVE)
                {
                    double scale = GetScaleFactor();
                    int deltaPhys = Cursor.Position.X - _dragStartMouseX;
                    _settings.OffsetX = _dragStartOffsetX + (int)Math.Round(deltaPhys / scale);
                    ApplyWindowBounds(_logicalW, _logicalH, false);
                    return;
                }
                if (m.Msg == WM_LBUTTONUP || m.Msg == WM_CAPTURECHANGED)
                {
                    _nativeDragging = false;
                    ReleaseCapture();
                    SaveSettings();
                    if (_compController != null && m.Msg == WM_LBUTTONUP)
                    {
                        _compController.SendMouseInput(
                            CoreWebView2MouseEventKind.LeftButtonUp,
                            ExtractVirtualKeys(m.WParam),
                            0,
                            ExtractPoint(m.LParam));
                    }
                    return;
                }
            }

            if (_compController != null)
            {
                switch (m.Msg)
                {
                    case WM_MOUSEMOVE:
                        if (!_trackingMouseLeave)
                        {
                            var tme = new TRACKMOUSEEVENT
                            {
                                cbSize = (uint)Marshal.SizeOf(typeof(TRACKMOUSEEVENT)),
                                dwFlags = TME_LEAVE,
                                hwndTrack = this.Handle
                            };
                            TrackMouseEvent(ref tme);
                            _trackingMouseLeave = true;
                        }
                        _compController.SendMouseInput(CoreWebView2MouseEventKind.Move, ExtractVirtualKeys(m.WParam), 0, ExtractPoint(m.LParam));
                        return;

                    case WM_MOUSELEAVE:
                        _trackingMouseLeave = false;
                        _compController.SendMouseInput(CoreWebView2MouseEventKind.Leave, CoreWebView2MouseEventVirtualKeys.None, 0, Point.Empty);
                        return;

                    case WM_LBUTTONDOWN:
                        SetCapture(this.Handle);
                        _compController.SendMouseInput(CoreWebView2MouseEventKind.LeftButtonDown, ExtractVirtualKeys(m.WParam), 0, ExtractPoint(m.LParam));
                        return;

                    case WM_LBUTTONUP:
                        ReleaseCapture();
                        _compController.SendMouseInput(CoreWebView2MouseEventKind.LeftButtonUp, ExtractVirtualKeys(m.WParam), 0, ExtractPoint(m.LParam));
                        return;

                    case WM_LBUTTONDBLCLK:
                        _compController.SendMouseInput(CoreWebView2MouseEventKind.LeftButtonDoubleClick, ExtractVirtualKeys(m.WParam), 0, ExtractPoint(m.LParam));
                        return;

                    case WM_RBUTTONDOWN:
                        _compController.SendMouseInput(CoreWebView2MouseEventKind.RightButtonDown, ExtractVirtualKeys(m.WParam), 0, ExtractPoint(m.LParam));
                        return;

                    case WM_RBUTTONUP:
                        _compController.SendMouseInput(CoreWebView2MouseEventKind.RightButtonUp, ExtractVirtualKeys(m.WParam), 0, ExtractPoint(m.LParam));
                        return;

                    case WM_MOUSEWHEEL:
                        uint wheelDelta = (uint)((unchecked((int)m.WParam.ToInt64()) >> 16) & 0xFFFF);
                        Point screenPt = ExtractPoint(m.LParam);
                        Point clientPt = this.PointToClient(screenPt);
                        _compController.SendMouseInput(CoreWebView2MouseEventKind.Wheel, ExtractVirtualKeys(m.WParam), wheelDelta, clientPt);
                        return;
                }
            }

            base.WndProc(ref m);
        }

        // ==================== Tray Icon ====================

        private NotifyIcon BuildTrayIcon()
        {
            var menu = new ContextMenuStrip();

            var showItem = new ToolStripMenuItem("展开 / 收起 (Alt + \\)", null, (s, e) => { EnsureTopMost(); SendCommandToUi("toggle_overview"); });
            var testHealth = new ToolStripMenuItem("测试：护腰跟练卡", null, (s, e) => TriggerTestHealth());
            var testAgy = new ToolStripMenuItem("测试：AGY 待审批/拍板", null, (s, e) => TriggerTestAgy());
            var resetSeat = new ToolStripMenuItem("刚站过（清零 + 打卡）", null, (s, e) => CompleteHealthBreak(true));
            _pauseMenuItem = new ToolStripMenuItem("暂停久坐提醒 1 小时", null, (s, e) => TogglePauseHealth());
            _muteMenuItem = new ToolStripMenuItem(_settings.Muted ? "开启提示音" : "静音提示音", null, (s, e) => ToggleMute());
            var recenterItem = new ToolStripMenuItem("重置到顶部居中", null, (s, e) =>
            {
                _settings.OffsetX = 0;
                SaveSettings();
                ApplyWindowBounds(_logicalW, _logicalH, true);
            });
            var reloadUi = new ToolStripMenuItem("热重载前端与动作库 (ui & actions.json)", null, (s, e) =>
            {
                LoadActions();
                if (_coreWebView != null) _coreWebView.Reload();
            });
            var exitItem = new ToolStripMenuItem("退出 AgentIsland", null, (s, e) =>
            {
                _exiting = true;
                this.Close();
            });

            var langMenu = new ToolStripMenuItem("Language / 语言");
            langMenu.DropDownItems.Add(new ToolStripMenuItem("Auto (System / 跟随系统)", null, (s, e) => SetLanguagePreference("auto")));
            langMenu.DropDownItems.Add(new ToolStripMenuItem("简体中文", null, (s, e) => SetLanguagePreference("zh")));
            langMenu.DropDownItems.Add(new ToolStripMenuItem("English", null, (s, e) => SetLanguagePreference("en")));

            menu.Items.Add(showItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(testHealth);
            menu.Items.Add(testAgy);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(resetSeat);
            menu.Items.Add(_pauseMenuItem);
            menu.Items.Add(_muteMenuItem);
            menu.Items.Add(langMenu);
            menu.Items.Add(recenterItem);
            menu.Items.Add(reloadUi);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            var icon = new NotifyIcon
            {
                Icon = CreateTrayIconBitmap(),
                Text = "Windows 灵动岛 · 护腰 & AGY 雷达",
                ContextMenuStrip = menu,
                Visible = true
            };
            icon.DoubleClick += (s, e) => { EnsureTopMost(); SendCommandToUi("toggle_overview"); };
            return icon;
        }

        private static Icon CreateTrayIconBitmap()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var bg = new SolidBrush(Color.FromArgb(20, 22, 28)))
                using (var pen = new Pen(Color.FromArgb(140, 255, 255, 255), 1.5f))
                {
                    FillRoundedRect(g, bg, 1, 9, 20, 14, 7);
                    DrawRoundedRect(g, pen, 1, 9, 20, 14, 7);
                    g.FillEllipse(bg, 22, 10, 9, 12);
                }
                using (var dot = new SolidBrush(Color.FromArgb(59, 130, 246)))
                {
                    g.FillEllipse(dot, 6, 13, 6, 6);
                }
                using (var ring = new Pen(Color.FromArgb(34, 197, 94), 2f))
                {
                    g.DrawEllipse(ring, 23, 12, 7, 7);
                }
                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        private static void FillRoundedRect(Graphics g, Brush brush, int x, int y, int w, int h, int r)
        {
            using (var path = CreateRoundedPath(x, y, w, h, r)) g.FillPath(brush, path);
        }

        private static void DrawRoundedRect(Graphics g, Pen pen, int x, int y, int w, int h, int r)
        {
            using (var path = CreateRoundedPath(x, y, w, h, r)) g.DrawPath(pen, path);
        }

        private static GraphicsPath CreateRoundedPath(int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath();
            int d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ==================== Approval Radar (transcript.jsonl tail check) ====================

        private bool CheckTranscriptApprovals()
        {
            bool newApprovalDetected = false;
            bool stateChanged = false;

            foreach (var session in _sessions.Values.ToList())
            {
                if (string.IsNullOrEmpty(session.TranscriptPath)) continue;
                if (session.Kind != "working" && session.Kind != "needs_approval") continue;
                if (!File.Exists(session.TranscriptPath)) continue;

                string lastLine = ReadLastNonEmptyLine(session.TranscriptPath);
                if (string.IsNullOrWhiteSpace(lastLine)) continue;

                try
                {
                    var obj = _json.DeserializeObject(lastLine) as Dictionary<string, object>;
                    if (obj == null) continue;

                    string type = GetStr(obj, "type");
                    string stepIdx = GetStr(obj, "step_index");
                    string createdAtStr = GetStr(obj, "created_at");

                    bool waitingForApproval = false;
                    string approvalTitle = "待批准操作";
                    string approvalSummary = "";

                    if (string.Equals(type, "PLANNER_RESPONSE", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime createdUtc;
                        double ageSec = 0;
                        if (DateTime.TryParse(createdAtStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out createdUtc))
                        {
                            ageSec = (DateTime.UtcNow - createdUtc).TotalSeconds;
                        }

                        object tcObj;
                        if (obj.TryGetValue("tool_calls", out tcObj))
                        {
                            IEnumerable calls = tcObj as IEnumerable;
                            if (calls != null)
                            {
                                foreach (object c in calls)
                                {
                                    var tc = c as Dictionary<string, object>;
                                    if (tc == null) continue;
                                    string tname = GetStr(tc, "name");
                                    object argsObj;
                                    tc.TryGetValue("args", out argsObj);
                                    var targs = argsObj as Dictionary<string, object>;

                                    if (tname == "run_command" && targs != null)
                                    {
                                        string bypass = GetStr(targs, "BypassSandbox").Trim().Trim('"').ToLowerInvariant();
                                        if (bypass == "true" && ageSec >= 4.0)
                                        {
                                            waitingForApproval = true;
                                            approvalTitle = "待批准命令";
                                            string sum = TrimQuotes(GetStr(targs, "toolSummary"));
                                            string cmd = TrimQuotes(GetStr(targs, "CommandLine"));
                                            approvalSummary = !string.IsNullOrEmpty(sum) ? ("越沙盒命令：" + sum) : ("越沙盒命令：" + Shorten(cmd, 72));
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (waitingForApproval)
                    {
                        string key = stepIdx + ":" + approvalSummary;
                        if (session.Kind != "needs_approval" || session.ApprovalStepKey != key)
                        {
                            session.Kind = "needs_approval";
                            session.Level = 3;
                            session.Title = approvalTitle;
                            session.Summary = approvalSummary;
                            session.ApprovalStepKey = key;
                            session.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            newApprovalDetected = true;
                            stateChanged = true;
                            Log("ApprovalRadar DETECTED | conv=" + session.ConversationId + " | " + approvalTitle + " | " + approvalSummary);
                        }
                    }
                    else if (session.Kind == "needs_approval")
                    {
                        // User approved and tool progressed past PLANNER_RESPONSE
                        session.Kind = "working";
                        session.Level = 0;
                        session.Title = "思考中";
                        session.ApprovalStepKey = "";
                        session.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        stateChanged = true;
                        Log("ApprovalRadar CLEARED | conv=" + session.ConversationId);
                    }
                }
                catch { }
            }

            if (newApprovalDetected)
            {
                PlayAlertSound(false);
                EnsureTopMost();
                PushStateToUi(true, "agy");
            }
            else if (stateChanged)
            {
                PushStateToUi(false, null);
            }

            return newApprovalDetected;
        }

        private static string ReadLastNonEmptyLine(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length == 0) return "";
                    long readLen = Math.Min(fs.Length, 16384);
                    fs.Seek(-readLen, SeekOrigin.End);
                    byte[] buf = new byte[readLen];
                    int n = fs.Read(buf, 0, buf.Length);
                    string chunk = Encoding.UTF8.GetString(buf, 0, n);
                    string[] lines = chunk.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = lines.Length - 1; i >= 0; i--)
                    {
                        string t = lines[i].Trim();
                        if (t.StartsWith("{") && t.EndsWith("}")) return t;
                    }
                }
            }
            catch { }
            return "";
        }

        private static string TrimQuotes(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                s = s.Substring(1, s.Length - 2);
            }
            return s.Replace("\\\"", "\"").Replace("\\n", " ").Trim();
        }

        private static string Shorten(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > maxLen ? s.Substring(0, maxLen - 1) + "…" : s;
        }

        // ==================== Health Timer & Tick ====================

        private static int GetIdleSeconds()
        {
            try
            {
                LASTINPUTINFO lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (GetLastInputInfo(ref lii))
                {
                    uint elapsedMs = unchecked((uint)Environment.TickCount - lii.dwTime);
                    return (int)(elapsedMs / 1000);
                }
            }
            catch { }
            return 0;
        }

        private static bool IsWorkstationLocked()
        {
            IntPtr hDesk = IntPtr.Zero;
            try
            {
                hDesk = OpenInputDesktop(0, false, 0x0001);
                return hDesk == IntPtr.Zero;
            }
            catch { return false; }
            finally { if (hDesk != IntPtr.Zero) CloseDesktop(hDesk); }
        }

        private void EnsureTodayCounter()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_settings.TodayDate != today)
            {
                _settings.TodayDate = today;
                _settings.TodayBreakCount = 0;
                if (string.IsNullOrEmpty(_selfTestDir)) SaveSettings();
            }
        }

        private bool CheckViewedSessions()
        {
            bool anyCleared = false;
            foreach (var session in _sessions.Values.ToList())
            {
                if (session.Kind == "working") continue;
                if (string.IsNullOrEmpty(session.AnnotationPath) || !File.Exists(session.AnnotationPath)) continue;

                long viewedMs = ReadAnnotationLastViewMs(session.AnnotationPath);
                if (viewedMs > session.Timestamp + 2500)
                {
                    Log("AutoCleared viewed session | conv=" + session.ConversationId + " | title=" + (session.SessionName ?? session.Project));
                    ArchiveAndRemoveSession(session.ConversationId);
                    anyCleared = true;
                }
            }
            return anyCleared;
        }

        private static long ReadAnnotationLastViewMs(string annotationPath)
        {
            try
            {
                string raw = File.ReadAllText(annotationPath, Encoding.UTF8);
                Match m = Regex.Match(raw, @"last_user_view_time\s*:\s*\{[^}]*seconds\s*:\s*(\d+)");
                if (m.Success)
                {
                    long sec;
                    if (long.TryParse(m.Groups[1].Value, out sec))
                    {
                        return sec * 1000L;
                    }
                }
            }
            catch { }
            return 0;
        }

        private void OnHealthAndRadarTick(object sender, EventArgs e)
        {
            EnsureTodayCounter();

            // 1. Check transcript.jsonl for blocked approval prompts
            if (CheckTranscriptApprovals())
            {
                UpdateTrayTooltip();
                return;
            }

            // 1.5 Auto-dismiss Antigravity alerts once the user has switched into and viewed that session
            CheckViewedSessions();

            // 2. Clean stale "working" sessions older than 25 minutes
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var staleWorking = _sessions.Values
                .Where(s => s.Kind == "working" && (nowMs - s.Timestamp) > 25 * 60 * 1000)
                .Select(s => s.ConversationId)
                .ToList();
            foreach (var id in staleWorking) _sessions.Remove(id);

            // 3. Auto-expire pause
            if (_pausedUntil.HasValue && DateTime.Now >= _pausedUntil.Value)
            {
                _pausedUntil = null;
                UpdatePauseMenuText();
            }

            // 4. Health timer (+3s per tick)
            bool locked = IsWorkstationLocked();
            int idleSec = GetIdleSeconds();

            if (locked || idleSec >= 300)
            {
                if (_seatedSeconds > 0 || _healthAlertActive || _snoozedUntil.HasValue)
                {
                    _seatedSeconds = 0;
                    _healthAlertActive = false;
                    _snoozedUntil = null;
                }
            }
            else if (!_pausedUntil.HasValue)
            {
                _seatedSeconds += 3;
                int thresholdSec = _settings.BreakIntervalMinutes * 60;
                bool snoozeExpired = !_snoozedUntil.HasValue || DateTime.Now >= _snoozedUntil.Value;

                if (_seatedSeconds >= thresholdSec && !_healthAlertActive && snoozeExpired)
                {
                    _healthAlertActive = true;
                    _snoozedUntil = null;
                    _actionIndex = _rng.Next(0, Math.Max(1, _actions.Count));
                    PlayAlertSound(true);
                    PushStateToUi(true, "health");
                    UpdateTrayTooltip();
                    return;
                }
            }

            UpdateTrayTooltip();
            PushStateToUi(false, null);
        }

        private void UpdateTrayTooltip()
        {
            if (_trayIcon == null) return;
            try
            {
                int mins = _seatedSeconds / 60;
                int pending = _sessions.Values.Count(s => s.Kind != "working");
                string text = ResolveEffectiveLanguage() == "en"
                    ? string.Format("AgentIsland · Sit {0}/{1}m · Breaks {2} · Pending {3}",
                        mins, _settings.BreakIntervalMinutes, _settings.TodayBreakCount, pending)
                    : string.Format("AgentIsland · 坐 {0}/{1}m · 今日救腰 {2} 次 · 待介入 {3}",
                        mins, _settings.BreakIntervalMinutes, _settings.TodayBreakCount, pending);
                if (text.Length > 63) text = text.Substring(0, 63);
                _trayIcon.Text = text;
            }
            catch { }
        }

        private void CompleteHealthBreak(bool countCheckin)
        {
            EnsureTodayCounter();
            if (countCheckin)
            {
                _settings.TodayBreakCount++;
                if (string.IsNullOrEmpty(_selfTestDir)) SaveSettings();
            }
            _seatedSeconds = 0;
            _healthAlertActive = false;
            _snoozedUntil = null;
            UpdateTrayTooltip();
            PushStateToUi(false, null);
        }

        private void SnoozeHealth(int minutes)
        {
            _healthAlertActive = false;
            _snoozedUntil = DateTime.Now.AddMinutes(minutes);
            PushStateToUi(false, null);
        }

        private void SetBreakInterval(int minutes)
        {
            if (minutes < 10 || minutes > 120) return;
            _settings.BreakIntervalMinutes = minutes;
            if (string.IsNullOrEmpty(_selfTestDir)) SaveSettings();
            UpdateTrayTooltip();
            PushStateToUi(false, null);
        }

        private void TogglePauseHealth()
        {
            if (_pausedUntil.HasValue) _pausedUntil = null;
            else
            {
                _pausedUntil = DateTime.Now.AddHours(1);
                _healthAlertActive = false;
            }
            UpdatePauseMenuText();
            PushStateToUi(false, null);
        }

        private void UpdatePauseMenuText()
        {
            if (_pauseMenuItem != null)
            {
                _pauseMenuItem.Text = _pausedUntil.HasValue ? "恢复久坐提醒" : "暂停久坐提醒 1 小时";
            }
        }

        private string ResolveEffectiveLanguage()
        {
            string pref = (_settings.Language ?? "auto").Trim().ToLowerInvariant();
            if (pref == "zh" || pref == "en") return pref;
            try
            {
                string uiLang = CultureInfo.CurrentUICulture.Name ?? "";
                if (uiLang.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh";
            }
            catch { }
            return "en";
        }

        private void SetLanguagePreference(string lang)
        {
            string norm = (lang ?? "auto").Trim().ToLowerInvariant();
            if (norm != "auto" && norm != "zh" && norm != "en") norm = "auto";
            _settings.Language = norm;
            if (string.IsNullOrEmpty(_selfTestDir)) SaveSettings();
            UpdateTrayTooltip();
            PushStateToUi(false, null);
        }

        private void ToggleMute()
        {
            _settings.Muted = !_settings.Muted;
            if (string.IsNullOrEmpty(_selfTestDir)) SaveSettings();
            if (_muteMenuItem != null)
            {
                _muteMenuItem.Text = _settings.Muted ? "开启提示音" : "静音提示音";
            }
            PushStateToUi(false, null);
        }

        private void TriggerTestHealth()
        {
            _seatedSeconds = _settings.BreakIntervalMinutes * 60;
            _healthAlertActive = true;
            _snoozedUntil = null;
            _actionIndex = (_actionIndex + 1) % Math.Max(1, _actions.Count);
            PlayAlertSound(true);
            EnsureTopMost();
            PushStateToUi(true, "health");
        }

        private void TriggerTestAgy()
        {
            string id = "test-" + DateTime.Now.Ticks;
            _sessions[id] = new SessionItem
            {
                ConversationId = id,
                Source = "antigravity",
                Project = "AgentIsland",
                Kind = "needs_approval",
                Title = "待批准命令",
                Summary = "越沙盒命令：Run full-stack verify.ps1 self-test suite",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            PlayAlertSound(false);
            EnsureTopMost();
            PushStateToUi(true, "agy");
        }

        private void PlayAlertSound(bool isHealth)
        {
            if (_settings.Muted) return;
            try
            {
                if (isHealth) SystemSounds.Exclamation.Play();
                else SystemSounds.Asterisk.Play();
            }
            catch { }
        }

        private void ArchiveAndRemoveSession(string convId)
        {
            if (string.IsNullOrEmpty(convId)) return;
            SessionItem item;
            if (_sessions.TryGetValue(convId, out item))
            {
                _sessions.Remove(convId);
                if (item.Kind != "working")
                {
                    _recentHistory.RemoveAll(h => h.ConversationId == convId);
                    _recentHistory.Insert(0, item);
                    if (_recentHistory.Count > 5) _recentHistory.RemoveRange(5, _recentHistory.Count - 5);
                }
            }
        }

        private void RestoreHistoryItem(string convId)
        {
            var found = _recentHistory.FirstOrDefault(h => h.ConversationId == convId);
            if (found == null) return;
            _recentHistory.Remove(found);
            _sessions[found.ConversationId] = found;
            UpdateTrayTooltip();
            PushStateToUi(true, "agy");
        }

        // ==================== Inbox IPC Watcher ====================

        private void StartInboxWatcher()
        {
            _inboxWatcher = new FileSystemWatcher(_inboxDir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _inboxWatcher.Created += OnInboxFileChanged;
            _inboxWatcher.Renamed += OnInboxFileChanged;
        }

        private void OnInboxFileChanged(object sender, FileSystemEventArgs e)
        {
            if (this.IsDisposed) return;
            try { this.BeginInvoke((Action)(() => DrainInbox())); }
            catch { }
        }

        private void DrainInbox()
        {
            string[] files;
            try { files = Directory.GetFiles(_inboxDir, "*.json").OrderBy(f => f).ToArray(); }
            catch { return; }

            bool hasNewAlert = false;
            bool hasHealthTest = false;
            bool changed = false;

            foreach (string file in files)
            {
                string raw = null;
                for (int retry = 0; retry < 3; retry++)
                {
                    try
                    {
                        raw = File.ReadAllText(file, Encoding.UTF8);
                        File.Delete(file);
                        break;
                    }
                    catch { Thread.Sleep(20); }
                }
                if (string.IsNullOrWhiteSpace(raw)) continue;

                try
                {
                    var dict = _json.DeserializeObject(raw) as Dictionary<string, object>;
                    if (dict == null) continue;

                    string kind = GetStr(dict, "kind");
                    Log("Consumed inbox event | kind=" + kind);

                    if (kind == "health_test")
                    {
                        _seatedSeconds = _settings.BreakIntervalMinutes * 60;
                        _healthAlertActive = true;
                        _snoozedUntil = null;
                        _actionIndex = _rng.Next(0, Math.Max(1, _actions.Count));
                        hasHealthTest = true;
                        changed = true;
                        continue;
                    }

                    string convId = GetStr(dict, "conversationId");
                    if (string.IsNullOrEmpty(convId)) convId = Guid.NewGuid().ToString("N");

                    if (kind == "cleared")
                    {
                        if (_sessions.Remove(convId)) changed = true;
                        continue;
                    }

                    string prevTranscript = "";
                    string prevAnnotation = "";
                    string prevSessionName = "";
                    string prevQuery = "";
                    SessionItem prev;
                    if (_sessions.TryGetValue(convId, out prev) && prev != null)
                    {
                        prevTranscript = prev.TranscriptPath ?? "";
                        prevAnnotation = prev.AnnotationPath ?? "";
                        prevSessionName = prev.SessionName ?? "";
                        prevQuery = prev.SearchQuery ?? "";
                    }

                    string newTranscript = GetStr(dict, "transcriptPath");
                    string resolvedTranscript = string.IsNullOrEmpty(newTranscript) ? prevTranscript : newTranscript;
                    string newAnnotation = GetStr(dict, "annotationPath");
                    string resolvedAnnotation = string.IsNullOrEmpty(newAnnotation) ? prevAnnotation : newAnnotation;
                    string newSessionName = GetStr(dict, "sessionName");
                    string resolvedSessionName = string.IsNullOrEmpty(newSessionName) ? prevSessionName : newSessionName;

                    string lastInput = GetStr(dict, "lastUserInput");
                    string src = GetStr(dict, "source");
                    string projectStr = string.IsNullOrEmpty(GetStr(dict, "project")) ? "Antigravity" : GetStr(dict, "project");
                    string summaryStr = GetStr(dict, "summary");
                    string searchQuery = ResolveSearchQuery(resolvedSessionName, lastInput, resolvedTranscript, prevQuery, summaryStr);

                    int level = GetInt(dict, "level", InferNotificationLevel(kind));

                    var item = new SessionItem
                    {
                        ConversationId = convId,
                        Source = string.IsNullOrEmpty(src) ? "antigravity" : src,
                        SessionName = resolvedSessionName,
                        Project = projectStr,
                        Kind = kind,
                        Level = level,
                        Title = GetStr(dict, "title"),
                        Summary = summaryStr,
                        LastUserInput = lastInput,
                        SearchQuery = searchQuery,
                        TranscriptPath = resolvedTranscript,
                        AnnotationPath = resolvedAnnotation,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    _sessions[convId] = item;
                    changed = true;

                    // Only L2 (decision/error) and L3 (blocking approval) actively expand & chime;
                    // L1 (needs_reply / info) stays quietly inside the rotating capsule ticker!
                    if (level >= 2)
                    {
                        hasNewAlert = true;
                    }
                }
                catch (Exception ex)
                {
                    Log("Parse inbox ERROR: " + ex.Message);
                }
            }

            if (changed)
            {
                UpdateTrayTooltip();
                EnsureTopMost();
                if (hasNewAlert)
                {
                    PlayAlertSound(false);
                    PushStateToUi(true, "agy");
                }
                else if (hasHealthTest)
                {
                    PlayAlertSound(true);
                    PushStateToUi(true, "health");
                }
                else
                {
                    PushStateToUi(false, null);
                }
            }
        }

        private static string GetStr(Dictionary<string, object> dict, string key)
        {
            object v;
            return (dict != null && dict.TryGetValue(key, out v) && v != null) ? v.ToString() : "";
        }

        private static int InferNotificationLevel(string kind)
        {
            if (kind == "needs_approval") return 3;
            if (kind == "needs_decision" || kind == "error") return 2;
            if (kind == "needs_reply") return 1;
            return 0;
        }

        private List<SessionItem> GetSortedAlerts()
        {
            return _sessions.Values
                .Where(s => s.Kind != "working")
                .OrderByDescending(s => s.Level > 0 ? s.Level : InferNotificationLevel(s.Kind))
                .ThenByDescending(s => s.Kind == "error" ? 1 : 0)
                .ThenBy(s => s.Timestamp) // oldest waiting first
                .ToList();
        }

        // ==================== WebView2 Bridge ====================

        private void PushStateToUi(bool triggerExpand, string focusKind)
        {
            if (!_webReady || _coreWebView == null) return;

            var alerts = GetSortedAlerts()
                .Select(s => new Dictionary<string, object>
                {
                    { "id", s.ConversationId },
                    { "source", s.Source ?? "antigravity" },
                    { "sessionName", !string.IsNullOrEmpty(s.SessionName) ? s.SessionName : s.Project },
                    { "project", s.Project },
                    { "kind", s.Kind },
                    { "level", s.Level > 0 ? s.Level : InferNotificationLevel(s.Kind) },
                    { "title", s.Title },
                    { "summary", s.Summary },
                    { "timestamp", s.Timestamp }
                })
                .ToList();

            var working = _sessions.Values
                .Where(s => s.Kind == "working")
                .OrderByDescending(s => s.Timestamp)
                .Select(s => new Dictionary<string, object>
                {
                    { "id", s.ConversationId },
                    { "source", s.Source ?? "antigravity" },
                    { "sessionName", !string.IsNullOrEmpty(s.SessionName) ? s.SessionName : s.Project },
                    { "project", s.Project },
                    { "summary", s.Summary },
                    { "timestamp", s.Timestamp }
                })
                .ToList();

            var history = _recentHistory
                .Select(s => new Dictionary<string, object>
                {
                    { "id", s.ConversationId },
                    { "source", s.Source ?? "antigravity" },
                    { "sessionName", !string.IsNullOrEmpty(s.SessionName) ? s.SessionName : s.Project },
                    { "project", s.Project },
                    { "kind", s.Kind },
                    { "title", s.Title },
                    { "summary", s.Summary },
                    { "timestamp", s.Timestamp }
                })
                .ToList();

            int hour = DateTime.Now.Hour;
            bool isLateNight = (hour >= 1 && hour < 6);
            int pauseRemainMinutes = (_pausedUntil.HasValue && DateTime.Now < _pausedUntil.Value)
                ? Math.Max(1, (int)Math.Ceiling((_pausedUntil.Value - DateTime.Now).TotalMinutes))
                : 0;

            var payload = new Dictionary<string, object>
            {
                { "type", "state" },
                { "nowMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                { "triggerExpand", triggerExpand },
                { "focusKind", focusKind ?? "" },
                { "health", new Dictionary<string, object>
                    {
                        { "seatedSeconds", _seatedSeconds },
                        { "intervalMinutes", _settings.BreakIntervalMinutes },
                        { "todayCount", _settings.TodayBreakCount },
                        { "alertActive", _healthAlertActive },
                        { "actionIndex", _actionIndex },
                        { "snoozed", _snoozedUntil.HasValue && DateTime.Now < _snoozedUntil.Value },
                        { "paused", pauseRemainMinutes > 0 },
                        { "pauseRemainMinutes", pauseRemainMinutes },
                        { "isLateNight", isLateNight }
                    }
                },
                { "actions", _actions },
                { "alerts", alerts },
                { "working", working },
                { "history", history },
                { "muted", _settings.Muted },
                { "language", _settings.Language ?? "auto" },
                { "resolvedLanguage", ResolveEffectiveLanguage() }
            };

            try { _coreWebView.PostWebMessageAsJson(_json.Serialize(payload)); }
            catch { }
        }

        private void SendCommandToUi(string command)
        {
            if (!_webReady || _coreWebView == null) return;
            var payload = new Dictionary<string, object>
            {
                { "type", "command" },
                { "command", command }
            };
            try { _coreWebView.PostWebMessageAsJson(_json.Serialize(payload)); }
            catch { }
        }

        private void SendToastToUi(string text)
        {
            if (!_webReady || _coreWebView == null || string.IsNullOrEmpty(text)) return;
            var payload = new Dictionary<string, object>
            {
                { "type", "toast" },
                { "text", text }
            };
            try { _coreWebView.PostWebMessageAsJson(_json.Serialize(payload)); }
            catch { }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = e.WebMessageAsJson;
                var msg = _json.DeserializeObject(raw) as Dictionary<string, object>;
                if (msg == null) return;

                string type = GetStr(msg, "type");
                switch (type)
                {
                    case "hit_rects":
                        double dpr = GetDouble(msg, "dpr", 0);
                        bool dprChanged = (dpr >= 0.75 && dpr <= 4.0 && Math.Abs(dpr - _webDpr) > 0.01);
                        if (dpr >= 0.75 && dpr <= 4.0) _webDpr = dpr;

                        object rectsObj;
                        if (msg.TryGetValue("rects", out rectsObj))
                        {
                            var nextRects = new List<RectangleF>();
                            IEnumerable list = rectsObj as IEnumerable;
                            if (list != null)
                            {
                                foreach (object item in list)
                                {
                                    var r = item as Dictionary<string, object>;
                                    if (r == null) continue;
                                    float x = (float)GetDouble(r, "x", 0);
                                    float y = (float)GetDouble(r, "y", 0);
                                    float w = (float)GetDouble(r, "w", 0);
                                    float h = (float)GetDouble(r, "h", 0);
                                    if (w > 2 && h > 2) nextRects.Add(new RectangleF(x, y, w, h));
                                }
                            }
                            if (nextRects.Count > 0) _hitRects = nextRects;
                        }

                        if (dprChanged)
                        {
                            ApplyWindowBounds(FixedCanvasLogicalW, FixedCanvasLogicalH, false);
                        }
                        break;

                    case "begin_drag":
                        _nativeDragging = true;
                        _dragStartMouseX = Cursor.Position.X;
                        _dragStartOffsetX = _settings.OffsetX;
                        SetCapture(this.Handle);
                        break;

                    case "health_done":
                        CompleteHealthBreak(true);
                        break;

                    case "health_reset_only":
                        CompleteHealthBreak(false);
                        break;

                    case "health_snooze":
                        SnoozeHealth(GetInt(msg, "minutes", 5));
                        break;

                    case "health_shuffle":
                        _actionIndex = (_actionIndex + 1) % Math.Max(1, _actions.Count);
                        PushStateToUi(false, null);
                        break;

                    case "set_interval":
                        SetBreakInterval(GetInt(msg, "minutes", 50));
                        break;

                    case "set_language":
                        SetLanguagePreference(GetStr(msg, "language"));
                        break;

                    case "play_done_chime":
                        PlayAlertSound(false);
                        break;

                    case "agy_focus":
                        HandleFocusAlert(GetStr(msg, "id"));
                        break;

                    case "agy_dismiss":
                        string dismissId = GetStr(msg, "id");
                        ArchiveAndRemoveSession(dismissId);
                        UpdateTrayTooltip();
                        PushStateToUi(false, null);
                        break;

                    case "agy_dismiss_all":
                        foreach (var alertItem in GetSortedAlerts())
                        {
                            ArchiveAndRemoveSession(alertItem.ConversationId);
                        }
                        UpdateTrayTooltip();
                        PushStateToUi(false, null);
                        break;

                    case "history_restore":
                        RestoreHistoryItem(GetStr(msg, "id"));
                        break;

                    case "copy_text":
                        string copyStr = GetStr(msg, "text");
                        if (!string.IsNullOrEmpty(copyStr))
                        {
                            try { Clipboard.SetText(copyStr); } catch { }
                        }
                        break;

                    case "toggle_mute":
                        ToggleMute();
                        break;

                    case "toggle_pause":
                        TogglePauseHealth();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("WebMsg ERROR: " + ex.Message);
            }
        }

        private static int GetInt(Dictionary<string, object> dict, string key, int fallback)
        {
            object v;
            if (dict != null && dict.TryGetValue(key, out v) && v != null)
            {
                int parsed;
                if (int.TryParse(v.ToString(), out parsed)) return parsed;
                double d;
                if (double.TryParse(v.ToString(), out d)) return (int)Math.Round(d);
            }
            return fallback;
        }

        private static double GetDouble(Dictionary<string, object> dict, string key, double fallback)
        {
            object v;
            if (dict != null && dict.TryGetValue(key, out v) && v != null)
            {
                double d;
                if (double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                {
                    return d;
                }
            }
            return fallback;
        }

        // ==================== Focus & Jump to Exact Antigravity Conversation ====================

        private string ResolveSearchQuery(string sessionName, string lastUserInput, string transcriptPath, string prevQuery, string fallbackSummary)
        {
            string cleanedTitle = CleanSearchPhrase(sessionName);
            if (cleanedTitle.Length >= 2)
            {
                return cleanedTitle;
            }

            string cleaned = CleanSearchPhrase(lastUserInput);
            if (cleaned.Length >= 6)
            {
                return cleaned;
            }

            if (!string.IsNullOrEmpty(prevQuery) && prevQuery.Length >= 6)
            {
                return prevQuery;
            }

            string firstUserMsg = ReadFirstUserInputFromTranscript(transcriptPath);
            string cleanedFirst = CleanSearchPhrase(firstUserMsg);
            if (cleanedFirst.Length >= 4)
            {
                return cleanedFirst;
            }

            if (cleaned.Length > 0) return cleaned;
            return CleanSearchPhrase(fallbackSummary);
        }

        private static string CleanSearchPhrase(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw;
            int startTag = s.IndexOf("<USER_REQUEST>", StringComparison.OrdinalIgnoreCase);
            if (startTag >= 0)
            {
                s = s.Substring(startTag + "<USER_REQUEST>".Length);
                int endTag = s.IndexOf("</USER_REQUEST>", StringComparison.OrdinalIgnoreCase);
                if (endTag >= 0) s = s.Substring(0, endTag);
            }
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            if (s.Length > 24) s = s.Substring(0, 24).Trim();
            return s;
        }

        private string ReadFirstUserInputFromTranscript(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    for (int i = 0; i < 12; i++)
                    {
                        string line = sr.ReadLine();
                        if (line == null) break;
                        if (line.IndexOf("\"USER_INPUT\"", StringComparison.Ordinal) < 0) continue;
                        var obj = _json.DeserializeObject(line) as Dictionary<string, object>;
                        if (obj != null && GetStr(obj, "type") == "USER_INPUT")
                        {
                            return GetStr(obj, "content");
                        }
                    }
                }
            }
            catch { }
            return "";
        }

        private IntPtr FocusAntigravityWindow()
        {
            IntPtr foundAppExe = IntPtr.Zero;
            IntPtr foundIdeExe = IntPtr.Zero;
            uint myPid = (uint)Process.GetCurrentProcess().Id;

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == myPid) return true;

                int len = GetWindowTextLength(hWnd);
                if (len <= 0) return true;

                try
                {
                    using (var p = Process.GetProcessById((int)pid))
                    {
                        string pname = p.ProcessName ?? "";
                        if (string.Equals(pname, "Antigravity", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(pname, "jetski", StringComparison.OrdinalIgnoreCase))
                        {
                            foundAppExe = hWnd;
                            return false;
                        }
                        if (foundIdeExe == IntPtr.Zero &&
                            pname.IndexOf("Antigravity", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            foundIdeExe = hWnd;
                        }
                    }
                }
                catch { }

                return true;
            }, IntPtr.Zero);

            IntPtr target = foundAppExe != IntPtr.Zero ? foundAppExe : foundIdeExe;
            if (target == IntPtr.Zero)
            {
                Log("FocusAntigravity: no Antigravity.exe visible window found");
                return IntPtr.Zero;
            }

            try
            {
                ShowWindow(target, IsIconic(target) ? SW_RESTORE : SW_SHOW);

                IntPtr fg = GetForegroundWindow();
                uint fgPid;
                uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out fgPid) : 0;
                uint curThread = GetCurrentThreadId();

                if (fgThread != 0 && fgThread != curThread)
                {
                    AttachThreadInput(curThread, fgThread, true);
                    SetForegroundWindow(target);
                    AttachThreadInput(curThread, fgThread, false);
                }
                else
                {
                    SetForegroundWindow(target);
                }
            }
            catch (Exception ex)
            {
                Log("FocusAntigravity ERROR: " + ex.Message);
            }
            return target;
        }

        private IntPtr FocusTerminalWindow()
        {
            string[] preferredProcs = new[] { "WindowsTerminal", "wezterm-gui", "alacritty", "Code", "pwsh", "powershell", "cmd" };
            IntPtr[] matches = new IntPtr[preferredProcs.Length];
            uint myPid = (uint)Process.GetCurrentProcess().Id;

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == myPid) return true;
                if (GetWindowTextLength(hWnd) <= 0) return true;

                try
                {
                    using (var p = Process.GetProcessById((int)pid))
                    {
                        string pname = p.ProcessName ?? "";
                        for (int i = 0; i < preferredProcs.Length; i++)
                        {
                            if (matches[i] == IntPtr.Zero && string.Equals(pname, preferredProcs[i], StringComparison.OrdinalIgnoreCase))
                            {
                                matches[i] = hWnd;
                                break;
                            }
                        }
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            IntPtr target = IntPtr.Zero;
            for (int i = 0; i < matches.Length; i++)
            {
                if (matches[i] != IntPtr.Zero) { target = matches[i]; break; }
            }
            if (target == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                ShowWindow(target, IsIconic(target) ? SW_RESTORE : SW_SHOW);
                IntPtr fg = GetForegroundWindow();
                uint fgPid;
                uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out fgPid) : 0;
                uint curThread = GetCurrentThreadId();
                if (fgThread != 0 && fgThread != curThread)
                {
                    AttachThreadInput(curThread, fgThread, true);
                    SetForegroundWindow(target);
                    AttachThreadInput(curThread, fgThread, false);
                }
                else
                {
                    SetForegroundWindow(target);
                }
            }
            catch { }
            return target;
        }

        private void HandleFocusAlert(string focusId)
        {
            SessionItem targetSession = null;
            if (!string.IsNullOrEmpty(focusId))
            {
                _sessions.TryGetValue(focusId, out targetSession);
            }
            if (targetSession == null)
            {
                UpdateTrayTooltip();
                PushStateToUi(false, null);
                return;
            }

            string src = targetSession.Source ?? "antigravity";
            if (string.Equals(src, "ClaudeCode", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(src, "Codex", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr termHwnd = FocusTerminalWindow();
                if (termHwnd == IntPtr.Zero)
                {
                    Log("FocusTerminal FAILED: no supported terminal window found");
                    SendToastToUi(ResolveEffectiveLanguage() == "en"
                        ? "No open terminal window found (alert kept)"
                        : "未检测到已打开的终端窗口，已保留通知");
                    return;
                }
                ArchiveAndRemoveSession(focusId);
                UpdateTrayTooltip();
                PushStateToUi(false, null);
                return;
            }

            if (!string.Equals(src, "antigravity", StringComparison.OrdinalIgnoreCase))
            {
                ArchiveAndRemoveSession(focusId);
                UpdateTrayTooltip();
                PushStateToUi(false, null);
                return;
            }

            IntPtr targetHwnd = FocusAntigravityWindow();
            if (targetHwnd == IntPtr.Zero)
            {
                Log("FocusAntigravity FAILED: window not found, keeping alert");
                SendToastToUi(ResolveEffectiveLanguage() == "en"
                    ? "Antigravity window not found (alert kept)"
                    : "未检测到 Antigravity 窗口，已保留通知");
                return;
            }

            ArchiveAndRemoveSession(focusId);
            UpdateTrayTooltip();
            PushStateToUi(false, null);
            JumpToAntigravityConversation(targetHwnd, targetSession);
        }

        private async void JumpToAntigravityConversation(IntPtr targetHwnd, SessionItem session)
        {
            if (targetHwnd == IntPtr.Zero || session == null) return;

            string query = ResolveSearchQuery(session.SessionName, session.LastUserInput, session.TranscriptPath, session.SearchQuery, session.Summary);
            if (string.IsNullOrWhiteSpace(query))
            {
                Log("JumpConversation: empty search query, focused window only");
                return;
            }

            Log("JumpConversation START | hwnd=0x" + targetHwnd.ToInt64().ToString("X") + " | query=" + query);

            bool hadTextClipboard = false;
            string savedClipboard = null;
            try
            {
                if (Clipboard.ContainsText())
                {
                    hadTextClipboard = true;
                    savedClipboard = Clipboard.GetText();
                }
            }
            catch { }

            try
            {
                await Task.Delay(90);
                // Guard: only send keys if Antigravity window actually took foreground
                IntPtr fgNow = GetForegroundWindow();
                uint fgPid, targetPid;
                GetWindowThreadProcessId(fgNow, out fgPid);
                GetWindowThreadProcessId(targetHwnd, out targetPid);
                if (fgPid != targetPid)
                {
                    Log("JumpConversation ABORT: foreground pid mismatch (" + fgPid + " != " + targetPid + ")");
                    return;
                }

                // 1. Ctrl + K -> open Antigravity Conversation Picker
                SendChord(VK_CONTROL, VK_K);
                await Task.Delay(210);

                // 2. Paste unique session phrase via clipboard (Unicode & IME safe)
                Clipboard.SetText(query);
                SendChord(VK_CONTROL, VK_V);
                await Task.Delay(340);

                // 3. Enter -> switch directly to matched conversation
                SendSingleKey(VK_RETURN);
                await Task.Delay(140);
                Log("JumpConversation SENT Ctrl+K -> Paste -> Enter");
            }
            catch (Exception ex)
            {
                Log("JumpConversation ERROR: " + ex.Message);
            }
            finally
            {
                // Restore user's original clipboard text cleanly
                if (hadTextClipboard && savedClipboard != null)
                {
                    try { Clipboard.SetText(savedClipboard); } catch { }
                }
            }
        }

        private static void SendChord(ushort modVk, ushort keyVk)
        {
            var inputs = new INPUT[4];
            inputs[0] = MakeKeyInput(modVk, false);
            inputs[1] = MakeKeyInput(keyVk, false);
            inputs[2] = MakeKeyInput(keyVk, true);
            inputs[3] = MakeKeyInput(modVk, true);
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static void SendSingleKey(ushort vk)
        {
            var inputs = new INPUT[2];
            inputs[0] = MakeKeyInput(vk, false);
            inputs[1] = MakeKeyInput(vk, true);
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static INPUT MakeKeyInput(ushort vk, bool keyUp)
        {
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = 0,
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        // ==================== Settings & External Actions Persistence ====================

        private void LoadActions()
        {
            var loaded = new List<Dictionary<string, object>>();
            try
            {
                if (!string.IsNullOrEmpty(_actionsPath) && File.Exists(_actionsPath))
                {
                    string raw = File.ReadAllText(_actionsPath, Encoding.UTF8);
                    object parsed = _json.DeserializeObject(raw);
                    IEnumerable arr = parsed as IEnumerable;
                    if (arr != null)
                    {
                        foreach (object o in arr)
                        {
                            var d = o as Dictionary<string, object>;
                            if (d == null) continue;
                            string title = GetStr(d, "title");
                            if (string.IsNullOrWhiteSpace(title)) continue;
                            loaded.Add(new Dictionary<string, object>
                            {
                                { "id", GetStr(d, "id") },
                                { "category", GetStr(d, "category") },
                                { "title", title },
                                { "title_en", GetStr(d, "title_en") },
                                { "seconds", Math.Max(10, GetInt(d, "seconds", 45)) },
                                { "tip", GetStr(d, "tip") },
                                { "tip_en", GetStr(d, "tip_en") }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("LoadActions ERROR: " + ex.Message);
            }
            if (loaded.Count > 0)
            {
                _actions = loaded;
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return;
                string raw = File.ReadAllText(_settingsPath, Encoding.UTF8);
                var dict = _json.DeserializeObject(raw) as Dictionary<string, object>;
                if (dict == null) return;
                _settings.OffsetX = GetInt(dict, "offsetX", 0);
                _settings.BreakIntervalMinutes = Math.Max(10, GetInt(dict, "breakIntervalMinutes", 50));
                _settings.TodayDate = GetStr(dict, "todayDate");
                _settings.TodayBreakCount = Math.Max(0, GetInt(dict, "todayBreakCount", 0));
                string lang = GetStr(dict, "language").Trim().ToLowerInvariant();
                if (lang == "auto" || lang == "zh" || lang == "en") _settings.Language = lang;
                object mutedObj;
                if (dict.TryGetValue("muted", out mutedObj) && mutedObj is bool)
                {
                    _settings.Muted = (bool)mutedObj;
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var dict = new Dictionary<string, object>
                {
                    { "offsetX", _settings.OffsetX },
                    { "muted", _settings.Muted },
                    { "breakIntervalMinutes", _settings.BreakIntervalMinutes },
                    { "todayDate", _settings.TodayDate },
                    { "todayBreakCount", _settings.TodayBreakCount },
                    { "language", _settings.Language ?? "auto" }
                };
                File.WriteAllText(_settingsPath, _json.Serialize(dict), new UTF8Encoding(false));
            }
            catch { }
        }

        // ==================== Automated Self-Test Engine ====================

        private async Task RunSelfTestSuiteAsync()
        {
            var checks = new List<Dictionary<string, object>>();
            var stateAudits = new List<Dictionary<string, object>>();
            var capturedPngs = new Dictionary<string, byte[]>();
            bool allPass = true;

            Action<string, bool, string> record = (name, ok, detail) =>
            {
                if (!ok) allPass = false;
                checks.Add(new Dictionary<string, object>
                {
                    { "name", name },
                    { "pass", ok },
                    { "detail", detail }
                });
                Log(string.Format("[CHECK] {0} => {1} | {2}", name, ok ? "PASS" : "FAIL", detail));
            };

            try
            {
                await Task.Delay(550);

                // 1. Win32 / DWM / DirectComposition Host Audits
                int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
                bool hasNoRedir = (exStyle & WS_EX_NOREDIRECTIONBITMAP) != 0;
                bool hasNoAct = (exStyle & WS_EX_NOACTIVATE) != 0;
                bool hasTopMost = (exStyle & WS_EX_TOPMOST) != 0;
                bool hasToolWin = (exStyle & WS_EX_TOOLWINDOW) != 0;
                record("win32_exstyle", hasNoRedir && hasNoAct && hasTopMost && hasToolWin,
                    string.Format("ExStyle=0x{0:X} NoRedir={1} NoAct={2} TopMost={3} ToolWin={4}",
                        exStyle, hasNoRedir, hasNoAct, hasTopMost, hasToolWin));

                int ncEnabled = -1;
                int ncHr = DwmGetWindowAttribute(this.Handle, DWMWA_NCRENDERING_ENABLED, out ncEnabled, sizeof(int));
                record("dwm_nc_disabled", ncHr == 0 && ncEnabled == 0,
                    string.Format("DWMWA_NCRENDERING_ENABLED={0} hr=0x{1:X}", ncEnabled, ncHr));

                record("no_gdi_region_clip", this.Region == null, "Region is null");
                record("direct_composition", _dcompOk, "hr=" + _dcompHrSummary);
                record("external_actions_loaded", _actions.Count >= 6, "count=" + _actions.Count + " path=" + _actionsPath);

                // 2. Test Approval Radar + Search Query Extraction on a synthetic transcript.jsonl
                string fakeTranscript = Path.Combine(_selfTestDir, "fake-transcript.jsonl");
                string pastUtc = DateTime.UtcNow.AddSeconds(-8).ToString("yyyy-MM-ddTHH:mm:ssZ");
                string firstUserLine = "{\"step_index\":1,\"source\":\"USER_EXPLICIT\",\"type\":\"USER_INPUT\",\"status\":\"DONE\",\"content\":\"<USER_REQUEST>\\nHelp debug Cloudflare Workers deployment error\\n</USER_REQUEST>\"}";
                string fakeLine = "{\"step_index\":99,\"source\":\"MODEL\",\"type\":\"PLANNER_RESPONSE\",\"status\":\"DONE\",\"created_at\":\"" + pastUtc + "\",\"tool_calls\":[{\"name\":\"run_command\",\"args\":{\"BypassSandbox\":\"true\",\"toolSummary\":\"Deploy production build\",\"CommandLine\":\"pnpm deploy\"}}]}";
                File.WriteAllText(fakeTranscript, firstUserLine + Environment.NewLine + fakeLine + Environment.NewLine, new UTF8Encoding(false));

                string extractedQ = ResolveSearchQuery("", "ok", fakeTranscript, "", "fallback");
                string titleQ = ResolveSearchQuery("灵动岛", "ok", fakeTranscript, "", "fallback");
                record("search_query_extraction", extractedQ == "Help debug Cloudflare Wo" && titleQ == "灵动岛",
                    "shortFallback=" + extractedQ + " | titleFirst=" + titleQ);

                // 2.5 Test Auto-Dismiss when Antigravity annotation shows user viewed the conversation
                string fakeAnn = Path.Combine(_selfTestDir, "viewed-conv.pbtxt");
                long futureViewSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 20;
                File.WriteAllText(fakeAnn, "title:\"灵动岛\" last_user_view_time:{seconds:" + futureViewSec + " nanos:0}", new UTF8Encoding(false));
                _sessions.Clear();
                _sessions["viewed-conv"] = new SessionItem
                {
                    ConversationId = "viewed-conv",
                    Source = "antigravity",
                    SessionName = "灵动岛",
                    Project = "AgentIsland",
                    Kind = "needs_reply",
                    Title = "等你回复",
                    Summary = "待你回复：确认这个方案吗？",
                    AnnotationPath = fakeAnn,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 5000
                };
                bool viewedCleared = CheckViewedSessions() && !_sessions.ContainsKey("viewed-conv");
                record("viewed_session_auto_clear", viewedCleared, "cleared=" + viewedCleared);

                // 2.6 Test drag offset clamping (no deadzone after dragging past screen edge)
                int origOffset = _settings.OffsetX;
                _settings.OffsetX = -99999;
                ApplyWindowBounds(FixedCanvasLogicalW, FixedCanvasLogicalH, false);
                bool offsetClamped = _settings.OffsetX > -5000 && _settings.OffsetX <= 0;
                _settings.OffsetX = origOffset;
                ApplyWindowBounds(FixedCanvasLogicalW, FixedCanvasLogicalH, false);
                record("drag_offset_clamped", offsetClamped, "clampedOk=" + offsetClamped);

                _sessions.Clear();
                _sessions["radar-conv"] = new SessionItem
                {
                    ConversationId = "radar-conv",
                    Source = "antigravity",
                    Project = "demo-web-app",
                    Kind = "working",
                    Title = "思考中",
                    Summary = "准备部署",
                    TranscriptPath = fakeTranscript,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                bool detected = CheckTranscriptApprovals();
                bool isApprovalKind = _sessions["radar-conv"].Kind == "needs_approval";
                record("approval_radar_detect", detected && isApprovalKind,
                    string.Format("detected={0} kind={1} title={2}", detected, _sessions["radar-conv"].Kind, _sessions["radar-conv"].Title));

                // 3. Capture & Audit 5 states (including Split Pill + Overview + History + Health Countdown)
                // State 1: Split Pill (compact idle + right health ring)
                _sessions.Clear();
                _recentHistory.Clear();
                _healthAlertActive = false;
                _seatedSeconds = 32 * 60;
                _settings.TodayBreakCount = 4;
                PushStateToUi(false, null);
                await Task.Delay(550);
                Rectangle b1 = this.Bounds;

                // Test REAL WindowFromPoint click-through against an underlying backdrop window!
                double sc = GetScaleFactor();
                using (var underWin = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = this.Bounds, ShowInTaskbar = false })
                {
                    underWin.Show();
                    EnsureTopMost();
                    Application.DoEvents();

                    // When click-through is enabled (mouse outside capsule), empty canvas + 8px gap MUST hit underWin
                    SetClickThrough(true);
                    Application.DoEvents();
                    Point emptyBottomPt = new Point(this.Bounds.Left + this.Bounds.Width / 2, this.Bounds.Top + (int)Math.Round(170 * sc));
                    IntPtr hitEmpty = WindowFromPoint(emptyBottomPt);

                    bool hasTwoRects = _hitRects.Count >= 2;
                    IntPtr hitGap = IntPtr.Zero;
                    IntPtr hitLeftSolid = IntPtr.Zero;
                    IntPtr hitRightSolid = IntPtr.Zero;
                    if (hasTwoRects)
                    {
                        RectangleF r0 = _hitRects[0];
                        RectangleF r1 = _hitRects[1];
                        float gapX = (r0.Right + r1.Left) / 2f;
                        Point gapPt = new Point(this.Bounds.Left + (int)Math.Round(gapX * sc), this.Bounds.Top + (int)Math.Round((r0.Top + r0.Height / 2) * sc));
                        hitGap = WindowFromPoint(gapPt);

                        // Simulate mouse entering capsule -> click-through turns off -> MUST hit DynamicIsland
                        SetClickThrough(false);
                        Application.DoEvents();
                        Point leftPt = new Point(this.Bounds.Left + (int)Math.Round((r0.Left + r0.Width / 2) * sc), this.Bounds.Top + (int)Math.Round((r0.Top + r0.Height / 2) * sc));
                        Point rightPt = new Point(this.Bounds.Left + (int)Math.Round((r1.Left + r1.Width / 2) * sc), this.Bounds.Top + (int)Math.Round((r1.Top + r1.Height / 2) * sc));
                        hitLeftSolid = WindowFromPoint(leftPt);
                        hitRightSolid = WindowFromPoint(rightPt);
                        SetClickThrough(true);
                    }

                    bool passThroughOk = hasTwoRects
                        && hitEmpty == underWin.Handle
                        && hitGap == underWin.Handle
                        && hitLeftSolid == this.Handle
                        && hitRightSolid == this.Handle;
                    record("real_click_through_windowfrompoint", passThroughOk,
                        string.Format("rects={0} emptyHitsUnder={1} gapHitsUnder={2} leftHitsIsland={3} rightHitsIsland={4}",
                            _hitRects.Count,
                            hitEmpty == underWin.Handle,
                            hitGap == underWin.Handle,
                            hitLeftSolid == this.Handle,
                            hitRightSolid == this.Handle));
                    underWin.Close();
                }

                stateAudits.Add(await CaptureAndAuditStateAsync("01-split-pill", capturedPngs, record));

                // State 2: Split Pill (AGY working + right health ring in parallel!)
                _sessions["st-work"] = new SessionItem
                {
                    ConversationId = "st-work",
                    Source = "antigravity",
                    Project = "AgentIsland",
                    Kind = "working",
                    Title = "思考中",
                    Summary = "正在构建与自检",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                PushStateToUi(false, null);
                await Task.Delay(500);
                Rectangle b2 = this.Bounds;
                stateAudits.Add(await CaptureAndAuditStateAsync("02-split-working", capturedPngs, record));

                // State 3: AGY Approval Alert (shows wait duration & source)
                _sessions["st-alert"] = new SessionItem
                {
                    ConversationId = "st-alert",
                    Source = "antigravity",
                    Project = "demo-web-app",
                    Kind = "needs_approval",
                    Title = "待批准命令",
                    Summary = "越沙盒命令：pnpm deploy — 等待你点 Allow",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 3 * 60 * 1000
                };
                PushStateToUi(true, "agy");
                await Task.Delay(600);
                Rectangle b3 = this.Bounds;
                stateAudits.Add(await CaptureAndAuditStateAsync("03-agy-approval", capturedPngs, record));

                // State 4: Health Countdown Card (with active countdown progress)
                _seatedSeconds = 50 * 60;
                _healthAlertActive = true;
                _actionIndex = 1; // 麦肯基轻撑 30 秒
                PushStateToUi(true, "health");
                await Task.Delay(600);
                Rectangle b4 = this.Bounds;
                stateAudits.Add(await CaptureAndAuditStateAsync("04-health-card", capturedPngs, record));

                // State 5: Overview Panel (with active session + recent history + today count + interval chips)
                ArchiveAndRemoveSession("st-alert");
                _healthAlertActive = false;
                _seatedSeconds = 32 * 60;
                PushStateToUi(false, null);
                SendCommandToUi("open_overview");
                await Task.Delay(600);
                Rectangle b5 = this.Bounds;
                stateAudits.Add(await CaptureAndAuditStateAsync("05-overview", capturedPngs, record));

                bool zeroJump = b1 == b2 && b2 == b3 && b3 == b4 && b4 == b5;
                record("no_window_jump_on_morph", zeroJump,
                    string.Format("all5={0} ({1})", zeroJump, b1));

                // 3.5 Automated UX State Machine Regression Suite (inside live WebView2)
                // (a) Manual health open must survive 3s background heartbeat tick!
                _sessions.Clear();
                _healthAlertActive = false;
                _seatedSeconds = 12 * 60;
                PushStateToUi(false, null);
                await Task.Delay(250);
                await _coreWebView.ExecuteScriptAsync("document.getElementById('health-orb').click()");
                await Task.Delay(200);
                PushStateToUi(false, null); // simulate 3s tick
                await Task.Delay(220);
                string manualHealthState = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("JSON.stringify({view:window.__uxDebug().view, title:document.getElementById('main-island').innerText})"));
                bool manualHealthOk = manualHealthState.Contains("\"view\":\"health\"") && (manualHealthState.Contains("12/50m") || manualHealthState.Contains("12m"));
                record("ux_manual_health_survives_tick", manualHealthOk, manualHealthState.Length > 120 ? manualHealthState.Substring(0, 120) : manualHealthState);

                // (b) Active health countdown must NOT be killed by incoming AI alert!
                await _coreWebView.ExecuteScriptAsync("document.getElementById('btn-countdown').click()");
                await Task.Delay(180);
                _sessions["ux-alert-1"] = new SessionItem
                {
                    ConversationId = "ux-alert-1",
                    Source = "ClaudeCode",
                    SessionName = "backend-api",
                    Project = "backend-api",
                    Kind = "needs_approval",
                    Title = "待批准权限",
                    Summary = "请求批准 Bash：npm test",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                PushStateToUi(true, "agy");
                await Task.Delay(220);
                string cdProtect = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("JSON.stringify(window.__uxDebug())"));
                bool cdProtectOk = cdProtect.Contains("\"view\":\"health\"") && cdProtect.Contains("\"countdownActive\":true");
                record("ux_countdown_protected_from_ai", cdProtectOk, cdProtect);

                // (c) Pause countdown must pause (retain remaining seconds), not reset!
                await _coreWebView.ExecuteScriptAsync("document.getElementById('btn-countdown').click()");
                await Task.Delay(150);
                string cdPause = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("JSON.stringify(window.__uxDebug())"));
                bool cdPauseOk = cdPause.Contains("\"countdownActive\":false") && cdPause.Contains("\"countdownPaused\":true");
                record("ux_countdown_true_pause", cdPauseOk, cdPause);

                // (d) Overview clicking 2nd alert '处理' must open exact 2nd alert ID!
                _sessions["ux-alert-2"] = new SessionItem
                {
                    ConversationId = "ux-alert-2",
                    Source = "antigravity",
                    SessionName = "灵动岛",
                    Project = "AgentIsland",
                    Kind = "needs_reply",
                    Title = "等你回复",
                    Summary = "待你回复：确认这 2 个交互偏好？",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 100
                };
                PushStateToUi(false, null);
                await Task.Delay(120);
                SendCommandToUi("open_overview");
                await Task.Delay(220);
                await _coreWebView.ExecuteScriptAsync("(function(){var btn=document.querySelector('[data-action=\"open-agy-pin\"][data-id=\"ux-alert-2\"]');if(btn)btn.click();})()");
                await Task.Delay(220);
                string exactTarget = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("JSON.stringify(window.__uxDebug())"));
                bool exactTargetOk = exactTarget.Contains("\"view\":\"agy\"") && exactTarget.Contains("\"activeAlertId\":\"ux-alert-2\"");
                record("ux_overview_exact_alert_id", exactTargetOk, exactTarget);

                // (e) Notification Tiering: L1 stays collapsed in ticker; L2 auto-collapses in 8s; L3 auto-collapses in 30s!
                _sessions.Clear();
                PushStateToUi(false, null);
                await Task.Delay(150);
                _sessions["ux-l1"] = new SessionItem
                {
                    ConversationId = "ux-l1",
                    Source = "antigravity",
                    SessionName = "灵动岛",
                    Project = "AgentIsland",
                    Kind = "needs_reply",
                    Level = 1,
                    Title = "等你回复",
                    Summary = "已替换完成，线上代码与发版包现已一致",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                PushStateToUi(true, "agy");
                await Task.Delay(180);
                string l1Snap = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("JSON.stringify(window.__uxDebug())"));

                _sessions["ux-l2"] = new SessionItem
                {
                    ConversationId = "ux-l2",
                    Source = "antigravity",
                    SessionName = "灵动岛",
                    Project = "AgentIsland",
                    Kind = "needs_decision",
                    Level = 2,
                    Title = "等你拍板",
                    Summary = "确认发版策略？（v0.1.1 / 覆盖 v0.1.0）",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                PushStateToUi(true, "agy");
                await Task.Delay(180);
                string l2Snap = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("JSON.stringify(window.__uxDebug())"));

                _sessions["ux-l3"] = new SessionItem
                {
                    ConversationId = "ux-l3",
                    Source = "Codex",
                    SessionName = "infra-cli",
                    Project = "infra-cli",
                    Kind = "needs_approval",
                    Level = 3,
                    Title = "待批准命令",
                    Summary = "请求批准 shell：terraform apply",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                PushStateToUi(true, "agy");
                await Task.Delay(180);
                string l3Snap = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("JSON.stringify(window.__uxDebug())"));

                bool tierRuleOk = l1Snap.Contains("\"view\":\"compact\"")
                    && l2Snap.Contains("\"view\":\"agy\"") && l2Snap.Contains("\"autoCollapseMs\":8000")
                    && l3Snap.Contains("\"view\":\"agy\"") && l3Snap.Contains("\"autoCollapseMs\":30000");
                record("ux_notification_tiering_l1_l2_l3", tierRuleOk, "L1=" + l1Snap + " | L2=" + l2Snap + " | L3=" + l3Snap);

                // (f) Outside click closes BOTH open context menu AND expanded card cleanly; AND expanded ctx menu MUST NOT clip outside canvas!
                SendCommandToUi("open_overview");
                await Task.Delay(150);
                await _coreWebView.ExecuteScriptAsync("document.getElementById('main-island').dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true}))");
                await Task.Delay(150);
                string ctxGeom = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("JSON.stringify((()=>{const r=document.getElementById('ctx-menu').getBoundingClientRect();return {top:Math.round(r.top),bottom:Math.round(r.bottom),winH:window.innerHeight,fits:r.height>40 && r.bottom <= window.innerHeight - 16};})())"));
                record("ux_ctx_menu_never_clipped_when_expanded", ctxGeom.Contains("\"fits\":true"), ctxGeom);

                string beforeOutside = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("JSON.stringify(window.__uxDebug())"));
                SendCommandToUi("outside_click");
                await Task.Delay(180);
                string afterOutside = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("JSON.stringify(window.__uxDebug())"));
                bool outsideOk = beforeOutside.Contains("\"ctxOpen\":true") && beforeOutside.Contains("\"view\":\"overview\"")
                    && afterOutside.Contains("\"ctxOpen\":false") && afterOutside.Contains("\"view\":\"compact\"");
                record("ux_outside_click_collapses_all", outsideOk, "before=" + beforeOutside + " | after=" + afterOutside);

                // (g) Language auto-detection & live EN/ZH toggle on pure Agent Plane
                SetLanguagePreference("en");
                SendCommandToUi("open_overview");
                await Task.Delay(180);
                string enText = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("document.getElementById('main-island').innerText"));
                SetLanguagePreference("zh");
                await Task.Delay(180);
                string zhText = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("document.getElementById('main-island').innerText"));
                bool i18nOk = enText.Contains("Agent Plane") && enText.Contains("Clear All") && zhText.Contains("Agent Plane") && zhText.Contains("全部已读");
                record("ux_i18n_auto_and_toggle", i18nOk, "en=" + enText.Replace("\n", " ") + " | zh=" + zhText.Replace("\n", " "));

                // (h) Hovering right Health Ring: hit_rects MUST immediately cover the orb's cursor spot mid-morph AND ignore transient DOM mouseleave (no bounce!)
                SendCommandToUi("outside_click");
                await Task.Delay(440);
                RectangleF orbRectBefore = _hitRects.Count >= 2 ? _hitRects[1] : RectangleF.Empty;
                float orbCenterX = orbRectBefore.Left + orbRectBefore.Width / 2f;
                float orbCenterY = orbRectBefore.Top + orbRectBefore.Height / 2f;
                await _coreWebView.ExecuteScriptAsync("document.getElementById('health-orb').dispatchEvent(new MouseEvent('mouseenter',{bubbles:false}))");
                await Task.Delay(370); // just after 320ms hover open (mid CSS transition!)
                bool orbStillCovered = false;
                foreach (var hr in _hitRects)
                {
                    if (orbCenterX >= hr.Left && orbCenterX <= hr.Right && orbCenterY >= hr.Top && orbCenterY <= hr.Bottom)
                    {
                        orbStillCovered = true;
                        break;
                    }
                }
                // Simulate Chromium's transient DOM mouseleave at the orb's coordinates mid-morph
                await _coreWebView.ExecuteScriptAsync("document.getElementById('dock').dispatchEvent(new MouseEvent('mouseleave',{clientX:300,clientY:34,bubbles:false}))");
                await Task.Delay(480); // longer than 380ms close timer
                string orbHoverState = UnquoteJsString(await _coreWebView.ExecuteScriptAsync("JSON.stringify(window.__uxDebug())"));
                bool orbHoverOk = !orbRectBefore.IsEmpty && orbStillCovered && orbHoverState.Contains("\"view\":\"health\"");
                record("ux_hover_health_orb_no_bounce", orbHoverOk,
                    string.Format("orbCenter=({0:F0},{1:F0}) coveredMidMorph={2} survivedBounce={3}", orbCenterX, orbCenterY, orbStillCovered, orbHoverState.Contains("\"view\":\"health\"")));

                // 4. Frontend JS error check
                string jsErrJson = await _coreWebView.ExecuteScriptAsync("JSON.stringify(window.__diagErrors || [])");
                string jsErrUnquoted = UnquoteJsString(jsErrJson);
                record("frontend_js_errors", jsErrUnquoted == "[]", "errors=" + jsErrUnquoted);

                // 5. Build visual verification sheet
                string sheetPath = Path.Combine(_selfTestDir, "verify-sheet.png");
                BuildVerificationSheet(capturedPngs, sheetPath);
                record("verify_sheet_generated", File.Exists(sheetPath), sheetPath);

                var report = new Dictionary<string, object>
                {
                    { "timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "allPass", allPass },
                    { "scaleFactor", sc },
                    { "sheetPath", sheetPath },
                    { "checks", checks },
                    { "states", stateAudits },
                    { "logs", _diagLogs }
                };
                File.WriteAllText(Path.Combine(_selfTestDir, "verify-report.json"), _json.Serialize(report), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                allPass = false;
                Log("SelfTest FATAL: " + ex.ToString());
                try
                {
                    File.WriteAllText(
                        Path.Combine(_selfTestDir, "verify-report.json"),
                        _json.Serialize(new Dictionary<string, object> { { "allPass", false }, { "fatal", ex.ToString() }, { "logs", _diagLogs } }),
                        new UTF8Encoding(false));
                }
                catch { }
            }
            finally
            {
                SelfTestExitCode = allPass ? 0 : 2;
                _exiting = true;
                this.Close();
            }
        }

        private static IntPtr MakeLParam(int x, int y)
        {
            return new IntPtr((y << 16) | (x & 0xFFFF));
        }

        private static string UnquoteJsString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            try
            {
                var ser = new JavaScriptSerializer();
                object obj = ser.DeserializeObject(raw);
                return obj != null ? obj.ToString() : raw;
            }
            catch { return raw; }
        }

        private async Task<Dictionary<string, object>> CaptureAndAuditStateAsync(
            string tag,
            Dictionary<string, byte[]> capturedPngs,
            Action<string, bool, string> record)
        {
            string domRaw = await _coreWebView.ExecuteScriptAsync(
                "JSON.stringify((()=>{const el=document.getElementById('main-island');const r=el.getBoundingClientRect();return {bodyBg:getComputedStyle(document.body).backgroundColor,winW:window.innerWidth,winH:window.innerHeight,w:Math.round(r.width),h:Math.round(r.height)};})())"
            );
            string domJson = UnquoteJsString(domRaw);

            byte[] pngBytes;
            using (var ms = new MemoryStream())
            {
                await _coreWebView.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                pngBytes = ms.ToArray();
            }

            string pngPath = Path.Combine(_selfTestDir, tag + ".png");
            File.WriteAllBytes(pngPath, pngBytes);
            capturedPngs[tag] = pngBytes;

            int imgW, imgH;
            int perimeterMaxAlpha = 0;
            int fullyTransparent = 0;
            int semiTransparent = 0;
            int opaquePixels = 0;
            string edgeSummary = "";

            using (var ms = new MemoryStream(pngBytes))
            using (var bmp = new Bitmap(ms))
            {
                imgW = bmp.Width;
                imgH = bmp.Height;

                int topMax = 0, bottomMax = 0, leftMax = 0, rightMax = 0;
                for (int x = 0; x < imgW; x++)
                {
                    topMax = Math.Max(topMax, bmp.GetPixel(x, 0).A);
                    bottomMax = Math.Max(bottomMax, bmp.GetPixel(x, imgH - 1).A);
                }
                for (int y = 0; y < imgH; y++)
                {
                    leftMax = Math.Max(leftMax, bmp.GetPixel(0, y).A);
                    rightMax = Math.Max(rightMax, bmp.GetPixel(imgW - 1, y).A);
                }
                perimeterMaxAlpha = Math.Max(Math.Max(topMax, bottomMax), Math.Max(leftMax, rightMax));
                edgeSummary = string.Format("edgeMaxA(T={0},B={1},L={2},R={3})", topMax, bottomMax, leftMax, rightMax);

                for (int y = 0; y < imgH; y++)
                {
                    for (int x = 0; x < imgW; x++)
                    {
                        byte a = bmp.GetPixel(x, y).A;
                        if (a == 0) fullyTransparent++;
                        else if (a == 255) opaquePixels++;
                        else semiTransparent++;
                    }
                }
            }

            bool perimeterClear = perimeterMaxAlpha == 0;
            bool hasAntiAlias = semiTransparent >= 80;
            bool hasContent = (opaquePixels + semiTransparent) >= 1500;
            record("pixel_" + tag, perimeterClear && hasAntiAlias && hasContent,
                string.Format("{0}x{1} | {2} | opaque={3} semiAA={4} clear={5}",
                    imgW, imgH, edgeSummary, opaquePixels, semiTransparent, fullyTransparent));

            return new Dictionary<string, object>
            {
                { "tag", tag },
                { "pngPath", pngPath },
                { "width", imgW },
                { "height", imgH },
                { "edgeSummary", edgeSummary },
                { "perimeterMaxAlpha", perimeterMaxAlpha },
                { "dom", domJson }
            };
        }

        private static void BuildVerificationSheet(Dictionary<string, byte[]> pngs, string outPath)
        {
            int sheetW = 1620;
            int sheetH = 1140;
            using (var sheet = new Bitmap(sheetW, sheetH, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(sheet))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(245, 246, 250));

                using (var darkBrush = new SolidBrush(Color.FromArgb(22, 24, 31)))
                {
                    g.FillRectangle(darkBrush, sheetW / 2, 0, sheetW / 2, sheetH);
                }

                using (var gridBrush = new SolidBrush(Color.FromArgb(232, 235, 242)))
                {
                    for (int y = 36; y < sheetH; y += 16)
                    {
                        for (int x = 0; x < sheetW / 2; x += 16)
                        {
                            if (((x / 16) + (y / 16)) % 2 == 0)
                                g.FillRectangle(gridBrush, x, y, 16, 16);
                        }
                    }
                }

                using (var titleFont = new Font("Segoe UI", 11, FontStyle.Bold))
                using (var darkText = new SolidBrush(Color.FromArgb(30, 34, 44)))
                using (var lightText = new SolidBrush(Color.FromArgb(235, 238, 245)))
                {
                    g.DrawString("AgentIsland · LIGHT BACKDROP (Split Pill / Approval / Countdown / Overview)", titleFont, darkText, 16, 10);
                    g.DrawString("AgentIsland · DARK BACKDROP + 4x AA MICROSCOPE", titleFont, lightText, sheetW / 2 + 16, 10);
                }

                string[] leftOrder = new[] { "01-split-pill", "02-split-working", "03-agy-approval", "04-health-card", "05-overview" };
                int[] leftY = new[] { 38, 152, 274, 520, 774 };

                for (int i = 0; i < leftOrder.Length; i++)
                {
                    byte[] data;
                    if (!pngs.TryGetValue(leftOrder[i], out data)) continue;
                    using (var ms = new MemoryStream(data))
                    using (var img = new Bitmap(ms))
                    {
                        int xL = (sheetW / 2 - img.Width) / 2;
                        if (xL < 6) xL = 6;
                        g.DrawImage(img, xL, leftY[i], img.Width, img.Height);

                        if (i < 4)
                        {
                            int xR = sheetW / 2 + (sheetW / 2 - img.Width) / 2;
                            g.DrawImage(img, xR, leftY[i], img.Width, img.Height);
                        }
                    }
                }

                // 4x zoom on 01-split-pill right ring edge
                byte[] splitBytes;
                if (pngs.TryGetValue("01-split-pill", out splitBytes))
                {
                    using (var ms = new MemoryStream(splitBytes))
                    using (var img = new Bitmap(ms))
                    {
                        int maxX = 0, minY = img.Height;
                        for (int y = 0; y < img.Height; y++)
                        {
                            for (int x = 0; x < img.Width; x++)
                            {
                                if (img.GetPixel(x, y).A > 180)
                                {
                                    if (x > maxX) maxX = x;
                                    if (y < minY) minY = y;
                                }
                            }
                        }
                        int cropW = 76;
                        int cropH = 68;
                        int cropX = Math.Max(0, Math.Min(img.Width - cropW, maxX - 66));
                        int cropY = Math.Max(0, minY - 8);

                        Rectangle zL = new Rectangle(sheetW / 2 + 40, 820, cropW * 4, cropH * 4);
                        Rectangle zR = new Rectangle(sheetW / 2 + 40 + cropW * 4 + 32, 820, cropW * 4, cropH * 4);

                        using (var lightPatch = new SolidBrush(Color.FromArgb(245, 246, 250)))
                        using (var darkPatch = new SolidBrush(Color.FromArgb(18, 20, 26)))
                        using (var pen = new Pen(Color.FromArgb(120, 128, 150), 1))
                        {
                            g.FillRectangle(lightPatch, zL);
                            g.FillRectangle(darkPatch, zR);
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(img, zL, new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
                            g.DrawImage(img, zR, new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
                            g.DrawRectangle(pen, zL);
                            g.DrawRectangle(pen, zR);
                        }
                    }
                }

                sheet.Save(outPath, ImageFormat.Png);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            if (_hotkeysRegistered)
            {
                try
                {
                    UnregisterHotKey(this.Handle, HOTKEY_TOGGLE_ID);
                    UnregisterHotKey(this.Handle, HOTKEY_FOCUS_AGY_ID);
                }
                catch { }
            }
            if (_passthroughTimer != null) _passthroughTimer.Stop();
            if (_tickTimer != null) _tickTimer.Stop();
            if (_inboxWatcher != null)
            {
                _inboxWatcher.EnableRaisingEvents = false;
                _inboxWatcher.Dispose();
            }
            if (_compController != null)
            {
                try { _compController.Close(); } catch { }
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            Log("Closed cleanly.");
        }
    }
}
