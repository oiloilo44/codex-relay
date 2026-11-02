using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexRelay {
    internal sealed class AppProcess {
        public int Id; public string Path; public bool Desktop; public long Started;
        public string PackageFamily="",ApplicationId="";
    }
    internal interface IProcessGuard {
        List<AppProcess> Snapshot();
        Task Close(List<AppProcess> items,bool force,CancellationToken ct,Action<string,string> log);
        void Reopen(string path);
    }
    internal sealed class ProcessGuard : IProcessGuard,IQuitObserver {
        readonly Dictionary<IntPtr,int> quitWindows=new Dictionary<IntPtr,int>();
        string reopenApplicationId;
        internal Action<string,string> DiagnosticLog;
        public QuitObservation ObserveQuit() {
            var result=new QuitObservation{Sent=quitWindows.Count};
            foreach(var pair in quitWindows){int state=QuitWindow.State(pair.Key,pair.Value);if(state>0)result.Visible++;if(state==2)result.Blocked++;}return result;
        }
        public List<AppProcess> Snapshot() {
            var roots=DesktopWindows.FindProcesses();var found=new List<AppProcess>();int session,selfId;
            using(var self=Process.GetCurrentProcess()){session=self.SessionId;selfId=self.Id;}
            var names=new HashSet<string>(roots.Select(p=>Path.GetFileNameWithoutExtension(p.Path)),StringComparer.OrdinalIgnoreCase){"ChatGPT","Codex"};
            var ids=new HashSet<int>();
            foreach(string name in names)foreach(var p in Process.GetProcessesByName(name))using(p){if(p.Id!=selfId&&p.SessionId==session)ids.Add(p.Id);}
            foreach(int id in ids){
                var item=ProcessInspection.Read(id,session,DiagnosticLog);if(item==null)continue;
                string path=item.Path,name=Path.GetFileNameWithoutExtension(path);
                var root=roots.FirstOrDefault(x=>string.Equals(x.Path,path,StringComparison.OrdinalIgnoreCase));
                item.Desktop=root!=null || (name.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase) && (path.IndexOf("OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0 || path.IndexOf("\\Codex\\",StringComparison.OrdinalIgnoreCase)>=0));
                if(!item.Desktop&&!name.Equals("Codex",StringComparison.OrdinalIgnoreCase))continue;
                item.PackageFamily=root==null?"":root.PackageFamily;item.ApplicationId=root==null?"":root.ApplicationId;found.Add(item);
            }
            return found;
        }
        public async Task Close(List<AppProcess> items,bool force,CancellationToken ct,Action<string,string> log) {
            if(force){
                foreach(var item in items)try{using(var p=Process.GetProcessById(item.Id)){
                    ct.ThrowIfCancellationRequested();
                    if(p.StartTime.ToUniversalTime().Ticks!=item.Started||!string.Equals(p.MainModule.FileName,item.Path,StringComparison.OrdinalIgnoreCase))continue;
                    log("FORCE_CLOSE_REQUEST","pid="+item.Id+"; started="+item.Started);p.Kill();
                }}catch(ArgumentException){}catch(InvalidOperationException){}return;
            }
            quitWindows.Clear();reopenApplicationId=null;
            if(!items.Any(p=>p.Desktop))return;
            var selected=await DesktopWindows.FindForSwitch(items,ct,log);
            reopenApplicationId=selected.Process.ApplicationId;
            log("QUIT_PROCESS","pid="+selected.Process.Id+"; started="+selected.Process.Started+"; path="+selected.Process.Path);
            if(!await DesktopQuit.RequestAsync(new QuitWindow(()=>DesktopWindows.Revalidate(selected)),selected.Handle,selected.Process.Id,ct,log))
                throw new QuitRequestException("종료 입력을 전달하지 못했습니다. Codex 창의 포커스와 눌린 키를 확인한 뒤 다시 전환하세요. 전환 로그에 원인을 기록했습니다.");
            quitWindows[selected.Handle]=selected.Process.Id;
        }
        public void Reopen(string path) {
            if(string.IsNullOrEmpty(path))return;
            if(!string.IsNullOrEmpty(reopenApplicationId)){Process.Start(new ProcessStartInfo("explorer.exe","shell:AppsFolder\\"+reopenApplicationId){UseShellExecute=true});return;}
            if(File.Exists(path))Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
        }
    }
    internal sealed class QuitRequestException : InvalidOperationException { public QuitRequestException(string message):base(message){} }
    internal sealed class Engine : IDisposable {
        internal const int GracefulCloseSeconds=60;
        public readonly Vault Vault;
        public readonly string CodexHome;
        public readonly string AuthPath;
        public string ActiveId { get; private set; }
        public string Status { get; private set; }
        public string LastSwitchLog { get; private set; }
        public readonly Api Api;
        readonly IProcessGuard processes;
        readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        readonly bool demo;
        readonly UsagePolling polling=new UsagePolling();
        readonly Func<int,CancellationToken,Task> delay;
        public Engine(Vault vault,string codexHome,Api api,IProcessGuard guard,bool demoMode=false,Func<int,CancellationToken,Task> wait=null) {
            Vault=vault; CodexHome=codexHome; AuthPath=Path.Combine(codexHome,"auth.json"); Api=api; processes=guard; demo=demoMode; delay=wait??Task.Delay; ActiveId=""; Status="준비됨";
        }
        public Account Active { get { return Vault.Data.Accounts.FirstOrDefault(a=>a.Id==ActiveId); } }
        public string LiveRaw() { return File.Exists(AuthPath) ? File.ReadAllText(AuthPath,Encoding.UTF8) : null; }
        public void SyncLive(bool add=false) {
            if(demo) { ActiveId=Vault.Data.Accounts.Count>0?Vault.Data.Accounts[0].Id:""; return; }
            var raw=LiveRaw(); if(raw==null) { ActiveId=""; return; }
            string id=Auth.Identity(raw); ActiveId=id; var a=Active;
            if(a==null && add) Vault.Put(raw,null);
            else if(a!=null && a.AuthJson!=raw) Vault.Put(raw,null);
        }
        public async Task<Account> Add(string raw,string name,string expectedId=null) {
            await gate.WaitAsync(); try {
                if(expectedId!=null && Auth.Identity(raw)!=expectedId)throw new InvalidDataException("선택한 계정과 다른 계정으로 로그인했습니다. 해당 계정으로 다시 로그인하세요.");
                var a=Vault.Put(raw,name); SyncLive(); return a;
            } finally { gate.Release(); }
        }
        async Task EnsureFresh(Account a,CancellationToken ct,bool force=false) {
            SyncLive(); if(!force && !Auth.Expiring(a.AuthJson)) return;
            // Active token ownership stays with Codex. It may rotate single-use refresh tokens.
            if(a.Id==ActiveId && processes.Snapshot().Count>0) return;
            string before=LiveRaw(); string updated=await Api.Refresh(a.AuthJson,ct);
            // Persist the rotated token before any subsequent validation or disk operation.
            a.AuthJson=updated; Vault.Save();
            if(Auth.Identity(updated)!=a.Id) throw new InvalidDataException("갱신된 계정이 일치하지 않아 전환을 중단했습니다.");
            if(a.Id==ActiveId) {
                if(processes.Snapshot().Count>0 || LiveRaw()!=before) throw new InvalidOperationException("인증 갱신 중 Codex 상태가 변경되었습니다. Codex를 종료하고 다시 전환하세요.");
                WriteAuth(updated,before);
            }
        }
        async Task Fetch(Account a,CancellationToken ct) {
            if(a.NeedsLogin)return;
            try {
                await EnsureFresh(a,ct);
                bool retry=false;
                try { a.Usage=await ReadUsage(a,ct); }
                catch(ServiceError e) { if(e.Status!=401 || (a.Id==ActiveId && processes.Snapshot().Count>0)) throw; retry=true; }
                if(retry) { await EnsureFresh(a,ct,true); a.Usage=await ReadUsage(a,ct); }
            } catch(Exception e) {
                if(ct.IsCancellationRequested) throw;
                var service=e as ServiceError;if(service!=null&&service.RequiresLogin)a.NeedsLogin=true;
                a.Usage=a.Usage??new Usage(); a.Usage.Error=Friendly(e); Status=a.Name+": "+a.Usage.Error;
                if(a.NeedsLogin)Vault.Save();
            }
        }
        async Task<Usage> ReadUsage(Account a,CancellationToken ct) {
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)) { timeout.CancelAfter(TimeSpan.FromSeconds(10));return await Api.ReadUsage(a.AuthJson,timeout.Token); }
        }
        async Task<bool> PollOne(Account a,bool force,CancellationToken ct,Action updated) {
            if(!polling.Due(a,a!=null&&a.Id==ActiveId,Clock.Now,force))return false;
            await Fetch(a,ct);polling.Record(a,Clock.Now);if(updated!=null)updated();return true;
        }
        bool ReadyToRotate() { return Vault.Data.Settings.AutoRotate && RotationCandidate()!=null; }
        public async Task<bool> Poll(bool force,CancellationToken ct,Action updated=null) {
            if(demo) return false;
            await gate.WaitAsync(ct);
            bool changed=false;
            try {
                SyncLive();
                changed|=await PollOne(Active,force,ct,updated);
                if(ReadyToRotate())return changed;
                foreach(var a in Vault.Data.Accounts.Where(x=>x.Id!=ActiveId).ToArray()) {
                    ct.ThrowIfCancellationRequested();
                    // Slow standby requests must not postpone every active-account refresh.
                    changed|=await PollOne(Active,false,ct,updated);
                    if(ReadyToRotate())break;
                    changed|=await PollOne(a,force,ct,updated);
                    if(ReadyToRotate())break;
                }
                return changed;
            } finally { try{if(changed)Vault.Save();}finally{gate.Release();} }
        }
        public async Task WarmDue(CancellationToken ct) {
            if(demo || !Vault.Data.Settings.AutoWarm) return;
            await gate.WaitAsync(ct);
            try {
                foreach(var a in Vault.Data.Accounts.ToArray()) {
                    if(Active!=null && UsagePolling.NearLimit(Active.Usage))break;
                    if(!Vault.Data.Settings.AutoWarm || !Rotation.WarmDue(a,Clock.Now)) continue;
                    string key=Rotation.WarmKey(a.Usage); a.LastWarmAt=Clock.Now; a.LastWarmKey=key; a.WarmStatus="요청 중"; Vault.Save();
                    try {
                        await EnsureFresh(a,ct); await Api.Warm(a.AuthJson,ct); a.WarmStatus="완료";
                        await Fetch(a,ct); if(a.Usage!=null && a.Usage.Fresh(Clock.Now)) a.LastWarmKey=Rotation.WarmKey(a.Usage);
                    } catch(Exception e) { a.WarmStatus=Friendly(e); }
                    Vault.Save(); ct.ThrowIfCancellationRequested();
                }
            } finally { gate.Release(); }
        }
        public Account RotationCandidate() {
            var active=Active; return active!=null && !active.NeedsLogin && active.Usage!=null && active.Usage.Fresh(Clock.Now) && !active.Usage.ExpiredWindow(Clock.Now) && active.Usage.Exhausted ? Rotation.Next(Vault.Data.Accounts,ActiveId,Clock.Now) : null;
        }
        static string ProcessSummary(List<AppProcess> items){return string.Join("; ",items.Select(p=>"pid="+p.Id+",start="+p.Started+",name="+Path.GetFileName(p.Path??"")));}
        public async Task<string> Switch(Account target,Func<List<AppProcess>,bool> consent,Func<List<AppProcess>,bool> forceConsent,CancellationToken ct,bool automatic=false,Action<string> progress=null) {
            if(demo) return "미리보기에서는 실제 계정을 전환하지 않습니다.";
            await gate.WaitAsync(ct);
            SwitchTrace trace=null;
            var nativeProcesses=processes as ProcessGuard;
            try {
                try{trace=new SwitchTrace(Vault.DirectoryPath);LastSwitchLog=trace.Path;}catch(IOException){throw new InvalidOperationException("전환 로그를 만들 수 없어 전환을 중단했습니다.");}
                if(nativeProcesses!=null)nativeProcesses.DiagnosticLog=trace.Write;
                trace.Write("SWITCH_BEGIN","automatic="+automatic+"; version="+typeof(Engine).Assembly.GetName().Version);
                if(target==null || !Vault.Data.Accounts.Contains(target)) return "계정을 선택하세요.";
                ValidateStoreMode(target); SyncLive();
                if(target.Id==ActiveId) return "이미 사용 중인 계정입니다.";
                if(Auth.Identity(target.AuthJson)!=target.Id)throw new InvalidDataException("저장된 계정의 인증 정보가 일치하지 않습니다. 다시 로그인하세요.");
                // Manual selection may use credits even when the included quota is exhausted.
                await Fetch(target,ct);
                if(target.NeedsLogin)return "선택 계정은 다시 로그인해야 합니다. 계정 메뉴 → 다시 로그인을 선택하세요.";
                if(automatic && (target.Usage==null || !target.Usage.Available(Clock.Now))) return "선택 계정의 사용 가능한 한도를 확인하지 못했습니다. 사용량을 새로고침하세요.";
                if(!automatic)await EnsureFresh(target,ct);
                if(Auth.Identity(target.AuthJson)!=target.Id)throw new InvalidDataException("갱신된 계정이 일치하지 않아 전환을 중단했습니다. 다시 로그인하세요.");
                var running=processes.Snapshot(); string reopen=running.Where(p=>p.Desktop).Select(p=>p.Path).FirstOrDefault();
                trace.Write("BEFORE_CLOSE",ProcessSummary(running));
                if(running.Count>0) {
                    if(!consent(running)){trace.Write("CLOSE_DECLINED","authentication unchanged");return "전환을 보류했습니다. 현재 계정을 유지합니다.";}
                    ct.ThrowIfCancellationRequested();
                    trace.Write("CLOSE_APPROVED","graceful_wait_seconds="+GracefulCloseSeconds);
                    await processes.Close(running,false,ct,trace.Write);
                    string previous="";var observer=processes as IQuitObserver;
                    for(int i=0;i<GracefulCloseSeconds*2;i++) {
                        var pending=processes.Snapshot();if(pending.Count==0)break;
                        var observed=observer==null?new QuitObservation():observer.ObserveQuit();
                        string current=observed.Key+"; "+ProcessSummary(pending);
                        if(current!=previous){
                            trace.Write("SHUTDOWN_OBSERVED","sent_windows="+observed.Sent+"; visible_windows="+observed.Visible+"; blocked_windows="+observed.Blocked+"; "+ProcessSummary(pending));
                            if(observed.Sent>0&&observed.Visible==0)trace.Write("MAIN_WINDOW_CLOSED","window hidden or destroyed; waiting for all Codex processes to exit");
                            previous=current;
                        }
                        if(i==6&&observed.Sent>0&&observed.Visible>0)trace.Write("MAIN_WINDOW_STILL_OPEN","input sent but application quit response not confirmed; blocked_windows="+observed.Blocked);
                        if(progress!=null)progress(observed.Message(pending.Count,i>=6));
                        await delay(500,ct);
                    }
                    var remaining=processes.Snapshot();
                    if(remaining.Count>0) {
                        trace.Write("GRACEFUL_TIMEOUT",ProcessSummary(remaining));
                        // Never terminate a newly opened process using an earlier approval.
                        if(remaining.Any(p=>!running.Any(x=>x.Id==p.Id && x.Started==p.Started))) return "새 Codex 프로세스가 실행되어 전환을 보류했습니다.";
                        if(!forceConsent(remaining)){trace.Write("FORCE_CLOSE_DECLINED","authentication unchanged");return "Codex가 실행 중이므로 전환하지 않았습니다.";}
                        ct.ThrowIfCancellationRequested();
                        trace.Write("FORCE_CLOSE_APPROVED",ProcessSummary(remaining));
                        await processes.Close(remaining,true,ct,trace.Write);
                        for(int i=0;i<12 && processes.Snapshot().Count>0;i++) await delay(500,ct);
                    }
                }
                if(processes.Snapshot().Count>0) return "Codex 종료를 확인하지 못해 전환하지 않았습니다.";
                trace.Write("ALL_CODEX_PROCESSES_EXITED","authentication not yet changed");
                ct.ThrowIfCancellationRequested();
                SyncLive(true); string before=LiveRaw();
                if(processes.Snapshot().Count>0) return "Codex가 다시 실행되어 전환하지 않았습니다.";
                WriteAuth(target.AuthJson,before); ActiveId=target.Id;
                trace.Write("AUTH_REPLACED","target login activated; credentials are not logged");
                bool paused=!automatic && Vault.Data.Settings.AutoRotate && (target.Usage==null || !target.Usage.Available(Clock.Now));
                if(paused){Vault.Data.Settings.AutoRotate=false;Vault.Save();}
                try { processes.Reopen(reopen);trace.Write("REOPEN_REQUESTED",reopen??"no desktop to reopen"); } catch {trace.Write("REOPEN_FAILED","launch manually");return "계정 전환 완료. Codex 앱을 직접 실행하세요."; }
                return "계정 전환 완료: "+target.Name+(paused?" · 선택 계정 유지: 자동 전환 OFF":"");
            } catch(Exception ex){if(trace!=null)trace.Write("SWITCH_FAILED",ex.GetType().Name+": "+Friendly(ex));throw;}
            finally {try{if(nativeProcesses!=null)nativeProcesses.DiagnosticLog=null;if(trace!=null){trace.Write("SWITCH_END","operation ended");trace.Dispose();}}finally{gate.Release();} }
        }
        internal void WriteAuth(string raw,string expected) {
            Auth.Identity(raw);
            if(LiveRaw()!=expected) throw new InvalidOperationException("인증 파일이 변경되어 덮어쓰지 않았습니다.");
            Vault.Data.BackupAuth=expected??""; Vault.Save();
            Directory.CreateDirectory(CodexHome);
            if(LiveRaw()!=expected) throw new InvalidOperationException("인증 파일이 변경되어 덮어쓰지 않았습니다.");
            Atomic.Write(AuthPath,Encoding.UTF8.GetBytes(raw));
            if(LiveRaw()!=raw) throw new IOException("인증 파일 저장 확인에 실패했습니다. 보관함의 이전 인증 정보가 유지됩니다.");
        }
        void ValidateStoreMode(Account target) {
            string config=Path.Combine(CodexHome,"config.toml"); if(!File.Exists(config)) return;
            string text=File.ReadAllText(config);
            var match=Regex.Match(text,"(?m)^\\s*cli_auth_credentials_store\\s*=\\s*[\"']([^\"']+)[\"']");
            if(match.Success && match.Groups[1].Value!="file") throw new InvalidOperationException("현재 Codex는 파일 인증 저장 방식을 사용하지 않습니다. cli_auth_credentials_store = \"file\" 설정이 필요합니다.");
            match=Regex.Match(text,"(?m)^\\s*forced_login_method\\s*=\\s*[\"']([^\"']+)[\"']");
            if(match.Success&&match.Groups[1].Value=="api")throw new InvalidOperationException("Codex 설정이 API 키 로그인을 요구하므로 ChatGPT 계정으로 전환할 수 없습니다.");
            match=Regex.Match(text,"(?m)^\\s*forced_chatgpt_workspace_id\\s*=\\s*[\"']([^\"']+)[\"']");
            if(match.Success&&match.Groups[1].Value!=Auth.AccountId(target.AuthJson))throw new InvalidOperationException("선택 계정이 Codex 설정에 지정된 워크스페이스와 다릅니다.");
        }
        public static string Friendly(Exception e) {
            if(e is ServiceError || e is InvalidDataException || e is InvalidOperationException) return e.Message;
            if(e is OperationCanceledException) return "요청 시간이 초과되었거나 취소되었습니다.";
            if(e is System.Net.Http.HttpRequestException) return "서버에 연결하지 못했습니다.";
            if(e is UnauthorizedAccessException) return "파일 접근 권한이 없습니다.";
            return "작업 실패 ("+e.GetType().Name+")";
        }
        public void Dispose() { Api.Dispose(); }
    }
}
