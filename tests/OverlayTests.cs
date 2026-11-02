using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CodexRelay {
    internal static partial class Tests {
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        sealed class OverlayBlocker : Form {
            protected override bool ShowWithoutActivation {get{return true;}}
            protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;return p;}}
        }
        static void PumpUntil(Func<bool> done,int milliseconds){var watch=Stopwatch.StartNew();while(!done()&&watch.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(10);}}
        static void MoveStack(Form form,int after){if(!SetWindowPos(form.Handle,new IntPtr(after),0,0,0,0,0x0001|0x0002|0x0010|0x0200))throw new Exception("Could not prepare test window Z order");}
        static void OverlayWindowChecks(){
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            var foreground=GetForegroundWindow();var area=Screen.PrimaryScreen.WorkingArea;
            var settings=new Settings{OverlayX=area.Left+24,OverlayY=area.Top+24,OverlayTransparency=37};
            using(var overlay=new Overlay(settings,()=>{},()=>{}))
            using(var blocker=new OverlayBlocker{Text="Relay overlay test fixture",ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(area.Left+24,area.Top+24,300,118)}) {
                Check(!OverlayZOrder.Repair(overlay)&&!overlay.IsHandleCreated,"hidden overlay repair never creates or shows a window");
                overlay.Show();blocker.Show();Application.DoEvents();
                var bounds=overlay.Bounds;var opacity=overlay.Opacity;
                Check(overlay.Visible&&OverlayZOrder.IsTopMost(overlay.Handle)&&GetForegroundWindow()==foreground,"showing the overlay preserves the user's foreground window");
                MoveStack(overlay,1);
                Check(overlay.TopMost&&!OverlayZOrder.IsTopMost(overlay.Handle),"native demotion reproduces stale managed TopMost state");
                PumpUntil(()=>OverlayZOrder.IsTopMost(overlay.Handle)&&!OverlayZOrder.CoveredByNormalWindow(overlay.Handle),2400);
                Check(OverlayZOrder.IsTopMost(overlay.Handle)&&!OverlayZOrder.CoveredByNormalWindow(overlay.Handle),"visible overlay timer repairs native demotion without a metrics or quota refresh");
                Check(overlay.Bounds==bounds&&overlay.Opacity==opacity&&GetForegroundWindow()==foreground,"automatic recovery preserves bounds opacity and keyboard focus");
                Check(!OverlayZOrder.Repair(overlay),"healthy overlay does not continuously rewrite native window order");
                MoveStack(blocker,-1);
                Check(!OverlayZOrder.Repair(overlay),"healthy overlay does not compete with other topmost windows");
                blocker.Hide();overlay.Hide();
                Check(!OverlayZOrder.Repair(overlay)&&!overlay.Visible,"explicit overlay off stays hidden during recovery");
                var timer=(System.Windows.Forms.Timer)typeof(Overlay).GetField("stackingTimer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(overlay);
                Check(!timer.Enabled,"hidden overlay stops its stacking monitor");
                overlay.Show();Application.DoEvents();
                Check(timer.Enabled&&OverlayZOrder.IsTopMost(overlay.Handle),"showing the overlay resumes monitoring");
                typeof(Control).GetMethod("RecreateHandle",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(overlay,null);Application.DoEvents();
                Check(OverlayZOrder.IsTopMost(overlay.Handle)&&overlay.Bounds==bounds&&GetForegroundWindow()==foreground,"handle recreation retains topmost placement geometry and focus");
            }
            Check(GetForegroundWindow()==foreground,"overlay fixture cleanup preserves the user's foreground window");
            Console.WriteLine("OFFLINE: synthetic windows only; no real account access, quit input or authentication changes.");
        }
    }
}
