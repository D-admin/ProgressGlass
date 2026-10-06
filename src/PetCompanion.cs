// ProgressGlass additions, Copyright (c) 2026. Licensed under MS-RL.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ProgressGlass {
    public class PetSettings { public int x=Int32.MinValue,y=Int32.MinValue,size=128; }
    public class PetGesture {
        public bool pressed,dragged;
        public Point origin,window;
        public void Down(Point cursor,Point location) {pressed=true;dragged=false;origin=cursor;window=location;}
        public Point Move(Point cursor) { if(pressed && (Math.Abs(cursor.X-origin.X)>5 || Math.Abs(cursor.Y-origin.Y)>5)) dragged=true;return new Point(window.X+cursor.X-origin.X,window.Y+cursor.Y-origin.Y); }
        public bool Up() {bool click=pressed && !dragged;pressed=false;return click;}
    }
    public static class PetLayout {
        public static Point Clamp(Point p,Size size,Rectangle area) {return new Point(Math.Max(area.Left,Math.Min(p.X,area.Right-size.Width)),Math.Max(area.Top,Math.Min(p.Y,area.Bottom-size.Height)));}
        public static Point Bubble(Rectangle pet,Size size,Rectangle area) {
            Point p=new Point(pet.Right-size.Width,pet.Top-size.Height+6);
            if(p.Y<area.Top) p=new Point(pet.Left-size.Width-8,pet.Top);
            if(p.X<area.Left && pet.Right+size.Width+8<=area.Right) p.X=pet.Right+8;
            return Clamp(p,size,area);
        }
    }
    internal static class LayeredPet {
        [StructLayout(LayoutKind.Sequential)] struct Blend {public byte op,flags,alpha,format;}
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h,IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dc,ref Point destination,ref Size size,IntPtr source,ref Point origin,int key,ref Blend blend,int flags);
        public static void Draw(Form form,Bitmap bitmap) {
            IntPtr dc=GetDC(IntPtr.Zero),mem=CreateCompatibleDC(dc),h=bitmap.GetHbitmap(Color.FromArgb(0)),old=SelectObject(mem,h);
            try {Point destination=form.Location,origin=Point.Empty;Size size=bitmap.Size;Blend blend=new Blend{alpha=255,format=1};
                if(!UpdateLayeredWindow(form.Handle,dc,ref destination,ref size,mem,ref origin,0,ref blend,2)) throw new System.ComponentModel.Win32Exception();
            } finally {SelectObject(mem,old);DeleteObject(h);DeleteDC(mem);ReleaseDC(IntPtr.Zero,dc);}
        }
    }
    public class PetCompanion : Form {
        readonly string root=AppDomain.CurrentDomain.BaseDirectory;
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        readonly Timer timer=new Timer {Interval=150};
        readonly NotifyIcon tray=new NotifyIcon();
        readonly PetGesture gesture=new PetGesture();
        readonly AllChatsObserver observer;
        readonly CompanionBubble bubble;
        readonly PetSprites art;
        readonly Icon mascotIcon;
        readonly System.Diagnostics.Stopwatch animationClock=System.Diagnostics.Stopwatch.StartNew();
        double greetingUntil;
        int lastPose=-1,lastSway=-1,renderCount;
        Size renderedSize;
        PetSettings prefs;
        Task<DashboardSnapshot> dataTask;
        Task<NetworkSnapshot> networkTask;
        DateTimeOffset nextData=DateTimeOffset.MinValue,nextNetwork=DateTimeOffset.MinValue,lastInside=DateTimeOffset.Now;
        DashboardSnapshot data=new DashboardSnapshot();
        NetworkSnapshot network=new NetworkSnapshot();
        bool pinned,hidden,suppressHover;
        readonly bool windowed;
        float dpi=1;
        public PetCompanion(string file,bool windowedMode=false) {
            windowed=windowedMode;
            try {prefs=json.Deserialize<PetSettings>(File.ReadAllText(Path.Combine(root,"pet-settings.json")));}catch{}
            if(prefs==null) prefs=new PetSettings();prefs.size=Math.Max(88,Math.Min(160,prefs.size));
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=windowed;TopMost=true;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;Text="ProgressGlass · 薄荷";
            using(var g=CreateGraphics()) dpi=g.DpiX/96f;
            art=new PetSprites(Path.Combine(root,"assets","mint-girl-sprites.png"));
            mascotIcon=new Icon(Path.Combine(root,"assets","mint-girl.ico"),32,32);Icon=mascotIcon;
            Size=new Size((int)(prefs.size*dpi),(int)(prefs.size*dpi));
            var area=Screen.PrimaryScreen.WorkingArea;
            Location=prefs.x==Int32.MinValue ? new Point(area.Right-Width-32,area.Bottom-Height-32) : PetLayout.Clamp(new Point(prefs.x,prefs.y),Size,Screen.FromPoint(new Point(prefs.x,prefs.y)).WorkingArea);
            bubble=new CompanionBubble(dpi);
            bubble.Icon=mascotIcon;
            bubble.PinRequested+=delegate {pinned=!pinned;bubble.Pinned=pinned;bubble.Invalidate();};
            bubble.CloseRequested+=delegate {pinned=false;suppressHover=true;bubble.Hide();};
            string home=Environment.GetEnvironmentVariable("CODEX_HOME");if(String.IsNullOrEmpty(home)) home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
            observer=new AllChatsObserver(home,root,file??Path.Combine(root,"progress.json"));
            var menu=new ContextMenuStrip();
            menu.Items.Add("显示 / 收起任务气泡",null,delegate {hidden=false;Show();ToggleBubble();});
            foreach(int size in new[]{96,128,160}) {int value=size;menu.Items.Add("角色大小 · "+size,null,delegate {prefs.size=value;Size=new Size((int)(value*dpi),(int)(value*dpi));Location=PetLayout.Clamp(Location,Size,Screen.FromPoint(Location).WorkingArea);PaintPet();PlaceBubble();Save();});}
            menu.Items.Add("隐藏角色（Ctrl+Alt+P 恢复）",null,delegate {hidden=true;Hide();bubble.Hide();});
            menu.Items.Add("退出",null,delegate {Close();});ContextMenuStrip=menu;
            tray.Icon=mascotIcon;tray.Text="薄荷 · 悬停看任务，点击固定";tray.ContextMenuStrip=menu;tray.Visible=true;
            tray.DoubleClick+=delegate {hidden=false;Show();OpenBubble();};
            Shown+=delegate {PaintPet();Native.RegisterHotKey(Handle,1,0x4003,(uint)Keys.P);Native.RegisterHotKey(Handle,2,0x4003,(uint)Keys.O);timer.Start();Tick();};
            MouseEnter+=delegate {greetingUntil=animationClock.Elapsed.TotalMilliseconds+1800;if(!suppressHover && !gesture.pressed) OpenBubble();};
            MouseDown+=delegate(object sender,MouseEventArgs e) {if(e.Button==MouseButtons.Left){gesture.Down(Cursor.Position,Location);Capture=true;}};
            MouseMove+=delegate {if(!gesture.pressed)return;Point p=gesture.Move(Cursor.Position);if(gesture.dragged){Location=PetLayout.Clamp(p,Size,Screen.FromPoint(Cursor.Position).WorkingArea);if(!pinned)bubble.Hide();else PlaceBubble();}};
            MouseUp+=delegate(object sender,MouseEventArgs e) {if(e.Button!=MouseButtons.Left)return;bool click=gesture.Up();Capture=false;if(click){greetingUntil=animationClock.Elapsed.TotalMilliseconds+1800;ToggleBubble();}else {suppressHover=true;Save();}};
            timer.Tick+=delegate {Tick();};
            FormClosed+=delegate {timer.Stop();Save();Native.UnregisterHotKey(Handle,1);Native.UnregisterHotKey(Handle,2);bubble.Dispose();tray.Visible=false;tray.Dispose();menu.Dispose();art.Dispose();mascotIcon.Dispose();timer.Dispose();};
        }
        protected override bool ShowWithoutActivation {get{return !windowed;}}
        protected override CreateParams CreateParams {get{var cp=base.CreateParams;cp.ExStyle|=0x80000;if(!windowed)cp.ExStyle|=0x80|0x8000000;return cp;}}
        protected override void WndProc(ref Message m) {if(m.Msg==0x312){if(m.WParam.ToInt32()==1){hidden=!hidden;if(hidden){Hide();bubble.Hide();}else Show();}else {hidden=false;Show();ToggleBubble();}return;}base.WndProc(ref m);}
        void PaintPet() {double elapsed=animationClock.Elapsed.TotalMilliseconds;int pose=PetSprites.Pose(elapsed,elapsed<greetingUntil),sway=PetSprites.Sway(elapsed);
            if(pose==lastPose && sway==lastSway && renderedSize==Size)return;
            LayeredPet.Draw(this,art.Frame(Size,pose,sway));lastPose=pose;lastSway=sway;renderedSize=Size;renderCount++;}
        void Save() {prefs.x=Left;prefs.y=Top;try{Overlay.AtomicWrite(Path.Combine(root,"pet-settings.json"),json.Serialize(prefs));}catch{}}
        void PlaceBubble() {bubble.Location=PetLayout.Bubble(Bounds,bubble.Size,Screen.FromRectangle(Bounds).WorkingArea);}
        void OpenBubble() {if(hidden)return;bubble.Pinned=pinned;PlaceBubble();bubble.SetData(data,network);if(!bubble.Visible)bubble.Show(this);lastInside=DateTimeOffset.Now;}
        void ToggleBubble() {if(pinned){pinned=false;bubble.Hide();suppressHover=true;}else {pinned=true;OpenBubble();}bubble.Pinned=pinned;bubble.Invalidate();}
        void Tick() {
            var now=DateTimeOffset.Now;
            if(!hidden && !(gesture.pressed && gesture.dragged))PaintPet();
            bool inPet=Bounds.Contains(Cursor.Position),inside=inPet || (bubble.Visible && bubble.Bounds.Contains(Cursor.Position));
            if(!inPet)suppressHover=false;
            if(inside)lastInside=now;else if(!pinned && (now-lastInside).TotalMilliseconds>650)bubble.Hide();
            if(dataTask!=null && dataTask.IsCompleted){
                if(dataTask.IsFaulted)data.error="会话观测暂不可用";else data=dataTask.Result;dataTask=null;bubble.SetData(data,network);PlaceBubble();
                try{Overlay.AtomicWrite(Path.Combine(root,"runtime.json"),json.Serialize(new{version="0.4.2",mode="desktop-pet",observedAt=now.ToString("o"),pet=new{x=Left,y=Top,size=prefs.size,style="mint-dress",animationPose=lastPose,animationSway=lastSway,renderCount=renderCount,trayIcon="mint-girl.ico"},scope="all-local-codex-projects",dashboard=data,network=network}));}catch{}
            }
            if(networkTask!=null && networkTask.IsCompleted){network=networkTask.IsFaulted ? new NetworkSnapshot{title="网络检测暂不可用"} : networkTask.Result;networkTask=null;bubble.SetData(data,network);}
            if(dataTask==null && now>=nextData){nextData=now.AddSeconds(1);dataTask=Task.Factory.StartNew(()=>observer.Read(DateTimeOffset.Now));}
            if(networkTask==null && now>=nextNetwork){nextNetwork=now.AddSeconds(30);networkTask=Task.Factory.StartNew(()=>ConnectionProbe.Read());}
        }
    }
    public class CompanionBubble : Form {
        DashboardSnapshot data=new DashboardSnapshot();NetworkSnapshot network=new NetworkSnapshot();
        readonly float scale;int page;readonly int maxRows;
        int rows {get{return Math.Max(1,Math.Min(maxRows,data.chats.Count));}}
        readonly ToolTip tips=new ToolTip{AutoPopDelay=25000,InitialDelay=450};string tip="";
        public bool Pinned;
        public event Action PinRequested,CloseRequested;
        readonly Color ink=Color.FromArgb(232,241,242),muted=Color.FromArgb(149,169,180),mint=Color.FromArgb(146,232,203),amber=Color.FromArgb(247,198,126);
        int Pages {get{return Math.Max(1,(data.chats.Count+rows-1)/rows);}}
        int Footer {get{return 275+rows*86;}}
        public CompanionBubble(float dpi) {
            scale=dpi;maxRows=Math.Max(1,Math.Min(3,(int)(Screen.PrimaryScreen.WorkingArea.Height/dpi-377)/86));
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;
            BackColor=Color.FromArgb(20,30,40);ResizeBubble();Text="薄荷 · 活动聊天";
            MouseWheel+=delegate(object s,MouseEventArgs e){page=Math.Max(0,Math.Min(Pages-1,page+(e.Delta>0?-1:1)));Invalidate();};
            MouseDown+=delegate(object s,MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;float x=e.X/scale,y=e.Y/scale;
                if(y<60 && x>=394){if(CloseRequested!=null)CloseRequested();}
                else if(y<60 && x>=330){if(PinRequested!=null)PinRequested();}
                else if(y>=Footer && y<Footer+42){if(x>=382)page=Math.Min(Pages-1,page+1);else if(x>=330)page=Math.Max(0,page-1);Invalidate();}
            };
            MouseMove+=delegate(object s,MouseEventArgs e){int i=(int)((e.Y/scale-275)/86);string next="";
                if(e.Y/scale>=275 && i>=0 && i<rows && page*rows+i<data.chats.Count){var c=data.chats[page*rows+i];next=c.title+"\n项目："+c.cwd+"\n"+c.activity.title+"\n"+c.activity.detail+"\n最后活动："+c.activity.lastEvent+"\n会话："+c.id+(c.current!="" ? "\n当前："+c.current : "")+(c.measurement!=null?"\n实测："+c.measurement.CountLabel+"\n来源："+c.measurement.source:"");}
                else if(e.Y/scale>=76 && e.Y/scale<170 && data.usage!=null)next=data.usage.source+"\n记录时间："+data.usage.observedAt+"\n这是账号额度，与项目完成百分比无关。";
                else if(e.Y/scale>=180 && e.Y/scale<241)next=network.detail+"\n检测时间："+network.observedAt+"\n每 30 秒通过系统代理访问 "+network.probeUrl+"，不发送账号凭证。";
                if(next!=tip){tip=next;tips.SetToolTip(this,tip);}
            };
        }
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var cp=base.CreateParams;cp.ExStyle|=0x80|0x8000000;return cp;}}
        protected override void Dispose(bool disposing){if(disposing)tips.Dispose();base.Dispose(disposing);}
        void ResizeBubble(){Size desired=new Size((int)(440*scale),(int)((Footer+94)*scale));if(Size==desired)return;Size=desired;
            using(var p=Rounded(new RectangleF(0,0,Width,Height-12*scale),22*scale))using(var tail=new GraphicsPath()){tail.AddPolygon(new[]{new PointF(Width-78*scale,Height-13*scale),new PointF(Width-60*scale,Height),new PointF(Width-45*scale,Height-13*scale)});Region old=Region;var region=new Region(p);region.Union(tail);Region=region;if(old!=null)old.Dispose();}}
        public void SetData(DashboardSnapshot value,NetworkSnapshot connection){data=value;network=connection;page=Math.Min(page,Pages-1);ResizeBubble();Invalidate();}
        public static GraphicsPath Rounded(RectangleF r,float radius){var p=new GraphicsPath();float d=radius*2;p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
        void TextAt(Graphics g,string text,float size,Color color,float x,float y,float w,float h,bool bold=false){using(var f=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))using(var b=new SolidBrush(color))using(var fmt=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text??"",f,b,new RectangleF(x,y,w,h),fmt);}
        void Card(Graphics g,RectangleF r,Color c){using(var p=Rounded(r,13))using(var b=new SolidBrush(c))g.FillPath(b,p);}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);PaintContent(e.Graphics);}
        void PaintContent(Graphics g){g.ScaleTransform(scale,scale);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using(var b=new LinearGradientBrush(new Rectangle(0,0,440,190),Color.FromArgb(33,54,59),BackColor,90f))g.FillRectangle(b,0,0,440,190);
            TextAt(g,"薄荷",21,ink,22,17,150,30,true);TextAt(g,"陪你看着每一项工作",12,muted,23,49,260,20);
            Card(g,new RectangleF(329,20,60,32),Color.FromArgb(48,66,75));TextAt(g,Pinned?"已固定":"固定",12,mint,340,27,50,21);TextAt(g,"×",26,muted,400,19,30,34);
            Card(g,new RectangleF(20,80,400,91),Color.FromArgb(36,51,61));
            if(data.usage==null){TextAt(g,"额度记录尚不可用",17,ink,34,94,365,27,true);TextAt(g,"收到真实额度记录后自动显示",12,muted,34,135,365,24);}
            else {var u=data.usage;int n=Math.Min(2,u.windows.Count);for(int i=0;i<n;i++){var w=u.windows[i];float x=34+i*193;bool expired=DateTimeOffset.Now>=w.Reset;
                TextAt(g,w.Name,12,muted,x,91,180,20);TextAt(g,expired?"待更新":w.Remaining.ToString("0.#")+"% 剩余",21,expired?amber:mint,x,111,190,31,true);
                TextAt(g,expired?"已过重置时间":w.Reset.ToLocalTime().ToString("MM-dd HH:mm")+" 重置",11,muted,x,146,190,20);}
            }
            TextAt(g,data.usage==null?"":data.usage.Freshness(DateTimeOffset.Now),10,muted,230,63,192,16);
            Card(g,new RectangleF(20,182,400,57),Color.FromArgb(29,43,53));TextAt(g,network.title,14,network.verifiedReachable?mint:amber,34,190,366,22,true);TextAt(g,network.detail,11,muted,34,213,370,20);
            TextAt(g,"活动聊天  "+data.chats.Count,14,ink,23,250,240,23,true);TextAt(g,"所有本机项目",11,muted,326,252,105,22);
            for(int i=0;i<rows && page*rows+i<data.chats.Count;i++){var c=data.chats[page*rows+i];int y=275+i*86;bool quiet=c.activity.state=="quiet" || c.activity.state=="error";
                Card(g,new RectangleF(20,y,400,78),Color.FromArgb(29,43,53));
                TextAt(g,c.project+"  /  "+c.title,13,ink,34,y+8,373,23,true);
                TextAt(g,c.activity.title,12,quiet?amber:mint,34,y+33,372,21);
                string detail=c.measurement!=null ? "实测 "+c.measurement.CountLabel+" · "+c.measurement.Percent.ToString("0.#")+"%" : c.current!="" ? c.current : c.activity.detail;
                TextAt(g,detail,11,muted,34,y+56,372,18);
            }
            if(data.chats.Count==0){TextAt(g,data.loading>0?"正在读取会话历史…":"现在没有观察到活动聊天",17,ink,35,305,370,30,true);TextAt(g,"已结束和已中断的回合会自动隐藏",12,muted,35,343,370,23);}
            string state=data.error!="" ? data.error : data.loading>0 ? data.loading+" 个会话正在读取" : data.uncertain>0 ? data.uncertain+" 个会话状态未确认，未计入列表" : "已扫描 "+data.discovered+" 个本机会话 · 云端未覆盖";
            TextAt(g,state,10,data.error!=""?amber:muted,23,Footer+8,300,30);
            Card(g,new RectangleF(330,Footer,38,34),Color.FromArgb(43,59,68));TextAt(g,"‹",26,muted,343,Footer-2,30,35);
            Card(g,new RectangleF(382,Footer,38,34),Color.FromArgb(43,59,68));TextAt(g,"›",26,muted,395,Footer-2,30,35);
            TextAt(g,"本地记录 ≠ 服务端心跳 · 静默时无法确认是否卡住",11,muted,23,Footer+42,398,20);TextAt(g,"悬停查看详情 · "+(page+1)+" / "+Pages+" 页 · 点击角色固定气泡",10,muted,23,Footer+62,398,18);
        }
        public void Render(string path){using(var bitmap=new Bitmap(Width,Height)){using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.SetClip(Region,CombineMode.Replace);using(var brush=new SolidBrush(BackColor))g.FillRectangle(brush,0,0,Width,Height);PaintContent(g);}bitmap.Save(path,ImageFormat.Png);}}
    }
}
