using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CodexRelay {
    internal static partial class Tests {
        static void ScrollInput(Control target,string method,object argument){target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,new[]{argument});}
        static void CheckScrollPalette(ThemeScrollBar bar,string label){
            using(var bitmap=new Bitmap(bar.Width,bar.Height)){
                bar.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));var thumb=bar.ThumbBounds;
                Check(bitmap.GetPixel(0,0).ToArgb()==Theme.ScrollTrack.ToArgb()&&bitmap.GetPixel(thumb.Left+thumb.Width/2,thumb.Top+thumb.Height/2).ToArgb()==Theme.ScrollThumb.ToArgb(),label);
            }
        }
        static void ScrollbarChecks(){
            string images=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-preview-hidden");Directory.CreateDirectory(images);
            var vault=new Vault(Path.Combine(root,"scrollbar-vault"));vault.Data.Settings.DarkMode=false;
            for(int i=0;i<12;i++){var account=vault.Put(Raw("scroll"+i),"스크롤 검증 "+(i+1));account.Usage=Usage.Parse(Limits(10+i,20+i,Clock.Now),Clock.Now);}
            var http=new FakeHttp{Handler=r=>{throw new Exception("Scrollbar tests must stay offline");}};
            using(var engine=new Engine(vault,Path.Combine(root,"scrollbar-home"),new Api(http),new FakeGuard(),true))
            using(var form=new MainForm(engine,true,false,new StartupRegistration(new FakeStartupStore(),Application.ExecutablePath))){
                engine.SyncLive();CallMain(form,"Render",new object[]{null});CallMain(form,"ApplyTheme");PrepareHidden(form);
                var host=Controls(form).OfType<ThemedGridHost>().Single();var grid=Controls(form).OfType<SmoothGrid>().Single();host.RefreshScrollbars();
                var bar=host.Vertical;var dark=Controls(form).OfType<CheckBox>().Single(c=>(string)c.Tag=="다크 모드");
                Check(host.VerticalNeeded&&!host.HorizontalNeeded&&bar.Maximum>0&&grid.ScrollBars==ScrollBars.None,"account overflow uses the themed vertical bar without a native scrollbar");
                bar.Value=bar.Maximum;var last=grid.GetRowDisplayRectangle(grid.Rows.Count-1,false);
                Check(grid.FirstDisplayedScrollingRowIndex>0&&last.Top>=0&&last.Bottom<=grid.ClientSize.Height,"account thumb reaches the last row without clipping its lower actions");
                bar.Value=0;ScrollInput(grid,"OnMouseWheel",new HandledMouseEventArgs(MouseButtons.None,0,20,20,-120));
                Check(SystemInformation.MouseWheelScrollLines==0||bar.Value>0,"account mouse wheel still scrolls when native scrollbars are disabled");
                ScrollInput(bar,"OnKeyDown",new KeyEventArgs(Keys.End));Check(bar.Value==bar.Maximum,"scrollbar End key reaches the end");
                ScrollInput(bar,"OnKeyDown",new KeyEventArgs(Keys.Home));Check(bar.Value==0,"scrollbar Home key returns to the beginning");
                var pageThumb=bar.ThumbBounds;
                ScrollInput(bar,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,bar.Width/2,pageThumb.Bottom+3,0));
                ScrollInput(bar,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,bar.Width/2,pageThumb.Bottom+3,0));
                Check(bar.Value==Math.Min(bar.Maximum,bar.PageSize),"clicking the scrollbar track moves by one visible page");bar.Value=0;
                var thumb=bar.ThumbBounds;int middle=thumb.Top+thumb.Height/2;
                ScrollInput(bar,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,bar.Width/2,middle,0));
                ScrollInput(bar,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,bar.Width/2,bar.Height+thumb.Height,0));
                ScrollInput(bar,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,bar.Width/2,bar.Height,0));
                Check(bar.Value==bar.Maximum,"dragging the account thumb clamps to the actual last page");
                ScrollInput(bar,"OnMouseLeave",EventArgs.Empty);bar.Value=2;
                grid.CurrentCell=grid.Rows[2].Cells[1];string selected=((Account)grid.Rows[2].Tag).Id;int position=bar.Value;
                CheckScrollPalette(bar,"account scrollbar renders the light palette");
                using(var bitmap=ClientPreview(form))bitmap.Save(Path.Combine(images,"scrollbars-main-light.png"));
                dark.Checked=true;
                CheckScrollPalette(bar,"account scrollbar changes to the dark palette immediately");
                Check(bar.Value==position&&((Account)grid.CurrentRow.Tag).Id==selected,"theme switching preserves scroll position and selected account");
                using(var bitmap=ClientPreview(form))bitmap.Save(Path.Combine(images,"scrollbars-main-dark.png"));
                dark.Checked=false;CheckScrollPalette(bar,"account scrollbar changes back to the light palette");
                vault.Data.Accounts.RemoveRange(1,vault.Data.Accounts.Count-1);CallMain(form,"Render",new object[]{null});host.RefreshScrollbars();
                Check(!host.VerticalNeeded&&!host.HorizontalNeeded,"account scrollbar disappears when the remaining rows fit");
                Check(!form.Visible&&http.Paths.Count==0,"account scrollbar checks remain hidden and offline");
            }
            using(var holder=new Form())using(var grid=new SmoothGrid{ColumnHeadersVisible=false,RowHeadersVisible=false,AllowUserToAddRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None,BorderStyle=BorderStyle.None}){
                holder.ClientSize=new Size(300,160);Theme.Form(holder);grid.Columns.Add(new DataGridViewTextBoxColumn{Width=320});grid.Columns.Add(new DataGridViewTextBoxColumn{Width=320});
                for(int i=0;i<12;i++)grid.Rows.Add("row "+i,"column 2");
                var host=new ThemedGridHost(grid);holder.Controls.Add(host);PrepareHidden(holder);host.RefreshScrollbars();
                Check(host.HorizontalNeeded&&host.VerticalNeeded,"horizontal and vertical overflow share the same scrollbar control");
                host.Horizontal.Value=host.Horizontal.Maximum;
                Check(grid.HorizontalScrollingOffset==host.Horizontal.Maximum&&grid.HorizontalScrollingOffset>0,"horizontal thumb reaches the rightmost content");
                foreach(bool dark in new[]{false,true}){Theme.SetDark(dark);Theme.Apply(holder);CheckScrollPalette(host.Horizontal,"horizontal scrollbar follows palette (dark="+dark+")");}
                holder.ClientSize=new Size(900,600);holder.PerformLayout();host.RefreshScrollbars();Check(!host.HorizontalNeeded&&!host.VerticalNeeded,"both bars disappear after resizing to fit the content");
            }
            Theme.SetDark(false);
            foreach(float scale in new[]{1.25f,1.5f,2f}){
                using(var holder=new Form())using(var grid=new SmoothGrid{ColumnHeadersVisible=false,RowHeadersVisible=false,AllowUserToAddRows=false,BorderStyle=BorderStyle.None}){
                    Theme.Form(holder);holder.ClientSize=new Size(300,160);grid.Columns.Add(new DataGridViewTextBoxColumn{Width=500});for(int i=0;i<15;i++)grid.Rows.Add("scale "+i);
                    var host=new ThemedGridHost(grid);holder.Controls.Add(host);PrepareHidden(holder);holder.Scale(new SizeF(scale,scale));grid.Columns[0].Width=(int)Math.Round(500*scale);foreach(DataGridViewRow row in grid.Rows)row.Height=(int)Math.Round(22*scale);holder.PerformLayout();host.RefreshScrollbars();
                    Check(host.Vertical.Width>=Math.Round(14*scale)-1&&grid.Right==host.Vertical.Left&&grid.Bottom==host.Horizontal.Top&&host.Vertical.Right<=host.ClientSize.Width,"scaled grid scrollbar geometry stays separate from content at "+scale);
                    host.Vertical.Value=host.Vertical.Maximum;host.Horizontal.Value=host.Horizontal.Maximum;
                    Check(host.Vertical.Value>0&&host.Horizontal.Value>0,"scaled scrollbars retain their full travel at "+scale);
                }
            }
            string longPath="C:\\"+string.Join("\\",Enumerable.Repeat("시작프로그램_경로_검증",15))+"\\CodexRelay.exe";
            using(var dialog=new StartupPathDialog(new StartupPathChange("\""+longPath+"\" --tray",longPath,longPath))){
                PrepareHidden(dialog);var areas=Controls(dialog).OfType<ThemedTextArea>().ToArray();foreach(var pathField in areas)pathField.RefreshScroll(true);
                Check(areas.Length==2&&areas.All(a=>a.ScrollNeeded&&a.Editor.ScrollBars==ScrollBars.None),"both long startup paths use the common scrollbar instead of native edit scrollbars");
                var area=areas[0];area.Editor.Select(0,12);string selection=area.Editor.SelectedText;
                area.Vertical.Value=area.Vertical.Maximum;
                Check(area.Editor.FirstLine==area.Vertical.Value&&area.Editor.FirstLine>0&&area.Editor.SelectedText==selection,"path thumb scrolling preserves text selection for copying");
                area.Vertical.Value=0;area.Editor.WheelScroll(-120);
                Check(SystemInformation.MouseWheelScrollLines==0||area.Editor.FirstLine>0,"path mouse wheel scrolls the native text contents");
                area.Editor.SelectionStart=area.Editor.TextLength;area.Editor.SelectionLength=0;area.Editor.ScrollToCaret();
                Check(area.Vertical.Value>0&&area.Vertical.Value==Math.Min(area.Editor.FirstLine,area.Vertical.Maximum),"caret and keyboard scrolling synchronize the path thumb");
                foreach(bool dark in new[]{false,true,false}){
                    Theme.SetDark(dark);Theme.Apply(dialog);foreach(var field in areas)CheckScrollPalette(field.Vertical,"startup path scrollbar follows live palette (dark="+dark+")");
                    using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(images,"scrollbars-path-"+(dark?"dark":"light")+".png"));}
                }
                foreach(var field in areas){field.Editor.Text="C:\\Tools\\CodexRelay.exe";field.RefreshScroll(true);}
                Check(areas.All(a=>!a.ScrollNeeded&&a.Editor.FirstLine==0),"short startup paths hide unnecessary scrollbars and reset the position");
                Check(!dialog.Visible,"startup scrolling tests never show a live confirmation popup");
            }
            Theme.SetDark(false);
        }
    }
}
