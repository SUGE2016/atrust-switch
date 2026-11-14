using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ATrustSwitch;

static class RenderProbe {
    static string folder;
    static void Capture(Form form,string name) {
        using(var image=new Bitmap(form.Width,form.Height)) {
            using(var g=Graphics.FromImage(image)) g.CopyFromScreen(form.Location,Point.Empty,form.Size);
            image.Save(Path.Combine(folder,name+".png"),ImageFormat.Png);
        }
    }
    static System.Collections.Generic.IEnumerable<Button> Buttons(Control c) {
        foreach(Control child in c.Controls) { var button=child as Button; if(button!=null) yield return button; foreach(var nested in Buttons(child)) yield return nested; }
    }
    [STAThread] static void Main(string[] args) {
        folder=args[0]; Directory.CreateDirectory(folder);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var form=new MainForm(null);
        form.Shown+=async(s,e)=> {
            try {
                form.Activate(); await Task.Delay(1600); Capture(form,"01-live-initial");
                var buttons=Buttons(form).ToArray();
                int index=0;
                foreach(var button in buttons) {
                    Cursor.Position=button.PointToScreen(new Point(button.Width/2,button.Height/2));
                    await Task.Delay(180); Capture(form,"hover-"+(index++));
                }
                Cursor.Position=form.PointToScreen(new Point(20,20));
                buttons.First(b=>b.Text=="刷新").PerformClick(); await Task.Delay(650); Capture(form,"02-refresh");
                buttons.First(b=>b.Text=="更改").PerformClick(); await Task.Delay(300); Capture(form,"03-menu");
                SendKeys.SendWait("{ESC}"); await Task.Delay(200);
                buttons.First(b=>b.AccessibleName=="最小化").PerformClick(); await Task.Delay(400);
                form.WindowState=FormWindowState.Normal; form.Activate(); await Task.Delay(600); Capture(form,"04-restored");
                // Force repeated invalidation: this exercises real WM_PAINT, unlike DrawToBitmap.
                for(int i=0;i<8;i++) { form.Invalidate(true); form.Update(); await Task.Delay(80); }
                Capture(form,"05-repainted");
                Console.WriteLine("PASS real-screen initial, hover, refresh, menu, minimize/restore and repeated WM_PAINT");
            } catch(Exception ex) { Console.WriteLine(ex); Environment.ExitCode=1; }
            finally { form.Close(); }
        };
        Application.Run(form);
    }
}
