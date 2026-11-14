using System.Drawing;
using System.Drawing.Drawing2D;

namespace ATrustSwitch {
    static class IconArt {
        public static void Draw(Graphics g,RectangleF rect) {
            var saved=g.Save();
            g.SmoothingMode=SmoothingMode.AntiAlias;
            g.TranslateTransform(rect.X,rect.Y); g.ScaleTransform(rect.Width/256F,rect.Height/256F);
            using(var shape=Round(new RectangleF(8,8,240,240),58)) {
                using(var fill=new LinearGradientBrush(new PointF(20,15),new PointF(238,251),Color.FromArgb(78,131,255),Color.FromArgb(39,61,181))) g.FillPath(fill,shape);
                using(var pen=new Pen(Color.FromArgb(90,255,255,255),2)) g.DrawPath(pen,shape);
            }
            using(var pen=new Pen(Color.FromArgb(50,10,35,123),20)) {
                pen.StartCap=pen.EndCap=LineCap.Round; g.DrawArc(pen,66,73,124,124,-48,276);
            }
            using(var pen=new Pen(Color.White,18)) {
                pen.StartCap=pen.EndCap=LineCap.Round; g.DrawArc(pen,66,67,124,124,-48,276);
                g.DrawLine(pen,128,60,128,123);
            }
            using(var brush=new SolidBrush(Color.FromArgb(119,238,214))) g.FillEllipse(brush,118,49,20,20);
            g.Restore(saved);
        }
        static GraphicsPath Round(RectangleF r,float radius) {
            float d=radius*2; var p=new GraphicsPath(); p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
        }
    }
}
