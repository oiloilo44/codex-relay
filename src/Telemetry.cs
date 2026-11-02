using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodexRelay {
    internal sealed class TurnMeter {
        public DateTime Started;
        public DateTime Updated;
        public long Tokens;
        public long Total=-1;
        public bool Running;
        public bool HasStart;
        public string Id="";
        public string AccountId="";
        public bool Desktop;
        public double? Tps { get { double seconds=(Updated-Started).TotalSeconds; return HasStart && Tokens>0 && seconds>0.1 ? (double?)(Tokens/seconds) : null; } }
        public void Accept(string line) {
            if(line.Length>0&&line[0]=='\uFEFF')line=line.Substring(1);
            // Large tool outputs and conversation bodies are irrelevant to the meter.
            // Filter before JSON parsing; still validate the top-level type below.
            bool metadata=line.IndexOf("\"session_meta\"",StringComparison.Ordinal)>=0;
            if(!metadata&&(line.IndexOf("\"event_msg\"",StringComparison.Ordinal)<0||
                (line.IndexOf("\"task_started\"",StringComparison.Ordinal)<0&&line.IndexOf("\"token_count\"",StringComparison.Ordinal)<0&&
                 line.IndexOf("\"task_complete\"",StringComparison.Ordinal)<0&&line.IndexOf("\"turn_aborted\"",StringComparison.Ordinal)<0)))return;
            try {
                var e=Json.Parse(line); string type=Json.Str(e,"type"); var p=Json.Get(e,"payload");
                if(type=="session_meta") { Desktop=Json.Str(p,"originator").IndexOf("Desktop",StringComparison.OrdinalIgnoreCase)>=0; AccountId=Json.Str(p,"creator_account_id"); Id=Json.Str(p,"id"); return; }
                if(!Desktop || type!="event_msg") return;
                DateTime time; if(!DateTime.TryParse(Json.Str(e,"timestamp"),null,System.Globalization.DateTimeStyles.RoundtripKind,out time)) return; time=time.ToUniversalTime();
                string kind=Json.Str(p,"type");
                if(kind=="task_started") { Started=time; Updated=time; Tokens=0; Running=true; HasStart=true; }
                if(kind=="token_count") {
                    var info=Json.Get(p,"info"); if(info==null) return;
                    long next=Json.Num(Json.Get(info,"total_token_usage"),"output_tokens",-1); if(next<0 || next==Total) return;
                    long delta=Total>=0 && next>=Total ? next-Total : Json.Num(Json.Get(info,"last_token_usage"),"output_tokens");
                    Total=next; if(Running && HasStart) { Tokens+=Math.Max(0,delta); Updated=time; }
                }
                if(kind=="task_complete" || kind=="turn_aborted") { Updated=time; Running=false; }
            } catch(ArgumentException) { } catch(InvalidOperationException) { }
        }
    }
    internal sealed class TpsSummary {
        public int ActiveCount;
        public int MeasuredCount;
        public int StaleCount;
        public int PendingFiles;
        public int InterruptedCount;
        public bool AppStateUnknown;
        public DateTime Updated;
        public double? Tps;
        public bool Running {get{return ActiveCount>0;}}
        public static readonly TimeSpan MaxAge=TimeSpan.FromMinutes(10);
        public static TpsSummary Aggregate(IEnumerable<TurnMeter> meters,DateTime now,DesktopRun runtime=null){
            // A copied session must not be counted twice, and a newer completion must beat an older running copy.
            var latest=meters.Where(m=>m.Desktop).GroupBy(m=>string.IsNullOrEmpty(m.Id)?(object)m:m.Id)
                .Select(g=>g.OrderByDescending(m=>m.Updated).ThenByDescending(m=>m.Started).ThenBy(m=>m.Running?1:0).First());
            var running=latest.Where(m=>m.HasStart&&m.Running).ToArray();
            var interrupted=runtime!=null&&runtime.Known?running.Where(m=>m.Started<=now&&m.Updated<=now&&(!runtime.Present||m.Started<runtime.StartedUtc)).ToArray():new TurnMeter[0];
            var current=running.Except(interrupted).ToArray();
            bool unknown=runtime!=null&&!runtime.Known;
            var active=current.Where(m=>!unknown&&m.Updated<=now&&m.Started<=now&&now-m.Updated<MaxAge).ToArray();
            var measured=active.Where(m=>m.Tps.HasValue).ToArray();
            return new TpsSummary {ActiveCount=active.Length,MeasuredCount=measured.Length,StaleCount=current.Length-active.Length,InterruptedCount=interrupted.Length,AppStateUnknown=unknown,
                Tps=measured.Length==0?(double?)null:measured.Average(m=>m.Tps.Value),
                Updated=measured.Length==0?DateTime.MinValue:measured.Max(m=>m.Updated)};
        }
    }
    internal sealed class Telemetry : IDisposable {
        sealed class Tail { public long Offset; public TurnMeter Meter=new TurnMeter(); }
        readonly string root;
        readonly Func<DesktopRun> desktopRun;
        readonly Dictionary<string,Tail> tails=new Dictionary<string,Tail>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> dirty=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly object sync=new object();
        FileSystemWatcher watcher;
        bool rescanRequested;
        public string Note="기록 대기";
        public Telemetry(string codexHome,Func<DesktopRun> run=null) { root=Path.Combine(codexHome,"sessions");desktopRun=run??new DesktopRuntime().Read; }
        public void Start() {
            if(!Directory.Exists(root)) return;
            watcher=new FileSystemWatcher(root,"*.jsonl") { IncludeSubdirectories=true,NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.Size|NotifyFilters.FileName,InternalBufferSize=8192 };
            watcher.Changed+=(s,e)=>Queue(e.FullPath); watcher.Created+=(s,e)=>Queue(e.FullPath);watcher.Deleted+=(s,e)=>Queue(e.FullPath);watcher.Renamed+=(s,e)=>{Queue(e.OldFullPath);Queue(e.FullPath);};watcher.Error+=(s,e)=>{ Note="일부 기록 변경 누락 · 최근 기록 재확인";lock(sync)rescanRequested=true; };
            watcher.EnableRaisingEvents=true;
            QueueRecent();
        }
        internal static DateTime InitialCutoff(DateTime now,DesktopRun run){
            var cutoff=now-TpsSummary.MaxAge;
            if(run!=null&&run.Known&&run.Present&&run.StartedUtc>DateTime.MinValue+TpsSummary.MaxAge&&run.StartedUtc<now){var beforeRun=run.StartedUtc-TpsSummary.MaxAge;if(beforeRun<cutoff)cutoff=beforeRun;}
            return cutoff;
        }
        void QueueRecent(){try{var cutoff=InitialCutoff(DateTime.UtcNow,desktopRun());foreach(var f in new DirectoryInfo(root).EnumerateFiles("*.jsonl",SearchOption.AllDirectories).Where(f=>f.LastWriteTimeUtc>=cutoff).OrderByDescending(f=>f.LastWriteTimeUtc))Queue(f.FullName);}catch(IOException){Note="최근 세션 기록 조회 실패";}catch(UnauthorizedAccessException){Note="세션 기록 접근 불가";}}
        void Queue(string path) { lock(sync) dirty.Add(path); }
        static void ReadAvailable(FileStream stream,Tail tail,bool initial){
            long start=tail.Offset,limit=stream.Length;stream.Position=start;
            var buffer=new byte[64*1024];
            using(var partial=new MemoryStream()){
                while(stream.Position<limit){
                    long blockStart=stream.Position;int size=stream.Read(buffer,0,(int)Math.Min(buffer.Length,limit-blockStart));if(size==0)break;
                    int from=0;
                    for(int end=Array.IndexOf(buffer,(byte)10,from,size-from);end>=0;end=from<size?Array.IndexOf(buffer,(byte)10,from,size-from):-1){
                        if(partial.Length>0){partial.Write(buffer,from,end-from);tail.Meter.Accept(Encoding.UTF8.GetString(partial.GetBuffer(),0,(int)partial.Length));partial.SetLength(0);partial.Position=0;}
                        else if(end>from)tail.Meter.Accept(Encoding.UTF8.GetString(buffer,from,end-from));
                        tail.Offset=blockStart+end+1;from=end+1;
                    }
                    if(from<size)partial.Write(buffer,from,size-from);
                    // Initial state is reconstructed once, including a long-running turn's
                    // start and previous token total. Subsequent reads only consume new bytes.
                    // Finish at least one line even when a single record exceeds the budget.
                    if(!initial&&stream.Position-start>=2*1024*1024&&tail.Offset>start)break;
                }
            }
        }
        public TpsSummary Read() {
            bool rescan;lock(sync){rescan=rescanRequested;rescanRequested=false;}if(rescan)QueueRecent();
            string[] changed; lock(sync) { changed=dirty.Take(16).ToArray(); foreach(var p in changed) dirty.Remove(p); }
            foreach(var path in changed) {
                try {
                    Tail tail;
                    using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                        bool initial=!tails.TryGetValue(path,out tail)||stream.Length<tail.Offset;
                        if(initial){tail=new Tail();tails[path]=tail;}
                        ReadAvailable(stream,tail,initial);
                        if(stream.Length>tail.Offset) Queue(path);
                    }
                } catch(FileNotFoundException){tails.Remove(path);}catch(DirectoryNotFoundException){tails.Remove(path);}catch(IOException) { } catch(UnauthorizedAccessException) { Note="세션 기록 접근 불가"; }
            }
            var now=DateTime.UtcNow;
            if(tails.Count>32) foreach(var key in tails.Where(k=>!k.Value.Meter.Running||now-k.Value.Meter.Updated>=TpsSummary.MaxAge).OrderByDescending(k=>k.Value.Meter.Updated).Skip(32).Select(k=>k.Key).ToArray()) tails.Remove(key);
            // creator_account_id is immutable thread metadata. A resumed thread can run
            // under a different account after a switch, so it cannot filter live usage.
            var summary=TpsSummary.Aggregate(tails.Values.Select(t=>t.Meter),now,desktopRun());lock(sync)summary.PendingFiles=dirty.Count;return summary;
        }
        public void Dispose() { if(watcher!=null) watcher.Dispose(); }
    }
}
