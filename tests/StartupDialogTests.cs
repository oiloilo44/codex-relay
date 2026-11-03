using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CodexRelay {
    internal static partial class Tests {
        static void StartupDialogChecks(){
            string previous="\"C:\\Users\\example\\Downloads\\Codex Relay\\CodexRelay.exe\" --tray";
            string current="C:\\Users\\example\\Documents\\도구\\Codex Relay\\CodexRelay.exe";
            string images=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-preview-hidden");Directory.CreateDirectory(images);
            foreach(bool dark in new[]{false,true}){
                Theme.SetDark(dark);
                using(var dialog=new StartupPathDialog(new StartupPathChange(previous,current,"\""+current+"\" --tray"))){
                    PrepareHidden(dialog);dialog.PerformLayout();
                    var boxes=Controls(dialog).OfType<TextBox>().ToArray();
                    Check(boxes.Length==2&&boxes.All(x=>x.ReadOnly)&&boxes.Any(x=>x.Text==previous)&&boxes.Any(x=>x.Text==current),"startup dialog shows both complete paths in read-only fields (dark="+dark+")");
                    Check(dialog.AcceptButton==dialog.Keep&&dialog.CancelButton==dialog.Keep&&dialog.ActiveControl==dialog.Keep&&dialog.Keep.DialogResult==DialogResult.Cancel&&dialog.Change.DialogResult==DialogResult.OK,"Enter Escape and initial focus default to keeping the existing path (dark="+dark+")");
                    Check(dialog.Keep.Parent.ClientRectangle.Contains(dialog.Keep.Bounds)&&dialog.Change.Parent.ClientRectangle.Contains(dialog.Change.Bounds)&&boxes.All(x=>x.Height>=40),"startup dialog fields and both action buttons fit (dark="+dark+")");
                    using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(images,"startup-path-"+(dark?"dark":"light")+".png"));}
                    Check(!dialog.Visible,"startup dialog render never displays a live popup");
                }
            }
            Theme.SetDark(false);
        }
    }
}
