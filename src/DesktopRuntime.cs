using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CodexRelay {
    internal sealed class DesktopRun {
        public bool Known,Present;
        public DateTime StartedUtc;
        public static DesktopRun RunningSince(DateTime started) { return new DesktopRun{Known=true,Present=true,StartedUtc=started}; }
        public static DesktopRun Stopped() { return new DesktopRun{Known=true}; }
    }
    internal sealed class DesktopRuntime {
        sealed class Candidate { public int Id;public DateTime Started;public bool Window; }
        int lastRootId;
        DateTime lastRootStarted;
        internal DesktopRun Read() {
            var found=new List<Candidate>();bool inaccessible=false;
            using(var self=Process.GetCurrentProcess()) {
                foreach(var process in Process.GetProcessesByName("ChatGPT"))using(process)try {
                    if(process.SessionId!=self.SessionId)continue;
                    string path=process.MainModule.FileName;
                    if(path.IndexOf("OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)<0 && path.IndexOf("\\Codex\\",StringComparison.OrdinalIgnoreCase)<0)continue;
                    found.Add(new Candidate{Id=process.Id,Started=process.StartTime.ToUniversalTime(),Window=process.MainWindowHandle!=IntPtr.Zero});
                } catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) { inaccessible=true; }
            }
            // A newly opened main window wins over orphaned renderers from a prior run.
            var root=found.Where(p=>p.Window).OrderBy(p=>p.Started).FirstOrDefault();
            if(root==null)root=found.FirstOrDefault(p=>p.Id==lastRootId&&p.Started==lastRootStarted);
            if(root!=null){lastRootId=root.Id;lastRootStarted=root.Started;return DesktopRun.RunningSince(root.Started);}
            if(found.Count==0&&!inaccessible){lastRootId=0;return DesktopRun.Stopped();}
            return new DesktopRun();
        }
    }
}
