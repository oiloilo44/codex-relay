using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CodexRelay {
    internal static class Program {
        [STAThread] static void Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool demo=Array.IndexOf(args,"--demo")>=0;
            bool startInTray=Array.IndexOf(args,"--tray")>=0;
            string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexRelay");
            string home=Environment.GetEnvironmentVariable("CODEX_HOME"); if(string.IsNullOrWhiteSpace(home))home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
            if(demo)data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"demo-data");
            string suffix=demo?"Demo":"";
            bool created; using(var singleton=new Mutex(true,"Local\\CodexRelay"+suffix,out created)) {
                if(!created){try{using(var signal=EventWaitHandle.OpenExisting("Local\\CodexRelayShow"+suffix))signal.Set();}catch{MessageBox.Show("Codex Relay가 이미 실행 중입니다. 트레이 아이콘을 열어주세요.","Codex Relay");}return;}
                try {
                    var vault=new Vault(data);
                    vault.Data.Settings.Overlay=true;
                    if(demo){vault.Data.Accounts.Clear();for(int i=0;i<3;i++)vault.Data.Accounts.Add(new Account{Id="demo"+i,Name=new[]{"업무 계정","개인 계정","예비 계정"}[i],Plan="plus",Usage=new Usage{Allowed=true,FetchedAt=Clock.Now,FiveHour=new WindowUsage{Used=new[]{64,18,0}[i],Seconds=18000,ResetAt=Clock.Now+7200},Weekly=new WindowUsage{Used=new[]{72,41,8}[i],Seconds=604800,ResetAt=Clock.Now+300000}}});vault.Data.Settings.AutoRotate=false;vault.Data.Settings.AutoWarm=false;}
                    using(var engine=new Engine(vault,home,new Api(),new ProcessGuard(),demo))using(var form=new MainForm(engine,demo))using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\CodexRelayShow"+suffix)) {
                        var registration=ThreadPool.RegisterWaitForSingleObject(signal,(s,t)=>{try{if(form.IsHandleCreated&&!form.IsDisposed)form.BeginInvoke((Action)form.ShowMain);}catch(InvalidOperationException){}},null,-1,false);
                        try{
                            if(startInTray){
                                var handle=form.Handle;
                                using(var context=new ApplicationContext()){
                                    form.FormClosed+=(s,e)=>context.ExitThread();
                                    form.BeginInvoke((Action)(async()=>await form.InitializeAsync()));
                                    Application.Run(context);
                                }
                            }else Application.Run(form);
                        }finally{registration.Unregister(null);}
                    }
                } catch(Exception e){MessageBox.Show(Engine.Friendly(e),"Codex Relay",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            }
        }
    }
}
