using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CodexRelay {
    internal sealed class SwitchTrace : IDisposable {
        readonly Stopwatch elapsed=Stopwatch.StartNew();
        readonly StreamWriter writer;
        public readonly string Path;
        internal SwitchTrace(string directory) {
            string folder=System.IO.Path.Combine(directory,"logs");Directory.CreateDirectory(folder);
            Path=System.IO.Path.Combine(folder,"switch-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".log");
            writer=new StreamWriter(Path,false,new UTF8Encoding(true)){AutoFlush=true};
        }
        internal void Write(string kind,string detail) {
            try{writer.WriteLine(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz")+" +"+elapsed.ElapsedMilliseconds+"ms "+kind+" | "+detail.Replace('\r',' ').Replace('\n',' '));}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        public void Dispose(){try{writer.Dispose();}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    internal sealed class QuitObservation {
        public int Sent,Visible,Blocked;
        internal string Key{get{return Sent+":"+Visible+":"+Blocked;}}
        internal string Message(int remaining,bool delayed){
            if(Blocked>0)return "Codex 종료 확인창을 확인하세요. · 남은 프로세스 "+remaining+"개";
            if(Sent>0&&Visible==0)return "메인 창 닫힘 · 남은 프로세스 정리 중 "+remaining+"개";
            if(Sent>0&&delayed)return "종료 입력 전달됨 · 메인 창 응답 대기 · "+remaining+"개";
            return "Codex 종료 대기 · 남은 프로세스 "+remaining+"개";
        }
    }
    internal interface IQuitObserver { QuitObservation ObserveQuit(); }
}
