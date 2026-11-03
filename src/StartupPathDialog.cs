using System.Drawing;
using System.Windows.Forms;

namespace CodexRelay {
    internal sealed class StartupPathDialog : Form {
        internal readonly Button Keep,Change;
        public StartupPathDialog(StartupPathChange change){
            Theme.Form(this);Text="시작프로그램 경로 변경";StartPosition=FormStartPosition.CenterParent;
            FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;ClientSize=new Size(680,318);
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=6};
            foreach(int height in new[]{56,24,62,24,62,50})root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            Controls.Add(root);
            var message=Theme.Label("다른 위치의 Codex Relay가 시작프로그램으로 등록되어 있습니다.\n현재 실행 파일로 변경하시겠습니까?",10);message.Dock=DockStyle.Fill;root.Controls.Add(message,0,0);
            var oldLabel=Theme.Label("현재 시작프로그램 등록",9,Theme.Muted);oldLabel.Dock=DockStyle.Fill;root.Controls.Add(oldLabel,0,1);
            root.Controls.Add(PathBox(change.RegisteredCommand),0,2);
            var newLabel=Theme.Label("변경할 실행 파일",9,Theme.Muted);newLabel.Dock=DockStyle.Fill;root.Controls.Add(newLabel,0,3);
            root.Controls.Add(PathBox(change.CurrentExecutable),0,4);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,12,0,0)};
            Keep=Theme.Button("기존 경로 유지",142);Keep.DialogResult=DialogResult.Cancel;
            Change=Theme.Button("현재 경로로 변경",156);Change.DialogResult=DialogResult.OK;
            buttons.Controls.Add(Keep);buttons.Controls.Add(Change);root.Controls.Add(buttons,0,5);
            AcceptButton=Keep;CancelButton=Keep;ActiveControl=Keep;
        }
        static ThemedTextArea PathBox(string text){return new ThemedTextArea(text){Dock=DockStyle.Fill,Margin=new Padding(0,2,0,6)};}
    }
}
