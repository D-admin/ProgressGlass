// ProgressGlass additions, Copyright (c) 2026. Licensed under MS-RL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using OnTopReplica.Native;

namespace ProgressGlass {
    public class TaskItem {
        public string id { get; set; }
        public string title { get; set; }
        public string status { get; set; }
        public string evidence { get; set; }
        public double? progress { get; set; }
        public double? weight { get; set; }
        public int? completedUnits { get; set; }
        public int? totalUnits { get; set; }
        public Measurement measurement { get; set; }
        public double Percent { get { return measurement!=null ? measurement.Percent : status=="done" && !String.IsNullOrWhiteSpace(evidence) ? 100 : 0; } }
        public bool HasProgress { get { return measurement!=null || (status=="done" && !String.IsNullOrWhiteSpace(evidence)); } }
        public double EffectiveWeight { get { return weight ?? 1; } }
    }
    public class Board {
        public string project { get; set; }
        public string current { get; set; }
        public string next { get; set; }
        public string updatedAt { get; set; }
        public List<TaskItem> tasks { get; set; }
        public Measurement measurement { get; set; }
        public MonitorConfig monitor { get; set; }
        public bool HasOverallProgress { get { return measurement!=null; } }
        public int Count(string status) { return tasks.Count(t => t.status == status); }
        public double ExactPercent { get { return measurement==null ? 0 : measurement.Percent; } }
        public int Percent { get { return measurement==null ? -1 : ExactPercent>=100 ? 100 : (int)Math.Floor(ExactPercent); } }
        public static Board Parse(string json) {
            var b = new JavaScriptSerializer().Deserialize<Board>(json);
            if (b == null || String.IsNullOrWhiteSpace(b.project) || b.tasks == null) throw new FormatException("缺少 project 或 tasks");
            if (b.tasks.Count > 500) throw new FormatException("任务数量超过 500");
            Measurement.Validate(b.measurement);
            var ids = new HashSet<string>();
            foreach (var t in b.tasks) {
                if (t == null || String.IsNullOrWhiteSpace(t.id) || !ids.Add(t.id) || String.IsNullOrWhiteSpace(t.title))
                    throw new FormatException("任务 ID 重复或缺少标题");
                if (!new[] {"todo", "doing", "blocked", "review", "done"}.Contains(t.status)) throw new FormatException("未知任务状态");
                Measurement.Validate(t.measurement);
                if (t.progress.HasValue && (Double.IsNaN(t.progress.Value) || Double.IsInfinity(t.progress.Value) || t.progress < 0 || t.progress > 100)) throw new FormatException("任务进度必须在 0–100 之间");
                if (Double.IsNaN(t.EffectiveWeight) || Double.IsInfinity(t.EffectiveWeight) || t.EffectiveWeight <= 0 || t.EffectiveWeight > 100000) throw new FormatException("任务权重必须大于 0 且不超过 100000");
                if (t.completedUnits.HasValue != t.totalUnits.HasValue) throw new FormatException("步骤数量必须同时填写");
                if (t.totalUnits.HasValue && (t.totalUnits <= 0 || t.completedUnits < 0 || t.completedUnits > t.totalUnits)) throw new FormatException("步骤数量超出范围");
                if (t.totalUnits.HasValue && t.progress.HasValue) throw new FormatException("请选择百分比或步骤计数，不要同时填写");
                if (t.status == "done" && t.Percent != 100) throw new FormatException("已验证任务必须为 100%");
            }
            DateTimeOffset dt;
            if (!DateTimeOffset.TryParse(b.updatedAt, out dt)) throw new FormatException("updatedAt 不是有效时间");
            return b;
        }
    }
    public class Preferences {
        public string file { get; set; }
        public double opacity { get; set; }
        public bool expanded { get; set; }
        public bool follow { get; set; }
        public int offsetX { get; set; }
        public int offsetY { get; set; }
        public Preferences() { opacity = .82; follow = true; offsetX = 20; offsetY = 90; }
    }
    internal static class Native {
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr param);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc, IntPtr param);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint id);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        internal static string ProcessName(IntPtr h) {
            try { uint id; GetWindowThreadProcessId(h, out id); using(var p = Process.GetProcessById((int)id)) return p.ProcessName; }
            catch { return ""; }
        }
        internal static bool IsChat(IntPtr h) {
            var name = ProcessName(h);
            return name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) || name.Equals("Codex", StringComparison.OrdinalIgnoreCase);
        }
        internal static List<IntPtr> ChatWindows() {
            var found = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr p) {
                NRectangle r;
                if (IsWindowVisible(h) && IsChat(h) && WindowMethods.GetWindowRect(h, out r) && r.Width > 200 && r.Height > 150) found.Add(h);
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }

    public class Overlay : Form {
        readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        Preferences prefs;
        Board board;
        string lastJson = "", error = "", hotkeyWarning = "";
        string connection = "正在寻找聊天窗口";
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly NotifyIcon tray = new NotifyIcon();
        IntPtr target;
        bool locked, userHidden, closing, explicitTarget, preview, windowed, dragging;
        int ticks, page;
        SessionObserver observer = new SessionObserver();
        MonitorSnapshot activity = new MonitorSnapshot();
        Task<MonitorSnapshot> observationTask;
        string observationKey="";
        readonly ToolTip tips = new ToolTip { AutoPopDelay=20000, InitialDelay=400 };
        string tip="";
        int PageSize { get { return Math.Max(1, Math.Min(prefs.expanded ? 6 : 3, (int)(Screen.FromPoint(Location).WorkingArea.Height / scale - 486) / 64)); } }
        int PageCount { get { return board == null ? 1 : Math.Max(1,(board.tasks.Count + PageSize - 1) / PageSize); } }
        int Rows { get { return Math.Max(1,Math.Min(PageSize,board == null ? 0 : board.tasks.Count - page * PageSize)); } }
        int PagerY { get { return 374 + Rows * 64; } }
        float scale = 1;
        readonly Color ink = Color.FromArgb(234, 241, 245), muted = Color.FromArgb(149, 165, 180);
        readonly Color mint = Color.FromArgb(120, 231, 196), amber = Color.FromArgb(250, 190, 97);
        ContextMenuStrip menu;

        public Overlay(string file, bool previewMode, bool windowedMode = false) {
            preview = previewMode;
            windowed = windowedMode;
            try { prefs = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(root, "settings.json"))); } catch { }
            if (prefs == null) prefs = new Preferences();
            if (windowed) prefs.follow = false;
            if (file != null) prefs.file = Path.GetFullPath(file);
            if (String.IsNullOrWhiteSpace(prefs.file)) prefs.file = Path.Combine(root, "progress.json");
            prefs.opacity = Math.Max(.25, Math.Min(1, prefs.opacity));
            Text = "ProgressGlass · 项目进度";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = windowed;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(21, 29, 39);
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            ResizeCard();
            Opacity = prefs.opacity;
            Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - Width - 24, Screen.PrimaryScreen.WorkingArea.Top + 90);
            Icon = SystemIcons.Information;
            if (!preview) {
                tray.Icon = Icon;
                tray.Text = "ProgressGlass · 双击切换穿透";
                tray.Visible = true;
                tray.DoubleClick += delegate { userHidden = false; SetLocked(!locked); Show(); };
                RebuildMenu();
            }
            ReadBoard();
            timer.Interval = 500;
            timer.Tick += delegate {
                if (++ticks % 2 == 0) ReadBoard();
                if(ticks%2==0) PollActivity();
                FollowWindow();
                Invalidate();
                if (ticks % 4 == 0) WriteRuntime();
            };
            Shown += delegate {
                if (preview) return;
                bool a = Native.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.P);
                bool b = Native.RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.O);
                if (!a || !b) hotkeyWarning = "快捷键被占用，请使用托盘菜单";
                timer.Start();
                FollowWindow();
            };
            MouseDown += OnDown;
            MouseMove += OnHover;
            MouseWheel += delegate(object sender, MouseEventArgs e) { if (!locked) ChangePage(e.Delta > 0 ? -1 : 1); };
            MouseUp += delegate { SaveOffset(); };
            FormClosing += delegate { closing = true; timer.Stop(); if (!preview) Save(); };
            FormClosed += delegate {
                Native.UnregisterHotKey(Handle, 1); Native.UnregisterHotKey(Handle, 2);
                tray.Visible = false; tray.Dispose(); timer.Dispose(); tips.Dispose(); if (menu != null) menu.Dispose();
            };
        }
        void PollActivity() {
            var config=board==null ? null : board.monitor;
            string desired=config==null ? "" : config.Key;
            if(observationTask!=null && !observationTask.IsCompleted) {
                if(desired!=observationKey) activity=new MonitorSnapshot();
                return;
            }
            if(observationTask!=null && observationTask.IsCompleted && desired==observationKey) {
                activity=observationTask.IsFaulted ? new MonitorSnapshot {state="source_error",title="观测模块异常",detail="不能据此判断是否仍在运行"} : observationTask.Result;
            }
            if(desired!=observationKey) { observer=new SessionObserver(); activity=new MonitorSnapshot(); observationKey=desired; }
            if(config==null) { activity=new MonitorSnapshot(); observationTask=null; return; }
            var reader=observer;
            observationTask=Task.Factory.StartNew(()=>reader.Read(config,DateTimeOffset.UtcNow,true));
        }
        void OnHover(object sender,MouseEventArgs e) {
            string nextTip=""; float y=e.Y/scale;
            if(y>=252 && y<346) nextTip=activity.title+"\n"+activity.detail+"\n"+activity.client+"；"+activity.localNetwork+"（不代表服务端可达）\n最近活动："+activity.lastEvent+"\n绑定会话："+activity.sessionId+"\n来源："+activity.sourcePath+"\n这是本地落盘记录，可能有延迟，不是服务端心跳。";
            if(board!=null && y>=385 && y<385+Rows*64) {
                int index=(int)((y-385)/64);
                var row=board.tasks.OrderBy(t=>t.status=="done"?1:0).Skip(page*PageSize+index).FirstOrDefault();
                if(row!=null) nextTip=row.title+"\n"+StatusName(row.status)+"\n"+(row.measurement==null ? "没有可核实的中间百分比。" : row.measurement.CountLabel+"\n来源："+row.measurement.source+"\n观测时间："+row.measurement.observedAt)+"\n证据："+row.evidence;
            }
            if(nextTip!=tip) { tip=nextTip; tips.SetToolTip(this,tip); }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get { var p = base.CreateParams; if (!windowed) p.ExStyle |= 0x80 | 0x08000000; return p; }
        }
        void WriteRuntime() {
            try {
                AtomicWrite(Path.Combine(root,"runtime.json"), new JavaScriptSerializer().Serialize(new {
                    visible=Visible, locked=locked, following=prefs.follow, targetFound=target!=IntPtr.Zero && Native.IsWindow(target),
                    bounds=new {x=Left,y=Top,width=Width,height=Height}, percent=board==null ? -1 : board.Percent,
                    current=board==null ? "" : board.current, error=error, hotkeyWarning=hotkeyWarning, page=page+1, pages=PageCount,
                    taskProgress=board==null ? null : board.tasks.Select(t=>new {id=t.id,percent=t.HasProgress ? (double?)t.Percent : null}).ToArray(),activity=activity
                }));
            } catch { }
        }
        void ResizeCard() {
            page = Math.Max(0,Math.Min(page,PageCount-1));
            Size = new Size((int)(384 * scale), (int)((PagerY + 88) * scale));
            using(var path = Rounded(new RectangleF(0, 0, Width, Height), 24 * scale)) {
                var old = Region; Region = new Region(path); if (old != null) old.Dispose();
            }
        }
        void Save() {
            if (preview) return;
            try { AtomicWrite(Path.Combine(root, "settings.json"), new JavaScriptSerializer().Serialize(prefs)); } catch { }
        }
        public static void AtomicWrite(string path, string text) {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, text, new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        void ReadBoard() {
            try {
                var fi = new FileInfo(prefs.file);
                if (!fi.Exists) throw new IOException("找不到任务记录，右键选择文件");
                if (fi.Length > 1024 * 1024) throw new IOException("任务文件超过 1 MB");
                string json;
                using(var f = new FileStream(prefs.file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using(var sr = new StreamReader(f, Encoding.UTF8)) json = sr.ReadToEnd();
                if (json != lastJson) { var fresh = Board.Parse(json); board = fresh; lastJson = json; ResizeCard(); }
                error = "";
            } catch (Exception ex) { error = "记录读取失败 · " + ex.Message; }
        }
        void FollowWindow() {
            if (preview || closing) return;
            if (userHidden) { Hide(); return; }
            var fg = Native.GetForegroundWindow();
            if (!explicitTarget && Native.IsChat(fg) && fg != Handle) target = fg;
            if (target == IntPtr.Zero || !Native.IsWindow(target)) {
                if (ticks % 4 == 0) target = Native.ChatWindows().FirstOrDefault();
            }
            bool alive = target != IntPtr.Zero && Native.IsWindow(target);
            connection = !prefs.follow ? "自由悬浮" : alive ? "已跟随聊天窗口" : "未找到聊天窗口 · 自由悬浮";
            if (prefs.follow && alive) {
                if (Native.IsIconic(target) || (fg != target && fg != Handle && Native.ProcessName(fg) != "ProgressGlass")) {
                    if (menu == null || !menu.Visible) Hide();
                    return;
                }
                NRectangle r;
                if (WindowMethods.GetWindowRect(target, out r) && !dragging) {
                    var area = Screen.FromHandle(target).WorkingArea;
                    int x = r.Right - Width - prefs.offsetX, y = r.Top + prefs.offsetY;
                    Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
                }
            }
            if (!Visible) Show();
        }
        void SaveOffset() {
            NRectangle r;
            if (prefs.follow && target != IntPtr.Zero && WindowMethods.GetWindowRect(target, out r)) {
                prefs.offsetX = r.Right - Right; prefs.offsetY = Top - r.Top;
            }
            Save();
        }
        void SetLocked(bool value) {
            locked = value;
            long s = WindowMethods.GetWindowLong(Handle, WindowMethods.WindowLong.ExStyle).ToInt64();
            if (value) s |= (long)WindowMethods.WindowExStyles.Transparent | (long)WindowMethods.WindowExStyles.Layered;
            else s &= ~(long)WindowMethods.WindowExStyles.Transparent;
            WindowMethods.SetWindowLong(Handle, WindowMethods.WindowLong.ExStyle, new IntPtr(s));
            RebuildMenu(); Invalidate();
        }
        void ToggleHidden() { userHidden = !userHidden; if (userHidden) Hide(); else { FollowWindow(); } }
        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x0312) {
                if (m.WParam.ToInt32() == 1) ToggleHidden();
                if (m.WParam.ToInt32() == 2) { userHidden = false; SetLocked(!locked); FollowWindow(); }
                return;
            }
            base.WndProc(ref m);
        }
        void OnDown(object sender, MouseEventArgs e) {
            if (locked) return;
            if (e.Button == MouseButtons.Right) { RebuildMenu(); menu.Show(this, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            float x = e.X / scale, y = e.Y / scale;
            if (y < 52 && x > 338) { ToggleHidden(); return; }
            if (y < 52 && x > 299) { prefs.expanded = !prefs.expanded; page = 0; ResizeCard(); Save(); return; }
            if (y >= PagerY && y < PagerY+30 && x >= 278) { ChangePage(x < 329 ? -1 : 1); return; }
            dragging = true;
            try { Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); }
            finally { dragging = false; SaveOffset(); }
        }
        void ChangePage(int delta) { page=Math.Max(0,Math.Min(PageCount-1,page+delta)); ResizeCard(); Invalidate(); }
        void RebuildMenu() {
            if (preview) return;
            var old = menu;
            menu = new ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏    Ctrl+Alt+P", null, delegate { ToggleHidden(); });
            menu.Items.Add(locked ? "解锁操作    Ctrl+Alt+O" : "鼠标穿透    Ctrl+Alt+O", null, delegate { SetLocked(!locked); });
            menu.Items.Add(prefs.expanded ? "紧凑视图 · 每页 3 项" : "展开视图 · 每页 6 项", null, delegate { prefs.expanded = !prefs.expanded; page=0; ResizeCard(); Save(); });
            menu.Items.Add("上一页任务", null, delegate { ChangePage(-1); });
            menu.Items.Add("下一页任务", null, delegate { ChangePage(1); });
            menu.Items.Add(new ToolStripSeparator());
            var alpha = new ToolStripMenuItem("不透明度");
            foreach (int n in new[] {35, 55, 70, 82, 95}) { int v = n; var item = new ToolStripMenuItem(n + "%"); item.Checked = Math.Abs(prefs.opacity - n / 100.0) < .01; item.Click += delegate { prefs.opacity = v / 100.0; Opacity = prefs.opacity; Save(); }; alpha.DropDownItems.Add(item); }
            menu.Items.Add(alpha);
            var follow = new ToolStripMenuItem("跟随聊天窗口（离开时隐藏）"); follow.Checked = prefs.follow;
            follow.Click += delegate { prefs.follow = !prefs.follow; userHidden = false; Save(); FollowWindow(); }; menu.Items.Add(follow);
            var windows = new ToolStripMenuItem("绑定聊天窗口");
            windows.DropDownItems.Add("自动跟随当前 ChatGPT / Codex", null, delegate { explicitTarget = false; target = IntPtr.Zero; prefs.follow = true; Save(); });
            foreach(var h in Native.ChatWindows()) { IntPtr captured = h; string title = WindowMethods.GetWindowText(h); windows.DropDownItems.Add(title.Length > 55 ? title.Substring(0,55) + "…" : title, null, delegate { target = captured; explicitTarget = true; prefs.follow = true; Save(); }); }
            menu.Items.Add(windows);
            menu.Items.Add("恢复到窗口右上角", null, delegate { prefs.offsetX = 20; prefs.offsetY = 90; Save(); FollowWindow(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("选择项目记录…", null, delegate {
                using(var d = new OpenFileDialog { Filter = "项目进度 (*.json)|*.json", FileName = prefs.file }) {
                    if (d.ShowDialog() == DialogResult.OK) { prefs.file = d.FileName; lastJson = ""; board = null; ReadBoard(); Save(); }
                }
            });
            menu.Items.Add("打开项目记录", null, delegate { try { Process.Start("notepad.exe", "\"" + prefs.file + "\""); } catch { } });
            menu.Items.Add("查看实时检测详情", null, delegate { try { Process.Start("notepad.exe", "\"" + Path.Combine(root,"runtime.json") + "\""); } catch {} });
            menu.Items.Add("绑定要检测的会话记录…", null, delegate {
                using(var d=new OpenFileDialog {Filter="Codex 会话记录 (*.jsonl)|*.jsonl"}) {
                    if(d.ShowDialog()!=DialogResult.OK) return;
                    try {
                        var serializer=new JavaScriptSerializer {MaxJsonLength=16*1024*1024};
                        string header; using(var reader=new StreamReader(d.FileName)) header=reader.ReadLine();
                        var h=serializer.Deserialize<Dictionary<string,object>>(header); object payload;
                        var p=h.TryGetValue("payload",out payload) ? payload as Dictionary<string,object> : null;
                        string id=ActivityState.Str(p,"id");
                        if(ActivityState.Str(h,"type")!="session_meta" || id=="") throw new FormatException("不是有效的会话记录");
                        var document=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(prefs.file));
                        document["monitor"]=new {sessionPath=d.FileName,sessionId=id};
                        document["updatedAt"]=DateTimeOffset.Now.ToString("o");
                        AtomicWrite(prefs.file,serializer.Serialize(document)); ReadBoard();
                    } catch { MessageBox.Show("绑定失败。请确认选择的是 Codex 会话 JSONL，并且项目记录可以写入。","ProgressGlass"); }
                }
            });
            menu.Items.Add("复制给 Codex 的接入说明", null, delegate {
                Clipboard.SetText("请维护项目进度文件：" + prefs.file + "。保留任务 ID。禁止把阶段数、耗时、调用次数或主观估计换成完成百分比。未知总工作量时只更新 current、next、任务状态和具体 evidence。只有实际计数时使用 measurement={completed,total,unit,source,observedAt}，来源必须可核实；例如处理条数、传输字节数。项目级 measurement 是单独的观测指标，不能按任务平均合成。done 仅用于已验证完成。监测会话需在 monitor 中填写准确的 sessionPath/sessionId；它只反映本地活动，不证明服务端仍在计算。使用 update-progress.ps1 或原子替换。");
            });
            menu.Items.Add("使用说明", null, delegate { Process.Start("notepad.exe", "\"" + Path.Combine(root, "README.md") + "\""); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出 ProgressGlass", null, delegate { Close(); });
            tray.ContextMenuStrip = menu;
            if (old != null) { BeginInvoke((Action)delegate { old.Dispose(); }); }
        }
        static GraphicsPath Rounded(RectangleF r, float radius) {
            var p = new GraphicsPath(); float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right-d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90); p.AddArc(r.X, r.Bottom-d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        void Label(Graphics g, string text, float size, Color color, float x, float y, float width, float height, bool bold) {
            using(var f = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
            using(var brush = new SolidBrush(color))
            using(var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit })
                g.DrawString(text ?? "", f, brush, new RectangleF(x, y, width, height), format);
        }
        string StatusName(string s) { switch(s) { case "done": return "已验证"; case "doing": return "进行中"; case "blocked": return "阻塞"; case "review": return "待验收"; default: return "待开始"; } }
        Color StatusColor(string s) { return s == "done" ? mint : s == "blocked" ? Color.FromArgb(255, 140, 139) : s == "doing" ? amber : muted; }
        void Panel(Graphics g, RectangleF r, Color fill, float radius) {
            using(var p=Rounded(r,radius)) using(var b=new SolidBrush(fill)) g.FillPath(b,p);
        }
        void ProgressBar(Graphics g, RectangleF r, double value, Color start, Color end) {
            Panel(g,r,Color.FromArgb(49,58,78),r.Height/2);
            if(value <= 0) return;
            using(var p=Rounded(r,r.Height/2)) {
                var state=g.Save(); g.SetClip(p);
                using(var b=new LinearGradientBrush(new PointF(r.Left,r.Top),new PointF(r.Right,r.Top),start,end))
                    g.FillRectangle(b,r.X,r.Y,(float)(r.Width*value/100),r.Height);
                g.Restore(state);
            }
        }
        string TaskPercent(TaskItem t) { return t.HasProgress ? (Math.Floor(t.Percent*10)/10).ToString("0.#") + "%" : "未量化"; }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            var g = e.Graphics; g.ScaleTransform(scale, scale); g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Color lavender=Color.FromArgb(170,164,255), blue=Color.FromArgb(110,165,255);
            using(var brush = new LinearGradientBrush(new Point(0,0), new Point(384,750), Color.FromArgb(31,36,57), Color.FromArgb(17,23,35))) g.FillRectangle(brush,0,0,384,Height/scale);
            using(var p = Rounded(new RectangleF(.5f,.5f,383,Height/scale-1),24)) using(var pen = new Pen(Color.FromArgb(75,85,115))) g.DrawPath(pen,p);
            Panel(g,new RectangleF(22,20,25,25),Color.FromArgb(59,61,102),8);
            Label(g,"P",15,lavender,29,22,20,23,true);
            Label(g,"PROGRESS GLASS",10,muted,56,25,160,20,true);
            Panel(g,new RectangleF(238,24,47,20),Color.FromArgb(33,59,62),10);
            using(var b=new SolidBrush(mint)) g.FillEllipse(b,246,32,4,4);
            Label(g,"LIVE",9,mint,256,27,32,15,true);
            Panel(g,new RectangleF(302,20,27,27),Color.FromArgb(43,49,69),8);
            Label(g,prefs.expanded ? "−" : "+",20,muted,308,19,25,28,false);
            Label(g,"×",18,muted,347,20,24,28,false);
            if(board == null) {
                Label(g,"等待项目记录",22,ink,24,78,332,38,true);
                Label(g,error,12,amber,24,130,332,80,false);
            } else {
                Label(g,board.project,20,ink,24,66,338,31,true);
                Label(g,board.HasOverallProgress ? board.Percent+"%" : "未量化",board.HasOverallProgress ? 40 : 30,ink,24,110,220,54,true);
                Label(g,board.HasOverallProgress ? "项目观测指标" : "总工作量尚未确定",11,muted,240,123,126,21,false);
                Label(g,board.HasOverallProgress ? board.measurement.CountLabel : "不按节点推算百分比",10,muted,240,143,126,20,false);
                ProgressBar(g,new RectangleF(24,174,336,9),board.ExactPercent,blue,mint);
                Panel(g,new RectangleF(24,200,336,44),Color.FromArgb(38,46,65),12);
                Label(g,"当前",10,lavender,36,215,36,19,true);
                Label(g,board.current,12,ink,77,210,271,32,false);
                Panel(g,new RectangleF(24,254,336,88),Color.FromArgb(33,43,57),12);
                Color signal=activity.state=="activity" ? mint : activity.state=="source_error" || activity.state=="error" ? Color.FromArgb(241,145,152) : amber;
                Label(g,activity.title,12,signal,36,265,310,23,true);
                Label(g,activity.detail,10,muted,36,290,310,22,false);
                string binding=activity.sessionId.Length>=8 ? activity.sessionId.Substring(activity.sessionId.Length-8) : "未绑定";
                Label(g,activity.localNetwork+" · 会话 "+binding+" · 本地记录",9,muted,36,316,310,17,false);
                Label(g,"任务进度与证据",12,ink,24,354,220,22,true);
                Label(g,"悬停看来源",10,muted,286,356,77,19,false);
                var ordered = board.tasks.OrderBy(t => t.status == "done" ? 1 : 0).Skip(page*PageSize).Take(PageSize).ToList();
                for(int i=0; i<ordered.Count; i++) {
                    var t=ordered[i]; int y=385+i*64;
                    Color accent=t.status=="blocked" ? Color.FromArgb(241,145,152) : t.status=="done" ? mint : lavender;
                    Label(g,t.title,12,ink,24,y,260,24,false);
                    Label(g,TaskPercent(t),12,accent,310,y,64,24,true);
                    ProgressBar(g,new RectangleF(24,y+27,336,5),t.Percent, t.status=="done" ? Color.FromArgb(94,167,159) : blue,accent);
                    string detail=t.measurement!=null ? t.measurement.CountLabel+" · "+t.measurement.source : StatusName(t.status)+" · "+(String.IsNullOrWhiteSpace(t.evidence) ? "尚无完成证据" : t.evidence);
                    Label(g,detail,9,muted,24,y+37,336,18,false);
                }
                if(board.tasks.Count==0) Label(g,"添加任务后，这里会显示进度与依据",12,muted,24,396,330,38,false);
            }
            Label(g,"下一步  " + (board==null ? "选择项目记录" : board.next),10,muted,24,PagerY+38,337,18,false);
            Label(g,(page+1) + " / " + PageCount + " 页",10,muted,24,PagerY+10,240,19,false);
            Panel(g,new RectangleF(282,PagerY+2,32,26),Color.FromArgb(39,47,65),8);
            Panel(g,new RectangleF(330,PagerY+2,32,26),Color.FromArgb(39,47,65),8);
            Label(g,"‹",21,page>0 ? ink : Color.FromArgb(77,88,108),293,PagerY-1,25,29,false);
            Label(g,"›",21,page<PageCount-1 ? ink : Color.FromArgb(77,88,108),341,PagerY-1,25,29,false);
            string stamp = "";
            if (board != null) { DateTimeOffset d; if(DateTimeOffset.TryParse(board.updatedAt,out d)) {
                double age = Math.Max(0,(DateTimeOffset.UtcNow-d.ToUniversalTime()).TotalMinutes);
                stamp = age < 1 ? "刚刚更新" : age < 60 ? (int)age + " 分钟前更新" : (int)(age/60) + " 小时前更新";
            } }
            Label(g,String.IsNullOrEmpty(error) ? stamp + " · " + (locked ? "穿透中" : "可拖动") : "读取异常 · 保留上次数据",9,String.IsNullOrEmpty(error) ? muted : amber,24,PagerY+63,220,17,false);
            Label(g,hotkeyWarning!="" ? "快捷键冲突 · 用托盘" : "Ctrl+Alt+O  切换穿透",9,muted,242,PagerY+63,127,17,false);
        }
        public void Render(string output, bool expanded = true) {
            prefs.expanded = expanded; ResizeCard(); connection = "本地任务记录 · 1 秒刷新";
            if(board!=null && board.monitor!=null) { for(int i=0;i<32;i++) { activity=observer.Read(board.monitor,DateTimeOffset.UtcNow,true); if(!activity.catchingUp) break; } }
            using(var bmp = new Bitmap(Width, Height)) { DrawToBitmap(bmp, new Rectangle(Point.Empty,Size)); bmp.Save(output, System.Drawing.Imaging.ImageFormat.Png); }
        }
    }
    internal static class Program {
        [STAThread] static int Main(string[] args) {
            Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string file = null;
            for(int i=0;i<args.Length-1;i++) if(args[i]=="--file") file=args[i+1];
            if(args.Length>1 && args[0]=="--render-style") {PetSprites.RenderPreview(AppDomain.CurrentDomain.BaseDirectory,args[1]);return 0;}
            if(args.Length>1 && args[0]=="--render-animation") {PetSprites.RenderAnimation(AppDomain.CurrentDomain.BaseDirectory,args[1]);return 0;}
            if(args.Length>1 && args[0]=="--render") { using(var f=new Overlay(file,true)) f.Render(args[1],!args.Contains("--compact")); return 0; }
            if(args.Length>1 && args[0]=="--render-pet") {
                string home=Environment.GetEnvironmentVariable("CODEX_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
                var observer=new AllChatsObserver(home,AppDomain.CurrentDomain.BaseDirectory,file??Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"progress.json"));
                DashboardSnapshot data=null;for(int i=0;i<32;i++){data=observer.Read(DateTimeOffset.Now);if(data.loading==0)break;}
                using(var f=new CompanionBubble(1)){f.SetData(data,ConnectionProbe.Read());f.Render(args[1]);}return 0;
            }
            if(args.Length>1 && args[0]=="--validate") {
                try { var b=Board.Parse(File.ReadAllText(args[1])); File.WriteAllText(args[1]+".validation.txt", "PASS: "+b.tasks.Count+" tasks; "+b.Percent+"% verified"); return 0; }
                catch(Exception ex) { File.WriteAllText(args[1]+".validation.txt", "FAIL: "+ex.Message); return 1; }
            }
            bool fresh;
            using(var mutex=new System.Threading.Mutex(true,"Local\\ProgressGlass-" + AppDomain.CurrentDomain.BaseDirectory.GetHashCode().ToString("X"),out fresh)) {
                if(!fresh) { MessageBox.Show("ProgressGlass 已在运行。请使用系统托盘图标，或 Ctrl+Alt+P 显示浮层。","ProgressGlass"); return 0; }
                if(args.Contains("--legacy")) Application.Run(new Overlay(file,false,args.Contains("--windowed")));
                else Application.Run(new PetCompanion(file,args.Contains("--windowed")));
            }
            return 0;
        }
    }
}
