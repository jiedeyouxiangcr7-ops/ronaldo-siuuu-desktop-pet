using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

static class PrepareAssets {
    static int[] Bounds(Bitmap b) {
        var rect=new Rectangle(0,0,b.Width,b.Height);
        var bits=b.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        byte[] pixels=new byte[bits.Stride*b.Height];Marshal.Copy(bits.Scan0,pixels,0,pixels.Length);b.UnlockBits(bits);
        int l=b.Width,t=b.Height,r=0,d=0;
        for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)if(pixels[y*bits.Stride+x*4+3]>40){l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);d=Math.Max(d,y);}
        if(l>r)throw new Exception("Empty sprite");return new[]{l,t,r+1,d+1};
    }
    static void Main(string[] args) {
        string root=args[0],assets=Path.Combine(root,"Assets");
        using(var atlas=new Bitmap(Path.Combine(assets,"poses-atlas.png"))) {
            int cw=atlas.Width/4,ch=atlas.Height/2; double scale=0;
            for(int i=0;i<8;i++)using(var cell=atlas.Clone(new Rectangle((i%4)*cw,(i/4)*ch,cw,ch),PixelFormat.Format32bppArgb)) {
                int[] a=Bounds(cell);if(i==0)scale=560.0/(a[3]-a[1]);
                using(var output=new Bitmap(512,640,PixelFormat.Format32bppArgb)) {
                    using(var g=Graphics.FromImage(output)) {
                        g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                        float w=(float)((a[2]-a[0])*scale),h=(float)((a[3]-a[1])*scale);
                        g.DrawImage(cell,new RectangleF((512-w)/2,616-h,w,h),new RectangleF(a[0],a[1],a[2]-a[0],a[3]-a[1]),GraphicsUnit.Pixel);
                    }
                    output.Save(Path.Combine(assets,"pose-"+i+".png"),ImageFormat.Png);
                }
                Console.WriteLine("pose "+i+": "+String.Join(",",a));
            }
        }
        using(var front=new Bitmap(Path.Combine(assets,"pose-0.png")))using(var icon=new Bitmap(64,64)) {
            using(var g=Graphics.FromImage(icon)) {g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(front,new Rectangle(0,0,64,64),new Rectangle(185,46,142,142),GraphicsUnit.Pixel);}
            IntPtr handle=icon.GetHicon();using(var ico=Icon.FromHandle(handle))using(var fs=File.Create(Path.Combine(assets,"ronaldo.ico")))ico.Save(fs);DestroyIcon(handle);
        }
        string decoded=Path.Combine(root,"Source","original-decoded.wav");
        if(File.Exists(decoded)) {
            byte[] wav=File.ReadAllBytes(decoded);int pos=12;
            while(System.Text.Encoding.ASCII.GetString(wav,pos,4)!="data") {int n=BitConverter.ToInt32(wav,pos+4);pos+=8+n+(n%2);}
            int data=pos+8,rate=44100,channels=2;
            // This source contains near silence until 1.94s, a shout, then audience applause.
            int first=(int)(1.94*rate),last=(int)(3.37*rate),length=(last-first)*channels*2;
            double peak=1;
            for(int i=0;i<length;i+=2)peak=Math.Max(peak,Math.Abs((int)BitConverter.ToInt16(wav,data+first*4+i)));
            using(var bw=new BinaryWriter(File.Create(Path.Combine(assets,"siuuu.wav")))) {
                bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));bw.Write(36+length);bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                bw.Write(16);bw.Write((short)1);bw.Write((short)channels);bw.Write(rate);bw.Write(rate*4);bw.Write((short)4);bw.Write((short)16);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));bw.Write(length);
                for(int i=0;i<length;i+=2) {
                    double time=i/(double)(rate*4),duration=length/(double)(rate*4);
                    double fade=Math.Min(1,Math.Min(time/.006,(duration-time)/.045));
                    double v=BitConverter.ToInt16(wav,data+first*4+i)*(.88*32767/peak)*fade;
                    bw.Write((short)Math.Max(-32767,Math.Min(32767,v)));
                }
            }
            Console.WriteLine("audio: 1.43s PCM, peak normalized -1.1dBFS, 6/45ms fades");
        }
    }
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr handle);
}
