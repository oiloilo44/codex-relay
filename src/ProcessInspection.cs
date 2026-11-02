using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexRelay {
    internal interface IProcessProbe : IDisposable {
        bool Exited(int waitMilliseconds);
        int SessionId {get;}
        string ImagePath {get;}
        long Started {get;}
    }
    internal sealed class NativeProcessProbe : IProcessProbe {
        IntPtr handle;
        readonly int processId;
        [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int id);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool ProcessIdToSessionId(int id,out int session);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr handle,uint flags,StringBuilder path,ref uint length);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr handle,out long created,out long exited,out long kernel,out long user);
        NativeProcessProbe(int id,IntPtr handle){processId=id;this.handle=handle;}
        internal static IProcessProbe Open(int id){
            // Keep the same process object alive while querying and checking exit; PID alone may be reused.
            IntPtr handle=OpenProcess(0x1000|0x100000,false,id);
            if(handle==IntPtr.Zero){int error=Marshal.GetLastWin32Error();if(error==87&&id>0)return null;throw new Win32Exception(error);}
            return new NativeProcessProbe(id,handle);
        }
        public bool Exited(int waitMilliseconds){uint result=WaitForSingleObject(handle,(uint)waitMilliseconds);if(result==0)return true;if(result==258)return false;throw new Win32Exception(Marshal.GetLastWin32Error());}
        public int SessionId {get{int session;if(!ProcessIdToSessionId(processId,out session))throw new Win32Exception(Marshal.GetLastWin32Error());return session;}}
        public string ImagePath {get{var path=new StringBuilder(32768);uint length=(uint)path.Capacity;if(!QueryFullProcessImageName(handle,0,path,ref length))throw new Win32Exception(Marshal.GetLastWin32Error());return path.ToString();}}
        public long Started {get{long created,exited,kernel,user;if(!GetProcessTimes(handle,out created,out exited,out kernel,out user))throw new Win32Exception(Marshal.GetLastWin32Error());return DateTime.FromFileTimeUtc(created).Ticks;}}
        public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
    }
    internal sealed class ProcessInspectionException : InvalidOperationException {
        internal ProcessInspectionException(int id,string stage,Exception cause):base("Codex 프로세스 정보를 확인할 수 없어 전환을 중단했습니다. PID="+id+", 단계="+stage+", "+Error(cause),cause){}
        internal static string Error(Exception cause){var native=cause as Win32Exception;return native==null?cause.GetType().Name:"Win32="+native.NativeErrorCode+" ("+native.Message+")";}
    }
    internal static class ProcessInspection {
        static bool Exited(IProcessProbe probe,int id,int wait){try{return probe.Exited(wait);}catch(Win32Exception e){throw new ProcessInspectionException(id,"exit-state",e);}}
        internal static AppProcess Read(int id,int session,Action<string,string> log=null,Func<int,IProcessProbe> open=null){
            Action<string,string> record=log??((kind,detail)=>{});IProcessProbe probe;
            try{probe=(open??NativeProcessProbe.Open)(id);}catch(Win32Exception e){throw new ProcessInspectionException(id,"open",e);}
            if(probe==null){record("PROCESS_ALREADY_EXITED","pid="+id+"; stage=open");return null;}
            using(probe){
                if(Exited(probe,id,0)){record("PROCESS_ALREADY_EXITED","pid="+id+"; stage=before-query");return null;}
                for(int attempt=1;attempt<=2;attempt++){
                    string stage="session";
                    try{
                        if(probe.SessionId!=session)return null;
                        stage="image-path";string path=probe.ImagePath;
                        if(string.IsNullOrEmpty(path))throw new Win32Exception(299);
                        stage="start-time";long started=probe.Started;
                        if(Exited(probe,id,0)){record("PROCESS_EXITED_DURING_INSPECTION","pid="+id+"; stage=after-query");return null;}
                        return new AppProcess{Id=id,Path=path,Started=started};
                    }catch(Win32Exception e){
                        string detail="pid="+id+"; stage="+stage+"; attempt="+attempt+"; "+ProcessInspectionException.Error(e);
                        // An unreadable live process still blocks switching. Ignore only a confirmed exit.
                        if(Exited(probe,id,100)){record("PROCESS_EXITED_DURING_INSPECTION",detail);return null;}
                        if(attempt==2)throw new ProcessInspectionException(id,stage,e);
                        record("PROCESS_INSPECTION_RETRY",detail);
                    }
                }
                throw new InvalidOperationException("프로세스 확인을 완료하지 못했습니다.");
            }
        }
    }
}
