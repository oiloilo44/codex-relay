using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexRelay {
    internal static class Json {
        public static string Write(object value) { return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Serialize(value); }
        public static T Read<T>(string text) { return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Deserialize<T>(text); }
        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(object o, string key) { object v; return Obj(o).TryGetValue(key, out v) ? v : null; }
        public static string Str(object o, string key) { return Convert.ToString(Get(o,key), CultureInfo.InvariantCulture); }
        public static long Num(object o, string key, long fallback = 0) { long v; return long.TryParse(Str(o,key), out v) ? v : fallback; }
        public static double? Double(object o, string key) { double v; return double.TryParse(Str(o,key), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v) ? (double?)v : null; }
        public static bool Bool(object o, string key) { return Get(o,key) is bool && (bool)Get(o,key); }
        public static Dictionary<string, object> Parse(string s) { return Read<Dictionary<string, object>>(s); }
    }
    internal static class Clock {
        public static long Now { get { return (long)(DateTime.UtcNow - new DateTime(1970,1,1)).TotalSeconds; } }
        public static DateTime Local(long unix) { return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(unix).ToLocalTime(); }
    }
    public sealed class Account {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Plan { get; set; }
        public string AuthJson { get; set; }
        public string LastWarmKey { get; set; }
        public long LastWarmAt { get; set; }
        public string WarmStatus { get; set; }
        public Usage Usage { get; set; }
        public bool NeedsLogin { get; set; }
        public Account() { Id=""; Name=""; Email=""; Plan=""; AuthJson=""; LastWarmKey=""; WarmStatus=""; }
        public override string ToString() { return Name; }
    }
    public sealed class Settings {
        public bool AutoRotate { get; set; }
        public bool AutoWarm { get; set; }
        public bool Overlay { get; set; }
        public bool DarkMode { get; set; }
        // Null identifies vaults written before startup preference persistence existed.
        public bool? StartWithWindows { get; set; }
        int overlayTransparency=50;
        public int OverlayTransparency { get { return overlayTransparency; } set { overlayTransparency=Math.Max(0,Math.Min(90,value)); } }
        [ScriptIgnore] public double OverlayOpacity { get { return (100-OverlayTransparency)/100d; } }
        public int OverlayX { get; set; }
        public int OverlayY { get; set; }
        public Settings() { Overlay=true;DarkMode=true;OverlayX=-1; OverlayY=-1; }
    }
    public sealed class VaultData {
        public int Version { get; set; }
        public List<Account> Accounts { get; set; }
        public Settings Settings { get; set; }
        public string BackupAuth { get; set; }
        public VaultData() { Version=1; Accounts=new List<Account>(); Settings=new Settings(); BackupAuth=""; }
    }
    public sealed class WindowUsage {
        public double Used { get; set; }
        public long ResetAt { get; set; }
        public long Seconds { get; set; }
        public string Display { get { return Used.ToString("0.#", CultureInfo.InvariantCulture)+"%"; } }
        public double Remaining { get { return Math.Max(0,Math.Min(100,100-Used)); } }
        public string RemainingDisplay { get { return Remaining.ToString("0.#", CultureInfo.InvariantCulture)+"%"; } }
    }
    public sealed class Usage {
        public WindowUsage FiveHour { get; set; }
        public WindowUsage Weekly { get; set; }
        public bool Reached { get; set; }
        public bool Allowed { get; set; }
        public long FetchedAt { get; set; }
        public string Error { get; set; }
        public Usage() { Error=""; }
        public bool Fresh(long now) { return string.IsNullOrEmpty(Error) && FetchedAt>0 && now>=FetchedAt && now-FetchedAt<=150; }
        public bool Exhausted { get { return Reached || (FiveHour!=null && FiveHour.Used>=100) || (Weekly!=null && Weekly.Used>=100); } }
        public bool Available(long now) { return Fresh(now) && Allowed && !Exhausted && (FiveHour!=null || Weekly!=null) && !ExpiredWindow(now); }
        public bool ExpiredWindow(long now) { return (FiveHour!=null && FiveHour.ResetAt>0 && FiveHour.ResetAt<=now) || (Weekly!=null && Weekly.ResetAt>0 && Weekly.ResetAt<=now); }
        public static Usage Parse(string body, long now) {
            var root=Json.Parse(body); var rate=Json.Get(root,"rate_limit");
            var u=new Usage { FetchedAt=now, Reached=Json.Bool(rate,"limit_reached"), Allowed=Json.Bool(rate,"allowed") };
            foreach(var key in new[]{"primary_window","secondary_window"}) {
                var w=Json.Get(rate,key); var used=Json.Double(w,"used_percent"); if(w==null || !used.HasValue) continue;
                var v=new WindowUsage { Used=Math.Max(0,Math.Min(100,used.Value)), ResetAt=Json.Num(w,"reset_at"), Seconds=Json.Num(w,"limit_window_seconds") };
                if(v.Seconds==18000) u.FiveHour=v; else if(v.Seconds==604800) u.Weekly=v;
            }
            if(u.FiveHour==null && u.Weekly==null) u.Error="한도 정보 없음";
            return u;
        }
    }
    internal static class Rotation {
        public static Account Next(IList<Account> accounts, string activeId, long now) {
            int index=-1; for(int i=0;i<accounts.Count;i++) if(accounts[i].Id==activeId) index=i;
            if(index<0) return null;
            for(int step=1;step<accounts.Count;step++) { var a=accounts[(index+step)%accounts.Count]; if(!a.NeedsLogin && a.Usage!=null && a.Usage.Available(now)) return a; }
            return null;
        }
        public static string WarmKey(Usage u) { if(u==null) return ""; var w=u.FiveHour??u.Weekly; return w==null || w.ResetAt<=0 ? "" : w.Seconds+":"+w.ResetAt; }
        public static bool WarmDue(Account a,long now) {
            var u=a.Usage; if(a.NeedsLogin || u==null || !u.Available(now)) return false;
            string key=WarmKey(u); return key.Length>0 && key!=a.LastWarmKey && (a.LastWarmAt==0 || now-a.LastWarmAt>=300);
        }
    }
    internal static class Auth {
        public static Dictionary<string,object> Claims(string token) {
            try { var p=token.Split('.')[1].Replace('-','+').Replace('_','/'); p=p.PadRight((p.Length+3)/4*4,'='); return Json.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(p))); } catch { return new Dictionary<string,object>(); }
        }
        public static object Tokens(string raw) { return Json.Get(Json.Parse(raw),"tokens"); }
        public static string Access(string raw) { return Json.Str(Tokens(raw),"access_token"); }
        public static string AccountId(string raw) { var t=Tokens(raw); string id=Json.Str(t,"account_id"); return id.Length>0?id:Json.Str(Json.Get(Claims(Json.Str(t,"id_token")),"https://api.openai.com/auth"),"chatgpt_account_id"); }
        public static string Identity(string raw) {
            if(!string.IsNullOrEmpty(Json.Str(Json.Parse(raw),"OPENAI_API_KEY"))) throw new InvalidDataException("API 키 계정은 지원하지 않습니다. ChatGPT로 로그인하세요.");
            var t=Tokens(raw); var c=Claims(Json.Str(t,"id_token")); var subject=Json.Str(c,"sub"); var id=Json.Str(t,"account_id");
            if(id.Length==0) id=Json.Str(Json.Get(c,"https://api.openai.com/auth"),"chatgpt_account_id");
            if(subject.Length==0 || id.Length==0 || Json.Str(t,"access_token").Length==0) throw new InvalidDataException("ChatGPT 로그인 정보가 올바르지 않습니다.");
            using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(id+"|"+subject))).Replace("-","").ToLowerInvariant();
        }
        public static Account ToAccount(string raw, string name) {
            string id=Identity(raw); var t=Tokens(raw); var c=Claims(Json.Str(t,"id_token")); var x=Json.Get(c,"https://api.openai.com/auth");
            string email=Json.Str(c,"email"); return new Account { Id=id, AuthJson=raw, Email=email, Plan=Json.Str(x,"chatgpt_plan_type"), Name=string.IsNullOrWhiteSpace(name)?(email.Length>0?email:"계정 "+id.Substring(0,6)):name.Trim() };
        }
        public static bool Expiring(string raw) { return Json.Num(Claims(Access(raw)),"exp")<=Clock.Now+90; }
        public static string FromResponse(string body, string previous=null) {
            var result=Json.Parse(body); var root=previous==null ? new Dictionary<string,object>() : Json.Parse(previous);
            var t=previous==null ? new Dictionary<string,object>() : Json.Obj(Json.Get(root,"tokens"));
            foreach(var k in new[]{"id_token","access_token","refresh_token"}) if(!string.IsNullOrEmpty(Json.Str(result,k))) t[k]=Json.Str(result,k);
            var id=Json.Str(Json.Get(Claims(Json.Str(t,"id_token")),"https://api.openai.com/auth"),"chatgpt_account_id");
            if(id.Length>0) t["account_id"]=id;
            root["tokens"]=t; root["last_refresh"]=DateTime.UtcNow.ToString("o"); return Json.Write(root);
        }
    }
    internal sealed class Vault {
        public readonly string DirectoryPath;
        public readonly string FilePath;
        public VaultData Data { get; private set; }
        public Vault(string path) {
            DirectoryPath=Path.GetFullPath(path); Directory.CreateDirectory(DirectoryPath); LockDirectory(DirectoryPath);
            FilePath=Path.Combine(DirectoryPath,"vault.bin");
            if(File.Exists(FilePath)) {
                try { Data=Json.Read<VaultData>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath),null,DataProtectionScope.CurrentUser))); }
                catch { throw new InvalidDataException("계정 보관함을 읽을 수 없습니다. 기존 vault.bin과 vault.bin.bak을 보존했습니다. 같은 Windows 사용자로 실행하세요."); }
                if(Data==null || Data.Version!=1 || Data.Accounts==null || Data.Settings==null) throw new InvalidDataException("지원하지 않는 계정 보관함입니다.");
            } else Data=new VaultData();
        }
        public static void LockDirectory(string path) {
            var acl=new DirectorySecurity(); acl.SetAccessRuleProtection(true,false);
            acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            Directory.SetAccessControl(path,acl);
        }
        public void Save() { Atomic.Write(FilePath,ProtectedData.Protect(Encoding.UTF8.GetBytes(Json.Write(Data)),null,DataProtectionScope.CurrentUser),FilePath+".bak"); }
        public Account Put(string auth,string name) {
            var parsed=Auth.ToAccount(auth,name); var old=Data.Accounts.FirstOrDefault(a=>a.Id==parsed.Id);
            if(old==null) { Data.Accounts.Add(parsed); old=parsed; } else {
                if(old.AuthJson!=auth || old.NeedsLogin) old.Usage=null;
                old.AuthJson=auth; old.NeedsLogin=false; old.Email=parsed.Email; old.Plan=parsed.Plan; if(!string.IsNullOrWhiteSpace(name)) old.Name=name.Trim();
            }
            Save(); return old;
        }
    }
    internal static class Atomic {
        public static void Write(string path,byte[] bytes,string backup=null) {
            string temp=path+".pending";
            using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { f.Write(bytes,0,bytes.Length); f.Flush(true); }
            if(File.Exists(path)) File.Replace(temp,path,backup,true); else File.Move(temp,path);
        }
    }
}
