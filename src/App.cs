using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ATrustSwitch {
    static class Program {
        [STAThread] static int Main(string[] args) {
            if (args.Length == 2 && args[0] == "--action") return Worker.Run(args[1]);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string capture = args.Length == 2 && args[0] == "--capture" ? args[1] : null;
            Application.Run(new MainForm(capture));
            return 0;
        }
    }
    sealed class MainForm : Form {
        readonly Color Ink = Color.FromArgb(32, 43, 62), Muted = Color.FromArgb(126, 137, 154);
        readonly Label headline, detail, serviceValue, clientValue, tunnelValue, agentValue, modeValue, notice;
        readonly Button start, stop, refresh, settings, logs;
        readonly Timer timer = new Timer();
        readonly Mark statusMark;
        readonly Label relatedSummary,modeWarning;
        RelatedState related;
        DateTime lastRelatedRead=DateTime.MinValue;
        string previousMode;
        bool connectionExited;
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int message,IntPtr w,IntPtr l);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h,int attribute,ref int value,int size);
        bool busy, reading; readonly string capture; readonly float scale;
        public MainForm(string capturePath) {
            capture = capturePath;
            using (Graphics graphics=Graphics.FromHwnd(IntPtr.Zero)) scale=graphics.DpiX/96F;
            AutoScaleMode = AutoScaleMode.None;
            Text = "aTrust 开关"; ClientSize = Sized(464, 634);
            Font = new Font("Microsoft YaHei UI", 9F); BackColor = Color.FromArgb(247,249,252);
            ForeColor = Ink; FormBorderStyle = FormBorderStyle.None; MaximizeBox = false;
            DoubleBuffered=true;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            var brand=new Mark { Brand=true,Factor=scale,Location=Located(22,30),Size=Sized(48,48) }; Controls.Add(brand);
            Label title = LabelAt(this, "aTrust", 82, 26, 240, 32, 19, true);
            Label subtitle=LabelAt(this, "连接管理", 83, 60, 240, 22, 8.5F, false); subtitle.ForeColor = Muted;
            var minimize=(ModernButton)ButtonAt(this,"",386,8,30,26,false); minimize.Kind="quiet"; minimize.Glyph="min";
            minimize.AccessibleName="最小化"; minimize.Click+=(s,e)=>WindowState=FormWindowState.Minimized;
            var close=(ModernButton)ButtonAt(this,"",422,8,30,26,false); close.Kind="close"; close.Glyph="close";
            close.AccessibleName="关闭窗口"; close.Click+=(s,e)=>Close();
            MouseEventHandler drag=(s,e)=> { if(e.Button==MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } };
            MouseDown+=drag; title.MouseDown+=drag; subtitle.MouseDown+=drag; brand.MouseDown+=drag;
            refresh = ButtonAt(this, "刷新", 366,54,72,28,false); ((ModernButton)refresh).Kind="quiet";
            refresh.Click += async (s,e) => await RefreshState();
            var panel = new Surface { Factor=scale,Divider=true,Location = Located(24,100), Size = Sized(416,188) };
            Controls.Add(panel);
            statusMark=new Mark { Factor=scale,Location=Located(20,22),Size=Sized(40,40) }; panel.Controls.Add(statusMark);
            headline = LabelAt(panel,"正在读取状态…",73,24,320,30,15,true);
            detail = LabelAt(panel,"",20,69,376,30,8.5F,false); detail.ForeColor = Muted;
            LabelAt(panel,"服务",20,123,86,22,8,false).ForeColor = Muted;
            serviceValue = LabelAt(panel,"—",20,148,86,25,9.5F,false);
            LabelAt(panel,"客户端",119,123,86,22,8,false).ForeColor = Muted;
            clientValue = LabelAt(panel,"—",119,148,86,25,9.5F,false);
            LabelAt(panel,"代理",218,123,86,22,8,false).ForeColor = Muted;
            agentValue = LabelAt(panel,"—",218,148,86,25,9.5F,false);
            LabelAt(panel,"隧道",317,123,86,22,8,false).ForeColor = Muted;
            tunnelValue = LabelAt(panel,"—",317,148,86,25,9.5F,false);
            var relatedPanel=new Surface { Factor=scale,Location=Located(24,300),Size=Sized(416,92) }; Controls.Add(relatedPanel);
            LabelAt(relatedPanel,"其他相关组件",20,9,250,24,9,true);
            relatedSummary=LabelAt(relatedPanel,"正在读取服务、进程与驱动…",20,36,376,46,8.5F,false); relatedSummary.ForeColor=Muted;
            var detailsButton=(ModernButton)ButtonAt(relatedPanel,"详情",332,8,64,28,false); detailsButton.Kind="quiet";
            detailsButton.Click+=(s,e)=>ShowRelated();
            var preference=new Surface { Factor=scale,Location=Located(24,404),Size=Sized(416,88) }; Controls.Add(preference);
            LabelAt(preference,"服务启动方式",20,10,180,24,9,true);
            LabelAt(preference,"按需运行，关闭自动恢复与登录自启",20,34,295,18,7.5F,false).ForeColor=Muted;
            modeValue = LabelAt(preference,"—",253,18,64,24,9,false); modeValue.ForeColor=Color.FromArgb(67,99,157);
            settings = ButtonAt(preference,"更改",332,14,64,34,false); ((ModernButton)settings).Kind="quiet";
            modeWarning=LabelAt(preference,"",20,60,376,20,7.5F,false); modeWarning.ForeColor=Color.FromArgb(165,100,30);
            var menu = new ContextMenuStrip();
            menu.Items.Add("设为按需使用（关闭自动恢复）",null,async (s,e) => await Act("manual"));
            menu.Items.Add("仅将主服务设为自动启动",null,async (s,e) => await Act("auto"));
            settings.Click += (s,e) => menu.Show(settings,new Point(0,settings.Height));
            start = ButtonAt(this,"启动并打开",24,512,202,46,true); ((ModernButton)start).Glyph="start";
            stop = ButtonAt(this,"彻底退出",238,512,202,46,false); ((ModernButton)stop).Glyph="stop";
            start.Click += async (s,e) => await Act("start");
            stop.Click += async (s,e) => await Act("stop");
            notice = LabelAt(this,"退出时关闭保活，并停止 aTrust 与 EAIO",26,565,412,32,8,false); notice.ForeColor = Muted;
            LabelAt(this,"关闭窗口后，aTrust 保持当前状态",26,606,330,20,7.5F,false).ForeColor = Muted;
            logs = ButtonAt(this,"日志",382,600,58,28,false); ((ModernButton)logs).Kind="quiet";
            logs.Click += (s,e) => {
                try { Directory.CreateDirectory(Log.Folder); if (!File.Exists(Log.FilePath)) Log.Write("打开日志"); Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Log.FilePath + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { Notice("日志无法打开：" + ex.Message, true); }
            };
            timer.Interval = 3000; timer.Tick += async (s,e) => await RefreshState();
            Shown += async (s,e) => {
                await RefreshState();
                if (capture != null) {
                    using (var bitmap = new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height)); bitmap.Save(capture); }
                    Close();
                } else timer.Start();
            };
            FormClosing += (s,e) => { if (busy) { e.Cancel = true; Notice("操作进行中，完成后可关闭窗口。",false); } };
            FormClosed += (s,e) => { timer.Dispose(); menu.Dispose(); };
        }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            // Windows 11 rounded frame; older Windows retains the same functional window.
            try { int round=2; DwmSetWindowAttribute(Handle,33,ref round,4); } catch(DllNotFoundException) { }
        }
        protected override CreateParams CreateParams { get { var p=base.CreateParams; p.Style|=0x20000; p.ClassStyle|=0x20000; return p; } }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var path=Shapes.Round(new RectangleF(.5F,.5F,Width-1,Height-1),10*scale)) using(var pen=new Pen(Color.FromArgb(219,225,236))) e.Graphics.DrawPath(pen,path);
        }
        Point Located(int x,int y) { return new Point((int)Math.Round(x*scale),(int)Math.Round(y*scale)); }
        Size Sized(int w,int h) { return new Size((int)Math.Round(w*scale),(int)Math.Round(h*scale)); }
        Label LabelAt(Control parent,string text,int x,int y,int w,int h,float size,bool bold) {
            var label = new Label { Text=text,Location=Located(x,y),Size=Sized(w,h),BackColor=Color.Transparent,Font=new Font("Microsoft YaHei UI",size,bold ? FontStyle.Bold : FontStyle.Regular),TextAlign=ContentAlignment.MiddleLeft };
            parent.Controls.Add(label); return label;
        }
        Button ButtonAt(Control parent,string text,int x,int y,int w,int h,bool primary) {
            var surface=parent as Surface;
            var button = new ModernButton { Factor=scale,Kind=primary ? "primary" : "normal",Text=text,Location=Located(x,y),Size=Sized(w,h),Font=new Font("Microsoft YaHei UI",9.5F),AccessibleName=text,BackColor=surface==null ? parent.BackColor : surface.Fill };
            parent.Controls.Add(button); return button;
        }
        void Notice(string text,bool error) { notice.Text=text; notice.ForeColor=error ? Color.FromArgb(166,62,47) : Muted; }
        void EnableActions(bool enabled) { start.Enabled=stop.Enabled=settings.Enabled=refresh.Enabled=enabled; }
        void Apply(Snapshot state) {
            connectionExited=state.Exited;
            if(previousMode!=null && previousMode!=state.Mode) {
                Log.Write("检测到启动方式变化 "+previousMode+" -> "+state.Mode);
                Notice("启动方式已变化，当前为"+(state.Mode=="Auto"?"自动":state.Mode=="Manual"?"手动":state.Mode)+"。",true);
                lastRelatedRead=DateTime.MinValue;
            }
            previousMode=state.Mode;
            serviceValue.Text=state.Service=="Running" ? "运行中" : state.Service=="Stopped" ? "已停止" : "切换中";
            clientValue.Text=CountText(state.Count("aTrustTray"),state.UnknownCount("aTrustTray"));
            tunnelValue.Text=CountText(state.Count("aTrustXtunnel"),state.UnknownCount("aTrustXtunnel"));
            agentValue.Text=CountText(state.Count("aTrustAgent"),state.UnknownCount("aTrustAgent"));
            modeValue.Text=state.Mode=="Auto" ? "自动" : state.Mode=="Manual" ? "按需" : state.Mode=="Disabled" ? "已禁用" : state.Mode;
            headline.Text=state.Unknown>0 ? "部分状态待核实" : state.Exited ? "连接组件已退出" : state.Service=="Running" ? "aTrust 正在运行" : "仍有连接进程";
            headline.ForeColor=state.Exited ? Color.FromArgb(38,124,102) : Ink;
            statusMark.Complete=state.Exited;
            statusMark.Accent=state.Exited ? Color.FromArgb(29,133,105) : Color.FromArgb(54,99,224); statusMark.Invalidate();
            detail.Text=state.Unknown>0 ? "部分同名进程路径不可读；退出时将提权核验" : state.Exited ? "服务、客户端、代理和隧道均已停止" : "登录与连接状态请查看客户端";
            if(related!=null) ApplyRelated(related);
        }
        void ApplyRelated(RelatedState value) {
            related=value;
            relatedSummary.Text="EAIO 服务 "+value.EaioService+" · Agent "+value.EaioAgents+" 个\r\n网络过滤驱动 "+value.Driver+(value.OtherProcesses>0?" · 其他进程 "+value.OtherProcesses+" 个":"")+(value.Unverified>0?" · 有路径待核实":"");
            if(value.Error.Length>0) relatedSummary.Text+=" · 部分状态未知";
            modeWarning.Text=previousMode=="Auto" ? value.ModeWarning : value.ExitProof.Length>=19 ? "上次退出核验："+value.ExitProof.Substring(5,14)+" 保活已关闭" : "";
            if(connectionExited && value.EaioService=="已停止" && value.EaioAgents==0 && value.OtherProcesses==0 && value.Unverified==0 && value.Error.Length==0) {
                headline.Text="已退出 · 按需使用";
                detail.Text="aTrust / EAIO 服务与进程均已停止";
            }
        }
        void ShowRelated() {
            using(var dialog=new Form { Text="相关组件详情",StartPosition=FormStartPosition.CenterParent,Size=Sized(620,390),Font=Font,MinimizeBox=false,MaximizeBox=false }) {
                var box=new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=Color.White,BorderStyle=BorderStyle.None,Font=Font };
                box.Text=related==null?"正在读取，请稍后刷新。":"彻底退出关闭 aTrustDaemonGuarder、eaio_watchdog_daily、两项服务的失败重启及当前用户登录启动项，并停止 aTrust / EAIO 安装目录内的进程。\r\n\r\n"+String.Join("\r\n\r\n",related.Details)+"\r\n\r\n"+related.Error;
                dialog.Controls.Add(box); dialog.ShowDialog(this);
            }
        }
        static string CountText(int count,int unknown) { return unknown>0 ? "待核实 · "+(count+unknown) : count==0 ? "未运行" : "运行中 · " + count; }
        async Task RefreshState() {
            if (busy || reading) return; reading=true;
            try {
                Snapshot state=await Task.Run(() => new WindowsSystem().Read()); if (!IsDisposed && !busy) Apply(state);
                if(!IsDisposed && !busy && (DateTime.UtcNow-lastRelatedRead).TotalSeconds>=12) {
                    RelatedState value=await Task.Run(()=>Diagnostics.Read());
                    if(!IsDisposed && !busy) { ApplyRelated(value); lastRelatedRead=DateTime.UtcNow; }
                }
            }
            catch (Exception e) { if (!IsDisposed && !busy) { headline.Text="状态读取失败"; detail.Text="请检查安装路径或查看日志"; serviceValue.Text=clientValue.Text=tunnelValue.Text=agentValue.Text=modeValue.Text="未知"; Notice(Policy.Message(Policy.ErrorCode(e)),true); Log.Write("读取状态失败 " + e); } }
            finally { reading=false; }
        }
        async Task Act(string action) {
            if (busy) return; busy=true; EnableActions(false); timer.Stop();
            Notice("等待管理员授权…",false);
            try {
                var info=new ProcessStartInfo(Application.ExecutablePath,"--action " + action) { UseShellExecute=true,Verb="runas",WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory,WindowStyle=ProcessWindowStyle.Hidden };
                int code;
                using (Process worker=Process.Start(info)) {
                    Notice(action=="stop" ? "正在停止服务并清理残留…" : "正在执行并核验…",false);
                    await Task.Run(() => worker.WaitForExit()); code=worker.ExitCode;
                }
                Log.Write("管理员操作 " + action + " 返回 " + code);
                if (code != 0) throw new Failure(code,Policy.Message(code));
                if (action=="start") {
                    Policy.VerifyFile(Policy.Client);
                    // Launch from the regular desktop process, not from the elevated helper.
                    Process launched=Process.Start(new ProcessStartInfo(Policy.Client) { UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(Policy.Client) });
                    if (launched != null) launched.Dispose();
                    bool ready=false;
                    for (int i=0;i<20;i++) {
                        await Task.Delay(500);
                        Snapshot state=await Task.Run(() => new WindowsSystem().Read());
                        if (state.Service=="Running" && state.Count("aTrustTray")>0) { ready=true; break; }
                    }
                    if (!ready) throw new Failure(15,"服务已启动，客户端进程未确认启动，请查看日志。");
                    OnDemand.DisableUserAutostart();
                    Notice("服务已启动，客户端已打开。",false);
                } else {
                    if(action!="auto") OnDemand.DisableUserAutostart();
                    Notice(action=="stop" ? "aTrust 与 EAIO 已退出，自动恢复入口已关闭。" : "当前已设为"+(action=="auto"?"主服务自动启动":"按需使用")+"。",false);
                }
                if(action=="manual" || action=="start" || action=="stop") previousMode="Manual";
                lastRelatedRead=DateTime.MinValue;
            } catch (Exception e) { int code=Policy.ErrorCode(e); Log.Write("界面操作 " + action + " 失败 " + e); Notice(e is Failure ? e.Message : Policy.Message(code),true); }
            finally { busy=false; EnableActions(true); timer.Start(); }
            await RefreshState();
        }
    }
}
