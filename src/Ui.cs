using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexRelay {
    internal static class Theme {
        public static Color Back,Panel,Line,Text,Muted,Accent,Warning;
        public static bool IsDark;
        static Theme(){SetDark(false);}
        static readonly Icon appIcon=LoadIcon();
        static Icon LoadIcon(){using(var stream=typeof(Theme).Assembly.GetManifestResourceStream("CodexRelay.AppIcon.ico")){if(stream==null)return SystemIcons.Application;using(var icon=new Icon(stream))return (Icon)icon.Clone();}}
        public static void SetDark(bool dark){IsDark=dark;Back=ColorTranslator.FromHtml(dark?"#23262B":"#FFFFFF");Panel=ColorTranslator.FromHtml(dark?"#2C3036":"#F5F6F8");Line=ColorTranslator.FromHtml(dark?"#3A3F46":"#E3E5E8");Text=ColorTranslator.FromHtml(dark?"#E6E8EB":"#1F2328");Muted=ColorTranslator.FromHtml(dark?"#A7AEB8":"#626B77");Accent=ColorTranslator.FromHtml(dark?"#7FB3A8":"#2563EB");Warning=ColorTranslator.FromHtml(dark?"#E0A458":"#B45309");}
        public static Label Label(string text,float size=10,Color? color=null) { return new Label { Text=text,Tag=color==Muted?"muted":color==Accent?"accent":"text",AutoSize=false,BackColor=Color.Transparent,ForeColor=color??Text,Font=new Font("맑은 고딕",size),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,UseMnemonic=false }; }
        public static Button Button(string text,int width=110) {
            var b=new Button { Text=text,Width=width,Height=32,FlatStyle=FlatStyle.Flat,BackColor=Back,ForeColor=Text,Font=new Font("맑은 고딕",9),Cursor=Cursors.Hand,Margin=new Padding(0,0,8,0),UseVisualStyleBackColor=false };
            b.FlatAppearance.BorderColor=Line; b.FlatAppearance.MouseOverBackColor=Panel; return b;
        }
        public static ContextMenuStrip Menu(){var menu=new ContextMenuStrip {ShowImageMargin=false,ShowCheckMargin=false,ShowItemToolTips=false,BackColor=Panel,ForeColor=Text,Font=new Font("맑은 고딕",9),Padding=new Padding(4),Renderer=new ThemeMenuRenderer()};menu.ItemAdded+=(s,e)=>{if(!(e.Item is ToolStripSeparator))e.Item.Padding=new Padding(8,4,8,4);};return menu;}
        public static void ApplyMenu(ContextMenuStrip menu){if(menu==null||menu.IsDisposed)return;menu.BackColor=Panel;menu.ForeColor=Text;foreach(ToolStripItem item in menu.Items)item.ForeColor=item.Enabled?Text:Muted;menu.Invalidate();}
        public static void Form(Form f) {
            f.BackColor=Back; f.ForeColor=Text; f.Font=new Font("맑은 고딕",9); f.AutoScaleDimensions=new SizeF(96,96); f.AutoScaleMode=AutoScaleMode.Dpi; f.StartPosition=FormStartPosition.CenterScreen; f.Icon=appIcon;
            // Modal windows create their native handle only when opened. Apply the
            // caption theme on every handle creation, including handle recreation.
            f.HandleCreated+=(s,e)=>ApplyCaption(f.Handle);
            if(f.IsHandleCreated)ApplyCaption(f.Handle);
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h,int attr,ref int value,int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int attr,out int value,int size);
        [DllImport("dwmapi.dll")] static extern int DwmFlush();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int message,IntPtr w,IntPtr l);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr h,IntPtr rect,IntPtr region,uint flags);
        const int WmSetRedraw=0x000B,DwmTransitionsForcedDisabled=3,DwmDarkMode=20;
        const uint RedrawAll=0x0001|0x0004|0x0080|0x0100|0x0400;
        static bool GetDwm(IntPtr handle,int attribute,out int value){value=0;try{return DwmGetWindowAttribute(handle,attribute,out value,4)==0;}catch(DllNotFoundException){return false;}catch(EntryPointNotFoundException){return false;}}
        static bool SetDwm(IntPtr handle,int attribute,int value){try{return DwmSetWindowAttribute(handle,attribute,ref value,4)==0;}catch(DllNotFoundException){return false;}catch(EntryPointNotFoundException){return false;}}
        static void ApplyCaption(IntPtr handle){SetDwm(handle,DwmDarkMode,IsDark?1:0);}
        public static void Apply(Control c){
            var form=c as Form;
            if(form==null||!form.IsHandleCreated){ApplyPalette(c);return;}
            IntPtr handle=form.Handle;bool redraw=form.Visible;int previousTransitions;
            bool restoreTransitions=GetDwm(handle,DwmTransitionsForcedDisabled,out previousTransitions)&&SetDwm(handle,DwmTransitionsForcedDisabled,1);
            form.SuspendLayout();
            if(redraw)SendMessage(handle,WmSetRedraw,IntPtr.Zero,IntPtr.Zero);
            try{
                ApplyPalette(form);
                // Keep the system caption, border, rounded corners and caption-button rendering.
                // Request only its standard light/dark variant; do not override caption/text colors.
                ApplyCaption(handle);
            }finally{
                form.ResumeLayout(true);
                // WM_SETREDRAW(TRUE) can show a hidden window, so only pair it for an already-visible form.
                if(redraw){SendMessage(handle,WmSetRedraw,(IntPtr)1,IntPtr.Zero);RedrawWindow(handle,IntPtr.Zero,IntPtr.Zero,RedrawAll);}
                if(restoreTransitions){try{DwmFlush();}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}finally{SetDwm(handle,DwmTransitionsForcedDisabled,previousTransitions);}}
            }
        }
        static void ApplyPalette(Control c){
            if(c is Label){c.BackColor=Color.Transparent;c.ForeColor=(string)c.Tag=="muted"?Muted:(string)c.Tag=="accent"?Accent:Text;}
            else if(c is Button){var b=(Button)c;bool primary=(string)b.Tag=="primary";b.BackColor=primary?Accent:Back;b.ForeColor=primary?(IsDark?Back:Color.White):Text;b.FlatAppearance.BorderColor=primary?Accent:Line;b.FlatAppearance.MouseOverBackColor=primary?Accent:Panel;}
            else {c.BackColor=(string)c.Tag=="surface"?Panel:c.Parent!=null?c.Parent.BackColor:Back;c.ForeColor=Text;}
            foreach(Control child in c.Controls)ApplyPalette(child);c.Invalidate();
        }
    }
    internal sealed class LoginDialog : Form {
        readonly Engine engine;
        readonly Account reconnect;
        LoginFlow flow;
        CancellationTokenSource cancel;
        readonly TextBox name=new TextBox(), link=new TextBox();
        readonly Label note=Theme.Label("로그인 링크를 만들면 브라우저에서 계정을 추가할 수 있습니다.",9,Theme.Muted);
        readonly Button start=Theme.Button("로그인 링크 만들기",160), browser=Theme.Button("브라우저 열기",130), copy=Theme.Button("링크 복사",110);
        public LoginDialog(Engine engine,Account reconnect=null) {
            this.engine=engine;this.reconnect=reconnect; Theme.Form(this); Text=(reconnect==null?"계정 추가":"다시 로그인")+" · Codex Relay"; ClientSize=new Size(580,310); FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false;
            var title=Theme.Label("ChatGPT로 로그인",18); title.SetBounds(24,18,530,40); Controls.Add(title);
            var caption=Theme.Label("계정 별칭 (선택)",9,Theme.Muted); caption.SetBounds(24,68,530,24); Controls.Add(caption);
            name.SetBounds(24,96,530,30); name.BackColor=Theme.Panel; name.ForeColor=Theme.Text; name.BorderStyle=BorderStyle.FixedSingle; name.MaxLength=60; Controls.Add(name);
            start.SetBounds(24,140,170,36); browser.SetBounds(204,140,135,36); copy.SetBounds(349,140,110,36); browser.Enabled=copy.Enabled=false; Controls.AddRange(new Control[]{start,browser,copy});
            link.SetBounds(24,188,530,27); link.ReadOnly=true; link.BackColor=Theme.Panel; link.ForeColor=Theme.Muted; Controls.Add(link);
            note.SetBounds(24,224,530,66); Controls.Add(note);
            if(reconnect!=null){name.Text=reconnect.Name;note.Text=reconnect.Email+" 계정으로 로그인하면 기존 순서와 별칭을 유지하고 인증 정보를 갱신합니다.";}
            start.Click+=async(s,e)=>await BeginLogin();
            browser.Click+=(s,e)=>{ if(flow!=null) OpenBrowser(); };
            copy.Click+=(s,e)=>{ try { Clipboard.SetText(link.Text); note.Text="링크를 복사했습니다. 로그인 완료까지 이 창을 열어 두세요."; } catch { note.Text="클립보드를 사용할 수 없습니다. 링크를 직접 복사하세요."; } };
            FormClosing+=(s,e)=>{ if(cancel!=null) cancel.Cancel(); if(flow!=null) flow.Dispose(); };
        }
        void OpenBrowser() { try { Process.Start(new ProcessStartInfo(flow.Url) { UseShellExecute=true }); } catch { note.Text="브라우저를 열지 못했습니다. 링크를 복사해 브라우저에 붙여 넣으세요."; } }
        async Task BeginLogin() {
            try {
                flow=new LoginFlow(); cancel=new CancellationTokenSource(TimeSpan.FromMinutes(5));
                link.Text=flow.Url; start.Enabled=false; browser.Enabled=copy.Enabled=true; note.Text=(reconnect==null?"브라우저에서 로그인하세요. 계정 추가만 진행하며 현재 Codex 계정은 유지됩니다.":reconnect.Email+" 계정으로 다시 로그인하세요. 현재 Codex 계정은 유지됩니다.")+"\n대기 시간: 5분";
                var completion=flow.Complete(engine.Api,cancel.Token); OpenBrowser();
                string raw=await completion; await engine.Add(raw,name.Text,reconnect==null?null:reconnect.Id); DialogResult=DialogResult.OK; Close();
            } catch(Exception ex) { if(!IsDisposed) { note.Text=Engine.Friendly(ex); start.Enabled=true; browser.Enabled=copy.Enabled=false; link.Clear(); } }
            finally { if(flow!=null) { flow.Dispose(); flow=null; } if(cancel!=null) { cancel.Dispose(); cancel=null; } }
        }
    }
    internal sealed class Overlay : Form {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
        readonly Label account=Theme.Label("Codex Relay",8.5f,Theme.Muted), tps=Theme.Label("— TPS",18), usage=Theme.Label("한도 확인 중",9);
        readonly Settings settings;
        readonly Action save;
        readonly Rectangle defaultArea;
        readonly System.Windows.Forms.Timer stackingTimer=new System.Windows.Forms.Timer{Interval=1000};
        bool defaultPosition=true;
        Point? dragAnchor;
        public Overlay(Settings settings,Action save,Action showMain,Rectangle? initialArea=null) {
            this.settings=settings; this.save=save; Theme.Form(this); StartPosition=FormStartPosition.Manual; Text="Codex Relay 오버레이"; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true; Opacity=settings.OverlayOpacity; ClientSize=new Size(300,96); BackColor=Theme.Back;
            stackingTimer.Tick+=(s,e)=>OverlayZOrder.Repair(this);
            VisibleChanged+=(s,e)=>{stackingTimer.Enabled=Visible;if(Visible)OverlayZOrder.Repair(this);};
            HandleCreated+=(s,e)=>{if(Visible)BeginInvoke((Action)(()=>OverlayZOrder.Repair(this)));};
            account.SetBounds(14,7,270,20); tps.SetBounds(12,27,274,35); usage.SetBounds(14,64,274,22); Controls.AddRange(new Control[]{account,tps,usage});
            tps.AccessibleDescription="실행 중인 로컬 Codex 세션들의 평균 TPS";
            foreach(Control c in Controls) { c.MouseDown+=BeginDrag;c.MouseMove+=Drag;c.MouseUp+=(s,e)=>dragAnchor=null;c.DoubleClick+=(s,e)=>{dragAnchor=null;showMain();}; }
            MouseDown+=BeginDrag;MouseMove+=Drag;MouseUp+=(s,e)=>dragAnchor=null;DoubleClick+=(s,e)=>{dragAnchor=null;showMain();};
            defaultArea=initialArea??Screen.PrimaryScreen.WorkingArea; PlaceDefault();
            var saved=new Rectangle(settings.OverlayX,settings.OverlayY,Width,Height);
            if(settings.OverlayX!=-1 && Screen.AllScreens.Any(s=>s.WorkingArea.Contains(saved))){Location=saved.Location;defaultPosition=false;}
            SizeChanged+=(s,e)=>{if(defaultPosition)PlaceDefault();};
        }
        void PlaceDefault(){Location=new Point(Math.Max(defaultArea.Left,defaultArea.Right-Width-24),Math.Max(defaultArea.Top,defaultArea.Bottom-Height-24));}
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void Dispose(bool disposing){if(disposing)stackingTimer.Dispose();base.Dispose(disposing);}
        protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x08000000|0x80; return p; } }
        void BeginDrag(object sender,MouseEventArgs e){dragAnchor=e.Button==MouseButtons.Left&&e.Clicks==1?(Point?)((Control)sender).PointToScreen(e.Location):null;}
        void Drag(object sender,MouseEventArgs e) { if(e.Button!=MouseButtons.Left||!dragAnchor.HasValue)return;var point=((Control)sender).PointToScreen(e.Location);var threshold=SystemInformation.DragSize;if(Math.Abs(point.X-dragAnchor.Value.X)<threshold.Width/2&&Math.Abs(point.Y-dragAnchor.Value.Y)<threshold.Height/2)return;dragAnchor=null;defaultPosition=false;ReleaseCapture(); SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero); settings.OverlayX=Left; settings.OverlayY=Top; save(); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using(var p=new Pen(Theme.Line)) e.Graphics.DrawRectangle(p,0,0,ClientSize.Width-1,ClientSize.Height-1); }
        public void UpdateData(Account a,string value,bool running) {
            account.Text=a==null?"계정 연결 대기":a.Name; tps.Text=value+" TPS";
            var u=a==null?null:a.Usage; bool fresh=u!=null && u.Fresh(Clock.Now);
            var limits=new List<string>();
            if(u!=null&&u.FiveHour!=null)limits.Add(QuotaLabels.FiveHour+"   "+QuotaLabels.Remaining(u.FiveHour));
            if(u!=null&&u.Weekly!=null)limits.Add(QuotaLabels.Weekly+"   "+QuotaLabels.Remaining(u.Weekly));
            string stale=!fresh&&limits.Count>0?" · 이전 값":"";
            usage.Text=limits.Count>0?string.Join(Environment.NewLine,limits.Select(line=>line+stale)):u==null?"한도 확인 중":!string.IsNullOrEmpty(u.Error)?"한도 조회 실패":"제공된 한도 없음";
            usage.ForeColor=fresh?Theme.Text:Theme.Muted;tps.ForeColor=running?Theme.Accent:Theme.Text;
            using(var g=CreateGraphics()){float scale=g.DpiX/96f;int rows=Math.Max(1,limits.Count);usage.SetBounds((int)(14*scale),(int)(64*scale),(int)(274*scale),(int)(22*rows*scale));ClientSize=new Size((int)(300*scale),(int)((74+22*rows)*scale));}
        }
    }
    internal sealed class MainForm : Form {
        readonly Engine engine;
        readonly bool demo;
        readonly Telemetry telemetry;
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly System.Windows.Forms.Timer tick=new System.Windows.Forms.Timer { Interval=1000 };
        readonly System.Windows.Forms.Timer appearanceSave=new System.Windows.Forms.Timer { Interval=300 };
        readonly TransparencySlider transparency=new TransparencySlider { Height=26,AccessibleName="오버레이 투명도" };
        readonly Label transparencyLabel=Theme.Label("",8,Theme.Muted);
        readonly DataGridView grid=new SmoothGrid();
        readonly Panel quotaHeader=new Panel {Dock=DockStyle.Fill,Margin=new Padding(3,0,3,0)};
        readonly Label active=Theme.Label("계정 연결 대기",12), activeState=Theme.Label("",8.5f,Theme.Accent), tps=Theme.Label("—",20), status=Theme.Label("준비됨",8.5f,Theme.Muted), metricNote=Theme.Label("실행 중인 세션 없음",8,Theme.Muted), countLabel=Theme.Label("계정 0개",8.5f,Theme.Muted);
        readonly Label lastChecked=Theme.Label("최근 확인 — ·",8.5f,Theme.Muted);
        readonly QuotaGauge five=new QuotaGauge{Caption=QuotaLabels.FiveHour}, week=new QuotaGauge{Caption=QuotaLabels.Weekly};
        readonly TableLayoutPanel metrics;
        readonly CheckBox warm=new ToggleSwitch(), rotate=new ToggleSwitch(), overlayToggle=new ToggleSwitch(), darkToggle=new ToggleSwitch();
        readonly Font rowNameFont=new Font("맑은 고딕",9.5f), rowSmallFont=new Font("맑은 고딕",8.5f);
        readonly NotifyIcon tray=new NotifyIcon();
        readonly ContextMenuStrip appMenu,accountMenu,trayMenu;
        readonly StartupRegistration startup;
        readonly ToolStripMenuItem startupItem;
        Account menuAccount;
        readonly ToolTip tips=new ToolTip();
        Overlay overlay;
        bool busy,closing,rendering,telemetryBusy,exitPending,appearanceDirty,initialized,switchNotice;
        long lastPoll,lastRotationPrompt;
        string deferredKey="";
        public MainForm(Engine engine,bool demoMode,bool showTray=true,StartupRegistration startupRegistration=null) {
            this.engine=engine;demo=demoMode;startup=startupRegistration??new StartupRegistration(new WindowsStartupStore(),Application.ExecutablePath);telemetry=new Telemetry(engine.CodexHome);Theme.SetDark(engine.Vault.Data.Settings.DarkMode);Theme.Form(this);Text="Codex Relay"+(demo?" · 미리보기":"");ClientSize=new Size(960,620);MinimumSize=new Size(880,620);Height=MinimumSize.Height;
            var root=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(24,18,24,14),ColumnCount=1,RowCount=4};root.RowStyles.Add(new RowStyle(SizeType.Absolute,58));root.RowStyles.Add(new RowStyle(SizeType.Absolute,126));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,40));Controls.Add(root);
            var head=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=5,Margin=Padding.Empty};head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,76));head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,76));head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,42));head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,126));
            var title=Theme.Label("Codex Relay",17);title.Dock=DockStyle.Fill;head.Controls.Add(title,0,0);var darkLabel=Theme.Label("다크 모드",8.5f,Theme.Muted);darkLabel.Dock=DockStyle.Fill;head.Controls.Add(darkLabel,1,0);darkToggle.Text="";darkToggle.Tag="다크 모드";darkToggle.AccessibleName="다크 모드";darkToggle.Anchor=AnchorStyles.Left;darkToggle.Checked=engine.Vault.Data.Settings.DarkMode;head.Controls.Add(darkToggle,2,0);
            var more=Theme.Button("⋯",32);more.Anchor=AnchorStyles.Left;more.AccessibleName="앱 메뉴";head.Controls.Add(more,3,0);appMenu=Theme.Menu();appMenu.ShowCheckMargin=true;appMenu.Items.Add("현재 Codex 계정 저장",null,async(s,e)=>await SaveCurrent());startupItem=new ToolStripMenuItem("Windows 시작 시 실행"){CheckOnClick=false};startupItem.Click+=(s,e)=>ToggleStartup();appMenu.Items.Add(startupItem);appMenu.Opening+=(s,e)=>RefreshStartupState();appMenu.Items.Add(new ToolStripSeparator());appMenu.Items.Add("종료",null,async(s,e)=>await ExitSafely());more.Click+=(s,e)=>appMenu.Show(more,new Point(0,more.Height));
            appMenu.Items.Insert(appMenu.Items.Count-2,new ToolStripMenuItem("전환 로그 열기",null,(s,e)=>{try{string folder=Path.Combine(engine.Vault.DirectoryPath,"logs");Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception ex){Report(ex);}}));
            accountMenu=Theme.Menu();accountMenu.Items.Add("계정 전환",null,async(s,e)=>{var target=MenuAccount();if(target!=null)await SwitchSelected(target,false);});accountMenu.Items.Add("다시 로그인",null,async(s,e)=>{var target=MenuAccount();if(target!=null)await Reconnect(target);});accountMenu.Items.Add("이름 변경",null,(s,e)=>{var target=MenuAccount();if(target!=null)Rename(target);});accountMenu.Items.Add(new ToolStripSeparator());accountMenu.Items.Add("목록에서 제거",null,(s,e)=>{var target=MenuAccount();if(target!=null)Remove(target);});
            var add=Theme.Button("+ 계정 추가",118);add.Tag="primary";add.Anchor=AnchorStyles.Right;head.Controls.Add(add,4,0);add.Click+=async(s,e)=>{if(busy)return;if(demo){status.Text="미리보기에서는 로그인을 실행하지 않습니다.";return;}busy=true;try{using(var d=new LoginDialog(engine))d.ShowDialog(this);}finally{busy=false;}await RefreshUsage(true);};root.Controls.Add(head,0,0);
            metrics=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,Padding=new Padding(0,12,0,13),Margin=new Padding(0,0,0,16)};metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,27));metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,27));metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,27));metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,19));
            var identity=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(0,12,12,0)};identity.RowStyles.Add(new RowStyle(SizeType.Absolute,34));identity.RowStyles.Add(new RowStyle(SizeType.Absolute,26));identity.RowStyles.Add(new RowStyle(SizeType.Percent,100));active.Dock=activeState.Dock=DockStyle.Fill;identity.Controls.Add(active,0,0);identity.Controls.Add(activeState,0,1);metrics.Controls.Add(identity,0,0);five.Dock=week.Dock=DockStyle.Fill;metrics.Controls.Add(five,1,0);metrics.Controls.Add(week,2,0);
            var speedPanel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12,0,0,0)};speedPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,22));speedPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,42));speedPanel.RowStyles.Add(new RowStyle(SizeType.Percent,100));var speedCaption=Theme.Label("세션 평균 TPS",8.5f,Theme.Muted);speedCaption.Dock=DockStyle.Fill;tps.Dock=DockStyle.Fill;metricNote.Dock=DockStyle.Fill;speedPanel.Controls.Add(speedCaption,0,0);speedPanel.Controls.Add(tps,0,1);speedPanel.Controls.Add(metricNote,0,2);metrics.Controls.Add(speedPanel,3,0);root.Controls.Add(metrics,0,1);
            var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,248));root.Controls.Add(body,0,2);
            var listPanel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=new Padding(0,0,24,0)};listPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,34));listPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,30));listPanel.RowStyles.Add(new RowStyle(SizeType.Percent,100));listPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,40));body.Controls.Add(listPanel,0,0);
            var listHead=new Panel{Dock=DockStyle.Fill};var orderLabel=Theme.Label("계정 순서",10);orderLabel.SetBounds(0,0,200,26);listHead.Controls.Add(orderLabel);var hint=Theme.Label("위에서 아래로 자동 전환",8,Theme.Muted);hint.Dock=DockStyle.Right;hint.Width=145;hint.TextAlign=ContentAlignment.MiddleRight;listHead.Controls.Add(hint);listPanel.Controls.Add(listHead,0,0);SetupGrid();quotaHeader.Paint+=PaintQuotaHeader;listPanel.Controls.Add(quotaHeader,0,1);listPanel.Controls.Add(grid,0,2);
            var listFoot=new Panel{Dock=DockStyle.Fill,Padding=new Padding(0,6,0,0)};countLabel.Dock=DockStyle.Fill;listFoot.Controls.Add(countLabel);var switcher=Theme.Button("계정 전환",118);switcher.Dock=DockStyle.Right;switcher.Click+=async(s,e)=>await SwitchSelected(Selected(),false);listFoot.Controls.Add(switcher);listPanel.Controls.Add(listFoot,0,3);
            var options=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Tag="surface",Padding=new Padding(16,18,16,10),Margin=Padding.Empty};options.RowStyles.Add(new RowStyle(SizeType.Absolute,86));options.RowStyles.Add(new RowStyle(SizeType.Absolute,86));options.RowStyles.Add(new RowStyle(SizeType.Absolute,116));options.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.Controls.Add(options,1,0);
            Option(options,0,rotate,"자동 계정 전환","한도 소진 시 다음 계정으로 전환",engine.Vault.Data.Settings.AutoRotate);Option(options,1,warm,"자동 웜업","한도 구간마다 요청 1회",engine.Vault.Data.Settings.AutoWarm);
            var overlayOption=Option(options,2,overlayToggle,"오버레이","남은 한도 · TPS",engine.Vault.Data.Settings.Overlay);
            overlayOption.RowCount=4;overlayOption.RowStyles[1]=new RowStyle(SizeType.Absolute,32);overlayOption.RowStyles.Add(new RowStyle(SizeType.Absolute,22));overlayOption.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            transparencyLabel.Dock=DockStyle.Fill;transparencyLabel.Margin=Padding.Empty;overlayOption.Controls.Add(transparencyLabel,0,2);overlayOption.SetColumnSpan(transparencyLabel,2);
            transparency.Dock=DockStyle.Fill;transparency.Margin=Padding.Empty;transparency.Value=engine.Vault.Data.Settings.OverlayTransparency;overlayOption.Controls.Add(transparency,0,3);overlayOption.SetColumnSpan(transparency,2);UpdateTransparencyLabel();
            transparency.ValueChanged+=(s,e)=>{engine.Vault.Data.Settings.OverlayTransparency=transparency.Value;UpdateTransparencyLabel();if(overlay!=null&&!overlay.IsDisposed)overlay.Opacity=engine.Vault.Data.Settings.OverlayOpacity;appearanceDirty=true;appearanceSave.Stop();appearanceSave.Start();};
            appearanceSave.Tick+=(s,e)=>SaveAppearance();transparency.MouseUp+=(s,e)=>SaveAppearance();transparency.KeyUp+=(s,e)=>SaveAppearance();
            warm.CheckedChanged+=(s,e)=>ChangeOptions();rotate.CheckedChanged+=(s,e)=>{deferredKey="";ChangeOptions();};overlayToggle.CheckedChanged+=(s,e)=>ChangeOptions();darkToggle.CheckedChanged+=(s,e)=>{engine.Vault.Data.Settings.DarkMode=darkToggle.Checked;Theme.SetDark(darkToggle.Checked);ApplyTheme();try{engine.Vault.Save();}catch(Exception ex){Report(ex);}};
            var footer=new Panel{Dock=DockStyle.Fill,Padding=new Padding(0,10,0,0)};status.Dock=DockStyle.Fill;footer.Controls.Add(status);var refresh=Theme.Button("↻ 새로고침",104);refresh.Dock=DockStyle.Right;refresh.FlatAppearance.BorderSize=0;refresh.Click+=async(s,e)=>await RefreshUsage(true);footer.Controls.Add(refresh);lastChecked.Dock=DockStyle.Left;lastChecked.Width=lastChecked.PreferredWidth+3;lastChecked.AccessibleName="최근 확인 시각";footer.Controls.Add(lastChecked);var version=Theme.Label("v"+typeof(MainForm).Assembly.GetName().Version.ToString(3)+" ·",8.5f,Theme.Muted);version.Dock=DockStyle.Left;version.Width=version.PreferredWidth;version.AccessibleName="앱 버전";footer.Controls.Add(version);root.Controls.Add(footer,0,3);
            tips.SetToolTip(warm,"켜면 현재 한도 구간에서 한 번, 이후 한도가 갱신될 때 한 번 요청합니다. 웜업은 소량의 사용량을 소비합니다.");
            tips.SetToolTip(tps,"실행 중인 로컬 세션별 턴 평균 TPS를 단순 평균합니다. 앱 종료·재시작으로 중단된 세션은 별도로 표시하고 TPS에서 제외합니다. 현재 앱 실행 전후의 최근 로그를 기준으로 합니다. 도구 실행·대기 시간을 포함하고 원격·클라우드 채팅은 제외합니다.");
            tips.SetToolTip(metricNote,"중단: 최근에 읽은 세션 중 앱 종료·재시작 전에 진행 중이던 작업입니다. 다시 시작하면 실행 중으로 집계합니다.");
            trayMenu=Theme.Menu(); trayMenu.Items.Add("관리 창 열기",null,(s,e)=>ShowMain()); trayMenu.Items.Add("오버레이 켜기/끄기",null,(s,e)=>overlayToggle.Checked=!overlayToggle.Checked); trayMenu.Items.Add(new ToolStripSeparator()); trayMenu.Items.Add("종료",null,async(s,e)=>await ExitSafely());
            tray.Icon=Icon; tray.Text="Codex Relay"; tray.ContextMenuStrip=trayMenu; tray.Visible=showTray; tray.DoubleClick+=(s,e)=>ShowMain();
            FormClosing+=(s,e)=>{ SaveAppearance();if(!closing && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); tray.ShowBalloonTip(2000,"Codex Relay","트레이에서 계속 실행합니다. 완전히 닫으려면 트레이 메뉴의 종료를 선택하세요.",ToolTipIcon.Info); } else { lifetime.Cancel(); tick.Stop();appearanceSave.Stop(); tray.Visible=false; if(overlay!=null)overlay.Close(); telemetry.Dispose(); } };
            Shown+=async(s,e)=>await InitializeAsync();
            tick.Tick+=async(s,e)=>{ await UpdateMetrics(); if(!busy && Clock.Now-lastPoll>=1) await RefreshUsage(false); };
        }
        public async Task InitializeAsync(){
            if(initialized)return;initialized=true;string startupError=null;bool importCurrent=!File.Exists(engine.Vault.FilePath);
            // Keep startup repair independent from network requests and app initialization.
            if(!demo)try{startup.Reconcile(engine.Vault.Data.Settings,()=>engine.Vault.Save());}catch(Exception ex){startupError="시작프로그램 설정 반영 실패: "+Engine.Friendly(ex);}
            try{ApplyTheme();engine.SyncLive(importCurrent);if(!demo)await Task.Run(()=>telemetry.Start());Render();UpdateOverlay();tick.Start();await RefreshUsage(true);}catch(Exception ex){Report(ex);}
            finally{RefreshStartupState();if(startupError!=null)status.Text=startupError;}
        }
        void RefreshStartupState(){try{startupItem.Checked=startup.IsEnabled;startupItem.Enabled=true;}catch{startupItem.Checked=false;startupItem.Enabled=false;status.Text="시작프로그램 등록 상태를 읽지 못했습니다.";}}
        void ToggleStartup(){if(demo){status.Text="미리보기에서는 시작프로그램을 변경하지 않습니다.";return;}try{bool enable=!startup.IsEnabled;startup.SetPreference(engine.Vault.Data.Settings,enable,()=>engine.Vault.Save());status.Text=enable?"Windows 시작 시 실행하도록 등록했습니다.":"시작프로그램 등록을 해제했습니다.";}catch(Exception ex){Report(ex);}finally{RefreshStartupState();}}
        void SetupGrid() {
            grid.Dock=DockStyle.Fill;grid.Margin=new Padding(3,0,3,3);grid.BackgroundColor=Theme.Back;grid.BorderStyle=BorderStyle.None;grid.AutoGenerateColumns=false;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.AllowUserToResizeRows=false;grid.AllowUserToResizeColumns=false;grid.ReadOnly=true;grid.MultiSelect=false;grid.RowHeadersVisible=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersVisible=false;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;grid.ColumnHeadersHeight=30;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;grid.ShowCellToolTips=false;
            grid.DefaultCellStyle=new DataGridViewCellStyle{BackColor=Theme.Back,ForeColor=Theme.Text,SelectionBackColor=Theme.Panel,SelectionForeColor=Theme.Text,Padding=Padding.Empty};grid.GridColor=Theme.Line;grid.RowTemplate.Height=78;grid.CellBorderStyle=DataGridViewCellBorderStyle.None;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
            string[] names={"순서","계정",QuotaLabels.FiveHour,QuotaLabels.Weekly,"위로","아래로","계정 메뉴"};for(int i=0;i<names.Length;i++){var c=new DataGridViewTextBoxColumn{HeaderText=i>=1&&i<=3?names[i]:"",Name=names[i],SortMode=DataGridViewColumnSortMode.NotSortable};if(i==1)c.FillWeight=100;else{c.AutoSizeMode=DataGridViewAutoSizeColumnMode.None;c.Width=i==0?28:i==2||i==3?132:28;}grid.Columns.Add(c);}
            grid.CellPainting+=PaintCell;
            grid.ColumnWidthChanged+=(s,e)=>quotaHeader.Invalidate();grid.ColumnStateChanged+=(s,e)=>quotaHeader.Invalidate();
            grid.CellClick+=(s,e)=>{if(e.RowIndex<0||busy)return;grid.Rows[e.RowIndex].Selected=true;try{if(e.ColumnIndex==4)MoveAccount(-1);else if(e.ColumnIndex==5)MoveAccount(1);else if(e.ColumnIndex==6)ShowAccountMenu(grid.PointToClient(Cursor.Position));}catch(Exception ex){Report(ex);}};
            grid.KeyDown+=(s,e)=>{if(busy)return;if(e.Alt&&(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down)){MoveAccount(e.KeyCode==Keys.Up?-1:1);e.Handled=true;}if(e.KeyCode==Keys.Apps){ShowAccountMenu(new Point(30,40));e.Handled=true;}};
            grid.CellMouseClick+=(s,e)=>{if(e.RowIndex>=0&&e.Button==MouseButtons.Right){grid.Rows[e.RowIndex].Selected=true;ShowAccountMenu(grid.PointToClient(Cursor.Position));}};
        }
        string AccountState(Account a){var u=a.Usage;return a.NeedsLogin?"재로그인 필요":u!=null&&!string.IsNullOrEmpty(u.Error)?"조회 실패":u!=null&&!u.Fresh(Clock.Now)?"갱신 필요":a.Id==engine.ActiveId?"사용 중":u!=null&&u.Exhausted?"한도 소진":"대기";}
        void PaintQuotaHeader(object sender,PaintEventArgs e){var g=e.Graphics;float scale=g.DpiX/96f;g.Clear(Theme.Back);int x=0;foreach(DataGridViewColumn column in grid.Columns){if(!column.Visible)continue;var r=new Rectangle(x+(int)(7*scale),0,Math.Max(0,column.Width-(int)(14*scale)),quotaHeader.Height);TextRenderer.DrawText(g,column.HeaderText,rowSmallFont,r,Theme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);x+=column.Width;}using(var p=new Pen(Theme.Line))g.DrawLine(p,0,quotaHeader.Height-1,quotaHeader.Width,quotaHeader.Height-1);}
        void PaintCell(object sender,DataGridViewCellPaintingEventArgs e){
            if(e.RowIndex<0)return;var g=e.Graphics;float scale=g.DpiX/96f;var r=e.CellBounds;
            var a=grid.Rows[e.RowIndex].Tag as Account;if(a==null)return;bool selected=(e.State&DataGridViewElementStates.Selected)!=0;using(var b=new SolidBrush(selected?Theme.Panel:Theme.Back))g.FillRectangle(b,r);using(var p=new Pen(Theme.Line))g.DrawLine(p,r.Left,r.Bottom-1,r.Right,r.Bottom-1);
            int pad=(int)(7*scale);var flags=TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding;
            if(e.ColumnIndex==0){TextRenderer.DrawText(g,(e.RowIndex+1).ToString(),rowSmallFont,r,a.Id==engine.ActiveId?Theme.Accent:Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);if(a.Id==engine.ActiveId)using(var b=new SolidBrush(Theme.Accent))g.FillRectangle(b,r.Left,r.Top+(int)(18*scale),Math.Max(2,(int)(3*scale)),r.Height-(int)(36*scale));}
            else if(e.ColumnIndex==1){TextRenderer.DrawText(g,a.Name,rowNameFont,new Rectangle(r.Left+pad,r.Top+(int)(8*scale),r.Width-pad*2,(int)(25*scale)),a.Id==engine.ActiveId?Theme.Accent:Theme.Text,flags);TextRenderer.DrawText(g,(a.Plan.Length>0?a.Plan+" · ":"")+AccountState(a),rowSmallFont,new Rectangle(r.Left+pad,r.Top+(int)(34*scale),r.Width-pad*2,(int)(22*scale)),Theme.Muted,flags);}
            else if(e.ColumnIndex==2||e.ColumnIndex==3){var u=a.Usage;var w=u==null?null:e.ColumnIndex==2?u.FiveHour:u.Weekly;bool absent=w==null&&u!=null&&(u.FiveHour!=null||u.Weekly!=null);if(!absent){int x=r.Left+pad,width=Math.Max(1,r.Width-pad*2);TextRenderer.DrawText(g,QuotaLabels.Remaining(w),rowNameFont,new Rectangle(x,r.Top+(int)(8*scale),width,(int)(25*scale)),u!=null&&u.Fresh(Clock.Now)?Theme.Text:Theme.Muted,flags);if(w!=null){int y=r.Top+(int)(39*scale);using(var b=new SolidBrush(Theme.Line))g.FillRectangle(b,x,y,width,Math.Max(2,(int)(3*scale)));using(var b=new SolidBrush(!u.Fresh(Clock.Now)?Theme.Muted:w.Remaining<30?Theme.Warning:Theme.Accent))g.FillRectangle(b,x,y,(float)(width*w.Remaining/100),Math.Max(2,(int)(3*scale)));TextRenderer.DrawText(g,QuotaLabels.ResetAt(w),rowSmallFont,new Rectangle(x,r.Top+(int)(48*scale),width,(int)(20*scale)),Theme.Muted,flags);}}}
            else TextRenderer.DrawText(g,e.ColumnIndex==4?"↑":e.ColumnIndex==5?"↓":"⋯",rowNameFont,r,Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            e.Handled=true;
        }
        Account MenuAccount(){return !busy&&menuAccount!=null&&engine.Vault.Data.Accounts.Contains(menuAccount)?menuAccount:null;}
        void ShowAccountMenu(Point location){var a=Selected();if(a==null||busy)return;menuAccount=a;Theme.ApplyMenu(accountMenu);accountMenu.Show(grid,location);}
        async Task SaveCurrent(){if(busy)return;try{engine.SyncLive(true);Render();await RefreshUsage(true);}catch(Exception ex){Report(ex);}}
        void ApplyTheme(){grid.BackgroundColor=Theme.Back;grid.DefaultCellStyle.BackColor=Theme.Back;grid.DefaultCellStyle.ForeColor=Theme.Text;grid.DefaultCellStyle.SelectionBackColor=Theme.Panel;grid.DefaultCellStyle.SelectionForeColor=Theme.Text;grid.GridColor=Theme.Line;Theme.Apply(this);Theme.ApplyMenu(appMenu);Theme.ApplyMenu(accountMenu);Theme.ApplyMenu(trayMenu);if(overlay!=null&&!overlay.IsDisposed){Theme.Apply(overlay);overlay.Opacity=engine.Vault.Data.Settings.OverlayOpacity;}}
        void UpdateTransparencyLabel(){transparencyLabel.Text="투명도 "+transparency.Value+"%";transparency.AccessibleDescription="0%는 불투명, 90%는 가장 투명하게 표시. 좌우 방향키로 5%씩 조절";}
        void SaveAppearance(){appearanceSave.Stop();if(!appearanceDirty)return;try{engine.Vault.Save();appearanceDirty=false;}catch(Exception ex){Report(ex);}}
        TableLayoutPanel Option(TableLayoutPanel parent,int row,CheckBox box,string title,string description,bool value) {
            var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Tag="surface",Margin=Padding.Empty};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,74));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,32));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));var label=Theme.Label(title,9.5f);label.Dock=DockStyle.Fill;label.Margin=Padding.Empty;panel.Controls.Add(label,0,0);box.Text="";box.Tag=title;box.AccessibleName=title;box.Checked=value;box.Anchor=AnchorStyles.Right;box.Margin=Padding.Empty;panel.Controls.Add(box,1,0);var note=Theme.Label(description,8.5f,Theme.Muted);note.Dock=DockStyle.Fill;note.TextAlign=ContentAlignment.TopLeft;note.Margin=new Padding(0,6,0,0);panel.Controls.Add(note,0,1);panel.SetColumnSpan(note,2);parent.Controls.Add(panel,0,row);return panel;
        }
        void ChangeOptions() {
            if(rendering)return; engine.Vault.Data.Settings.AutoWarm=warm.Checked; engine.Vault.Data.Settings.AutoRotate=rotate.Checked; engine.Vault.Data.Settings.Overlay=overlayToggle.Checked;
            foreach(var b in new[]{warm,rotate,overlayToggle})b.Invalidate();
            try { engine.Vault.Save(); } catch(Exception ex) { Report(ex); }
            UpdateOverlay(); lastPoll=0;
        }
        void UpdateOverlay() { if(overlayToggle.Checked) { if(overlay==null || overlay.IsDisposed) overlay=new Overlay(engine.Vault.Data.Settings,()=>{ try{engine.Vault.Save();}catch(Exception ex){Report(ex);} },ShowMain,Screen.FromControl(this).WorkingArea);Theme.Apply(overlay);overlay.UpdateData(engine.Active,tps.Text,false);overlay.Show(); } else if(overlay!=null) overlay.Hide(); }
        public void ShowMain() { Show(); WindowState=FormWindowState.Normal; Activate(); }
        async Task ExitSafely() {
            // Let an in-flight token refresh finish and persist a possibly rotated token.
            exitPending=true;tick.Stop();
            if(busy) { status.Text="진행 중인 인증·사용량 요청을 마친 후 종료합니다…"; ShowMain(); while(busy)await Task.Delay(100); }
            closing=true;Close();
        }
        void AddAction(FlowLayoutPanel p,string text,int width,Action action) { var b=Theme.Button(text,width); p.Controls.Add(b); b.Click+=(s,e)=>{ if(!busy) { try{action();}catch(Exception ex){Report(ex);} } }; }
        Account Selected() { return grid.SelectedRows.Count==0 ? null : grid.SelectedRows[0].Tag as Account; }
        void MoveAccount(int direction) { var a=Selected(); if(a==null)return; var list=engine.Vault.Data.Accounts; int i=list.IndexOf(a),n=i+direction; if(n<0||n>=list.Count)return; list.RemoveAt(i); list.Insert(n,a); engine.Vault.Save(); Render(a.Id); }
        void Rename(Account a) { if(a==null||busy)return; using(var f=new Form()) { Theme.Form(f); f.Text="계정 이름 변경"; f.ClientSize=new Size(360,115); f.FormBorderStyle=FormBorderStyle.FixedDialog; f.MaximizeBox=false; f.MinimizeBox=false; var t=new TextBox { Text=a.Name,Left=18,Top=18,Width=322,MaxLength=60 }; var b=Theme.Button("저장"); b.Left=230;b.Top=64;b.DialogResult=DialogResult.OK;f.Controls.AddRange(new Control[]{t,b});f.AcceptButton=b;if(f.ShowDialog(this)==DialogResult.OK&&!string.IsNullOrWhiteSpace(t.Text)){a.Name=t.Text.Trim();engine.Vault.Save();Render(a.Id);} } }
        void Remove(Account a) { if(a==null||busy)return; if(MessageBox.Show(this,a.Name+" 계정을 목록에서 제거할까요?\n현재 Codex 로그인은 유지됩니다.","계정 제거",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return; engine.Vault.Data.Accounts.Remove(a);engine.Vault.Save();Render(); }
        void Report(Exception ex) { status.Text=Engine.Friendly(ex); }
        void UpdateLastChecked(DateTime time){lastChecked.Text="최근 확인 "+time.ToString("yyyy-MM-dd HH:mm",System.Globalization.CultureInfo.InvariantCulture)+" ·";lastChecked.Width=lastChecked.PreferredWidth+3;}
        async Task Reconnect(Account account) {
            if(busy||demo||exitPending)return;busy=true;
            try{using(var dialog=new LoginDialog(engine,account))dialog.ShowDialog(this);}finally{busy=false;}
            await RefreshUsage(true);
        }
        void Render(string selectedId=null) {
            selectedId=selectedId??(Selected()==null?"":Selected().Id); rendering=true;
            using(var g=CreateGraphics()){float scale=g.DpiX/96f;grid.RowTemplate.Height=(int)Math.Round(78*scale);grid.ColumnHeadersHeight=(int)Math.Round(30*scale);for(int c=0;c<grid.Columns.Count;c++)if(c!=1)grid.Columns[c].Width=(int)Math.Round((c==0?28:c==2||c==3?132:28)*scale);}
            grid.Columns[2].Visible=engine.Vault.Data.Accounts.Count==0||engine.Vault.Data.Accounts.Any(a=>QuotaLabels.ShowFiveHour(a.Usage));grid.Columns[3].Visible=engine.Vault.Data.Accounts.Count==0||engine.Vault.Data.Accounts.Any(a=>QuotaLabels.ShowWeekly(a.Usage));
            quotaHeader.AccessibleName=string.Join(", ",grid.Columns.Cast<DataGridViewColumn>().Where(c=>c.Visible&&!string.IsNullOrEmpty(c.HeaderText)).Select(c=>c.HeaderText));quotaHeader.Invalidate();
            grid.SuspendLayout(); grid.Rows.Clear(); int i=0;
            foreach(var a in engine.Vault.Data.Accounts) {
                var u=a.Usage;string state=AccountState(a);
                int r=grid.Rows.Add(++i,a.Name+" · "+a.Plan+" · "+state,QuotaLabels.ShowFiveHour(u)?QuotaLabels.Remaining(u==null?null:u.FiveHour):"",QuotaLabels.ShowWeekly(u)?QuotaLabels.Remaining(u==null?null:u.Weekly):"","↑","↓","⋯");grid.Rows[r].Tag=a;
                if(a.Id==selectedId)grid.Rows[r].Selected=true;
            }
            grid.ResumeLayout(); rendering=false;
            countLabel.Text=engine.Vault.Data.Accounts.Count==0?"등록된 계정이 없습니다":"계정 "+engine.Vault.Data.Accounts.Count+"개";
            var current=engine.Active;active.Text=current==null?"계정 연결 대기":current.Name;activeState.Text=current==null?"계정을 추가하세요":current.Plan+" · "+AccountState(current);
            var usage=current==null?null:current.Usage;five.Value=usage==null?null:usage.FiveHour;week.Value=usage==null?null:usage.Weekly;five.Fresh=week.Fresh=usage!=null&&usage.Fresh(Clock.Now);
            bool showFive=QuotaLabels.ShowFiveHour(usage),showWeek=QuotaLabels.ShowWeekly(usage);metrics.SuspendLayout();five.Visible=showFive;week.Visible=showWeek;metrics.ColumnStyles[0].Width=showFive&&showWeek?27:34;metrics.ColumnStyles[1].Width=showFive?(showWeek?27:40):0;metrics.ColumnStyles[2].Width=showWeek?(showFive?27:40):0;metrics.ColumnStyles[3].Width=showFive&&showWeek?19:26;metrics.ResumeLayout(true);
        }
        async Task UpdateMetrics() {
            if(telemetryBusy)return;telemetryBusy=true;
            try {
                var summary=demo?new TpsSummary {ActiveCount=2,MeasuredCount=2,Tps=38.4}:await Task.Run(()=>telemetry.Read());
                if(IsDisposed)return;DisplayMetrics(summary,demo);
            } catch(Exception ex) { Report(ex); } finally { telemetryBusy=false; }
        }
        void DisplayMetrics(TpsSummary summary,bool preview){
            bool measured=summary.PendingFiles==0&&summary.Tps.HasValue;string value=measured?summary.Tps.Value.ToString("0.0"):"—";
            string note=summary.PendingFiles>0?"세션 집계 중":summary.ActiveCount==0?(summary.StaleCount>0?"기록 갱신 대기":"실행 중인 세션 없음"):summary.MeasuredCount==0?"실행 "+summary.ActiveCount+"개 · 측정 대기":summary.MeasuredCount==summary.ActiveCount?"실행 중 "+summary.ActiveCount+"개":"집계 "+summary.MeasuredCount+" / 실행 "+summary.ActiveCount+"개";
            if(summary.PendingFiles==0&&summary.InterruptedCount>0)note="실행 중 "+summary.ActiveCount+"개 · 중단 "+summary.InterruptedCount+"개";
            if(summary.PendingFiles==0&&summary.AppStateUnknown)note="앱 상태 확인 대기";
            tps.Text=value;metricNote.Text=preview?"미리보기 · "+summary.ActiveCount+"개 세션":note;
            if(overlay!=null&&!overlay.IsDisposed)overlay.UpdateData(engine.Active,value,measured&&summary.Running);
        }
        async Task RefreshUsage(bool force) {
            if(busy||exitPending)return;busy=true;lastPoll=Clock.Now;
            bool changed=false;
            try {
                if(force){switchNotice=false;status.Text=demo?"미리보기 · 실제 계정과 네트워크를 변경하지 않습니다.":"사용량 조회 중…";}
                using(var c=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)) {
                    c.CancelAfter(TimeSpan.FromSeconds(Math.Max(60,engine.Vault.Data.Accounts.Count*50)));
                    changed=await engine.Poll(force,c.Token,()=>{Render();if(overlay!=null&&!overlay.IsDisposed)overlay.UpdateData(engine.Active,tps.Text,false);});
                }
                if(changed&&!demo)UpdateLastChecked(DateTime.Now);
                if(force&&!demo)status.Text="준비됨";
            } catch(Exception ex) { Report(ex); } finally {busy=false;}
            // Offer a ready account before optional warm-up network requests.
            if(!lifetime.IsCancellationRequested) { await UpdateMetrics(); await AutoRotate(); }
            if(changed&&!busy&&!exitPending&&engine.Vault.Data.Settings.AutoWarm) {
                busy=true;
                try{using(var c=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)){c.CancelAfter(TimeSpan.FromMinutes(5));await engine.WarmDue(c.Token);}}
                catch(Exception ex){Report(ex);}finally{busy=false;}
            }
        }
        async Task AutoRotate() {
            if(busy||demo||exitPending)return;var a=engine.Active;
            const string noCandidate="사용 가능한 다음 계정이 없습니다.";
            if(!rotate.Checked||!engine.Vault.Data.Settings.AutoRotate||a==null||a.Usage==null||!a.Usage.Fresh(Clock.Now)||!a.Usage.Exhausted){if(status.Text==noCandidate)status.Text="준비됨";return;}
            string key=a.Id+":"+Rotation.WarmKey(a.Usage); if(key==deferredKey&&Clock.Now-lastRotationPrompt<600)return;
            var next=engine.RotationCandidate(); if(next==null){if(!switchNotice)status.Text=noCandidate;return;}
            deferredKey=key;lastRotationPrompt=Clock.Now;await SwitchSelected(next,true);
        }
        internal bool AcceptSwitchConfirmation(bool accepted,bool automatic) {
            if(!accepted&&automatic) {
                engine.Vault.Data.Settings.AutoRotate=false;
                bool wasRendering=rendering;rendering=true;
                try { rotate.Checked=false; } finally { rendering=wasRendering; }
                // Persist the refusal across quota refreshes and app restarts.
                engine.Vault.Save();
            }
            return accepted;
        }
        async Task SwitchSelected(Account a,bool automatic) {
            if(busy||a==null)return;busy=true;switchNotice=true;
            try {
                bool declined=false;
                Func<bool,bool> confirm=accepted=>{declined|=!accepted;return AcceptSwitchConfirmation(accepted,automatic);};
                status.Text="전환 준비: "+a.Name;
                using(var c=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)) {
                    c.CancelAfter(TimeSpan.FromMinutes(5));
                    status.Text=await engine.Switch(a,items=>{
                        ShowMain();
                        bool accepted=confirm(MessageBox.Show(this,(automatic?"현재 계정의 한도에 도달했습니다.\n\n":"")+a.Name+" 계정으로 전환하려면 Codex를 종료해야 합니다.\n진행 중인 작업이 중단될 수 있습니다.\n\nCodex를 정상 종료하고 계정을 전환한 뒤 다시 열까요?"+(automatic?"\n\n아니요를 선택하면 자동 전환을 끕니다.":""),"Codex 종료 후 계정 전환",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes);
                        if(accepted)status.Text="Codex 정상 종료를 기다리는 중… (최대 "+Engine.GracefulCloseSeconds+"초)";
                        return accepted;
                    },items=>confirm(MessageBox.Show(this,Engine.GracefulCloseSeconds+"초 안에 Codex가 완전히 종료되지 않았습니다.\n앱의 종료 확인창이 열려 있다면 먼저 처리하세요.\n\n남은 Codex 앱/CLI 프로세스 "+items.Count+"개를 강제 종료하고 전환할까요?\n저장하지 않은 작업이 손실될 수 있습니다.\n아니요를 선택하면 현재 인증 정보를 유지하고 전환을 취소합니다."+(automatic?"\n자동 계정 전환도 OFF로 바뀝니다.":""),"강제 종료 확인",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes),c.Token,automatic,message=>{if(!IsDisposed)status.Text=message;});
                }
                if(declined&&automatic)status.Text="자동 계정 전환을 껐습니다. 다시 켤 때까지 전환을 묻지 않습니다.";
                bool wasRendering=rendering;rendering=true;
                try{rotate.Checked=engine.Vault.Data.Settings.AutoRotate;}finally{rendering=wasRendering;}
                Render(a.Id);
            } catch(Exception ex){Report(ex);} finally{busy=false;if(!string.IsNullOrEmpty(engine.LastSwitchLog))tips.SetToolTip(status,"전환 로그: "+engine.LastSwitchLog);}
        }
        protected override void Dispose(bool disposing) { if(disposing){appearanceSave.Dispose();tick.Dispose();tray.Dispose();if(appMenu!=null)appMenu.Dispose();if(accountMenu!=null)accountMenu.Dispose();if(trayMenu!=null)trayMenu.Dispose();tips.Dispose();telemetry.Dispose();rowNameFont.Dispose();rowSmallFont.Dispose();}base.Dispose(disposing); }
    }
}
