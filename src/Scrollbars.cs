using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexRelay {
    // Both grid and text scrolling use this palette and interaction model.
    internal sealed class ThemeScrollBar : Control {
        readonly Orientation orientation;
        readonly Timer repeat=new Timer();
        int maximum,page=1,value,wheelRemainder,dragOffset,pointer;
        bool dragging,hovered;
        public event EventHandler ValueChanged;
        public int SmallChange=1;
        public int Maximum {get{return maximum;}}
        public int PageSize {get{return page;}}
        public int Value {get{return value;}set{int next=Math.Max(0,Math.Min(maximum,value));if(this.value==next)return;this.value=next;Invalidate();AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1);var changed=ValueChanged;if(changed!=null)changed(this,EventArgs.Empty);}}
        public ThemeScrollBar(Orientation orientation){
            this.orientation=orientation;Size=orientation==Orientation.Vertical?new Size(14,100):new Size(100,14);
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
            TabStop=true;AccessibleRole=AccessibleRole.ScrollBar;
            repeat.Tick+=(s,e)=>{repeat.Interval=60;PageTowardPointer();};
        }
        public void SetRange(int maximum,int pageSize,int position){
            int nextMax=Math.Max(0,maximum),nextPage=Math.Max(1,pageSize),nextValue=Math.Max(0,Math.Min(nextMax,position));
            if(this.maximum==nextMax&&page==nextPage&&value==nextValue)return;
            this.maximum=nextMax;page=nextPage;value=nextValue;Invalidate();
        }
        int Length {get{return orientation==Orientation.Vertical?Height:Width;}}
        int Cross {get{return orientation==Orientation.Vertical?Width:Height;}}
        int Inset {get{return Math.Max(2,Cross/6);}}
        int Axis(Point p){return orientation==Orientation.Vertical?p.Y:p.X;}
        internal Rectangle ThumbBounds {
            get{
                int inset=Inset,track=Math.Max(1,Length-inset*2);
                int size=Math.Min(track,Math.Max(Cross*2,(int)Math.Round(track*page/(double)(maximum+page))));
                int start=inset+(maximum==0?0:(int)Math.Round((track-size)*value/(double)maximum));
                return orientation==Orientation.Vertical?new Rectangle(inset,start,Math.Max(1,Cross-inset*2),size):new Rectangle(start,inset,size,Math.Max(1,Cross-inset*2));
            }
        }
        public void ScrollWheel(int delta){
            int lines=SystemInformation.MouseWheelScrollLines;if(lines==0)return;
            wheelRemainder+=delta;int steps=wheelRemainder/SystemInformation.MouseWheelScrollDelta;
            wheelRemainder%=SystemInformation.MouseWheelScrollDelta;
            Value-=steps*(lines<0?page:Math.Max(1,lines)*SmallChange);
        }
        protected override void OnMouseWheel(MouseEventArgs e){ScrollWheel(e.Delta);var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;}
        protected override void OnMouseDown(MouseEventArgs e){
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||maximum==0)return;
            pointer=Axis(e.Location);var thumb=ThumbBounds;dragging=thumb.Contains(e.Location);Capture=true;
            if(dragging)dragOffset=pointer-(orientation==Orientation.Vertical?thumb.Top:thumb.Left);
            else {PageTowardPointer();repeat.Interval=400;repeat.Start();}
            Invalidate();
        }
        void PageTowardPointer(){var thumb=ThumbBounds;int start=orientation==Orientation.Vertical?thumb.Top:thumb.Left,end=orientation==Orientation.Vertical?thumb.Bottom:thumb.Right;if(pointer<start)Value-=page;else if(pointer>=end)Value+=page;}
        protected override void OnMouseMove(MouseEventArgs e){
            base.OnMouseMove(e);pointer=Axis(e.Location);bool nextHover=ThumbBounds.Contains(e.Location);if(nextHover!=hovered){hovered=nextHover;Invalidate();}
            if(!dragging)return;var thumb=ThumbBounds;int size=orientation==Orientation.Vertical?thumb.Height:thumb.Width;
            int travel=Math.Max(1,Length-Inset*2-size);Value=(int)Math.Round((pointer-dragOffset-Inset)*maximum/(double)travel);
        }
        protected override void OnMouseUp(MouseEventArgs e){if(e.Button==MouseButtons.Left){dragging=false;repeat.Stop();Capture=false;Invalidate();}base.OnMouseUp(e);}
        protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture){dragging=false;repeat.Stop();Invalidate();}base.OnMouseCaptureChanged(e);}
        protected override void OnMouseLeave(EventArgs e){hovered=false;Invalidate();base.OnMouseLeave(e);}
        protected override bool IsInputKey(Keys keys){switch(keys&Keys.KeyCode){case Keys.Left:case Keys.Right:case Keys.Up:case Keys.Down:case Keys.PageUp:case Keys.PageDown:case Keys.Home:case Keys.End:return true;default:return base.IsInputKey(keys);}}
        protected override void OnKeyDown(KeyEventArgs e){
            switch(e.KeyCode){case Keys.Up:case Keys.Left:Value-=SmallChange;break;case Keys.Down:case Keys.Right:Value+=SmallChange;break;case Keys.PageUp:Value-=page;break;case Keys.PageDown:Value+=page;break;case Keys.Home:Value=0;break;case Keys.End:Value=maximum;break;default:base.OnKeyDown(e);return;}
            e.Handled=true;e.SuppressKeyPress=true;
        }
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
        protected override void OnPaint(PaintEventArgs e){
            e.Graphics.Clear(Theme.ScrollTrack);if(maximum>0){var r=ThumbBounds;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;int d=Math.Min(r.Width,r.Height);using(var shape=new GraphicsPath())using(var brush=new SolidBrush(dragging?Theme.Accent:hovered?Theme.ScrollHover:Theme.ScrollThumb)){
                shape.AddArc(r.Left,r.Top,d,d,180,90);shape.AddArc(r.Right-d,r.Top,d,d,270,90);shape.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);shape.AddArc(r.Left,r.Bottom-d,d,d,90,90);shape.CloseFigure();e.Graphics.FillPath(brush,shape);
            }}
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle,Theme.Muted,Theme.ScrollTrack);
        }
        protected override AccessibleObject CreateAccessibilityInstance(){return new ScrollAccessibility(this);}
        sealed class ScrollAccessibility : ControlAccessibleObject {
            readonly ThemeScrollBar bar;
            public ScrollAccessibility(ThemeScrollBar bar):base(bar){this.bar=bar;}
            public override string Value {get{return bar.Value.ToString();}set{int position;if(int.TryParse(value,out position))bar.Value=position;}}
        }
        protected override void Dispose(bool disposing){if(disposing)repeat.Dispose();base.Dispose(disposing);}
    }

    internal sealed class ThemedGridHost : UserControl {
        readonly SmoothGrid grid;
        internal readonly ThemeScrollBar Vertical=new ThemeScrollBar(Orientation.Vertical){AccessibleName="계정 목록 세로 스크롤"};
        internal readonly ThemeScrollBar Horizontal=new ThemeScrollBar(Orientation.Horizontal){AccessibleName="계정 목록 가로 스크롤",SmallChange=24};
        bool updating,verticalNeeded,horizontalNeeded;
        internal bool VerticalNeeded {get{return verticalNeeded;}}
        internal bool HorizontalNeeded {get{return horizontalNeeded;}}
        public ThemedGridHost(SmoothGrid grid){
            this.grid=grid;Dock=DockStyle.Fill;Margin=grid.Margin;TabStop=false;grid.Margin=Padding.Empty;grid.Dock=DockStyle.None;grid.ScrollBars=ScrollBars.None;
            Controls.Add(grid);Controls.Add(Vertical);Controls.Add(Horizontal);Vertical.Visible=Horizontal.Visible=false;
            Vertical.ValueChanged+=(s,e)=>{if(updating)return;var rows=grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible&&!r.Frozen).ToArray();if(rows.Length>0)grid.FirstDisplayedScrollingRowIndex=rows[Math.Min(Vertical.Value,rows.Length-1)].Index;RefreshScrollbars();};
            Horizontal.ValueChanged+=(s,e)=>{if(!updating){grid.HorizontalScrollingOffset=Horizontal.Value;RefreshScrollbars();}};
            grid.Scroll+=(s,e)=>RefreshScrollbars();grid.RowsAdded+=(s,e)=>RefreshScrollbars();grid.RowsRemoved+=(s,e)=>RefreshScrollbars();grid.RowHeightChanged+=(s,e)=>RefreshScrollbars();grid.ColumnWidthChanged+=(s,e)=>RefreshScrollbars();grid.ColumnStateChanged+=(s,e)=>RefreshScrollbars();grid.HandleCreated+=(s,e)=>RefreshScrollbars();
            grid.WheelScroll=delta=>{if((ModifierKeys&Keys.Shift)!=0&&horizontalNeeded)Horizontal.ScrollWheel(delta);else Vertical.ScrollWheel(delta);};
        }
        protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);RefreshScrollbars();}
        internal void RefreshScrollbars(){
            if(updating||grid==null||grid.IsDisposed)return;updating=true;
            try{
                var rows=grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible&&!r.Frozen).ToArray();
                for(int pass=0;pass<3;pass++){
                    int width=Math.Max(1,ClientSize.Width-(verticalNeeded?Vertical.Width:0)),height=Math.Max(1,ClientSize.Height-(horizontalNeeded?Horizontal.Height:0));
                    grid.SetBounds(0,0,width,height);
                    int header=grid.ColumnHeadersVisible?grid.ColumnHeadersHeight:0;
                    bool nextVertical=rows.Sum(r=>r.Height)>Math.Max(1,height-header);
                    bool nextHorizontal=grid.Columns.Cast<DataGridViewColumn>().Where(c=>c.Visible).Sum(c=>c.Width)>width;
                    if(verticalNeeded==nextVertical&&horizontalNeeded==nextHorizontal)break;
                    verticalNeeded=nextVertical;horizontalNeeded=nextHorizontal;
                }
                int contentWidth=Math.Max(1,ClientSize.Width-(verticalNeeded?Vertical.Width:0)),contentHeight=Math.Max(1,ClientSize.Height-(horizontalNeeded?Horizontal.Height:0));
                grid.SetBounds(0,0,contentWidth,contentHeight);
                Vertical.SetBounds(contentWidth,0,Vertical.Width,contentHeight);Horizontal.SetBounds(0,contentHeight,contentWidth,Horizontal.Height);
                Vertical.Visible=verticalNeeded;Horizontal.Visible=horizontalNeeded;
                int page=Math.Max(1,grid.DisplayedRowCount(false));int position=Array.FindIndex(rows,r=>r.Index==grid.FirstDisplayedScrollingRowIndex);
                Vertical.SetRange(Math.Max(0,rows.Length-page),page,Math.Max(0,position));
                int totalWidth=grid.Columns.Cast<DataGridViewColumn>().Where(c=>c.Visible).Sum(c=>c.Width);
                Horizontal.SetRange(Math.Max(0,totalWidth-contentWidth),contentWidth,grid.HorizontalScrollingOffset);
            }finally{updating=false;}
        }
    }

    internal sealed class ScrollTextBox : TextBox {
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        public event EventHandler ScrollPositionChanged;
        public Action<int> WheelScroll;
        public int FirstLine {get{return IsHandleCreated?(int)SendMessage(Handle,0xCE,IntPtr.Zero,IntPtr.Zero):0;}}
        public int LineCount {get{return IsHandleCreated?(int)SendMessage(Handle,0xBA,IntPtr.Zero,IntPtr.Zero):1;}}
        public void ScrollTo(int line){if(IsHandleCreated)SendMessage(Handle,0xB6,IntPtr.Zero,(IntPtr)(line-FirstLine));}
        protected override void WndProc(ref Message m){
            if(m.Msg==0x20A&&WheelScroll!=null){WheelScroll(unchecked((short)((long)m.WParam>>16)));m.Result=IntPtr.Zero;return;}
            base.WndProc(ref m);
            if(m.Msg==0x115||m.Msg==0x100||m.Msg==0x101||m.Msg==0xB6||m.Msg==0xB7||m.Msg==0xB1||m.Msg==0x200||m.Msg==0x202||m.Msg==0x113){var changed=ScrollPositionChanged;if(changed!=null)changed(this,EventArgs.Empty);}
        }
    }
    internal sealed class ThemedTextArea : UserControl {
        internal readonly ScrollTextBox Editor=new ScrollTextBox{Multiline=true,ReadOnly=true,WordWrap=true,ScrollBars=ScrollBars.None,BorderStyle=BorderStyle.None,HideSelection=false};
        internal readonly ThemeScrollBar Vertical=new ThemeScrollBar(Orientation.Vertical){AccessibleName="경로 세로 스크롤"};
        bool updating,needed;
        internal bool ScrollNeeded {get{return needed;}}
        public ThemedTextArea(string text){
            Tag="surface";BackColor=Theme.Panel;ForeColor=Theme.Text;Padding=new Padding(4);TabStop=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            Editor.BackColor=Theme.Panel;Editor.ForeColor=Theme.Text;Controls.Add(Editor);Controls.Add(Vertical);Vertical.Visible=false;
            Editor.Text=text;Editor.TextChanged+=(s,e)=>RefreshScroll(true);Editor.FontChanged+=(s,e)=>RefreshScroll(true);Editor.HandleCreated+=(s,e)=>RefreshScroll(true);
            Editor.ScrollPositionChanged+=(s,e)=>RefreshScroll(false);Editor.WheelScroll=delta=>Vertical.ScrollWheel(delta);
            Vertical.ValueChanged+=(s,e)=>{if(!updating){Editor.ScrollTo(Vertical.Value);RefreshScroll(false);}};
        }
        protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);RefreshScroll(true);}
        internal void RefreshScroll(bool measure){
            if(updating||Editor==null||Editor.IsDisposed)return;updating=true;
            try{
                int first=Editor.FirstLine,width=Math.Max(1,ClientSize.Width-Padding.Horizontal),height=Math.Max(1,ClientSize.Height-Padding.Vertical);
                if(measure){Editor.SetBounds(Padding.Left,Padding.Top,width,height);needed=Editor.LineCount>Math.Max(1,height/Editor.Font.Height);}
                Editor.SetBounds(Padding.Left,Padding.Top,Math.Max(1,width-(needed?Vertical.Width:0)),height);
                Vertical.SetBounds(ClientSize.Width-Padding.Right-Vertical.Width,Padding.Top,Vertical.Width,height);Vertical.Visible=needed;
                int page=Math.Max(1,height/Editor.Font.Height),maximum=needed?Math.Max(0,Editor.LineCount-page):0;
                if(measure)Editor.ScrollTo(Math.Min(first,maximum));
                Vertical.SetRange(maximum,page,Editor.FirstLine);
            }finally{updating=false;}
        }
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var pen=new Pen(Theme.Line))e.Graphics.DrawRectangle(pen,0,0,Math.Max(0,Width-1),Math.Max(0,Height-1));}
    }
}
