using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace CodexRelay {
    internal sealed class DesktopWindowInfo {
        public IntPtr Handle;
        public AppProcess Process;
        public bool Visible,Enabled,Owned,ToolWindow,NoActivate,WindowControl,WindowPattern,MenuBar;
        public bool TaskbarWindow,Resizable,MinMaxButtons,CanMinimize,CanMaximize,Modal;
        public int Cloaked=-1;
        public string Error="";
        public bool MainWindowShape { get { return Process!=null && string.Equals(Process.PackageFamily,DesktopWindows.PackageFamily,StringComparison.OrdinalIgnoreCase) && Visible && !Owned && !ToolWindow && !NoActivate && !Modal && Cloaked==0 && WindowControl && WindowPattern && Error.Length==0; } }
        public bool DesktopFrame {get{return TaskbarWindow&&Resizable&&MinMaxButtons;}}
        public bool ApplicationWindow {get{return MainWindowShape&&DesktopFrame&&CanMinimize&&CanMaximize&&!Modal;}}
        public bool Candidate { get { return MainWindowShape && (MenuBar||ApplicationWindow); } }
        public string Describe(){return "hwnd=0x"+Handle.ToInt64().ToString("X")+"; pid="+(Process==null?0:Process.Id)+"; family="+(Process==null?"":Process.PackageFamily)+"; visible="+Visible+"; enabled="+Enabled+"; owned="+Owned+"; tool_window="+ToolWindow+"; no_activate="+NoActivate+"; cloaked="+Cloaked+"; window_control="+WindowControl+"; window_pattern="+WindowPattern+"; application_menu="+MenuBar+"; taskbar_window="+TaskbarWindow+"; resizable="+Resizable+"; min_max_buttons="+MinMaxButtons+"; can_minimize="+CanMinimize+"; can_maximize="+CanMaximize+"; modal="+Modal+"; error="+Error;}
    }
    internal static class DesktopWindows {
        // Package family identity omits version, architecture and executable filename.
        internal const string PackageFamily="OpenAI.Codex_2p2nqsd0c76g0";
        internal const int MenuScanAttempts=4,MenuRetryDelayMs=350;
        delegate bool EnumCallback(IntPtr window,IntPtr state);
        [DllImport("user32.dll",SetLastError=true)]static extern bool EnumWindows(EnumCallback callback,IntPtr state);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
        [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
        [DllImport("user32.dll")]internal static extern IntPtr GetForegroundWindow();
        [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
        [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int processId);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")]static extern bool ProcessIdToSessionId(int processId,out int sessionId);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetPackageFamilyName(IntPtr process,ref uint length,StringBuilder name);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetApplicationUserModelId(IntPtr process,ref uint length,StringBuilder name);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref uint length);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr process,out long created,out long exited,out long kernel,out long user);
        static readonly object scanSync=new object();
        static Task<List<DesktopWindowInfo>> scan;
        static int Session { get { using(var self=Process.GetCurrentProcess())return self.SessionId; } }
        internal static AppProcess ReadProcess(int processId) {
            int session;if(!ProcessIdToSessionId(processId,out session)||session!=Session)return null;
            IntPtr handle=OpenProcess(0x1000,false,processId);if(handle==IntPtr.Zero)return null;
            try {
                uint length=0;if(GetPackageFamilyName(handle,ref length,null)!=122)return null;
                var family=new StringBuilder((int)length);if(GetPackageFamilyName(handle,ref length,family)!=0||!string.Equals(family.ToString(),PackageFamily,StringComparison.OrdinalIgnoreCase))return null;
                long created,exited,kernel,user;if(!GetProcessTimes(handle,out created,out exited,out kernel,out user)||exited!=0)return null;
                var path=new StringBuilder(32768);length=(uint)path.Capacity;if(!QueryFullProcessImageName(handle,0,path,ref length))return null;
                string applicationId="";length=0;
                if(GetApplicationUserModelId(handle,ref length,null)==122){var app=new StringBuilder((int)length);if(GetApplicationUserModelId(handle,ref length,app)==0)applicationId=app.ToString();}
                return new AppProcess{Id=processId,Started=DateTime.FromFileTimeUtc(created).Ticks,Path=path.ToString(),Desktop=true,PackageFamily=family.ToString(),ApplicationId=applicationId};
            }finally{CloseHandle(handle);}
        }
        internal static List<AppProcess> FindProcesses() {
            var ids=new HashSet<int>();
            if(!EnumWindows((window,state)=>{uint pid;if(IsWindowVisible(window)&&GetWindowThreadProcessId(window,out pid)!=0)ids.Add((int)pid);return true;},IntPtr.Zero))throw new Win32Exception();
            var found=new List<AppProcess>();foreach(int id in ids){var p=ReadProcess(id);if(p!=null)found.Add(p);}return found;
        }
        static DesktopWindowInfo ReadNative(IntPtr window,AppProcess process) {
            uint owner;GetWindowThreadProcessId(window,out owner);
            if(owner!=(uint)process.Id||!IsWindow(window))return null;
            uint style=unchecked((uint)GetWindowLongPtr(window,-20).ToInt64()),windowStyle=unchecked((uint)GetWindowLongPtr(window,-16).ToInt64());int cloaked;
            int hr=DwmGetWindowAttribute(window,14,out cloaked,4);
            return new DesktopWindowInfo{Handle=window,Process=process,Visible=IsWindowVisible(window),Enabled=IsWindowEnabled(window),Owned=GetWindow(window,4)!=IntPtr.Zero,ToolWindow=(style&0x80)!=0,NoActivate=(style&0x8000000)!=0,Cloaked=hr==0?cloaked:-1,TaskbarWindow=(style&0x40000)!=0,Resizable=(windowStyle&0x40000)!=0,MinMaxButtons=(windowStyle&0x30000)==0x30000};
        }
        static List<DesktopWindowInfo> Inspect() {
            var processes=FindProcesses().ToDictionary(p=>p.Id);var rows=new List<DesktopWindowInfo>();
            if(!EnumWindows((window,state)=>{uint pid;GetWindowThreadProcessId(window,out pid);AppProcess process;if(processes.TryGetValue((int)pid,out process)){var row=ReadNative(window,process);if(row!=null&&row.Visible)rows.Add(row);}return true;},IntPtr.Zero))throw new Win32Exception();
            foreach(var row in rows) {
                // Native tool windows can have the same title and class as the real UI.
                if(row.ToolWindow||row.NoActivate||row.Owned||row.Cloaked!=0)continue;
                try {
                    var root=AutomationElement.FromHandle(row.Handle);var current=root.Current;
                    row.WindowControl=current.ProcessId==row.Process.Id&&current.ControlType==ControlType.Window;
                    object pattern;row.WindowPattern=root.TryGetCurrentPattern(WindowPattern.Pattern,out pattern);
                    if(row.WindowPattern){var capability=((WindowPattern)pattern).Current;row.CanMinimize=capability.CanMinimize;row.CanMaximize=capability.CanMaximize;row.Modal=capability.IsModal;}
                    if(row.WindowControl&&row.WindowPattern)row.MenuBar=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuBar))!=null;
                }catch(ElementNotAvailableException){row.Error="window_disappeared";}catch(InvalidOperationException){row.Error="uia_unavailable";}catch(COMException){row.Error="uia_unavailable";}
            }
            return rows;
        }
        internal static async Task<List<DesktopWindowInfo>> InspectAsync(CancellationToken ct) {
            Task<List<DesktopWindowInfo>> pending;
            lock(scanSync){
                if(scan!=null&&!scan.IsCompleted)throw new QuitRequestException("이전 창 확인이 아직 끝나지 않았습니다. 잠시 후 다시 전환하세요.");
                // UI Automation runs on a worker, never on the Relay UI thread.
                scan=pending=Task.Run((Func<List<DesktopWindowInfo>>)Inspect);
                ObserveScanFailure(pending);
            }
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)){
                if(await Task.WhenAny(pending,Task.Delay(8000,timeout.Token))!=pending){ct.ThrowIfCancellationRequested();throw new QuitRequestException("Codex 주 창 확인 시간이 초과되었습니다. 앱 상태를 확인한 뒤 다시 전환하세요.");}
                timeout.Cancel();return await pending;
            }
        }
        static void ObserveScanFailure(Task pending){pending.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        internal static DesktopWindowInfo Select(IEnumerable<DesktopWindowInfo> windows,IntPtr foreground) {
            var valid=windows.Where(w=>w.Candidate).ToArray();
            if(valid.Length==0)throw new QuitRequestException("Codex 주 창의 메뉴나 일반 앱 창 기능을 확인하지 못했습니다. Codex 주 창을 열고 다시 전환하세요.");
            var focused=valid.Where(w=>w.Handle==foreground).ToArray();
            DesktopWindowInfo chosen=focused.Length==1?focused[0]:valid.Length==1?valid[0]:null;
            if(chosen==null)throw new QuitRequestException("Codex 주 창 후보가 여러 개여서 전환하지 않았습니다. 사용할 주 창을 확인하세요.");
            if(!chosen.Enabled)throw new QuitRequestException("Codex에 확인창이 열려 있어 종료 명령을 보내지 않았습니다. 확인창을 처리한 뒤 다시 전환하세요.");
            return chosen;
        }
        internal static bool Revalidate(DesktopWindowInfo selected) {
            var process=ReadProcess(selected.Process.Id);
            if(process==null||process.Started!=selected.Process.Started)return false;
            var native=ReadNative(selected.Handle,process);
            return native!=null&&native.Visible&&native.Enabled&&!native.ToolWindow&&!native.NoActivate&&!native.Owned&&native.Cloaked==0&&(selected.MenuBar||native.DesktopFrame);
        }
        internal static async Task<DesktopWindowInfo> FindForSwitch(List<AppProcess> approved,CancellationToken ct,Action<string,string> log) {
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)){
                timeout.CancelAfter(8000);
                try{return await FindForSwitchCore(approved,timeout.Token,log,InspectAsync,GetForegroundWindow,Revalidate,Task.Delay);}
                catch(OperationCanceledException){ct.ThrowIfCancellationRequested();throw new QuitRequestException("Codex 주 창 재확인 시간이 초과되었습니다. 앱 상태를 확인한 뒤 다시 전환하세요.");}
            }
        }
        static bool SameWindow(DesktopWindowInfo a,DesktopWindowInfo b){return a.Handle==b.Handle&&a.Process!=null&&b.Process!=null&&a.Process.Id==b.Process.Id&&a.Process.Started==b.Process.Started;}
        internal static async Task<DesktopWindowInfo> FindForSwitchCore(List<AppProcess> approved,CancellationToken ct,Action<string,string> log,Func<CancellationToken,Task<List<DesktopWindowInfo>>> inspect,Func<IntPtr> foreground,Func<DesktopWindowInfo,bool> validate,Func<int,CancellationToken,Task> delay) {
            DesktopWindowInfo expected=null;
            for(int attempt=1;attempt<=MenuScanAttempts;attempt++){
                ct.ThrowIfCancellationRequested();var rows=await inspect(ct);ct.ThrowIfCancellationRequested();
                foreach(var row in rows)log("WINDOW_CANDIDATE","attempt="+attempt+"; "+row.Describe());
                if(expected!=null&&!rows.Any(w=>SameWindow(expected,w)&&w.MainWindowShape&&w.Enabled))throw new QuitRequestException("재확인 중 Codex 주 창이나 프로세스 상태가 바뀌어 전환을 중단했습니다.");
                if(!rows.Any(w=>w.MainWindowShape&&w.MenuBar)){
                    var possible=rows.Where(w=>w.MainWindowShape&&w.Enabled).ToArray();
                    if(possible.Length==1){
                        var candidate=possible[0];
                        if(!approved.Any(p=>p.Id==candidate.Process.Id&&p.Started==candidate.Process.Started))throw new QuitRequestException("확인 이후 새 Codex 앱이 실행되어 전환을 중단했습니다.");
                        if(attempt<MenuScanAttempts){
                            expected=expected??candidate;
                            log("WINDOW_MENU_RETRY","attempt="+attempt+"; delay_ms="+MenuRetryDelayMs+"; "+candidate.Describe());
                            await delay(MenuRetryDelayMs,ct);continue;
                        }
                        if(!candidate.ApplicationWindow)throw new QuitRequestException("Codex 주 창을 "+MenuScanAttempts+"회 확인했지만 메뉴나 일반 앱 창 기능을 확인하지 못했습니다. 주 창을 연 뒤 다시 전환하세요.");
                        log("WINDOW_CAPABILITIES_CONFIRMED","menu_hidden_or_unavailable; "+candidate.Describe());
                    }
                }
                var selected=Select(rows,foreground());
                if(expected!=null&&!SameWindow(expected,selected))throw new QuitRequestException("재확인 중 다른 Codex 주 창이 선택되어 전환을 중단했습니다.");
                if(!approved.Any(p=>p.Id==selected.Process.Id&&p.Started==selected.Process.Started))throw new QuitRequestException("확인 이후 새 Codex 앱이 실행되어 전환을 중단했습니다.");
                if(!validate(selected))throw new QuitRequestException("선택한 Codex 주 창의 상태가 바뀌어 전환을 중단했습니다.");
                log("MAIN_WINDOW_SELECTED","attempt="+attempt+"; "+selected.Describe());return selected;
            }
            throw new QuitRequestException("Codex 주 창을 확인하지 못했습니다.");
        }
    }
}
