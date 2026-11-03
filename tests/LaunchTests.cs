using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CodexRelay {
    internal sealed class FakeApplicationLauncher : IApplicationLauncher {
        public string ApplicationId,Path;
        public int Activations,Starts;
        public Exception Error;
        public uint Activate(string id){Activations++;ApplicationId=id;if(Error!=null)throw Error;return 41;}
        public int StartExecutable(string path){Starts++;Path=path;if(Error!=null)throw Error;return 42;}
    }
    internal static partial class Tests {
        static void LaunchChecks(){
            var launcher=new FakeApplicationLauncher();var events=new List<string>();
            string path=Path.Combine(root,"unpacked Codex.exe");File.WriteAllText(path,"test fixture");
            DesktopLaunch.Reopen(path,"OpenAI.Codex_test!App",launcher,(kind,detail)=>events.Add(kind+" "+detail));
            Check(launcher.Activations==1&&launcher.Starts==0&&launcher.ApplicationId=="OpenAI.Codex_test!App"&&events[0].Contains("REOPEN_PACKAGE")&&events[0].Contains("pid=41"),"packaged relaunch uses the exact application ID and logs the returned process");
            launcher.Error=new COMException("simulated activation failure",unchecked((int)0x80070490));bool failed=false;
            try{DesktopLaunch.Reopen(path,"OpenAI.Codex_test!App",launcher);}catch(COMException){failed=true;}
            Check(failed&&launcher.Starts==0,"package activation failure propagates without an Explorer or EXE fallback");
            launcher.Error=null;DesktopLaunch.Reopen(path,null,launcher,(kind,detail)=>events.Add(kind+" "+detail));
            Check(launcher.Starts==1&&launcher.Path==path&&events.Last().Contains("REOPEN_EXECUTABLE"),"unpackaged relaunch directly launches the verified executable path");
            failed=false;try{DesktopLaunch.Reopen(Path.Combine(root,"missing.exe"),null,launcher);}catch(InvalidOperationException){failed=true;}
            Check(failed&&launcher.Starts==1,"missing executable reports relaunch failure instead of silent success");
            failed=false;try{DesktopLaunch.Reopen("Codex.exe",null,launcher);}catch(InvalidOperationException){failed=true;}
            Check(failed&&launcher.Starts==1,"relative executable names cannot resolve through PATH");
            DesktopLaunch.Reopen(null,null,launcher);Check(launcher.Starts==1&&launcher.Activations==2,"switching without an open desktop does not launch an unrelated app");
            string packaged=Path.Combine(root,"WindowsApps","Codex.exe");Directory.CreateDirectory(Path.GetDirectoryName(packaged));File.WriteAllText(packaged,"fixture");
            failed=false;try{DesktopLaunch.Reopen(packaged,null,launcher);}catch(InvalidOperationException){failed=true;}
            Check(failed&&launcher.Starts==1,"packaged executable without application ID asks for manual launch");
            DesktopLaunch.Reopen(Path.Combine(root,"previous-package-version.exe"),"OpenAI.Codex_test!App",launcher);
            Check(launcher.Activations==3&&launcher.Starts==1,"package identity remains usable when the old version path no longer exists");
            var guard=new ProcessGuard(launcher);var fields=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(ProcessGuard).GetField("reopenApplicationId",fields).SetValue(guard,"OpenAI.Codex_test!App");
            typeof(ProcessGuard).GetField("reopenPath",fields).SetValue(guard,path);
            guard.Reopen(null);
            Check(launcher.Activations==3&&launcher.Starts==1,"stale reopen state from an interrupted switch cannot launch a manually closed app");
            guard.Reopen(path);
            Check(launcher.Activations==3&&launcher.Starts==2,"reopen state is cleared before a later unpackaged relaunch");
        }
        static async Task LaunchFailureSwitchChecks(){
            var vault=new Vault(Path.Combine(root,"launch-failure-vault"));var a=vault.Put(Raw("launch-a"),"A");var b=vault.Put(Raw("launch-b"),"B");
            vault.Data.Settings.AutoRotate=true;
            string home=Path.Combine(root,"launch-failure-home");Directory.CreateDirectory(home);File.WriteAllText(Path.Combine(home,"auth.json"),a.AuthJson);
            var guard=new FakeGuard{OpenError=new COMException("fixture relaunch failure",unchecked((int)0x80070490))};
            guard.Items.Add(new AppProcess{Id=17,Started=123,Desktop=true,Path="C:\\fixture\\Codex.exe"});
            using(var engine=new Engine(vault,home,new Api(new FakeHttp{Handler=r=>Response(Limits(100,20,Clock.Now))}),guard)){
                string message=await engine.Switch(b,x=>true,x=>true,CancellationToken.None);
                Check(Auth.Identity(engine.LiveRaw())==b.Id&&engine.ActiveId==b.Id&&vault.Data.BackupAuth==a.AuthJson,"relaunch failure preserves the completed account switch and its previous-auth backup");
                Check(message.Contains("직접 실행")&&message.Contains("자동 전환 OFF")&&!vault.Data.Settings.AutoRotate,"relaunch failure explains manual launch and keeps a manually selected exhausted account");
                string trace=File.ReadAllText(engine.LastSwitchLog);
                Check(trace.Contains("REOPEN_FAILED")&&trace.Contains("HRESULT=0x80070490")&&!trace.Contains("REOPEN_REQUESTED")&&!trace.Contains("test-refresh"),"relaunch failure logs the native error without reporting launch success or credentials");
            }
        }
        static void ActivationProbe(bool activateExisting){
            var launcher=new WindowsApplicationLauncher();bool failed=false;
            try{launcher.Activate("CodexRelay.Nonexistent_0000000000000!Missing");}catch(COMException ex){failed=true;Console.WriteLine("Invalid AUMID HRESULT=0x"+ex.HResult.ToString("X8"));}
            Check(failed,"native activation manager is reachable and rejects an unregistered application ID");
            if(!activateExisting)return;
            var running=DesktopWindows.FindProcesses().Where(p=>!string.IsNullOrWhiteSpace(p.ApplicationId)).ToArray();
            if(running.Length==0)throw new Exception("No running Codex application to validate");
            Console.WriteLine("Existing application processes: "+string.Join(",",running.Select(p=>p.Id)));
            string id=running[0].ApplicationId;uint pid=launcher.Activate(id);
            Console.WriteLine("Activated registered application: "+id+"; returned_pid="+pid);
            // Electron may return a short-lived activation process which hands off to its existing instance.
            var returned=DesktopWindows.ReadProcess((int)pid);
            Check(pid>0&&(returned==null||returned.ApplicationId==id),"Windows accepts the installed application ID and returns its activation process");
            Check(running.All(p=>{var current=DesktopWindows.ReadProcess(p.Id);return current!=null&&current.Started==p.Started;}),"activation preserves all previously running Codex application processes");
            Console.WriteLine("Activation process differs from existing main process: "+!running.Any(p=>p.Id==pid));
        }
    }
}
