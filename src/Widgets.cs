using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexRelay {
    internal sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer {
        sealed class MenuColors : ProfessionalColorTable {
            public override Color ToolStripDropDownBackground {get{return Theme.Panel;}}
            public override Color ImageMarginGradientBegin {get{return Theme.Panel;}}
            public override Color ImageMarginGradientMiddle {get{return Theme.Panel;}}
            public override Color ImageMarginGradientEnd {get{return Theme.Panel;}}
            public override Color MenuBorder {get{return Theme.Line;}}
            public override Color MenuItemBorder {get{return ThemeMenuRenderer.Hover;}}
            public override Color MenuItemSelected {get{return ThemeMenuRenderer.Hover;}}
            public override Color MenuItemSelectedGradientBegin {get{return ThemeMenuRenderer.Hover;}}
            public override Color MenuItemSelectedGradientEnd {get{return ThemeMenuRenderer.Hover;}}
            public override Color MenuItemPressedGradientBegin {get{return ThemeMenuRenderer.Hover;}}
            public override Color MenuItemPressedGradientMiddle {get{return ThemeMenuRenderer.Hover;}}
            public override Color MenuItemPressedGradientEnd {get{return ThemeMenuRenderer.Hover;}}
            public override Color SeparatorDark {get{return Theme.Line;}}
            public override Color SeparatorLight {get{return Theme.Line;}}
        }
        static Color Hover {get{return Color.FromArgb((Theme.Panel.R*84+Theme.Accent.R*16)/100,(Theme.Panel.G*84+Theme.Accent.G*16)/100,(Theme.Panel.B*84+Theme.Accent.B*16)/100);}}
        public ThemeMenuRenderer():base(new MenuColors()){RoundedEdges=false;}
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled?Theme.Text:Theme.Muted;var rect=new Rectangle(e.TextRectangle.Left,0,e.TextRectangle.Width,e.Item.Height);var flags=(e.TextFormat&~TextFormatFlags.Bottom)|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine;TextRenderer.DrawText(e.Graphics,e.Text,e.TextFont,rect,e.TextColor,flags);}
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e){e.ArrowColor=e.Item.Enabled?Theme.Text:Theme.Muted;base.OnRenderArrow(e);}
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var p=new Pen(Theme.Line))e.Graphics.DrawLine(p,8,e.Item.Height/2,Math.Max(8,e.Item.Width-8),e.Item.Height/2);}
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e){var r=e.ImageRectangle;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=new Pen(e.Item.Enabled?Theme.Accent:Theme.Muted,2)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLines(p,new[]{new Point(r.Left+3,r.Top+r.Height/2),new Point(r.Left+r.Width/2-1,r.Bottom-4),new Point(r.Right-2,r.Top+3)});}}
    }
    internal static class QuotaLabels {
        public const string FiveHour="5시간 한도",Weekly="주간 한도";
        public static bool ShowFiveHour(Usage usage){return usage==null||usage.FiveHour!=null||usage.Weekly==null;}
        public static bool ShowWeekly(Usage usage){return usage==null||usage.Weekly!=null||usage.FiveHour==null;}
        public static string Remaining(WindowUsage window){return window==null?"—":window.RemainingDisplay+" 남음";}
        public static string ResetAt(WindowUsage window){return window==null||window.ResetAt<=0?"재설정 시각 없음":Clock.Local(window.ResetAt).ToString("yyyy-MM-dd HH:mm",System.Globalization.CultureInfo.InvariantCulture);}
    }
    internal sealed class TransparencySlider : Control {
        int value=50;bool dragging;
        public event EventHandler ValueChanged;
        public int Value {get{return value;}set{int next=Math.Max(0,Math.Min(90,value));if(this.value==next)return;this.value=next;Invalidate();AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1);var changed=ValueChanged;if(changed!=null)changed(this,EventArgs.Empty);}}
        public TransparencySlider(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque|ControlStyles.Selectable,true);Height=26;TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.Slider;}
        protected override bool IsInputKey(Keys keyData){var key=keyData&Keys.KeyCode;return key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down||key==Keys.Home||key==Keys.End||key==Keys.PageUp||key==Keys.PageDown||base.IsInputKey(keyData);}
        protected override void OnKeyDown(KeyEventArgs e){int next=Value;switch(e.KeyCode){case Keys.Left:case Keys.Down:next-=5;break;case Keys.Right:case Keys.Up:next+=5;break;case Keys.PageDown:next-=10;break;case Keys.PageUp:next+=10;break;case Keys.Home:next=0;break;case Keys.End:next=90;break;default:base.OnKeyDown(e);return;}Value=next;e.Handled=true;e.SuppressKeyPress=true;base.OnKeyDown(e);}
        void SetFromMouse(int x){using(var g=CreateGraphics()){int inset=(int)Math.Round(8*g.DpiX/96f);Value=(int)Math.Round((x-inset)*100d/Math.Max(1,Width-2*inset));}}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||!Enabled)return;Focus();dragging=true;Capture=true;SetFromMouse(e.X);}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging)SetFromMouse(e.X);}
        protected override void OnMouseUp(MouseEventArgs e){if(e.Button==MouseButtons.Left&&dragging){SetFromMouse(e.X);dragging=false;Capture=false;}base.OnMouseUp(e);}
        protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture)dragging=false;base.OnMouseCaptureChanged(e);}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
        protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;float scale=g.DpiX/96f,inset=8*scale,y=Height/2f,end=Math.Max(inset,Width-inset),x=inset+(end-inset)*Value/100f;using(var rail=new Pen(Theme.Line,3*scale)){rail.StartCap=rail.EndCap=LineCap.Round;g.DrawLine(rail,inset,y,end,y);}using(var fill=new Pen(Enabled?Theme.Accent:Theme.Muted,3*scale)){fill.StartCap=fill.EndCap=LineCap.Round;g.DrawLine(fill,inset,y,x,y);}using(var thumb=new SolidBrush(Enabled?Theme.Accent:Theme.Muted))g.FillEllipse(thumb,x-5*scale,y-5*scale,10*scale,10*scale);if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,ClientRectangle,Theme.Muted,BackColor);}
        protected override AccessibleObject CreateAccessibilityInstance(){return new SliderAccessibility(this);}
        sealed class SliderAccessibility : ControlAccessibleObject {
            readonly TransparencySlider slider;
            public SliderAccessibility(TransparencySlider slider):base(slider){this.slider=slider;}
            public override string Value {get{return slider.Value+"%";}set{int next;if(int.TryParse((value??"").Trim().TrimEnd('%'),out next))slider.Value=next;}}
        }
    }
    internal sealed class ToggleSwitch : CheckBox {
        public ToggleSwitch(){AutoSize=false;Size=new Size(70,26);FlatStyle=FlatStyle.Flat;UseVisualStyleBackColor=false;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true);Cursor=Cursors.Hand;}
        protected override void OnCheckedChanged(EventArgs e){base.OnCheckedChanged(e);Invalidate();}
        protected override void OnPaint(PaintEventArgs e){
            // Own the entire surface so native checkbox text and hover/focus paint cannot remain underneath.
            var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;float s=g.DpiX/96f;int w=(int)(32*s),h=(int)(18*s),x=1,y=(Height-h)/2;
            Color fill=!Enabled?Theme.Line:Checked?Theme.Accent:Theme.Line;
            using(var brush=new SolidBrush(fill))using(var shape=Round(new Rectangle(x,y,w,h),h/2)){g.FillPath(brush,shape);}
            int d=(int)(12*s),inset=(int)(3*s);using(var b=new SolidBrush(Checked&&Theme.IsDark?Theme.Back:Color.White))g.FillEllipse(b,Checked?x+w-d-inset:x+inset,y+inset,d,d);
            TextRenderer.DrawText(g,Checked?"ON":"OFF",Font,new Rectangle(x+w+(int)(5*s),0,Math.Max(0,Width-w-(int)(6*s)),Height),Theme.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,ClientRectangle,Theme.Muted,BackColor);
        }
        static GraphicsPath Round(Rectangle r,int radius){var p=new GraphicsPath();int d=radius*2;p.AddArc(r.X,r.Y,d,d,90,180);p.AddArc(r.Right-d,r.Y,d,d,270,180);p.CloseFigure();return p;}
    }
    internal sealed class QuotaGauge : Control {
        WindowUsage value;bool fresh;string caption="";
        readonly Font small=new Font("맑은 고딕",8.5f),number=new Font("맑은 고딕",20f);
        public WindowUsage Value{get{return value;}set{this.value=value;Invalidate();}}
        public bool Fresh{get{return fresh;}set{fresh=value;Invalidate();}}
        public string Caption{get{return caption;}set{caption=value;Invalidate();}}
        public QuotaGauge(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Height=98;}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;float s=g.DpiX/96f;int left=(int)(8*s),width=Math.Max(1,Width-left-(int)(16*s));
            string label=caption;
            TextRenderer.DrawText(g,label,small,new Rectangle(left,0,width,(int)(22*s)),Theme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            string display=value==null?"—":value.RemainingDisplay;
            TextRenderer.DrawText(g,display,value==null?Font:number,new Rectangle(left,(int)(22*s),width,(int)(36*s)),fresh?Theme.Text:Theme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            if(value!=null){int numberWidth=TextRenderer.MeasureText(g,display,number,new Size(width,(int)(36*s)),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;TextRenderer.DrawText(g,"남음",small,new Rectangle(left+numberWidth+(int)(7*s),(int)(32*s),Math.Max(0,width-numberWidth-(int)(7*s)),(int)(24*s)),Theme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);}
            int y=(int)(65*s),height=Math.Max(3,(int)(4*s));using(var b=new SolidBrush(Theme.Line))g.FillRectangle(b,left,y,width,height);
            if(value!=null){Color color=fresh?(value.Remaining<30?Theme.Warning:Theme.Accent):Theme.Muted;using(var b=new SolidBrush(color))g.FillRectangle(b,left,y,(float)(width*value.Remaining/100),height);}
            string reset=value==null?"":value.ResetAt>0?"재설정 "+QuotaLabels.ResetAt(value):"재설정 시각 없음";
            TextRenderer.DrawText(g,reset,small,new Rectangle(left,(int)(73*s),width,(int)(24*s)),Theme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
        }
        protected override void Dispose(bool disposing){if(disposing){small.Dispose();number.Dispose();}base.Dispose(disposing);}
    }
    internal sealed class SmoothGrid : DataGridView {public SmoothGrid(){DoubleBuffered=true;}}
}
