using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace CodexRelay {
    internal sealed class ServiceError : Exception {
        public int Status { get; private set; }
        public bool RequiresLogin { get; private set; }
        public ServiceError(int status,string action,string code=""):base(action+" 실패 (HTTP "+status+"). "+(AuthRejected(status,code)?"다시 로그인하세요.":status==429?"잠시 후 다시 시도하세요.":"네트워크 또는 서비스 상태를 확인하세요.")) { Status=status; RequiresLogin=AuthRejected(status,code); }
        static bool AuthRejected(int status,string code) { return status==401 || code=="invalid_grant" || code=="invalid_token" || code=="refresh_token_reused" || code=="refresh_token_expired" || code=="refresh_token_invalidated"; }
    }
    internal sealed class Api : IDisposable {
        internal const string ClientId="app_EMoamEEZ73f0CkXaXp7hrann";
        readonly HttpClient client;
        public Api(HttpMessageHandler handler=null) {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            client=handler==null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) : new HttpClient(handler);
            client.Timeout=TimeSpan.FromSeconds(45); client.DefaultRequestHeaders.UserAgent.ParseAdd("codex_cli_rs/0.0.0 CodexRelay/0.1");
        }
        internal static string Query(IDictionary<string,string> fields) {
            var parts=new List<string>(); foreach(var kv in fields) parts.Add(Uri.EscapeDataString(kv.Key)+"="+Uri.EscapeDataString(kv.Value)); return string.Join("&",parts);
        }
        async Task<string> Token(Dictionary<string,string> fields,CancellationToken ct) {
            using(var content=new FormUrlEncodedContent(fields))
            using(var response=await client.PostAsync("https://auth.openai.com/oauth/token",content,ct)) {
                if(!response.IsSuccessStatusCode) {
                    string code="";
                    try { var error=Json.Get(Json.Parse(await Bounded(response,ct)),"error");code=error as string??Json.Str(error,"code"); } catch(ArgumentException) { } catch(InvalidOperationException) { }
                    throw new ServiceError((int)response.StatusCode,"로그인 인증",code);
                }
                return await Bounded(response,ct);
            }
        }
        public async Task<string> Exchange(string code,string redirect,string verifier,CancellationToken ct) {
            return Auth.FromResponse(await Token(new Dictionary<string,string> { {"grant_type","authorization_code"},{"client_id",ClientId},{"code",code},{"redirect_uri",redirect},{"code_verifier",verifier} },ct));
        }
        public async Task<string> Refresh(string raw,CancellationToken ct) {
            var refresh=Json.Str(Auth.Tokens(raw),"refresh_token"); if(refresh.Length==0) throw new InvalidDataException("다시 로그인해야 합니다.");
            return Auth.FromResponse(await Token(new Dictionary<string,string> { {"grant_type","refresh_token"},{"client_id",ClientId},{"refresh_token",refresh} },ct),raw);
        }
        HttpRequestMessage Request(HttpMethod method,string path,string raw) {
            var r=new HttpRequestMessage(method,"https://chatgpt.com/backend-api/"+path);
            r.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Auth.Access(raw));
            r.Headers.Add("ChatGPT-Account-Id",Auth.AccountId(raw)); r.Headers.Accept.ParseAdd("application/json"); return r;
        }
        public async Task<Usage> ReadUsage(string raw,CancellationToken ct) {
            using(var req=Request(HttpMethod.Get,"wham/usage",raw))
            using(var response=await client.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct)) {
                if(!response.IsSuccessStatusCode) throw new ServiceError((int)response.StatusCode,"사용량 조회");
                return Usage.Parse(await Bounded(response,ct),Clock.Now);
            }
        }
        public async Task Warm(string raw,CancellationToken ct) {
            var payload=new {model="gpt-6-luna", instructions="Reply with OK only.", input=new[]{new {type="message",role="user",content=new[]{new{type="input_text",text="OK"}}}},tools=new object[0],store=false,stream=true,reasoning=new{effort="low"}};
            using(var req=Request(HttpMethod.Post,"codex/responses",raw)) {
                req.Content=new StringContent(Json.Write(payload),Encoding.UTF8,"application/json");
                using(var response=await client.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct)) {
                    if(!response.IsSuccessStatusCode) throw new ServiceError((int)response.StatusCode,"웜업");
                    var body=await Bounded(response,ct);
                    bool complete=false;
                    foreach(var line in body.Split('\n')) if(line.StartsWith("data: ")) {
                        var s=line.Substring(6).Trim(); if(s=="[DONE]") continue;
                        try { var e=Json.Parse(s); string kind=Json.Str(e,"type"); if(kind=="response.completed") complete=true; if(kind=="response.failed" || kind=="error") throw new InvalidDataException("웜업 응답이 실패했습니다."); }
                        catch(ArgumentException) { }
                    }
                    if(!complete) throw new InvalidDataException("웜업 완료 응답을 확인하지 못했습니다.");
                }
            }
        }
        static async Task<string> Bounded(HttpResponseMessage response,CancellationToken ct) {
            using(var stream=await response.Content.ReadAsStreamAsync())
            using(ct.Register(()=>stream.Dispose()))
            using(var memory=new MemoryStream()) {
                byte[] buffer=new byte[8192]; int n;
                while((n=await stream.ReadAsync(buffer,0,buffer.Length,ct))>0) { if(memory.Length+n>2*1024*1024) throw new InvalidDataException("서버 응답이 너무 큽니다."); memory.Write(buffer,0,n); }
                return Encoding.UTF8.GetString(memory.ToArray());
            }
        }
        public void Dispose() { client.Dispose(); }
    }
    internal sealed class LoginFlow : IDisposable {
        readonly TcpListener listener;
        readonly string verifier;
        readonly string state;
        readonly string redirect;
        public string Url { get; private set; }
        public LoginFlow() {
            listener=new TcpListener(IPAddress.Loopback,1455); listener.Server.ExclusiveAddressUse=true;
            try { listener.Start(8); } catch(SocketException) { throw new InvalidOperationException("로그인 포트 1455가 사용 중입니다. 다른 로그인 창을 닫고 다시 시도하세요."); }
            verifier=Random(64); state=Random(32); redirect="http://localhost:1455/auth/callback";
            string challenge; using(var sha=SHA256.Create()) challenge=B64(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            Url="https://auth.openai.com/oauth/authorize?"+Api.Query(new Dictionary<string,string> {
                {"response_type","code"},{"client_id",Api.ClientId},{"redirect_uri",redirect},{"scope","openid profile email offline_access"},
                {"code_challenge",challenge},{"code_challenge_method","S256"},{"state",state},{"id_token_add_organizations","true"},{"codex_cli_simplified_flow","true"},{"originator","codex_cli_rs"},{"prompt","login"}
            });
        }
        internal static string B64(byte[] bytes) { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_'); }
        static string Random(int length) { var b=new byte[length]; using(var rng=RandomNumberGenerator.Create()) rng.GetBytes(b); return B64(b); }
        internal static bool ValidState(string expected,string actual) {
            if(actual==null || actual.Length!=expected.Length) return false; int diff=0; for(int i=0;i<expected.Length;i++) diff |= expected[i]^actual[i]; return diff==0;
        }
        public async Task<string> Complete(Api api,CancellationToken ct) {
            using(ct.Register(()=>listener.Stop())) {
                while(true) {
                    ct.ThrowIfCancellationRequested(); TcpClient socket;
                    try { socket=await listener.AcceptTcpClientAsync(); } catch { ct.ThrowIfCancellationRequested(); throw; }
                    using(socket)
                    using(var stream=socket.GetStream()) {
                        // Limit both header size and read time. Never log callback URL or code.
                        byte[] buffer=new byte[8192]; int count=0; string request="";
                        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                            timeout.CancelAfter(TimeSpan.FromSeconds(5));
                            using(timeout.Token.Register(()=>socket.Close())) try {
                                while(count<buffer.Length && !request.Contains("\r\n\r\n")) {
                                    int n=await stream.ReadAsync(buffer,count,buffer.Length-count,timeout.Token); if(n==0) break; count+=n; request=Encoding.ASCII.GetString(buffer,0,count);
                                }
                            } catch(OperationCanceledException) { ct.ThrowIfCancellationRequested(); continue; }
                            catch(IOException) { ct.ThrowIfCancellationRequested(); continue; }
                            catch(ObjectDisposedException) { ct.ThrowIfCancellationRequested(); continue; }
                        }
                        var first=request.Split('\n')[0].Trim().Split(' '); Uri callback;
                        if(first.Length!=3 || first[0]!="GET" || !first[1].StartsWith("/") || !Uri.TryCreate("http://localhost:1455"+first[1],UriKind.Absolute,out callback) || callback.AbsolutePath!="/auth/callback") { await Reply(stream,404,"로그인 콜백 경로가 아닙니다."); continue; }
                        var query=HttpUtility.ParseQueryString(callback.Query);
                        if(!ValidState(state,query["state"])) { await Reply(stream,400,"로그인 요청이 일치하지 않습니다. 앱에서 다시 로그인하세요."); continue; }
                        if(query["error"]!=null) { await Reply(stream,400,"로그인이 취소되었습니다."); throw new OperationCanceledException("로그인이 취소되었습니다."); }
                        if(string.IsNullOrEmpty(query["code"])) { await Reply(stream,400,"인증 코드가 없습니다."); continue; }
                        string raw=null; Exception error=null;
                        try { raw=await api.Exchange(query["code"],redirect,verifier,ct); Auth.Identity(raw); } catch(Exception ex) { error=ex; }
                        if(error!=null) { await Reply(stream,400,"로그인을 완료하지 못했습니다. 앱에서 다시 시도하세요."); System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw(); }
                        await Reply(stream,200,"로그인이 완료되었습니다. Codex Relay로 돌아가세요."); return raw;
                    }
                }
            }
        }
        static async Task Reply(Stream stream,int status,string message) {
            byte[] body=Encoding.UTF8.GetBytes("<!doctype html><meta charset=\"utf-8\"><title>Codex Relay</title><body style=\"font:18px system-ui;padding:60px;background:#131619;color:#eee\"><h1>Codex Relay</h1><p>"+message+"</p></body>");
            byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 "+status+" Response\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'\r\nConnection: close\r\nContent-Length: "+body.Length+"\r\n\r\n");
            try { await stream.WriteAsync(header,0,header.Length); await stream.WriteAsync(body,0,body.Length); } catch(IOException) { }
        }
        public void Dispose() { listener.Stop(); }
    }
}
