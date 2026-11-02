using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodexRelay {
    internal static partial class Tests {
        static DesktopWindowInfo MainWindow(int handle=100) {
            return new DesktopWindowInfo{Handle=new IntPtr(handle),Process=new AppProcess{Id=17,Started=1,Path="C:\\old-version\\ChatGPT.exe",PackageFamily=DesktopWindows.PackageFamily},Visible=true,Enabled=true,Cloaked=0,WindowControl=true,WindowPattern=true,MenuBar=true};
        }
        static bool SelectionBlocked(DesktopWindowInfo[] rows,IntPtr foreground) {try{DesktopWindows.Select(rows,foreground);return false;}catch(QuitRequestException){return true;}}
        static void WindowSelectionChecks() {
            var main=MainWindow();var tool=MainWindow(200);tool.ToolWindow=true;tool.WindowControl=false;tool.WindowPattern=false;tool.MenuBar=false;
            Check(DesktopWindows.Select(new[]{tool,main},tool.Handle)==main,"tool window with the same process is never chosen over the application window");
            Check(DesktopWindows.Select(new[]{main,tool},IntPtr.Zero)==main,"main window selection does not depend on enumeration order");
            main.Handle=new IntPtr(123456);main.Process.Id=1234;main.Process.Path="D:\\updated-installation\\RenamedApp.exe";
            Check(DesktopWindows.Select(new[]{tool,main},IntPtr.Zero)==main,"changing process id handle executable name and installation path preserves semantic selection");
            main.Process.PackageFamily=DesktopWindows.PackageFamily.ToUpperInvariant();Check(main.Candidate,"package identity matching is case insensitive");
            main.Process.PackageFamily="Unrelated.App_otherpublisher";Check(SelectionBlocked(new[]{main,tool},main.Handle),"matching window features cannot authorize another app package");main.Process.PackageFamily=DesktopWindows.PackageFamily;
            main.MenuBar=false;Check(SelectionBlocked(new[]{main,tool},main.Handle),"missing app menu does not fall back to the visible tool window");main.MenuBar=true;
            main.WindowPattern=false;Check(SelectionBlocked(new[]{main},main.Handle),"missing window pattern prevents a guessed quit target");main.WindowPattern=true;
            main.Enabled=false;Check(SelectionBlocked(new[]{main},main.Handle),"a main window blocked by a dialog cannot receive quit input");main.Enabled=true;
            main.Cloaked=1;Check(SelectionBlocked(new[]{main},main.Handle),"cloaked app window is not selected");main.Cloaked=0;
            main.Owned=true;Check(SelectionBlocked(new[]{main},main.Handle),"an owned modal window is not selected as the main app");main.Owned=false;
            main.NoActivate=true;Check(SelectionBlocked(new[]{main},main.Handle),"no-activate windows cannot become quit targets");main.NoActivate=false;
            main.Error="uia_unavailable";Check(SelectionBlocked(new[]{main},main.Handle),"incomplete UI inspection fails closed");main.Error="";
            var other=MainWindow(300);Check(SelectionBlocked(new[]{main,other},tool.Handle),"ambiguous real windows are not resolved by list order");
            Check(DesktopWindows.Select(new[]{main,other},other.Handle)==other,"an observed foreground app window resolves multiple verified windows");
        }
        static async Task WindowReadOnly() {
            var snapshot=new ProcessGuard().Snapshot();
            var selected=await DesktopWindows.FindForSwitch(snapshot,CancellationToken.None,(kind,detail)=>Console.WriteLine(kind+" "+detail));
            Check(selected.Candidate&&!selected.ToolWindow&&(selected.MenuBar||selected.ApplicationWindow)&&selected.WindowPattern,"live selection has verified menu or full application window capabilities");
            Check(DesktopWindows.Revalidate(selected),"selected live handle still belongs to the same package process and start time");
            Check(snapshot.Any(p=>p.Id==selected.Process.Id&&p.Started==selected.Process.Started&&p.Desktop),"process guard includes the package-discovered app root");
            Console.WriteLine("SELECTED "+selected.Describe());
            Console.WriteLine("READ ONLY: no keyboard input, activation, process termination, authentication access or relaunch was performed.");
        }
        static async Task WindowRetryChecks(){
            var approved=new List<AppProcess>{MainWindow().Process};var events=new List<string>();int scans=0,waits=0,validated=0;
            Func<CancellationToken,Task<List<DesktopWindowInfo>>> inspect=ct=>{scans++;var main=MainWindow();main.MenuBar=scans>=2;return Task.FromResult(new List<DesktopWindowInfo>{main});};
            Func<int,CancellationToken,Task> delay=(ms,ct)=>{ct.ThrowIfCancellationRequested();waits++;return Task.FromResult(0);};
            Func<DesktopWindowInfo,bool> validate=window=>{validated++;return true;};
            Action<string,string> log=(kind,detail)=>events.Add(kind+" "+detail);
            var selected=await DesktopWindows.FindForSwitchCore(approved,CancellationToken.None,log,inspect,()=>IntPtr.Zero,validate,delay);
            Check(selected.MenuBar&&scans==2&&waits==1&&validated==1&&events.Any(x=>x.StartsWith("WINDOW_MENU_RETRY")),"temporarily missing menu is reread on the same approved window before selection");
            scans=waits=validated=0;events.Clear();
            inspect=ct=>{scans++;var main=MainWindow();main.MenuBar=false;return Task.FromResult(new List<DesktopWindowInfo>{main});};
            bool blocked=false;try{await DesktopWindows.FindForSwitchCore(approved,CancellationToken.None,log,inspect,()=>IntPtr.Zero,validate,delay);}catch(QuitRequestException){blocked=true;}
            Check(blocked&&scans==4&&waits==3&&validated==0&&!events.Any(x=>x.StartsWith("MAIN_WINDOW_SELECTED")),"persistent missing menu stops after four scans and never authorizes input");
            scans=waits=validated=0;events.Clear();
            inspect=ct=>{scans++;var main=MainWindow();main.MenuBar=false;main.TaskbarWindow=main.Resizable=main.MinMaxButtons=main.CanMinimize=main.CanMaximize=true;return Task.FromResult(new List<DesktopWindowInfo>{main});};
            selected=await DesktopWindows.FindForSwitchCore(approved,CancellationToken.None,log,inspect,()=>IntPtr.Zero,validate,delay);
            Check(selected.ApplicationWindow&&!selected.MenuBar&&scans==4&&waits==3&&validated==1&&events.Any(x=>x.StartsWith("WINDOW_CAPABILITIES_CONFIRMED")),"persistently hidden menu requires a stable taskbar window with native and accessibility minimize maximize capabilities");
            var capabilities=MainWindow();capabilities.MenuBar=false;capabilities.TaskbarWindow=capabilities.Resizable=capabilities.MinMaxButtons=capabilities.CanMinimize=capabilities.CanMaximize=true;
            capabilities.Modal=true;bool hiddenMenuModalBlocked=!capabilities.Candidate;capabilities.MenuBar=true;Check(hiddenMenuModalBlocked&&!capabilities.Candidate,"modal windows cannot qualify with either visible or hidden menus");capabilities.Modal=false;capabilities.MenuBar=false;
            capabilities.TaskbarWindow=false;Check(!capabilities.Candidate,"menu-less windows without taskbar identity cannot qualify");capabilities.TaskbarWindow=true;
            capabilities.CanMaximize=false;Check(!capabilities.Candidate,"missing accessibility window capability prevents menu-less selection");
            scans=waits=0;
            inspect=ct=>{scans++;var main=MainWindow(scans==1?100:101);main.MenuBar=scans>1;return Task.FromResult(new List<DesktopWindowInfo>{main});};
            blocked=false;try{await DesktopWindows.FindForSwitchCore(approved,CancellationToken.None,log,inspect,()=>IntPtr.Zero,validate,delay);}catch(QuitRequestException){blocked=true;}
            Check(blocked&&scans==2&&waits==1,"a replacement window cannot inherit the missing-menu retry authorization");
            scans=waits=0;
            inspect=ct=>{scans++;var main=MainWindow();main.Process.Started=scans==1?1:2;main.MenuBar=scans>1;return Task.FromResult(new List<DesktopWindowInfo>{main});};
            blocked=false;try{await DesktopWindows.FindForSwitchCore(approved,CancellationToken.None,log,inspect,()=>IntPtr.Zero,validate,delay);}catch(QuitRequestException){blocked=true;}
            Check(blocked&&scans==2,"reused process id with a new start time cannot receive a quit command");
            scans=waits=0;
            inspect=ct=>{scans++;var main=MainWindow();main.ToolWindow=true;main.MenuBar=false;return Task.FromResult(new List<DesktopWindowInfo>{main});};
            blocked=false;try{await DesktopWindows.FindForSwitchCore(approved,CancellationToken.None,log,inspect,()=>IntPtr.Zero,validate,delay);}catch(QuitRequestException){blocked=true;}
            Check(blocked&&scans==1&&waits==0,"tool windows never enter the menu retry path");
            scans=waits=0;
            inspect=ct=>{scans++;var a=MainWindow();a.MenuBar=false;var b=MainWindow(200);b.MenuBar=false;return Task.FromResult(new List<DesktopWindowInfo>{a,b});};
            blocked=false;try{await DesktopWindows.FindForSwitchCore(approved,CancellationToken.None,log,inspect,()=>IntPtr.Zero,validate,delay);}catch(QuitRequestException){blocked=true;}
            Check(blocked&&scans==1&&waits==0,"ambiguous menu-less main windows do not select by enumeration order");
            scans=waits=0;
            inspect=ct=>{scans++;var main=MainWindow();main.MenuBar=false;return Task.FromResult(new List<DesktopWindowInfo>{main});};
            using(var cancellation=new CancellationTokenSource()){
                bool canceled=false;try{await DesktopWindows.FindForSwitchCore(approved,cancellation.Token,log,inspect,()=>IntPtr.Zero,validate,(ms,ct)=>{cancellation.Cancel();ct.ThrowIfCancellationRequested();return Task.FromResult(0);});}catch(OperationCanceledException){canceled=true;}
                Check(canceled&&scans==1,"cancellation stops menu retries before another scan or quit authorization");
            }
            scans=waits=0;inspect=ct=>{scans++;return Task.FromResult(new List<DesktopWindowInfo>{MainWindow()});};
            blocked=false;try{await DesktopWindows.FindForSwitchCore(approved,CancellationToken.None,log,inspect,()=>IntPtr.Zero,window=>false,delay);}catch(QuitRequestException){blocked=true;}
            Check(blocked&&scans==1&&waits==0,"menu success still requires final process and window revalidation");
        }
    }
}
