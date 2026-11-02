using System;
using System.IO;
using Microsoft.Win32;

namespace CodexRelay {
    internal interface IStartupStore {
        string Read();
        void Write(string command);
        void Remove();
    }
    internal sealed class WindowsStartupStore : IStartupStore {
        const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        const string EntryName="CodexRelay";
        public string Read(){using(var key=Registry.CurrentUser.OpenSubKey(RunKey,false))return key==null?null:key.GetValue(EntryName,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;}
        public void Write(string command){using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(key==null)throw new InvalidOperationException("시작프로그램 설정을 열 수 없습니다.");key.SetValue(EntryName,command,RegistryValueKind.String);}}
        public void Remove(){using(var key=Registry.CurrentUser.OpenSubKey(RunKey,true)){if(key!=null)key.DeleteValue(EntryName,false);}}
    }
    internal sealed class StartupRegistration {
        readonly IStartupStore store;
        readonly string executable;
        public StartupRegistration(IStartupStore store,string executable){this.store=store;this.executable=Path.GetFullPath(executable);}
        public string Command {get{return "\""+executable+"\" --tray";}}
        public bool IsEnabled {get{return !string.IsNullOrWhiteSpace(store.Read());}}
        public void SetEnabled(bool enabled){
            if(enabled){if(!File.Exists(executable))throw new InvalidOperationException("현재 실행 파일을 찾을 수 없어 시작프로그램을 등록하지 못했습니다.");if(Command.Length>260)throw new InvalidOperationException("실행 파일 경로가 너무 깁니다. 더 짧은 경로에서 실행한 뒤 등록하세요.");store.Write(Command);if(store.Read()!=Command)throw new InvalidOperationException("시작프로그램 등록 상태를 확인하지 못했습니다.");}
            else {store.Remove();if(IsEnabled)throw new InvalidOperationException("시작프로그램 해제 상태를 확인하지 못했습니다.");}
        }
        public bool Reconcile(Settings settings,Action save){
            string registered=store.Read();
            if(!settings.StartWithWindows.HasValue){
                settings.StartWithWindows=!string.IsNullOrWhiteSpace(registered);
                try{save();}catch{settings.StartWithWindows=null;throw;}
            }
            bool enabled=settings.StartWithWindows.Value;
            if(enabled?registered==Command:string.IsNullOrWhiteSpace(registered))return false;
            SetEnabled(enabled);return true;
        }
        public void SetPreference(Settings settings,bool enabled,Action save){
            string previousCommand=store.Read();bool? previousPreference=settings.StartWithWindows;
            try{
                SetEnabled(enabled);
                settings.StartWithWindows=enabled;save();
            }catch(Exception changeError){
                settings.StartWithWindows=previousPreference;
                try{
                    if(store.Read()!=previousCommand){
                        if(previousCommand==null)store.Remove();else store.Write(previousCommand);
                        if(store.Read()!=previousCommand)throw new InvalidOperationException("이전 시작프로그램 등록 복원 실패");
                    }
                }catch(Exception restoreError){throw new InvalidOperationException("시작프로그램 설정 변경과 이전 등록 복원에 실패했습니다. 등록 상태를 확인하세요.",new AggregateException(changeError,restoreError));}
                throw;
            }
        }
    }
}
