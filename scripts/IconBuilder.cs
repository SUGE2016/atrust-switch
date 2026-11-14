using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using ATrustSwitch;

static class IconBuilder {
    static void Main(string[] args) {
        int[] sizes={16,24,32,48,64,128,256}; var images=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++) {
            using(var bitmap=new Bitmap(sizes[i],sizes[i],PixelFormat.Format32bppArgb)) {
                using(var g=Graphics.FromImage(bitmap)) { g.Clear(Color.Transparent); IconArt.Draw(g,new RectangleF(0,0,sizes[i],sizes[i])); }
                using(var memory=new MemoryStream()) { bitmap.Save(memory,ImageFormat.Png); images[i]=memory.ToArray(); }
                if(sizes[i]==256) bitmap.Save(args[1],ImageFormat.Png);
            }
        }
        using(var writer=new BinaryWriter(File.Create(args[0]))) {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) {
                writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset); offset+=images[i].Length;
            }
            foreach(byte[] image in images) writer.Write(image);
        }
    }
}
