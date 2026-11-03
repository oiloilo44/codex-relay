using System;
using System.Collections.Generic;
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
    internal sealed class StartupPathChange {
        public readonly string RegisteredCommand,CurrentExecutable,CurrentCommand;
        public StartupPathChange(string registered,string executable,string command){RegisteredCommand=registered;CurrentExecutable=executable;CurrentCommand=command;}
        internal string Key {get{return RegisteredCommand.Trim()+"\n"+CurrentCommand;}}
    }
    internal sealed class StartupRegistration {
        readonly IStartupStore store;
        readonly string executable;
        public StartupRegistration(IStartupStore store,string executable){this.store=store;this.executable=Path.GetFullPath(executable);}
        public string Command {get{return "\""+executable+"\" --tray";}}
        public bool IsEnabled {get{return !string.IsNullOrWhiteSpace(store.Read());}}
        public bool CanChangePath {get{string registered=store.Read();return !string.IsNullOrWhiteSpace(registered)&&!Matches(registered,Command);}}
        static bool Matches(string left,string right){return string.Equals((left??"").Trim(),(right??"").Trim(),StringComparison.OrdinalIgnoreCase);}
        public void SetEnabled(bool enabled){
            if(enabled){if(!File.Exists(executable))throw new InvalidOperationException("현재 실행 파일을 찾을 수 없어 시작프로그램을 등록하지 못했습니다.");if(Command.Length>260)throw new InvalidOperationException("실행 파일 경로가 너무 깁니다. 더 짧은 경로에서 실행한 뒤 등록하세요.");store.Write(Command);if(store.Read()!=Command)throw new InvalidOperationException("시작프로그램 등록 상태를 확인하지 못했습니다.");}
            else {store.Remove();if(IsEnabled)throw new InvalidOperationException("시작프로그램 해제 상태를 확인하지 못했습니다.");}
        }
        public bool Reconcile(Settings settings,Action save,Func<StartupPathChange,bool> confirm=null){
            string registered=store.Read();
            bool enabled=!string.IsNullOrWhiteSpace(registered);
            // Reading startup state must not recreate, remove or relocate a Run entry.
            if(settings.StartWithWindows!=enabled){
                bool? previous=settings.StartWithWindows;settings.StartWithWindows=enabled;
                try{save();}catch{settings.StartWithWindows=previous;throw;}
            }
            return ChangePath(settings,save,confirm,false);
        }
        public bool ChangePath(Settings settings,Action save,Func<StartupPathChange,bool> confirm,bool askAgain=true){
            string registered=store.Read();
            if(string.IsNullOrWhiteSpace(registered)||Matches(registered,Command)||confirm==null)return false;
            var change=new StartupPathChange(registered,executable,Command);
            var declined=settings.DeclinedStartupChanges??new List<string>();
            if(!askAgain&&declined.Exists(x=>Matches(x,change.Key)))return false;
            bool approved=confirm(change);
            if(store.Read()!=registered)throw new InvalidOperationException("확인 중 시작프로그램 등록이 바뀌었습니다. 메뉴에서 등록 상태를 다시 확인하세요.");
            if(approved){SetPreference(settings,true,save,registered);return true;}
            if(!declined.Exists(x=>Matches(x,change.Key))){
                var previous=settings.DeclinedStartupChanges;
                settings.DeclinedStartupChanges=new List<string>(declined){change.Key};
                try{save();}catch{settings.DeclinedStartupChanges=previous;throw;}
            }
            return false;
        }
        public void SetPreference(Settings settings,bool enabled,Action save,string expectedCommand=null){
            string previousCommand=store.Read();bool? previousPreference=settings.StartWithWindows;
            var previousDeclined=settings.DeclinedStartupChanges;
            if(expectedCommand!=null&&previousCommand!=expectedCommand)throw new InvalidOperationException("시작프로그램 등록이 바뀌어 변경하지 않았습니다. 다시 확인하세요.");
            try{
                SetEnabled(enabled);
                settings.StartWithWindows=enabled;
                settings.DeclinedStartupChanges=new List<string>(previousDeclined??new List<string>());
                if(enabled&&!string.IsNullOrWhiteSpace(previousCommand))settings.DeclinedStartupChanges.RemoveAll(x=>Matches(x,previousCommand.Trim()+"\n"+Command));
                save();
            }catch(Exception changeError){
                settings.StartWithWindows=previousPreference;settings.DeclinedStartupChanges=previousDeclined;
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
