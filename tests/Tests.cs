using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Drawing;
using System.Windows.Forms;

namespace CodexRelay {
    internal sealed class FakeQuitWindow : IQuitWindow {
        public bool Target=true,Activated=true,Foreground=true,Released=true,Sent=true;
        public Action OnActivate;
        public int Activations,Sends;
        public bool IsTarget(IntPtr window,int processId){return Target;}
        public bool Activate(IntPtr window){Activations++;if(OnActivate!=null)OnActivate();return Activated;}
        public bool IsForeground(IntPtr window){return Foreground;}
        public bool KeysReleased(){return Released;}
        public bool SendQuit(){Sends++;return Sent;}
    }
    internal sealed class FakeStartupStore : IStartupStore {
        public string Value;
        public bool IgnoreWrites;
        public int Writes,Removes;
        public string Read(){return Value;}
        public void Write(string command){Writes++;if(!IgnoreWrites)Value=command;}
        public void Remove(){Removes++;if(!IgnoreWrites)Value=null;}
    }
    internal sealed class FakeGuard : IProcessGuard,IQuitObserver {
        public List<AppProcess> Items=new List<AppProcess>();
        public int Closed,Opened;
        public Action<List<AppProcess>,bool> OnClose;
        public Func<List<AppProcess>> OnSnapshot;
        public QuitObservation Observation=new QuitObservation();
        public QuitObservation ObserveQuit(){return Observation;}
        public List<AppProcess> Snapshot(){return OnSnapshot==null?Items.ToList():OnSnapshot();}
        public Task Close(List<AppProcess> x,bool force,CancellationToken ct,Action<string,string> log){ct.ThrowIfCancellationRequested();Closed++;if(OnClose!=null)OnClose(x,force);else Items.Clear();return Task.FromResult(0);}
        public void Reopen(string path){if(path!=null)Opened++;}
    }
    internal sealed class FakeHttp : HttpMessageHandler {
        public Func<HttpRequestMessage,HttpResponseMessage> Handler;
        public List<string> Paths=new List<string>();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct) { Paths.Add(r.RequestUri.AbsolutePath); return Task.FromResult(Handler(r)); }
    }
    internal static partial class Tests {
        static int count;
        static string root;
        static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
        internal static string Raw(string sub,string account="workspace") {
            string claim=LoginFlow.B64(Encoding.UTF8.GetBytes(Json.Write(new Dictionary<string,object>{{"sub",sub},{"email",sub+"@example.invalid"},{"exp",Clock.Now+7200},{"https://api.openai.com/auth",new{chatgpt_account_id=account,chatgpt_plan_type="plus"}}})));
            string token="test."+claim+".signature";return Json.Write(new{tokens=new{id_token=token,access_token=token,refresh_token="test-refresh-"+sub,account_id=account},last_refresh=DateTime.UtcNow.ToString("o")});
        }
        static string Limits(double five,double week,long now) {return Json.Write(new{rate_limit=new{allowed=five<100&&week<100,limit_reached=five>=100||week>=100,primary_window=new{used_percent=five,limit_window_seconds=18000,reset_at=now+3600},secondary_window=new{used_percent=week,limit_window_seconds=604800,reset_at=now+600000}}});}
        static HttpResponseMessage Response(string body,int code=200){return new HttpResponseMessage((HttpStatusCode)code){Content=new StringContent(body)};}
        [STAThread] static void Main(string[] args) {
            if(args.Length==2&&args[0]=="--process-exit-fixture"){using(var signal=EventWaitHandle.OpenExisting(args[1]))signal.WaitOne(15000);return;}
            root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(root);
            if(args.Contains("--telemetry-readonly")){try{TelemetryReadOnly();}catch(Exception e){Console.Error.WriteLine("FAIL "+e);Environment.ExitCode=1;}return;}
            if(args.Contains("--window-readonly")){try{WindowReadOnly().GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine("FAIL "+e);Environment.ExitCode=1;}return;}
            if(args.Contains("--overlay-window-check")){try{OverlayWindowChecks();Console.WriteLine("TOTAL "+count+" OVERLAY WINDOW CHECKS PASSED");}catch(Exception e){Console.Error.WriteLine("FAIL "+e);Environment.ExitCode=1;}return;}
            try{if(args.Contains("--render-hidden")){HiddenRender();Console.WriteLine("TOTAL "+count+" HIDDEN UI CHECKS PASSED");return;}if(args.Contains("--ui-render")){UiRender();Console.WriteLine("TOTAL "+count+" UI CHECKS PASSED");return;}if(args.Contains("--live-readonly")||args.Contains("--live-warmup-once")){LiveReadOnly(args.Contains("--live-warmup-once")).GetAwaiter().GetResult();return;}Run().GetAwaiter().GetResult(); Console.WriteLine("TOTAL "+count+" PASSED");}catch(Exception e){Console.Error.WriteLine("FAIL "+e);Environment.ExitCode=1;}
        }
        static async Task LiveReadOnly(bool warmOnce) {
            string home=Environment.GetEnvironmentVariable("CODEX_HOME");if(string.IsNullOrWhiteSpace(home))home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
            string raw=File.ReadAllText(Path.Combine(home,"auth.json"));Auth.Identity(raw);
            using(var api=new Api())using(var ct=new CancellationTokenSource(45000)) {var usage=await api.ReadUsage(raw,ct.Token);Console.WriteLine("Live usage: five="+(usage.FiveHour==null?"unavailable":usage.FiveHour.Display)+", weekly="+(usage.Weekly==null?"unavailable":usage.Weekly.Display)+", fresh="+usage.Fresh(Clock.Now));if(warmOnce){if(!usage.Available(Clock.Now))throw new Exception("Warm preflight unavailable");await api.Warm(raw,ct.Token);Console.WriteLine("Live warm-up: response.completed received (one request)");}}
            using(var telemetry=new Telemetry(home)){telemetry.Start();var m=telemetry.Read();Console.WriteLine("Live telemetry: active="+m.ActiveCount+", measured="+m.MeasuredCount+", pending="+m.PendingFiles+", tps="+(!m.Tps.HasValue?"unavailable":m.Tps.Value.ToString("0.00")));}
            Console.WriteLine("Detected Codex processes: "+new ProcessGuard().Snapshot().Count);
            Console.WriteLine("Auth unchanged: "+(File.ReadAllText(Path.Combine(home,"auth.json"))==raw));
        }
        static async Task Run() {
            long now=Clock.Now;
            StartupChecks();
            QuitRequestChecks();
            WindowSelectionChecks();
            await WindowRetryChecks();
            ProcessInspectionChecks();
            NativeProcessExitChecks();
            await ProcessExitSwitchChecks();
            await AsyncQuitChecks();
            await ShutdownChecks();
            PollScheduleChecks();
            await ManualQuotaAndLoginChecks();
            await PollPriorityChecks();
            Check(Json.Read<Settings>("{\"Overlay\":true}").OverlayTransparency==50,"legacy settings preserve 50 percent transparency");
            Check(new Settings{OverlayTransparency=-5}.OverlayOpacity==1,"transparency lower bound stays opaque");
            Check(Math.Abs(new Settings{OverlayTransparency=100}.OverlayOpacity-.1)<.00001,"transparency upper bound keeps overlay visible");
            Check(Json.Read<Settings>(Json.Write(new Settings{OverlayTransparency=37})).OverlayTransparency==37,"transparency survives settings serialization");
            var normal=Usage.Parse(Limits(99,20,now),now);Check(normal.Available(now),"99 percent remains usable");
            Check(normal.FiveHour.Remaining==1&&normal.Weekly.RemainingDisplay=="80%","display converts used to remaining without changing quota logic");
            Check(new WindowUsage{Used=105}.Remaining==0&&new WindowUsage{Used=-4}.Remaining==100,"remaining percentage clamped");
            Check(Usage.Parse(Limits(100,20,now),now).Exhausted,"five hour threshold");Check(Usage.Parse(Limits(10,100,now),now).Exhausted,"weekly threshold");
            var weekly=Usage.Parse(Json.Write(new{rate_limit=new{allowed=true,primary_window=new{used_percent=73,limit_window_seconds=604800,reset_at=now+1000}}}),now);
            Check(!QuotaLabels.ShowFiveHour(weekly)&&QuotaLabels.ShowWeekly(weekly),"weekly-only quota omits unavailable five-hour period");
            Check(QuotaLabels.ShowFiveHour(null)&&QuotaLabels.ShowWeekly(null),"unknown quota is not treated as unsupported period");
            Check(weekly.FiveHour==null&&weekly.Weekly.Used==73&&weekly.Available(now),"weekly promoted to primary is not five hour");
            var missing=Usage.Parse("{}",now);Check(!missing.Available(now)&&missing.FiveHour==null,"missing limits are unknown");
            var reversed=Usage.Parse(Json.Write(new{rate_limit=new{allowed=true,primary_window=new{used_percent=50,limit_window_seconds=604800,reset_at=now+5000},secondary_window=new{used_percent=10,limit_window_seconds=18000,reset_at=now+2000}}}),now);Check(reversed.FiveHour.Used==10&&reversed.Weekly.Used==50,"reversed windows retain their real durations");
            Check(!normal.Available(now+151),"stale data cannot trigger rotation");normal.FiveHour.ResetAt=now;Check(!normal.Available(now),"expired quota window needs refetch");
            var list=new List<Account>{new Account{Id="a",Usage=Usage.Parse(Limits(100,20,now),now)},new Account{Id="b",Usage=Usage.Parse(Limits(20,100,now),now)},new Account{Id="c",Usage=Usage.Parse(Limits(20,20,now),now)}};
            Check(Rotation.Next(list,"a",now).Id=="c","rotation skips exhausted account"); list[0].Usage=Usage.Parse(Limits(1,1,now),now); Check(Rotation.Next(list,"c",now).Id=="a","rotation wraps in configured order");
            list[0].Usage.Error="offline";Check(Rotation.Next(list,"c",now)==null,"no usable account holds current");
            var warm=new Account{Usage=Usage.Parse(Limits(0,1,now),now)};Check(Rotation.WarmDue(warm,now),"warm once in initial window");warm.LastWarmKey=Rotation.WarmKey(warm.Usage);warm.LastWarmAt=now;Check(!Rotation.WarmDue(warm,now+301),"warm deduplicated per window");warm.Usage.FiveHour.ResetAt+=18000;warm.Usage.FetchedAt=now+301;Check(Rotation.WarmDue(warm,now+301),"warm new quota window");
            Check(Auth.Identity(Raw("alice"))!=Auth.Identity(Raw("bob")),"same workspace users remain distinct");
            var vault=new Vault(Path.Combine(root,"vault"));var a=vault.Put(Raw("alice"),"A");var b=vault.Put(Raw("bob"),"B");
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(vault.FilePath)).Contains("test-refresh"),"vault contains no plaintext auth");
            var loaded=new Vault(vault.DirectoryPath);Check(loaded.Data.Accounts.Count==2&&loaded.Data.Accounts[0].AuthJson==a.AuthJson,"DPAPI vault roundtrip");Check(File.Exists(vault.FilePath+".bak"),"encrypted recovery backup");
            var noId=Json.Parse(a.AuthJson);Json.Obj(Json.Get(noId,"tokens")).Remove("account_id");Check(Auth.AccountId(Json.Write(noId))=="workspace","account ID falls back to identity token");
            string merged=Auth.FromResponse(Json.Write(new{access_token=Auth.Access(Raw("alice")),refresh_token="rotated"}),a.AuthJson);Check(Json.Str(Auth.Tokens(merged),"refresh_token")=="rotated"&&Auth.Identity(merged)==a.Id,"refresh preserves omitted id token");
            Check(!LoginFlow.ValidState("expected","different")&&LoginFlow.ValidState("expected","expected"),"OAuth state validation");
            var http=new FakeHttp{Handler=r=>Response(Limits(10,20,now))};var guard=new FakeGuard();var home=Path.Combine(root,"codex");Directory.CreateDirectory(home);File.WriteAllText(Path.Combine(home,"auth.json"),a.AuthJson);
            using(var engine=new Engine(vault,home,new Api(http),guard)) {
                engine.SyncLive();string original=engine.LiveRaw();guard.Items.Add(new AppProcess{Id=17,Desktop=true,Path="fake.exe",Started=1});
                bool asked=false;string message=await engine.Switch(b,x=>{asked=true;return false;},x=>false,CancellationToken.None);
                Check(asked&&guard.Closed==0&&engine.LiveRaw()==original,"declined closure leaves process and auth unchanged");
                message=await engine.Switch(b,x=>true,x=>false,CancellationToken.None);Check(guard.Closed==1&&engine.ActiveId==b.Id&&Auth.Identity(engine.LiveRaw())==b.Id,"approved closure then switch");Check(guard.Opened==1,"desktop reopened after switching");Check(vault.Data.BackupAuth==original,"previous auth backed up before replacement");
                // Live file token rotation must be retained when leaving an account.
                string updated=Auth.FromResponse(Json.Write(new{access_token=Auth.Access(Raw("bob")),refresh_token="new-live-refresh"}),b.AuthJson);File.WriteAllText(engine.AuthPath,updated);
                await engine.Switch(a,x=>true,x=>true,CancellationToken.None);Check(b.AuthJson==updated,"switch captures latest live refresh token");
                bool conflict=false;try{engine.WriteAuth(b.AuthJson,"stale-content");}catch(InvalidOperationException){conflict=true;}Check(conflict&&Auth.Identity(engine.LiveRaw())==a.Id,"concurrent auth change not overwritten");
                File.WriteAllText(Path.Combine(home,"config.toml"),"cli_auth_credentials_store = \"keyring\"");bool blocked=false;try{await engine.Switch(b,x=>true,x=>true,CancellationToken.None);}catch(InvalidOperationException){blocked=true;}Check(blocked,"keyring mode fails closed");
                File.WriteAllText(Path.Combine(home,"config.toml"),"forced_login_method = \"api\"");blocked=false;try{await engine.Switch(b,x=>true,x=>true,CancellationToken.None);}catch(InvalidOperationException){blocked=true;}Check(blocked,"forced API mode respected");
                File.WriteAllText(Path.Combine(home,"config.toml"),"forced_chatgpt_workspace_id = \"different\"");blocked=false;try{await engine.Switch(b,x=>true,x=>true,CancellationToken.None);}catch(InvalidOperationException){blocked=true;}Check(blocked,"forced workspace respected");
            }
            // A real loopback callback goes through parser + state + PKCE exchange with a fake remote server.
            string exchanged="";var oauth=new FakeHttp{Handler=r=>{exchanged=r.Content.ReadAsStringAsync().Result;return Response(Json.Write(Auth.Tokens(Raw("oauth"))));}};
            using(var api=new Api(oauth))using(var flow=new LoginFlow())using(var ct=new CancellationTokenSource(10000))using(var local=new HttpClient(new HttpClientHandler{UseProxy=false})) {
                var query=HttpUtility.ParseQueryString(new Uri(flow.Url).Query);var complete=flow.Complete(api,ct.Token);
                var bad=await local.GetAsync("http://127.0.0.1:1455/auth/callback?code=bad&state=wrong");Check((int)bad.StatusCode==400&&!complete.IsCompleted,"invalid callback rejected without consuming login");
                var good=await local.GetAsync("http://127.0.0.1:1455/auth/callback?code=test-code&state="+Uri.EscapeDataString(query["state"]));string logged=await complete;
                var exchange=HttpUtility.ParseQueryString(exchanged);string challenge;using(var sha=System.Security.Cryptography.SHA256.Create())challenge=LoginFlow.B64(sha.ComputeHash(Encoding.ASCII.GetBytes(exchange["code_verifier"])));
                Check((int)good.StatusCode==200&&Auth.Identity(logged)==Auth.Identity(Raw("oauth")),"OAuth loopback success");Check(challenge==query["code_challenge"]&&exchange["code"]=="test-code","PKCE verifier matches authorization challenge");
            }
            var sse=new FakeHttp{Handler=r=>Response("data: {\"type\":\"response.failed\"}\n\n")};using(var api=new Api(sse)){bool failed=false;try{await api.Warm(Raw("w"),CancellationToken.None);}catch(InvalidDataException){failed=true;}Check(failed,"warm SSE failure is not success");}
            sse=new FakeHttp{Handler=r=>Response("data: {\"type\":\"response.completed\"}\n\ndata: [DONE]\n")};using(var api=new Api(sse)){await api.Warm(Raw("w"),CancellationToken.None);Check(true,"warm completion acknowledged");}
            var noWarm=new FakeHttp{Handler=r=>{throw new Exception("warmup off must send no request");}};using(var engine=new Engine(vault,home,new Api(noWarm),new FakeGuard())){vault.Data.Settings.AutoWarm=false;await engine.WarmDue(CancellationToken.None);Check(noWarm.Paths.Count==0,"warmup OFF emits no traffic");}
            using(var flow=new LoginFlow())using(var api=new Api(new FakeHttp()))using(var c=new CancellationTokenSource(100)) {bool cancelled=false;try{await flow.Complete(api,c.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"cancel releases OAuth listener");}
            var meter=new TurnMeter();meter.Accept(Json.Write(new{type="session_meta",payload=new{originator="Codex Desktop",creator_account_id="workspace",id="test"}}));
            var started=DateTime.UtcNow;meter.Accept(Event("task_started",started,null));var usage=new{total_token_usage=new{output_tokens=100},last_token_usage=new{output_tokens=100}};meter.Accept(Event("token_count",started.AddSeconds(2),usage));meter.Accept(Event("token_count",started.AddSeconds(2),usage));Check(meter.Tokens==100&&meter.Tps==50,"TPS uses output tokens without duplicate counts");
            meter.Accept(Event("task_complete",started.AddSeconds(4),null));Check(!meter.Running&&meter.Tps==25,"completed turn average includes elapsed time");
            var cli=new TurnMeter();cli.Accept(Json.Write(new{type="session_meta",payload=new{originator="codex_cli_rs"}}));cli.Accept(Event("task_started",started,null));Check(!cli.HasStart,"CLI sessions excluded from desktop TPS");
            await MultiSessionTelemetry();
            await InterruptedSessionTelemetry();
            await LongSessionTelemetry();
            Check(!new Settings().AutoWarm&&!new Settings().AutoRotate&&new Settings().Overlay&&new Settings().DarkMode,"automation defaults off and overlay plus dark mode default on");
        }
        static string MeterLog(string id,long tokens,double seconds,DateTime updated,string origin="Codex Desktop"){
            return Json.Write(new{type="session_meta",payload=new{originator=origin,id=id}})+"\n"+Event("task_started",updated.AddSeconds(-seconds),null)+"\n"+Event("token_count",updated,new{total_token_usage=new{output_tokens=tokens},last_token_usage=new{output_tokens=tokens}})+"\n";
        }
        static TurnMeter Meter(string id,long tokens,double seconds,DateTime updated,string origin="Codex Desktop"){var meter=new TurnMeter();foreach(var line in MeterLog(id,tokens,seconds,updated,origin).Split('\n'))if(line.Length>0)meter.Accept(line);return meter;}
        static async Task MultiSessionTelemetry(){
            var now=DateTime.UtcNow;var a=Meter("mean-a",100,5,now);var b=Meter("mean-b",800,20,now);
            var summary=TpsSummary.Aggregate(new[]{a,b},now);Check(summary.ActiveCount==2&&summary.MeasuredCount==2&&summary.Tps==30,"two unequal-duration sessions use arithmetic TPS average");
            var pending=Meter("pending",0,2,now);summary=TpsSummary.Aggregate(new[]{a,b,pending},now);Check(summary.ActiveCount==3&&summary.MeasuredCount==2&&summary.Tps==30,"unmeasured running session does not dilute the average");
            var complete=Meter("complete",900,1,now);complete.Accept(Event("task_complete",now,null));var aborted=Meter("aborted",700,1,now);aborted.Accept(Event("turn_aborted",now,null));
            summary=TpsSummary.Aggregate(new[]{a,b,complete,aborted,Meter("cli",1000,1,now,"codex_cli_rs")},now);Check(summary.ActiveCount==2&&summary.Tps==30,"completed aborted and non-desktop sessions excluded");
            a.Accept(Event("task_complete",now,null));summary=TpsSummary.Aggregate(new[]{a,b},now);Check(summary.ActiveCount==1&&summary.Tps==40,"completion immediately leaves the remaining session TPS");
            b.Accept(Event("turn_aborted",now,null));summary=TpsSummary.Aggregate(new[]{a,b},now);Check(summary.ActiveCount==0&&!summary.Tps.HasValue,"no running sessions clears the previous TPS");
            summary=TpsSummary.Aggregate(new[]{Meter("old",100,1,now.AddMinutes(-10)),Meter("future",100,1,now.AddSeconds(1)),pending},now);Check(summary.ActiveCount==1&&summary.MeasuredCount==0&&summary.StaleCount==2&&!summary.Tps.HasValue,"stale future and tokenless records never become a numeric average");
            var copy=Meter("copy",100,5,now);var copy2=Meter("copy",100,5,now);var other=Meter("other",800,20,now);summary=TpsSummary.Aggregate(new[]{copy,copy2,other},now);Check(summary.MeasuredCount==2&&summary.Tps==30,"duplicate session IDs are counted once");copy2.Accept(Event("task_complete",now,null));summary=TpsSummary.Aggregate(new[]{copy,copy2,other},now);Check(summary.MeasuredCount==1&&summary.Tps==40,"latest terminal state wins over duplicate running state");
            var home=Path.Combine(root,"multi-session");var sessions=Path.Combine(home,"sessions");Directory.CreateDirectory(sessions);var stamp=DateTime.UtcNow.AddSeconds(-1);
            for(int i=1;i<=40;i++)File.WriteAllText(Path.Combine(sessions,"session-"+i+".jsonl"),MeterLog("file-"+i,i*100,10,stamp),new UTF8Encoding(false));
            using(var telemetry=new Telemetry(home,()=>DesktopRun.RunningSince(DateTime.MinValue))){
                telemetry.Start();for(int i=0;i<8;i++){summary=telemetry.Read();if(summary.PendingFiles==0)break;}
                Check(summary.ActiveCount==40&&summary.MeasuredCount==40&&summary.Tps==205,"startup scan and retention include all 40 fresh active sessions");
                File.AppendAllText(Path.Combine(sessions,"session-1.jsonl"),Event("task_complete",DateTime.UtcNow,null)+"\n",new UTF8Encoding(false));
                var until=DateTime.UtcNow.AddSeconds(5);do{await Task.Delay(25);summary=telemetry.Read();}while(summary.ActiveCount!=39&&DateTime.UtcNow<until);
                Check(summary.ActiveCount==39&&summary.Tps==210,"file watcher removes completed session from concurrent average");
                File.WriteAllText(Path.Combine(sessions,"session-41.jsonl"),MeterLog("file-41",4100,10,DateTime.UtcNow.AddMilliseconds(-10)),new UTF8Encoding(false));
                until=DateTime.UtcNow.AddSeconds(5);do{await Task.Delay(25);summary=telemetry.Read();}while(summary.ActiveCount!=40&&DateTime.UtcNow<until);
                Check(summary.ActiveCount==40&&summary.Tps==215,"new concurrent session joins the average through file watcher");
            }
        }
        static void TelemetryReadOnly(){
            string home=Environment.GetEnvironmentVariable("CODEX_HOME");if(string.IsNullOrWhiteSpace(home))home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
            var timer=System.Diagnostics.Stopwatch.StartNew();
            using(var telemetry=new Telemetry(home)){telemetry.Start();var summary=telemetry.Read();for(int i=0;i<32&&summary.PendingFiles>0;i++)summary=telemetry.Read();Console.WriteLine("Read-only telemetry: active="+summary.ActiveCount+", interrupted="+summary.InterruptedCount+", measured="+summary.MeasuredCount+", pending="+summary.PendingFiles+", tps="+(summary.Tps.HasValue?summary.Tps.Value.ToString("0.00"):"unavailable")+", initialMs="+timer.ElapsedMilliseconds);timer.Restart();summary=telemetry.Read();Console.WriteLine("Unchanged read: active="+summary.ActiveCount+", interrupted="+summary.InterruptedCount+", elapsedMs="+timer.ElapsedMilliseconds);}
        }
        static async Task LongSessionTelemetry(){
            var home=Path.Combine(root,"long-session");var sessions=Path.Combine(home,"sessions");Directory.CreateDirectory(sessions);var stamp=DateTime.UtcNow.AddSeconds(-1);var path=Path.Combine(sessions,"long.jsonl");
            string padding=Json.Write(new{type="response_item",payload=new{type="function_call_output",output=new string('가',1100000)}})+"\n";
            string history=MeterLog("long",100,5,stamp.AddHours(-2))+Event("task_complete",stamp.AddHours(-2).AddSeconds(1),null)+"\n"+
                Event("task_started",stamp.AddHours(-1),null)+"\n"+Event("token_count",stamp.AddMinutes(-30),new{total_token_usage=new{output_tokens=3700},last_token_usage=new{output_tokens=3600}})+"\n"+padding+
                Event("token_count",stamp,new{total_token_usage=new{output_tokens=7300},last_token_usage=new{output_tokens=3600}})+"\n";
            File.WriteAllText(path,history.Replace("\n","\r\n"),new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(sessions,"other.jsonl"),MeterLog("other",1000,10,stamp),new UTF8Encoding(false));
            using(var telemetry=new Telemetry(home,()=>DesktopRun.RunningSince(DateTime.MinValue))){
                telemetry.Start();var summary=telemetry.Read();Check(summary.ActiveCount==2&&summary.MeasuredCount==2&&summary.Tps==51,"startup recovers long-running session beyond 2 MiB with exact prior token baseline");
                summary=telemetry.Read();Check(summary.Tps==51,"unchanged long log does not replay or double count tokens");
                File.AppendAllText(path,padding+Event("task_complete",DateTime.UtcNow,null),new UTF8Encoding(false));
                await Task.Delay(100);summary=telemetry.Read();Check(summary.ActiveCount==2,"incomplete terminal line is not applied before its newline");
                File.AppendAllText(path,"\n",new UTF8Encoding(false));var until=DateTime.UtcNow.AddSeconds(5);do{await Task.Delay(25);summary=telemetry.Read();}while((summary.ActiveCount!=1||summary.PendingFiles>0)&&DateTime.UtcNow<until);
                Check(summary.ActiveCount==1&&summary.Tps==100&&summary.PendingFiles==0,"oversized UTF-8 record never blocks a later completion event");
                File.WriteAllText(path,MeterLog("replacement",500,10,DateTime.UtcNow.AddMilliseconds(-10)),new UTF8Encoding(false));until=DateTime.UtcNow.AddSeconds(5);do{await Task.Delay(25);summary=telemetry.Read();}while(summary.ActiveCount!=2&&DateTime.UtcNow<until);
                Check(summary.ActiveCount==2&&summary.Tps==75,"truncated log reloads identity and current turn without stale state");
            }
        }
        static async Task InterruptedSessionTelemetry(){
            var now=DateTime.UtcNow;var startup=now.AddMinutes(-1);var a=Meter("resumed",100,5,now);var b=Meter("paused-b",500,10,startup.AddSeconds(-1));var c=Meter("paused-c",600,10,startup.AddSeconds(-2));
            Check(Telemetry.InitialCutoff(now,DesktopRun.RunningSince(now.AddHours(-1)))==now.AddMinutes(-70),"late Relay startup also scans recent pre-restart session history");
            var all=new[]{a,b,c};var summary=TpsSummary.Aggregate(all,now,DesktopRun.RunningSince(startup));
            Check(summary.ActiveCount==1&&summary.InterruptedCount==2&&summary.Tps==20,"app restart excludes two unfinished prior-run sessions from live TPS");
            summary=TpsSummary.Aggregate(all,now,DesktopRun.Stopped());Check(summary.ActiveCount==0&&summary.InterruptedCount==3&&!summary.Tps.HasValue,"app exit interrupts unfinished sessions without requiring terminal log events");
            summary=TpsSummary.Aggregate(all,now,new DesktopRun());Check(summary.AppStateUnknown&&summary.ActiveCount==0&&!summary.Tps.HasValue,"unknown app lifetime is not reported as running sessions");
            b.Accept(Event("task_started",now,null));summary=TpsSummary.Aggregate(all,now,DesktopRun.RunningSince(startup));
            Check(summary.ActiveCount==2&&summary.InterruptedCount==1&&summary.MeasuredCount==1,"resumed session moves from interrupted to running before new tokens arrive");
            var home=Path.Combine(root,"interrupted-sessions");var folder=Path.Combine(home,"sessions");Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"one.jsonl"),MeterLog("one",100,5,now));File.WriteAllText(Path.Combine(folder,"two.jsonl"),MeterLog("two",500,10,startup.AddSeconds(-1)));
            DesktopRun runtime=DesktopRun.RunningSince(startup.AddMinutes(-1));
            using(var telemetry=new Telemetry(home,()=>runtime)){
                telemetry.Start();summary=telemetry.Read();Check(summary.ActiveCount==2,"initial app run counts both live session files");
                runtime=DesktopRun.Stopped();summary=telemetry.Read();Check(summary.ActiveCount==0&&summary.InterruptedCount==2,"process exit updates counts even when no log file changes");
                runtime=DesktopRun.RunningSince(startup);summary=telemetry.Read();Check(summary.ActiveCount==1&&summary.InterruptedCount==1,"new app lifetime recalculates existing log state without rescanning contents");
                File.AppendAllText(Path.Combine(folder,"two.jsonl"),Event("task_started",DateTime.UtcNow,null)+"\n");
                var until=DateTime.UtcNow.AddSeconds(5);do{await Task.Delay(25);summary=telemetry.Read();}while(summary.ActiveCount!=2&&DateTime.UtcNow<until);
                Check(summary.ActiveCount==2&&summary.InterruptedCount==0,"file watcher restores resumed interrupted session to active count");
            }
        }
        static void PollScheduleChecks(){
            long now=Clock.Now;var schedule=new UsagePolling();var a=new Account{Id="a",Usage=Usage.Parse(Limits(50,50,now),now)};
            schedule.Record(a,now);
            Check(!schedule.Due(a,true,now+9,false)&&schedule.Due(a,true,now+10,false),"normal active quota is queried after ten seconds");
            a.Usage=Usage.Parse(Limits(98,50,now),now);
            Check(!schedule.Due(a,true,now+4,false)&&schedule.Due(a,true,now+5,false),"two percent remaining uses five second interval");
            a.Usage=Usage.Parse(Limits(50,95,now),now);Check(schedule.Due(a,true,now+5,false),"weekly threshold also accelerates quota checks");
            Check(!schedule.Due(a,false,now+89,false)&&schedule.Due(a,false,now+90,false),"standby accounts retain ninety second interval");
            a.Usage.Error="offline";Check(!schedule.Due(a,true,now+29,false)&&schedule.Due(a,true,now+30,false),"failed checks back off instead of retrying every UI tick");
            Check(schedule.Due(a,true,now,true),"manual refresh bypasses quota cadence");a.NeedsLogin=true;
            Check(!schedule.Due(a,false,now+1000,true),"invalid credentials are not retried by refresh");
        }
        static async Task ManualQuotaAndLoginChecks(){
            long now=Clock.Now;var vault=new Vault(Path.Combine(root,"manual-quota-vault"));var a=vault.Put(Raw("manual-a"),"A");var b=vault.Put(Raw("manual-b"),"B");
            string home=Path.Combine(root,"manual-quota-home");Directory.CreateDirectory(home);string path=Path.Combine(home,"auth.json");File.WriteAllText(path,a.AuthJson);
            var http=new FakeHttp{Handler=r=>Response(Limits(100,100,Clock.Now))};var guard=new FakeGuard();
            using(var engine=new Engine(vault,home,new Api(http),guard)) {
                guard.Items.Add(new AppProcess{Id=17,Started=1,Desktop=true,Path="fake.exe"});bool asked=false;
                await engine.Switch(b,x=>{asked=true;return true;},x=>true,CancellationToken.None,true);
                Check(!asked&&guard.Closed==0&&engine.LiveRaw()==a.AuthJson,"automatic switching still rejects exhausted target");
                vault.Data.Settings.AutoRotate=true;
                string result=await engine.Switch(b,x=>{asked=true;return true;},x=>true,CancellationToken.None);
                Check(asked&&guard.Closed==1&&Auth.Identity(engine.LiveRaw())==b.Id,"manual switching accepts zero quota for credit use");
                Check(!vault.Data.Settings.AutoRotate&&!new Vault(vault.DirectoryPath).Data.Settings.AutoRotate&&result.Contains("자동 전환 OFF"),"manual exhausted selection remains selected with auto rotation disabled");
                File.WriteAllText(path,a.AuthJson);http.Handler=r=>Response("{}",503);
                await engine.Switch(b,x=>true,x=>true,CancellationToken.None);
                Check(Auth.Identity(engine.LiveRaw())==b.Id&&!b.NeedsLogin,"usage service failure does not prevent explicit manual switch or imply invalid auth");
                File.WriteAllText(path,a.AuthJson);http.Handler=r=>Response("{}",401);int requests=http.Paths.Count;
                await engine.Switch(b,x=>true,x=>true,CancellationToken.None);
                Check(engine.LiveRaw()==a.AuthJson&&b.NeedsLogin&&new Vault(vault.DirectoryPath).Data.Accounts[1].NeedsLogin,"revoked refresh credentials block switch and persist relogin state");
                Check(http.Paths.Count==requests+2,"401 tries one refresh before requiring relogin");
                requests=http.Paths.Count;await engine.Switch(b,x=>true,x=>true,CancellationToken.None);
                Check(http.Paths.Count==requests,"known revoked account is not refreshed again on switch");
                string oldAuth=b.AuthJson;bool failed=false;
                try{await engine.Add(Raw("wrong-user"),"Wrong",b.Id);}catch(InvalidDataException){failed=true;}
                Check(failed&&b.AuthJson==oldAuth&&vault.Data.Accounts.Count==2,"relogin with a different account cannot replace the selected account");
                string newAuth=Auth.FromResponse(Json.Write(new{access_token=Auth.Access(Raw("manual-b")),refresh_token="replacement-login"}),b.AuthJson);
                await engine.Add(newAuth,null,b.Id);
                Check(!b.NeedsLogin&&b.Usage==null&&b.AuthJson==newAuth&&b.Name=="B"&&vault.Data.Accounts[1]==b&&vault.Data.Accounts.Count==2,"relogin replaces credentials without losing name order or adding duplicate");
                http.Handler=r=>Response(Limits(20,20,Clock.Now));await engine.Poll(true,CancellationToken.None);
                Check(b.Usage.Available(Clock.Now)&&!b.NeedsLogin,"reconnected account resumes normal quota queries");
                http.Handler=r=>r.RequestUri.AbsolutePath=="/oauth/token"?Response(Json.Write(Auth.Tokens(Raw("wrong-refresh-user")))):Response("{}",401);
                failed=false;try{await engine.Switch(b,x=>true,x=>true,CancellationToken.None);}catch(InvalidDataException){failed=true;}
                Check(failed&&engine.LiveRaw()==a.AuthJson,"manual quota bypass still rejects refreshed credentials for a different identity");
                await engine.Add(newAuth,null,b.Id);
                http.Handler=r=>r.RequestUri.AbsolutePath=="/oauth/token"?Response("{\"error\":\"invalid_grant\"}",400):Response("{}",401);
                await engine.Switch(b,x=>true,x=>true,CancellationToken.None);
                Check(b.NeedsLogin&&engine.LiveRaw()==a.AuthJson,"OAuth invalid_grant response requires relogin without switching");
            }
            Check(new ServiceError(400,"auth","invalid_grant").RequiresLogin&&!new ServiceError(429,"usage").RequiresLogin&&!new ServiceError(503,"usage").RequiresLogin,"invalid grant is distinguished from rate limiting and service failure");
        }
        static async Task PollPriorityChecks(){
            long now=Clock.Now;var vault=new Vault(Path.Combine(root,"poll-priority-vault"));var standby=vault.Put(Raw("poll-standby"),"Standby");var active=vault.Put(Raw("poll-active"),"Active");
            standby.Usage=Usage.Parse(Limits(20,20,now),now);active.Usage=Usage.Parse(Limits(98,20,now),now);vault.Data.Settings.AutoRotate=true;
            string home=Path.Combine(root,"poll-priority-home");Directory.CreateDirectory(home);File.WriteAllText(Path.Combine(home,"auth.json"),active.AuthJson);
            var order=new List<string>();var http=new FakeHttp{Handler=r=>{bool current=r.Headers.Authorization.Parameter==Auth.Access(active.AuthJson);order.Add(current?"active":"standby");return Response(Limits(current?100:20,20,Clock.Now));}};
            using(var engine=new Engine(vault,home,new Api(http),new FakeGuard())) {
                int renders=0;await engine.Poll(true,CancellationToken.None,()=>renders++);
                Check(order.SequenceEqual(new[]{"active"})&&renders==1&&engine.RotationCandidate()==standby,"active result renders and ready rotation returns before standby refresh");
                vault.Data.Settings.AutoRotate=false;order.Clear();await engine.Poll(true,CancellationToken.None,()=>renders++);
                Check(order.SequenceEqual(new[]{"active","standby"}),"full poll queries active account first regardless of list position");
                order.Clear();await engine.Poll(false,CancellationToken.None);
                Check(order.Count==0,"one second UI scheduling sends no duplicate usage requests");
                standby.NeedsLogin=true;order.Clear();await engine.Poll(true,CancellationToken.None);
                Check(order.SequenceEqual(new[]{"active"}),"periodic and manual refresh skip relogin-required accounts");
            }
        }
        static void QuitRequestChecks(){
            var native=new FakeQuitWindow();var window=new IntPtr(42);
            Check(!DesktopQuit.Request(native,IntPtr.Zero,17)&&native.Activations==0&&native.Sends==0,"no shortcut without a desktop window");
            native.Target=false;Check(!DesktopQuit.Request(native,window,17)&&native.Sends==0,"wrong window owner cannot receive quit");
            native.Target=true;native.Released=false;Check(!DesktopQuit.Request(native,window,17)&&native.Activations==0,"held modifier prevents quit shortcut");
            native.Released=true;native.Activated=false;Check(!DesktopQuit.Request(native,window,17)&&native.Sends==0,"failed activation sends no shortcut");
            native.Activated=true;native.Foreground=false;Check(!DesktopQuit.Request(native,window,17)&&native.Sends==0,"focus loss sends no shortcut to another app");
            native.Foreground=true;native.OnActivate=()=>native.Target=false;Check(!DesktopQuit.Request(native,window,17)&&native.Sends==0,"window ownership rechecked after activation");
            native.Target=true;native.OnActivate=()=>native.Released=false;Check(!DesktopQuit.Request(native,window,17)&&native.Sends==0,"new key press during activation blocks shortcut");
            native.Released=true;native.OnActivate=null;Check(DesktopQuit.Request(native,window,17)&&native.Sends==1,"validated foreground receives one quit request");
            native.Sent=false;Check(!DesktopQuit.Request(native,window,17),"failed input delivery is not reported as success");
        }
        static async Task AsyncQuitChecks(){
            var window=new IntPtr(42);var events=new List<string>();int waited=0;
            var native=new FakeQuitWindow{Released=false,Foreground=false};
            bool sent=await DesktopQuit.RequestAsync(native,window,17,CancellationToken.None,(kind,detail)=>events.Add(kind),(ms,ct)=>{waited+=ms;if(waited>=450)native.Released=true;if(ms==150)native.Foreground=true;return Task.FromResult(0);});
            Check(sent&&native.Sends==1&&waited>=600&&events.Contains("QUIT_WAIT_KEYS")&&events.Contains("QUIT_INPUT_SENT"),"quit waits for released keys and settled foreground before a single send");
            native=new FakeQuitWindow{Foreground=false};events.Clear();
            sent=await DesktopQuit.RequestAsync(native,window,17,CancellationToken.None,(kind,detail)=>events.Add(kind),(ms,ct)=>Task.FromResult(0));
            Check(!sent&&native.Sends==0&&events.Contains("QUIT_FOCUS_NOT_READY"),"foreground timeout logs failure without sending into another app");
            native=new FakeQuitWindow();native.OnActivate=()=>native.Target=false;events.Clear();
            sent=await DesktopQuit.RequestAsync(native,window,17,CancellationToken.None,(kind,detail)=>events.Add(kind),(ms,ct)=>Task.FromResult(0));
            Check(!sent&&native.Sends==0&&events.Contains("QUIT_TARGET_CHANGED"),"async foreground wait revalidates window ownership");
            native=new FakeQuitWindow{Sent=false};events.Clear();
            sent=await DesktopQuit.RequestAsync(native,window,17,CancellationToken.None,(kind,detail)=>events.Add(kind),(ms,ct)=>Task.FromResult(0));
            Check(!sent&&native.Sends==1&&events.Contains("QUIT_INPUT_FAILED"),"failed or partial input is logged and never blindly resent");
            Check(new QuitObservation{Sent=1,Visible=0}.Message(4,true).Contains("메인 창 닫힘")&&new QuitObservation{Sent=1,Visible=1}.Message(4,true).Contains("응답 대기")&&new QuitObservation{Sent=1,Visible=1,Blocked=1}.Message(4,true).Contains("확인창"),"main window closure unresponsive window and confirmation dialog are distinct states");
            Check(Engine.Friendly(new QuitRequestException("종료 입력 전달 실패"))=="종료 입력 전달 실패","quit delivery error is displayed with its actual reason");
        }
        static async Task ShutdownChecks(){
            var vault=new Vault(Path.Combine(root,"shutdown-vault"));var a=vault.Put(Raw("close-a"),"A");var b=vault.Put(Raw("close-b"),"B");
            string home=Path.Combine(root,"shutdown-home");Directory.CreateDirectory(home);string auth=Path.Combine(home,"auth.json");File.WriteAllText(auth,a.AuthJson);
            var guard=new FakeGuard();var initial=new AppProcess{Id=17,Started=1,Desktop=true,Path="fake.exe"};
            int waits=0;bool forceAsked=false;Action onWait=null;
            using(var engine=new Engine(vault,home,new Api(new FakeHttp{Handler=r=>Response(Limits(10,20,Clock.Now))}),guard,false,(ms,ct)=>{ct.ThrowIfCancellationRequested();waits++;if(onWait!=null)onWait();return Task.FromResult(0);})) {
                guard.Items.Add(initial);guard.OnClose=(items,force)=>{};guard.Observation=new QuitObservation{Sent=1,Visible=1};var progress=new List<string>();
                onWait=()=>{if(waits==2)guard.Observation.Visible=0;if(waits==14)guard.Items.Clear();};
                await engine.Switch(b,x=>true,x=>{forceAsked=true;return false;},CancellationToken.None,false,text=>progress.Add(text));
                Check(waits==14&&!forceAsked&&guard.Opened==1&&Auth.Identity(engine.LiveRaw())==b.Id,"seven second quit immediately switches and reopens without waiting sixty seconds");
                string log=File.ReadAllText(engine.LastSwitchLog);
                Check(progress.Any(p=>p.Contains("메인 창 닫힘"))&&log.Contains("MAIN_WINDOW_CLOSED")&&log.IndexOf("ALL_CODEX_PROCESSES_EXITED")<log.IndexOf("AUTH_REPLACED")&&!log.Contains("test-refresh")&&!log.Contains(Auth.Access(a.AuthJson)),"switch trace records window closure before auth replacement without logging tokens");
                File.WriteAllText(auth,a.AuthJson);guard.Items.Add(initial);waits=0;onWait=null;
                guard.Observation.Visible=1;
                await engine.Switch(b,x=>true,x=>{forceAsked=true;return false;},CancellationToken.None);
                Check(waits==120&&forceAsked&&engine.LiveRaw()==a.AuthJson&&guard.Items.Count==1,"sixty second timeout and force refusal preserve running account");
                log=File.ReadAllText(engine.LastSwitchLog);Check(log.Contains("MAIN_WINDOW_STILL_OPEN")&&log.Contains("GRACEFUL_TIMEOUT")&&log.Contains("FORCE_CLOSE_DECLINED")&&!log.Contains("AUTH_REPLACED"),"timeout trace distinguishes no quit response from a completed shutdown");
                waits=0;forceAsked=false;guard.OnClose=(items,force)=>{if(force)guard.Items.Clear();};
                await engine.Switch(b,x=>true,x=>{forceAsked=true;return true;},CancellationToken.None);
                Check(waits==120&&forceAsked&&Auth.Identity(engine.LiveRaw())==b.Id,"force close occurs only after sixty seconds and separate approval");
                File.WriteAllText(auth,a.AuthJson);guard.Items.Add(initial);guard.OnClose=(items,force)=>{};waits=0;forceAsked=false;
                onWait=()=>{if(waits==1)guard.Items.Add(new AppProcess{Id=18,Started=2});};
                await engine.Switch(b,x=>true,x=>{forceAsked=true;return true;},CancellationToken.None);
                Check(!forceAsked&&engine.LiveRaw()==a.AuthJson&&guard.Items.Count==2,"new process during quit blocks force close and auth replacement");
                guard.Items.Clear();guard.Items.Add(initial);onWait=null;waits=0;forceAsked=false;int opened=guard.Opened;
                await engine.Switch(b,x=>true,x=>{forceAsked=true;return true;},CancellationToken.None);
                Check(waits==132&&forceAsked&&engine.LiveRaw()==a.AuthJson&&guard.Opened==opened,"failed approved force close never replaces authentication or reopens app");
                guard.Items.Clear();guard.Items.Add(initial);onWait=null;guard.OnClose=(items,force)=>{throw new IOException("simulated quit delivery failure");};
                bool failed=false;try{await engine.Switch(b,x=>true,x=>true,CancellationToken.None);}catch(IOException){failed=true;}
                Check(failed&&engine.LiveRaw()==a.AuthJson,"quit delivery failure leaves authentication untouched");
                int closed=guard.Closed;using(var cancel=new CancellationTokenSource()){
                    failed=false;try{await engine.Switch(b,x=>{cancel.Cancel();return true;},x=>true,cancel.Token);}catch(OperationCanceledException){failed=true;}
                    Check(failed&&guard.Closed==closed&&engine.LiveRaw()==a.AuthJson,"cancellation before quit cannot close app or replace auth");
                }
            }
        }
        static void StartupChecks(){
            string folder=Path.Combine(root,"portable relay");Directory.CreateDirectory(folder);string exe=Path.Combine(folder,"CodexRelay.exe");File.WriteAllText(exe,"test fixture");
            var store=new FakeStartupStore();var registration=new StartupRegistration(store,exe);
            Check(!registration.IsEnabled&&store.Value==null,"startup query does not register an absent entry");
            registration.SetEnabled(true);Check(registration.IsEnabled&&store.Value=="\""+exe+"\" --tray","startup registration quotes executable path and starts in tray");
            registration.SetEnabled(false);Check(!registration.IsEnabled&&store.Value==null,"startup disable removes the owned entry");
            store.IgnoreWrites=true;bool failed=false;try{registration.SetEnabled(true);}catch(InvalidOperationException){failed=true;}Check(failed&&!registration.IsEnabled,"failed startup write cannot report registration success");
            store.Value="old startup command";failed=false;try{registration.SetEnabled(false);}catch(InvalidOperationException){failed=true;}Check(failed&&registration.IsEnabled,"failed startup removal retains checked state");
            store.IgnoreWrites=false;registration.SetEnabled(true);Check(store.Value==registration.Command,"explicit registration updates an older executable path");
            var missing=new StartupRegistration(store,Path.Combine(folder,"missing.exe"));failed=false;try{missing.SetEnabled(true);}catch(InvalidOperationException){failed=true;}Check(failed&&store.Value==registration.Command,"missing executable never replaces working startup entry");
            var vault=new Vault(Path.Combine(root,"startup-preferences"));var settings=vault.Data.Settings;
            Check(!settings.StartWithWindows.HasValue&&!Json.Read<Settings>("{}").StartWithWindows.HasValue,"old vaults have unknown startup preference for migration");
            store.Value="\"C:\\old relay\\CodexRelay.exe\" --tray";registration.Reconcile(settings,()=>vault.Save());
            Check(settings.StartWithWindows==true&&new Vault(vault.DirectoryPath).Data.Settings.StartWithWindows==true&&store.Value==registration.Command,"legacy startup registration is persisted and moved to the current executable");
            int writes=store.Writes;bool saved=false;Check(!registration.Reconcile(settings,()=>saved=true)&&!saved&&store.Writes==writes,"matching startup path requires no registry or vault write");
            store.Value=null;registration.Reconcile(settings,()=>vault.Save());Check(store.Value==registration.Command,"saved enabled preference restores a missing startup entry");
            registration.SetPreference(settings,false,()=>vault.Save());Check(settings.StartWithWindows==false&&new Vault(vault.DirectoryPath).Data.Settings.StartWithWindows==false&&!registration.IsEnabled,"menu disable persists preference and removes registration");
            registration.Reconcile(settings,()=>vault.Save());Check(!registration.IsEnabled,"disabled preference stays disabled on subsequent launches");
            store.Value="older registration";registration.Reconcile(settings,()=>vault.Save());Check(!registration.IsEnabled,"saved disabled preference clears a stale registration");
            registration.SetPreference(settings,true,()=>vault.Save());Check(new Vault(vault.DirectoryPath).Data.Settings.StartWithWindows==true&&store.Value==registration.Command,"menu enable persists preference and registers current path");
            failed=false;try{registration.SetPreference(settings,false,()=>{throw new IOException("simulated vault write failure");});}catch(IOException){failed=true;}
            Check(failed&&settings.StartWithWindows==true&&store.Value==registration.Command&&new Vault(vault.DirectoryPath).Data.Settings.StartWithWindows==true,"failed vault save restores prior enabled preference and registry command");
            store.IgnoreWrites=true;failed=false;saved=false;try{registration.SetPreference(settings,false,()=>saved=true);}catch(InvalidOperationException){failed=true;}
            Check(failed&&!saved&&settings.StartWithWindows==true&&registration.IsEnabled,"failed registry change never persists a contrary preference");store.IgnoreWrites=false;
            var fresh=new Settings();store.Value=null;registration.Reconcile(fresh,()=>{});Check(fresh.StartWithWindows==false&&!registration.IsEnabled,"fresh install migrates to disabled without registering itself");
            var legacy=new Settings();store.Value="old startup command";failed=false;try{registration.Reconcile(legacy,()=>{throw new IOException("simulated migration failure");});}catch(IOException){failed=true;}
            Check(failed&&!legacy.StartWithWindows.HasValue&&store.Value=="old startup command","migration save failure leaves legacy registration unchanged");
        }
        static void RotationRefusalChecks(){
            var vault=new Vault(Path.Combine(root,"rotation-refusal-vault"));vault.Data.Settings.Overlay=false;vault.Data.Settings.AutoRotate=true;
            var account=vault.Put(Raw("refused-active"),"A");account.Usage=Usage.Parse(Limits(100,20,Clock.Now),Clock.Now);
            var next=vault.Put(Raw("refused-next"),"B");next.Usage=Usage.Parse(Limits(10,20,Clock.Now),Clock.Now);
            string home=Path.Combine(root,"rotation-refusal-home");Directory.CreateDirectory(home);File.WriteAllText(Path.Combine(home,"auth.json"),account.AuthJson);
            var http=new FakeHttp{Handler=r=>{throw new Exception("Refused rotation must not call the network");}};
            using(var engine=new Engine(vault,home,new Api(http),new FakeGuard()))
            using(var form=new MainForm(engine,false,false,new StartupRegistration(new FakeStartupStore(),Application.ExecutablePath))) {
                engine.SyncLive();var toggle=MainField<CheckBox>(form,"rotate");
                Check(!form.AcceptSwitchConfirmation(false,false)&&toggle.Checked&&vault.Data.Settings.AutoRotate,"manual refusal preserves automatic switching preference");
                Check(form.AcceptSwitchConfirmation(true,true)&&toggle.Checked,"approved automatic switch stays enabled");
                Check(!form.AcceptSwitchConfirmation(false,true)&&!toggle.Checked&&!vault.Data.Settings.AutoRotate,"automatic refusal immediately turns toggle off");
                Check(!new Vault(vault.DirectoryPath).Data.Settings.AutoRotate,"automatic refusal remains disabled after vault reload");
                typeof(MainForm).GetField("lastRotationPrompt",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(form,Clock.Now-601);
                var pending=(Task)typeof(MainForm).GetMethod("AutoRotate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(form,null);
                Check(pending.IsCompleted&&!pending.IsFaulted&&http.Paths.Count==0,"no repeat automatic prompt after the old ten minute cooldown");
                toggle.Checked=true;Check(new Vault(vault.DirectoryPath).Data.Settings.AutoRotate,"explicit user toggle re-enables automatic switching");
                form.AcceptSwitchConfirmation(false,true);Check(!new Vault(vault.DirectoryPath).Data.Settings.AutoRotate,"refusal after re-enabling disables automatic switching again");
            }
        }
        static IEnumerable<Control> Controls(Control rootControl) {foreach(Control c in rootControl.Controls){yield return c;foreach(var child in Controls(c))yield return child;}}
        static void CallMain(MainForm form,string method,params object[] args){typeof(MainForm).GetMethod(method,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(form,args);}
        static T MainField<T>(MainForm form,string name){return (T)typeof(MainForm).GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(form);}
        // Complete control creation without showing a top-level window; DataGridView needs this lifecycle for WM_PRINT.
        static void PrepareHidden(Control control){typeof(Control).GetMethod("CreateControl",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(bool)},null).Invoke(control,new object[]{true});control.PerformLayout();foreach(Control child in control.Controls)PrepareHidden(child);}
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr handle,int attribute,out int value,int size);
        static Bitmap ClientPreview(MainForm form){var rootPanel=form.Controls[0];var bitmap=new Bitmap(rootPanel.Width,rootPanel.Height);rootPanel.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));return bitmap;}
        static void MenuPreview(ContextMenuStrip menu,string path){menu.Size=menu.GetPreferredSize(Size.Empty);PrepareHidden(menu);menu.PerformLayout();menu.Items[0].Select();using(var bitmap=new Bitmap(menu.Width,menu.Height)){menu.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(path);}Check(!menu.Visible,"menu preview remains hidden");}
        static void HiddenRender(){
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            RotationRefusalChecks();
            var vault=new Vault(Path.Combine(root,"hidden-ui-vault"));vault.Data.Settings.DarkMode=false;long now=Clock.Now;
            for(int i=0;i<2;i++){var a=vault.Put(Raw("preview"+i),new[]{"업무 계정","개인 계정"}[i]);a.Usage=Usage.Parse(Limits(new[]{64,18}[i],new[]{72,41}[i],now),now);}
            var http=new FakeHttp{Handler=r=>{throw new Exception("Hidden rendering must not use network");}};
            int overlayOpenRequests=0,overlaySaves=0;ContextMenuStrip[] ownedMenus=null;var startupStore=new FakeStartupStore();
            using(var engine=new Engine(vault,Path.Combine(root,"unused-codex"),new Api(http),new FakeGuard(),true))
            using(var form=new MainForm(engine,true,false,new StartupRegistration(startupStore,Application.ExecutablePath)))
            using(var overlay=new Overlay(vault.Data.Settings,()=>overlaySaves++,()=>overlayOpenRequests++)){
                engine.SyncLive();
                CallMain(form,"Render",new object[]{null});CallMain(form,"ApplyTheme");
                PrepareHidden(form);PrepareHidden(overlay);
                var footerStatus=MainField<Label>(form,"status");footerStatus.Text="사용 가능한 다음 계정이 없습니다.";
                CallMain(form,"UpdateLastChecked",new DateTime(2026,10,30,16,30,0));var footerTime=MainField<Label>(form,"lastChecked");
                Check(footerTime.Text=="최근 확인 2026-10-30 16:30 ·"&&footerStatus.Text=="사용 가능한 다음 계정이 없습니다."&&footerTime.Right<=footerStatus.Left,"quota refresh time has its own footer field and preserves the status message");
                CallMain(form,"UpdateLastChecked",new DateTime(2026,10,30,16,31,0));Check(footerStatus.Text=="사용 가능한 다음 계정이 없습니다."&&footerTime.Text.Contains("16:31"),"successive refreshes update time without flashing the unavailable-account message");
                Application.DoEvents();CallMain(form,"Render",new object[]{null});
                var firstGrid=Controls(form).OfType<DataGridView>().Single();firstGrid.CurrentCell=firstGrid.Rows[0].Cells[1];firstGrid.FirstDisplayedScrollingRowIndex=0;
                typeof(MainForm).GetField("overlay",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(form,overlay);
                var slider=Controls(form).OfType<TransparencySlider>().Single();
                var toggles=Controls(form).OfType<CheckBox>().ToArray();var dark=toggles.Single(c=>(string)c.Tag=="다크 모드");
                ownedMenus=new[]{MainField<ContextMenuStrip>(form,"appMenu"),MainField<ContextMenuStrip>(form,"accountMenu"),MainField<ContextMenuStrip>(form,"trayMenu")};
                Check(ownedMenus.All(m=>m.Renderer is ThemeMenuRenderer&&!m.ShowImageMargin&&m.BackColor==Theme.Panel&&m.ForeColor==Theme.Text)&&ownedMenus[0].ShowCheckMargin&&!ownedMenus[1].ShowCheckMargin&&!ownedMenus[2].ShowCheckMargin,"all menus share light theme with check gutter only for startup menu");
                var startupItem=MainField<ToolStripMenuItem>(form,"startupItem");CallMain(form,"RefreshStartupState");Check(!startupItem.Checked&&startupItem.Enabled,"startup menu is unchecked when absent");startupStore.Value="registered";CallMain(form,"RefreshStartupState");Check(startupItem.Checked,"startup menu check follows registered state");startupStore.Value=null;CallMain(form,"RefreshStartupState");
                var accountContext=ownedMenus[1];typeof(MainForm).GetField("menuAccount",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(form,vault.Data.Accounts[0]);
                vault.Data.Accounts[1].NeedsLogin=true;var state=typeof(MainForm).GetMethod("AccountState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(form,new object[]{vault.Data.Accounts[1]});
                Check((string)state=="재로그인 필요","authentication failure is displayed separately from quota query failure");vault.Data.Accounts[1].NeedsLogin=false;
                int menuClicks=0;accountContext.Items[0].Click+=(s,e)=>menuClicks++;
                for(int i=0;i<3;i++){typeof(ToolStripDropDown).GetMethod("OnClosed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(accountContext,new object[]{new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.ItemClicked)});Check(!accountContext.IsDisposed,"account menu survives close notification "+(i+1));accountContext.Items[0].PerformClick();}
                Check(menuClicks==3&&!accountContext.IsDisposed&&accountContext.Items.Count==5&&accountContext.Items[1].Text=="다시 로그인","click callbacks and reconnect action remain valid after repeated menu closure");
                var selectedGrid=Controls(form).OfType<DataGridView>().Single();selectedGrid.ClearSelection();selectedGrid.Rows[1].Selected=true;var contextTarget=typeof(MainForm).GetMethod("MenuAccount",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(form,null);Check(object.ReferenceEquals(contextTarget,vault.Data.Accounts[0]),"menu command target is not changed by a later row selection");
                var optionPanels=Controls(form).OfType<TableLayoutPanel>().Where(p=>p.Controls.OfType<CheckBox>().Any(c=>(string)c.Tag!="다크 모드")).ToArray();
                Check(optionPanels.Length==3&&optionPanels.All(p=>{var title=p.Controls.OfType<Label>().Single(l=>p.GetRow(l)==0);var note=p.Controls.OfType<Label>().Single(l=>p.GetRow(l)==1);var toggle=p.Controls.OfType<CheckBox>().Single();return note.TextAlign==ContentAlignment.TopLeft&&note.Top-title.Bottom==6&&title.Left==note.Left&&toggle.Right==p.ClientSize.Width;}),"all option titles descriptions and toggles share spacing and alignment");
                Check(optionPanels.All(p=>{var note=p.Controls.OfType<Label>().Single(l=>p.GetRow(l)==1);return TextRenderer.MeasureText(note.Text,note.Font,new Size(10000,1000),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width<=note.Width;}),"all three option descriptions fit one line");
                Check(toggles.Length==4&&toggles.All(c=>c.Text==""&&!string.IsNullOrEmpty(c.AccessibleName)),"four toggles retain accessible names without native captions");
                Check(slider.Value==50&&overlay.Opacity==.5,"slider and overlay restore initial 50 percent transparency");
                string sliderImages=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-preview-hidden");Directory.CreateDirectory(sliderImages);
                using(var bitmap=new Bitmap(slider.Width,slider.Height)){slider.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(sliderImages,"slider-50.png"));}
                slider.Value=0;Check(overlay.Opacity==1,"zero transparency produces opaque overlay immediately");
                slider.Value=90;Check(Math.Abs(overlay.Opacity-.1)<.00001,"maximum transparency updates overlay immediately");
                using(var bitmap=new Bitmap(slider.Width,slider.Height)){slider.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(sliderImages,"slider-90.png"));}
                slider.Value=37;Check(Math.Abs(overlay.Opacity-.63)<.00001&&vault.Data.Settings.OverlayTransparency==37,"slider updates settings and overlay together");
                CallMain(form,"SaveAppearance");Check(new Vault(vault.DirectoryPath).Data.Settings.OverlayTransparency==37,"transparency persisted in isolated vault");
                typeof(TransparencySlider).GetMethod("OnKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(slider,new object[]{new KeyEventArgs(Keys.Right)});Check(slider.Value==42,"themed slider supports keyboard adjustment");slider.Value=37;CallMain(form,"SaveAppearance");
                overlay.UpdateData(vault.Data.Accounts[0],"30.0",true);
                string images=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-preview-hidden");Directory.CreateDirectory(images);
                CallMain(form,"DisplayMetrics",new TpsSummary {ActiveCount=2,MeasuredCount=2,Tps=30},false);Check(MainField<Label>(form,"tps").Text=="30.0"&&MainField<Label>(form,"metricNote").Text=="실행 중 2개"&&overlay.Controls.OfType<Label>().Any(l=>l.Text=="30.0 TPS"),"main window and overlay show the same multi-session average");
                CallMain(form,"DisplayMetrics",new TpsSummary {ActiveCount=1,InterruptedCount=2,MeasuredCount=1,Tps=20},false);Check(MainField<Label>(form,"tps").Text=="20.0"&&MainField<Label>(form,"metricNote").Text=="실행 중 1개 · 중단 2개","interrupted sessions have a separate count and do not dilute displayed TPS");
                CallMain(form,"DisplayMetrics",new TpsSummary {ActiveCount=3,MeasuredCount=2,Tps=30},false);Check(MainField<Label>(form,"metricNote").Text=="집계 2 / 실행 3개","main window distinguishes measured sessions from pending tokens");
                CallMain(form,"DisplayMetrics",new TpsSummary(),false);Check(MainField<Label>(form,"tps").Text=="—"&&overlay.Controls.OfType<Label>().Any(l=>l.Text=="— TPS"),"idle summary clears TPS in both displays");
                CallMain(form,"DisplayMetrics",new TpsSummary {ActiveCount=2,MeasuredCount=2,Tps=30,PendingFiles=1},false);Check(MainField<Label>(form,"tps").Text=="—"&&MainField<Label>(form,"metricNote").Text=="세션 집계 중","incomplete file batch does not present a partial average as final");
                CallMain(form,"DisplayMetrics",new TpsSummary {ActiveCount=2,MeasuredCount=2,Tps=30},false);
                MainField<Label>(form,"status").Text="사용 가능한 다음 계정이 없습니다.";
                MenuPreview(accountContext,Path.Combine(images,"account-menu-light.png"));
                startupStore.Value="registered";CallMain(form,"RefreshStartupState");MenuPreview(ownedMenus[0],Path.Combine(images,"app-menu-light.png"));MenuPreview(ownedMenus[2],Path.Combine(images,"tray-menu-light.png"));
                using(var bmp=new Bitmap(overlay.Width,overlay.Height)){overlay.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(images,"overlay-both.png"));}
                using(var bmp=ClientPreview(form)){int panelPixels=0;for(int y=0;y<bmp.Height;y+=3)for(int x=0;x<bmp.Width;x+=3)if(bmp.GetPixel(x,y).ToArgb()==Theme.Panel.ToArgb())panelPixels++;Check(panelPixels>1000,"hidden bitmap includes rendered child controls");var table=Controls(form).OfType<DataGridView>().Single();var origin=Point.Empty;for(Control child=table;child!=form.Controls[0];child=child.Parent)origin.Offset(child.Location);int rowPixels=0;for(int y=Math.Max(0,origin.Y);y<Math.Min(bmp.Height,origin.Y+table.Height);y++)for(int x=Math.Max(0,origin.X);x<Math.Min(bmp.Width,origin.X+table.Width);x++)if(bmp.GetPixel(x,y).ToArgb()==Theme.Accent.ToArgb())rowPixels++;bmp.Save(Path.Combine(images,"main-client.png"));Check(rowPixels>50,"account rows are painted rather than blank in hidden preview");}
                dark.Checked=true;Check(Theme.IsDark&&form.BackColor==Theme.Back&&overlay.BackColor==Theme.Back,"dark palette applied to main window and overlay");
                Check(ownedMenus.All(m=>m.BackColor==Theme.Panel&&m.ForeColor==Theme.Text&&((ThemeMenuRenderer)m.Renderer).ColorTable.ImageMarginGradientBegin==Theme.Panel),"all context menus and margins follow dark theme");
                MenuPreview(accountContext,Path.Combine(images,"account-menu-dark.png"));
                MenuPreview(ownedMenus[0],Path.Combine(images,"app-menu-dark.png"));MenuPreview(ownedMenus[2],Path.Combine(images,"tray-menu-dark.png"));
                Check(Math.Abs(overlay.Opacity-.63)<.00001,"theme change preserves chosen transparency");
                using(var bmp=ClientPreview(form))bmp.Save(Path.Combine(images,"main-dark-client.png"));
                slider.Value=50;
                using(var bmp=ClientPreview(form))bmp.Save(Path.Combine(images,"public-main.png"));
                using(var bmp=new Bitmap(overlay.Width,overlay.Height)){overlay.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(images,"public-overlay.png"));}
                slider.Value=37;CallMain(form,"SaveAppearance");
                Check(form.FormBorderStyle==FormBorderStyle.Sizable&&form.ControlBox&&form.MinimizeBox&&form.MaximizeBox,"native resizable frame and caption buttons retained");
                int captionColor,textColor;
                if(DwmGetWindowAttribute(form.Handle,35,out captionColor,4)==0&&DwmGetWindowAttribute(form.Handle,36,out textColor,4)==0)Check(captionColor==-1&&textColor==-1,"caption and text colors remain system defaults");
                else Console.WriteLine("SKIP native caption color query is not supported on this Windows version");
                Check(!form.Visible&&!overlay.Visible,"theme redraw never shows hidden forms");
                dark.Checked=false;Check(!Theme.IsDark&&Math.Abs(overlay.Opacity-.63)<.00001,"light theme restored without resetting transparency");
                var quotaGrid=Controls(form).OfType<DataGridView>().Single();Check(quotaGrid.Columns[2].HeaderText=="5시간 한도"&&quotaGrid.Columns[3].HeaderText=="주간 한도","quota headings use requested Codex terminology");
                vault.Data.Accounts[0].Usage.FiveHour=null;CallMain(form,"Render",new object[]{null});
                var summary=(TableLayoutPanel)typeof(MainForm).GetField("metrics",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(form);
                Check(summary.ColumnStyles[1].Width==0&&Convert.ToString(quotaGrid.Rows[0].Cells[2].Value)==""&&quotaGrid.Columns[2].Visible,"weekly-only summary hides period while mixed table keeps supported column");
                overlay.UpdateData(vault.Data.Accounts[0],"30.0",true);
                var overlayTexts=overlay.Controls.OfType<Label>().Select(c=>c.Text).ToArray();
                Check(overlayTexts.Length==3&&!overlayTexts.Any(t=>t.Contains("5시간")||t.Contains("기록 갱신")||t.Contains("턴 평균"))&&overlayTexts.Any(t=>t.Contains("주간 한도")),"overlay omits unsupported quota and recording footer");
                Check(overlay.ContextMenuStrip==null&&overlay.Controls.Cast<Control>().All(c=>c.ContextMenuStrip==null),"overlay and children have no right-click menu");
                typeof(Control).GetMethod("OnDoubleClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(overlay,new object[]{EventArgs.Empty});
                foreach(Control child in overlay.Controls)typeof(Control).GetMethod("OnDoubleClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(child,new object[]{EventArgs.Empty});
                Check(overlayOpenRequests==4,"double click on background and every label opens manager once");
                var mouse=new MouseEventArgs(MouseButtons.Left,1,10,10,0);typeof(Control).GetMethod("OnMouseDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(overlay,new object[]{mouse});typeof(Control).GetMethod("OnMouseMove",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(overlay,new object[]{mouse});Check(overlaySaves==0,"stationary click does not start or save a drag");
                using(var bmp=new Bitmap(overlay.Width,overlay.Height)){overlay.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(images,"overlay-weekly.png"));}
                vault.Data.Accounts.RemoveRange(1,vault.Data.Accounts.Count-1);CallMain(form,"Render",new object[]{null});Check(!quotaGrid.Columns[2].Visible,"five-hour table column omitted when no account supplies it");
                using(var bmp=ClientPreview(form))bmp.Save(Path.Combine(images,"main-weekly-client.png"));dark.Checked=true;using(var bmp=ClientPreview(form))bmp.Save(Path.Combine(images,"main-weekly-dark-client.png"));dark.Checked=false;
                form.Size=form.MinimumSize;form.PerformLayout();
                using(var bmp=ClientPreview(form))bmp.Save(Path.Combine(images,"main-minimum-client.png"));
                bool fits=true;for(Control child=slider;child.Parent!=null;child=child.Parent)fits=fits&&child.Parent.ClientRectangle.Contains(child.Bounds);
                Check(slider.Height>=20&&fits,"transparency slider fits at minimum window size");
                Check(!form.Visible&&!overlay.Visible&&http.Paths.Count==0,"hidden render stayed invisible and offline");
                Console.WriteLine("Hidden previews: "+images);
            }
            Check(ownedMenus!=null&&ownedMenus.All(m=>m.IsDisposed),"owned menus are disposed only with their main form");
            var firstHome=Path.Combine(root,"first-start-codex");Directory.CreateDirectory(firstHome);File.WriteAllText(Path.Combine(firstHome,"auth.json"),Raw("first-start"),new UTF8Encoding(false));
            var firstVault=new Vault(Path.Combine(root,"first-start-vault"));firstVault.Data.Settings.Overlay=false;
            var firstHttp=new FakeHttp{Handler=r=>Response(Limits(10,20,Clock.Now))};var firstStartup=new FakeStartupStore();
            using(var engine=new Engine(firstVault,firstHome,new Api(firstHttp),new FakeGuard()))
            using(var form=new MainForm(engine,false,false,new StartupRegistration(firstStartup,Application.ExecutablePath))){
                var initialize=form.InitializeAsync();var until=DateTime.UtcNow.AddSeconds(5);while(!initialize.IsCompleted&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(1);}if(!initialize.IsCompleted)throw new Exception("first-start initialization timed out");initialize.GetAwaiter().GetResult();
                Check(firstVault.Data.Accounts.Count==1&&engine.Active!=null&&firstVault.Data.Settings.StartWithWindows==false&&!form.Visible,"startup preference migration preserves first-launch account import without showing the form");
            }
        }
        static void UiRender() {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            var vault=new Vault(Path.Combine(root,"ui-vault"));vault.Data.Settings.DarkMode=false;long now=Clock.Now;
            for(int i=0;i<3;i++){var a=vault.Put(Raw("demo"+i),new[]{"업무 계정","개인 계정","예비 계정"}[i]);a.Usage=Usage.Parse(Limits(new[]{64,18,0}[i],new[]{72,41,8}[i],now),now);}
            var http=new FakeHttp{Handler=r=>{throw new Exception("UI test must not use network");}};
            using(var engine=new Engine(vault,Path.Combine(root,"unused-codex"),new Api(http),new FakeGuard(),true))using(var form=new MainForm(engine,true)) {
                form.Show();Application.DoEvents();
                var grid=Controls(form).OfType<DataGridView>().Single();Check(grid.Rows.Count==3,"account rows rendered");
                string first=vault.Data.Accounts[0].Id;grid.ClearSelection();grid.Rows[0].Selected=true;typeof(DataGridView).GetMethod("OnCellClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(grid,new object[]{new DataGridViewCellEventArgs(5,0)});
                Check(vault.Data.Accounts[1].Id==first,"down button changes rotation order");Check(new Vault(vault.DirectoryPath).Data.Accounts[1].Id==first,"rotation order persists");
                typeof(DataGridView).GetMethod("OnCellClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(grid,new object[]{new DataGridViewCellEventArgs(4,1)});Check(vault.Data.Accounts[0].Id==first,"up button preserves selection and restores order");
                Check(Convert.ToString(grid.Rows[0].Cells[2].Value)=="36% 남음"&&Convert.ToString(grid.Rows[0].Cells[3].Value)=="28% 남음","account rows display remaining quota");
                var toggles=Controls(form).OfType<CheckBox>().ToArray();var overlayBox=toggles.Single(c=>(string)c.Tag=="오버레이");overlayBox.Checked=true;Application.DoEvents();
                var overlay=Application.OpenForms.OfType<Overlay>().Single();Check(overlay.Visible&&overlay.TopMost&&overlay.Opacity==.5,"overlay visible topmost at 50 percent opacity");
                Check(vault.Data.Settings.Overlay&&new Vault(vault.DirectoryPath).Data.Settings.Overlay,"overlay toggle persisted");
                var auto=toggles.Single(c=>(string)c.Tag=="자동 계정 전환");auto.Checked=true;Check(vault.Data.Settings.AutoRotate,"rotation toggle wired");auto.Checked=false;
                var warm=toggles.Single(c=>(string)c.Tag=="자동 웜업");warm.Checked=true;Check(vault.Data.Settings.AutoWarm,"warmup toggle wired");warm.Checked=false;
                overlay.UpdateData(vault.Data.Accounts[0],"38.4",true);
                string images=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-preview");Directory.CreateDirectory(images);
                using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(images,"main.png"));}
                using(var bmp=new Bitmap(overlay.Width,overlay.Height)){overlay.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(images,"overlay.png"));}
                var dark=toggles.Single(c=>(string)c.Tag=="다크 모드");dark.Checked=true;Application.DoEvents();Check(vault.Data.Settings.DarkMode&&new Vault(vault.DirectoryPath).Data.Settings.DarkMode,"dark mode persisted");Check(form.BackColor==Theme.Back&&overlay.BackColor==Theme.Back&&Theme.IsDark,"dark theme reaches main window and overlay");using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(images,"main-dark.png"));}dark.Checked=false;Check(!Theme.IsDark&&!vault.Data.Settings.DarkMode,"light theme restored");
                var restored=new Settings{OverlayX=123,OverlayY=77};using(var positioned=new Overlay(restored,()=>{},()=>{})){positioned.Show();Application.DoEvents();Check(positioned.Left==123&&positioned.Top==77,"saved overlay position survives first Show");positioned.Close();}
                overlayBox.Checked=false;Check(!overlay.Visible&&!vault.Data.Settings.Overlay,"overlay off hides window");
                form.Close();Check(!form.Visible&&!form.IsDisposed,"close button keeps tray process");form.ShowMain();Check(form.Visible,"tray reopen path displays window");
                typeof(MainForm).GetField("closing",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(form,true);form.Close();
                Check(http.Paths.Count==0,"UI tests never contacted server");Console.WriteLine("UI previews: "+images);
            }
        }
        static string Event(string type,DateTime time,object info){return Json.Write(new{type="event_msg",timestamp=time.ToString("o"),payload=new{type=type,info=info}});}
    }
}
