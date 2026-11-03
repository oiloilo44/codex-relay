using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CodexRelay {
    internal interface IApplicationLauncher {
        uint Activate(string applicationId);
        int StartExecutable(string path);
    }
    internal sealed class WindowsApplicationLauncher : IApplicationLauncher {
        [ComImport,Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IApplicationActivationManager {
            [PreserveSig]int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string applicationId,[MarshalAs(UnmanagedType.LPWStr)]string arguments,uint options,out uint processId);
            [PreserveSig]int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)]string applicationId,IntPtr items,[MarshalAs(UnmanagedType.LPWStr)]string verb,out uint processId);
            [PreserveSig]int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)]string applicationId,IntPtr items,out uint processId);
        }
        [DllImport("ole32.dll",ExactSpelling=true)]
        static extern int CoCreateInstance(ref Guid classId,IntPtr outer,uint context,ref Guid interfaceId,[MarshalAs(UnmanagedType.Interface)]out IApplicationActivationManager instance);
        public uint Activate(string applicationId){
            var classId=new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C");
            var interfaceId=typeof(IApplicationActivationManager).GUID;
            IApplicationActivationManager manager;
            // A local COM server owns activation arguments independently of Relay's lifetime.
            int result=CoCreateInstance(ref classId,IntPtr.Zero,4,ref interfaceId,out manager);
            Marshal.ThrowExceptionForHR(result);
            try{
                uint processId;result=manager.ActivateApplication(applicationId,null,2,out processId); // AO_NOERRORUI: Relay reports failure itself.
                Marshal.ThrowExceptionForHR(result);
                if(processId==0)throw new InvalidOperationException("Codex 실행 프로세스를 확인하지 못했습니다.");
                return processId;
            }finally{if(manager!=null)Marshal.ReleaseComObject(manager);}
        }
        public int StartExecutable(string path){
            using(var process=Process.Start(new ProcessStartInfo(path){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(path)})){
                if(process==null)throw new InvalidOperationException("Codex 실행 요청을 전달하지 못했습니다.");
                return process.Id;
            }
        }
    }
    internal static class DesktopLaunch {
        public static void Reopen(string path,string applicationId,IApplicationLauncher launcher,Action<string,string> log=null){
            if(!string.IsNullOrWhiteSpace(applicationId)){
                uint processId=launcher.Activate(applicationId);
                if(log!=null)log("REOPEN_PACKAGE","application_id="+applicationId+"; pid="+processId);
                return;
            }
            if(string.IsNullOrEmpty(path))return;
            if(!Path.IsPathRooted(path)||!string.Equals(Path.GetFullPath(path),path,StringComparison.OrdinalIgnoreCase)||!string.Equals(Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(path))
                throw new InvalidOperationException("Codex 실행 파일을 찾을 수 없습니다. 앱을 직접 실행하세요.");
            if(path.IndexOf("\\WindowsApps\\",StringComparison.OrdinalIgnoreCase)>=0)
                throw new InvalidOperationException("Codex 패키지의 앱 식별자를 확인하지 못했습니다. 앱을 직접 실행하세요.");
            int id=launcher.StartExecutable(path);
            if(log!=null)log("REOPEN_EXECUTABLE","path="+path+"; pid="+id);
        }
    }
}
