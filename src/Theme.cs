using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ATrustSwitch {
    static class Shapes {
        public static GraphicsPath Round(RectangleF r,float radius) {
            float d=radius*2; var p=new GraphicsPath();
            p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
        }
        public static void Power(Graphics g,RectangleF r,Color color,float width) {
            using(var pen=new Pen(color,width)) {
                pen.StartCap=pen.EndCap=LineCap.Round;
                g.DrawArc(pen,r,-48,276); g.DrawLine(pen,r.X+r.Width/2,r.Y-r.Height*0.12F,r.X+r.Width/2,r.Y+r.Height*0.45F);
            }
        }
    }
    sealed class Surface : Panel {
        public float Factor=1; public Color Fill=Color.White; public bool Divider;
        public Surface() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); BackColor=Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=Shapes.Round(new RectangleF(0.5F,0.5F,Width-1,Height-1),16*Factor)) {
                if(Divider) { using(var brush=new LinearGradientBrush(ClientRectangle,Color.FromArgb(243,250,248),Color.White,90F)) e.Graphics.FillPath(brush,path); }
                else using(var brush=new SolidBrush(Fill)) e.Graphics.FillPath(brush,path);
                using(var pen=new Pen(Color.FromArgb(231,235,241))) e.Graphics.DrawPath(pen,path);
            }
            if(Divider) using(var pen=new Pen(Color.FromArgb(228,237,235))) e.Graphics.DrawLine(pen,20*Factor,110*Factor,Width-20*Factor,110*Factor);
            base.OnPaint(e);
        }
    }
    sealed class Mark : Control {
        public float Factor=1; public bool Brand,Complete; public Color Accent=Color.FromArgb(29,133,105);
        public Mark() { SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); BackColor=Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            if(Brand) { IconArt.Draw(g,ClientRectangle); return; }
            using(var brush=new SolidBrush(Color.FromArgb(226,243,235))) g.FillEllipse(brush,1,1,Width-2,Height-2);
            using(var pen=new Pen(Color.FromArgb(205,231,218),Factor)) g.DrawEllipse(pen,1,1,Width-2,Height-2);
            if(Complete) using(var pen=new Pen(Accent,2.2F*Factor)) {
                pen.StartCap=pen.EndCap=LineCap.Round; pen.LineJoin=LineJoin.Round;
                g.DrawLines(pen,new[]{new PointF(Width*.29F,Height*.51F),new PointF(Width*.44F,Height*.65F),new PointF(Width*.73F,Height*.36F)});
            } else Shapes.Power(g,new RectangleF(Width*.29F,Height*.30F,Width*.42F,Height*.42F),Accent,2*Factor);
        }
    }
    sealed class ModernButton : Button {
        public float Factor=1; public string Kind="normal"; public string Glyph="";
        bool hover,down;
        public ModernButton() {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque,true);
            SetStyle(ControlStyles.SupportsTransparentBackColor,false);
            FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; UseVisualStyleBackColor=false;
            BackColor=Color.FromArgb(247,249,252); Cursor=Cursors.Hand;
        }
        protected override void OnPaintBackground(PaintEventArgs e) {
            // ButtonBase's transparent background path replays parent content incorrectly
            // during actual WM_PAINT. Paint every pixel locally, including rounded corners.
            e.Graphics.Clear(BackColor);
        }
        protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover=down=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down=true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics; g.Clear(BackColor); g.SmoothingMode=SmoothingMode.AntiAlias;
            bool primary=Kind=="primary",quiet=Kind=="quiet",close=Kind=="close";
            Color fill=primary ? Color.FromArgb(54,99,224) : Color.White;
            Color ink=primary ? Color.White : Color.FromArgb(54,65,84);
            if(hover) fill=primary ? Color.FromArgb(43,83,201) : Color.FromArgb(234,239,247);
            if(close && hover) { fill=Color.FromArgb(223,70,75); ink=Color.White; }
            if(down) fill=primary ? Color.FromArgb(34,69,176) : Color.FromArgb(220,229,242);
            if(!Enabled) { fill=Color.FromArgb(234,238,244); ink=Color.FromArgb(154,165,181); }
            using(var path=Shapes.Round(new RectangleF(1,1,Width-2,Height-2),10*Factor)) {
                if(primary && Enabled) { using(var b=new LinearGradientBrush(ClientRectangle,fill,Color.FromArgb(down?35:48,down?63:79,down?166:200),90F)) g.FillPath(b,path); }
                else if(!(quiet||close) || hover || !Enabled) using(var b=new SolidBrush(fill)) g.FillPath(b,path);
                if(Kind=="normal") using(var p=new Pen(Color.FromArgb(221,227,237))) g.DrawPath(p,path);
                if(Focused && ShowFocusCues) using(var p=new Pen(Color.FromArgb(129,159,240),2*Factor)) g.DrawPath(p,path);
            }
            float cx=Width/2F,cy=Height/2F;
            if(Glyph=="close" || Glyph=="min") using(var pen=new Pen(ink,1.3F*Factor)) {
                float r=4*Factor;
                if(Glyph=="close") { g.DrawLine(pen,cx-r,cy-r,cx+r,cy+r); g.DrawLine(pen,cx-r,cy+r,cx+r,cy-r); }
                else g.DrawLine(pen,cx-r,cy,cx+r,cy);
            } else {
                Rectangle area=ClientRectangle;
                if(Glyph=="start" || Glyph=="stop") {
                    int textWidth=TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;
                    float groupWidth=textWidth+24*Factor;
                    float x=(Width-groupWidth)/2+5*Factor; float y=cy;
                    using(var b=new SolidBrush(ink)) {
                        if(Glyph=="start") g.FillPolygon(b,new[]{new PointF(x-3*Factor,y-5*Factor),new PointF(x+5*Factor,y),new PointF(x-3*Factor,y+5*Factor)});
                        else using(var p=Shapes.Round(new RectangleF(x-4*Factor,y-4*Factor,8*Factor,8*Factor),2*Factor)) g.FillPath(b,p);
                    }
                    area.X=(int)(x+16*Factor); area.Width=textWidth+(int)(5*Factor);
                }
                TextRenderer.DrawText(g,Text,Font,area,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform);
            }
        }
    }
}
