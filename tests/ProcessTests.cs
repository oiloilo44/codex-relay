using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodexRelay {
    internal sealed class FakeProcessProbe : IProcessProbe {
        public bool IsExited,Disposed;
        public int Session=3,Reads;
        public Func<string> ReadPath;
        public bool Exited(int wait){return IsExited;}
        public int SessionId{get{return Session;}}
        public string ImagePath{get{Reads++;return ReadPath==null?"C:\\fixture\\ChatGPT.exe":ReadPath();}}
        public long Started{get{return 123;}}
        public void Dispose(){Disposed=true;}
    }
    internal sealed class ExitingProcessProbe : IProcessProbe {
        readonly IProcessProbe inner;
        readonly EventWaitHandle signal;
        readonly Process child;
        public ExitingProcessProbe(IProcessProbe inner,EventWaitHandle signal,Process child){this.inner=inner;this.signal=signal;this.child=child;}
        public bool Exited(int wait){return inner.Exited(wait);}
        public int SessionId{get{return inner.SessionId;}}
        public string ImagePath{get{signal.Set();if(!child.WaitForExit(3000))throw new Exception("Fixture did not exit");throw new Win32Exception(299);}}
        public long Started{get{return inner.Started;}}
        public void Dispose(){inner.Dispose();}
    }
    internal static partial class Tests {
        static void ProcessInspectionChecks(){
            var events=new List<string>();Action<string,string> log=(kind,detail)=>events.Add(kind+" "+detail);
            var p=new FakeProcessProbe();var found=ProcessInspection.Read(17,3,log,id=>p);
            Check(found.Id==17&&found.Started==123&&found.Path.EndsWith("ChatGPT.exe")&&p.Disposed,"process inspection reads identity and releases the retained handle");
            p=new FakeProcessProbe();p.ReadPath=()=>{p.IsExited=true;throw new Win32Exception(299);};events.Clear();
            found=ProcessInspection.Read(17,3,log,id=>p);
            Check(found==null&&p.Disposed&&events.Any(x=>x.StartsWith("PROCESS_EXITED_DURING_INSPECTION")&&x.Contains("Win32=299")),"path query failure is skipped only after confirming exit and logs the original error");
            p=new FakeProcessProbe();p.ReadPath=()=>{if(p.Reads==1)throw new Win32Exception(299);return "C:\\fixture\\ChatGPT.exe";};events.Clear();
            found=ProcessInspection.Read(17,3,log,id=>p);
            Check(found!=null&&p.Reads==2&&events.Any(x=>x.StartsWith("PROCESS_INSPECTION_RETRY")),"transient query failure on a live process is retried rather than treated as exit");
            p=new FakeProcessProbe();p.ReadPath=()=>{throw new Win32Exception(5);};ProcessInspectionException failure=null;
            try{ProcessInspection.Read(17,3,log,id=>p);}catch(ProcessInspectionException e){failure=e;}
            Check(failure!=null&&failure.Message.Contains("PID=17")&&failure.Message.Contains("image-path")&&failure.Message.Contains("Win32=5")&&p.Reads==2&&p.Disposed,"unreadable live process fails closed with pid stage and native error");
            failure=null;try{ProcessInspection.Read(17,3,log,id=>{throw new Win32Exception(5);});}catch(ProcessInspectionException e){failure=e;}
            Check(failure!=null&&failure.Message.Contains("open")&&failure.Message.Contains("Win32=5"),"access denied while opening a process is never interpreted as exit");
            found=ProcessInspection.Read(17,3,log,id=>null);Check(found==null,"process gone before handle acquisition is no longer pending");
            p=new FakeProcessProbe{Session=4};found=ProcessInspection.Read(17,3,log,id=>p);Check(found==null&&p.Reads==0&&p.Disposed,"other Windows sessions are excluded without reading executable paths");
            p=new FakeProcessProbe();p.ReadPath=()=>{p.IsExited=true;return "C:\\fixture\\ChatGPT.exe";};found=ProcessInspection.Read(17,3,log,id=>p);
            Check(found==null,"process exiting after successful metadata query is not reported as running");
        }
        static void NativeProcessExitChecks(){
            string name="Local\\CodexRelayProbeTest-"+Guid.NewGuid().ToString("N");
            using(var signal=new EventWaitHandle(false,EventResetMode.ManualReset,name))
            using(var child=Process.Start(new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath,"--process-exit-fixture "+name){UseShellExecute=false,CreateNoWindow=true})){
                try{
                    int session;using(var self=Process.GetCurrentProcess())session=self.SessionId;
                    var live=ProcessInspection.Read(child.Id,session);
                    Check(live!=null&&live.Started==child.StartTime.ToUniversalTime().Ticks&&string.Equals(live.Path,System.Windows.Forms.Application.ExecutablePath,StringComparison.OrdinalIgnoreCase),"native process reader captures the fixture path and exact process start time");
                    var retained=NativeProcessProbe.Open(child.Id);
                    var result=ProcessInspection.Read(child.Id,session,null,id=>new ExitingProcessProbe(retained,signal,child));
                    Check(result==null&&child.HasExited,"real child exit during path failure is confirmed through the retained Windows process handle");
                }finally{signal.Set();child.WaitForExit(3000);}
            }
        }
        static async Task ProcessExitSwitchChecks(){
            var vault=new Vault(Path.Combine(root,"process-race-vault"));var a=vault.Put(Raw("race-a"),"A");var b=vault.Put(Raw("race-b"),"B");
            var home=Path.Combine(root,"process-race-home");Directory.CreateDirectory(home);File.WriteAllText(Path.Combine(home,"auth.json"),a.AuthJson);
            var guard=new FakeGuard();var initial=new AppProcess{Id=17,Started=123,Desktop=true,Path="C:\\fixture\\ChatGPT.exe"};bool closing=false,unreadable=false,forceAsked=false;
            guard.OnClose=(items,force)=>closing=true;
            guard.OnSnapshot=()=>{
                if(!closing)return new List<AppProcess>{initial};
                var probe=new FakeProcessProbe();probe.ReadPath=()=>{probe.IsExited=!unreadable;throw new Win32Exception(unreadable?5:299);};
                var item=ProcessInspection.Read(17,3,null,id=>probe);return item==null?new List<AppProcess>():new List<AppProcess>{item};
            };
            using(var engine=new Engine(vault,home,new Api(new FakeHttp{Handler=r=>Response(Limits(10,20,Clock.Now))}),guard)){
                await engine.Switch(b,x=>true,x=>{forceAsked=true;return false;},CancellationToken.None);
                Check(!forceAsked&&Auth.Identity(engine.LiveRaw())==b.Id&&guard.Opened==1,"confirmed exit during inspection allows switching and reopening without force-close approval");
                File.WriteAllText(engine.AuthPath,a.AuthJson);closing=false;unreadable=true;bool failed=false;
                try{await engine.Switch(b,x=>true,x=>true,CancellationToken.None);}catch(ProcessInspectionException){failed=true;}
                string trace=File.ReadAllText(engine.LastSwitchLog);
                Check(failed&&Auth.Identity(engine.LiveRaw())==a.Id&&guard.Opened==1&&!trace.Contains("AUTH_REPLACED")&&trace.Contains("Win32=5"),"live process query failure preserves authentication and records actionable diagnostics");
            }
        }
    }
}
