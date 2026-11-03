using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Forms = System.Windows.Forms;

namespace RonaldoPet {
    // All time values are milliseconds. Rendering and preview share this timeline.
    public sealed class Frame {
        public int Pose, Next;
        public double Blend, Lift, Sx = 1, Sy = 1, Caption;
    }
    public static class Motion {
        public const double Duration = 3500, Landing = 1420;
        public static readonly double[] Times = {0, 240, 410, 530, 790, 1050, 1420, 1600, 2720, 3010, 3260, 3500};
        public static readonly int[] Poses = {0, 1, 1, 2, 3, 4, 5, 6, 6, 7, 0, 0};
        public static double Ease(double t) { t = Math.Max(0, Math.Min(1, t)); return t*t*(3-2*t); }
        public static Frame At(double ms) {
            Frame f = new Frame();
            ms = Math.Max(0, Math.Min(Duration, ms));
            int i = 0;
            while (i < Times.Length-2 && ms >= Times[i+1]) i++;
            f.Pose = Poses[i]; f.Next = Poses[i+1];
            // A short dissolve softens a pose cut without showing two full figures.
            double transition = Math.Min(85, Times[i+1]-Times[i]);
            f.Blend = Ease((ms - Times[i+1] + transition) / transition);
            if (ms >= 410 && ms < Landing) {
                double u = (ms-410)/(Landing-410);
                f.Lift = 82 * 4 * u * (1-u);
            }
            if (ms >= Landing && ms < 1600) {
                double squash = Math.Sin(Math.PI * (ms-Landing)/180);
                f.Sy = 1 - .045*squash; f.Sx = 1 + .025*squash;
            }
            f.Caption = ms < Landing || ms > 2720 ? 0 : Math.Min(1, (ms-Landing)/110) * Math.Min(1, (2720-ms)/160);
            return f;
        }
    }
    sealed class Preferences {
        public double Height = 300, Volume = .65, X = double.NaN, Y = double.NaN;
        public bool Muted;
        readonly string path;
        public Preferences(string folder) {
            path = Path.Combine(folder, "settings.xml");
            try {
                if (!File.Exists(path)) return;
                XElement e = XElement.Load(path);
                Height = Read(e,"height",300,180,480); Volume = Read(e,"volume",.65,0,1);
                X = Read(e,"x",double.NaN,-100000,100000); Y = Read(e,"y",double.NaN,-100000,100000);
                Muted = (string)e.Attribute("muted") == "true";
            } catch { }
        }
        static double Read(XElement e, string key, double fallback, double min, double max) {
            double v;
            return double.TryParse((string)e.Attribute(key), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v) ? Math.Max(min, Math.Min(max,v)) : fallback;
        }
        public void Save() {
            try {
                XElement e = new XElement("preferences", new XAttribute("height",Height), new XAttribute("volume",Volume),new XAttribute("muted",Muted));
                if (!double.IsNaN(X)) e.Add(new XAttribute("x", X));
                if (!double.IsNaN(Y)) e.Add(new XAttribute("y", Y));
                string temp = path+".tmp"; e.Save(temp);
                if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
            } catch (Exception ex) { Program.Log("settings: "+ex.Message); }
        }
    }
    sealed class PetArt {
        public readonly BitmapSource[] Poses = new BitmapSource[8];
        public PetArt(string folder) {
            for (int i=0;i<8;i++) {
                BitmapImage b = new BitmapImage(); b.BeginInit(); b.CacheOption=BitmapCacheOption.OnLoad;
                b.UriSource=new Uri(Path.Combine(folder,"Assets", "pose-"+i+".png")); b.EndInit(); b.Freeze(); Poses[i]=b;
            }
        }
        public void Draw(DrawingContext dc, double h, double ms, bool active, bool preview) {
            double s=h/300, w=360*s, floor=410*s;
            Frame f = active ? Motion.At(ms) : new Frame();
            if (preview) {
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(17,23,31)), null, new Rect(0,0,w,440*s),20*s,20*s);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(95,0,0,0)),null,new Point(w/2,floor+2*s),55*s,5*s);
            }
            // Pose files share a 512x640 canvas with a fixed foot baseline at y=616.
            double unit=(h/560), imageW=512*unit, imageH=640*unit;
            double x=(w-imageW)/2, y=floor-616*unit-f.Lift*s;
            dc.PushTransform(new ScaleTransform(f.Sx,f.Sy,w/2,floor-f.Lift*s));
            Rect r=new Rect(x,y,imageW,imageH);
            dc.PushOpacity(1-f.Blend); dc.DrawImage(Poses[f.Pose],r); dc.Pop();
            if (f.Blend>0) { dc.PushOpacity(f.Blend); dc.DrawImage(Poses[f.Next],r); dc.Pop(); }
            dc.Pop();
            if (f.Caption>0) {
                dc.PushOpacity(f.Caption);
                FormattedText text = new FormattedText("SIUUUU!",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Italic,FontWeights.Black,FontStretches.Normal),27*s, new SolidColorBrush(Color.FromRgb(255,219,112)),1);
                double bx=(w-text.Width)/2-15*s, by=45*s;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(232,19,26,34)),null,new Rect(bx,by,text.Width+30*s,43*s),14*s,14*s);
                dc.DrawText(text,new Point((w-text.Width)/2,by+4*s)); dc.Pop();
            }
        }
    }
    sealed class PetSurface : FrameworkElement {
        public PetArt Art;
        public double PetHeight=300, Time;
        public bool Active;
        protected override void OnRender(DrawingContext dc) { Art.Draw(dc,PetHeight,Time,Active,false); }
        protected override HitTestResult HitTestCore(PointHitTestParameters p) {
            // Transparent space around the athlete passes mouse clicks through to the desktop.
            Frame f=Active?Motion.At(Time):new Frame();
            double s=PetHeight/300, unit=PetHeight/560;
            double x=(360*s-512*unit)/2, y=410*s-616*unit-f.Lift*s;
            double px=(p.HitPoint.X-x)/unit, py=(p.HitPoint.Y-y)/unit;
            if (px<0 || px>=512 || py<0 || py>=640) return null;
            BitmapSource b=Art.Poses[f.Blend>.5?f.Next:f.Pose];
            byte[] pixel=new byte[4]; b.CopyPixels(new Int32Rect((int)px,(int)py,1,1),pixel,4,0);
            return pixel[3]>30 ? new PointHitTestResult(this,p.HitPoint) : null;
        }
    }
    sealed class PetWindow : Window {
        readonly Preferences prefs;
        readonly PetSurface surface;
        readonly MediaPlayer player=new MediaPlayer();
        readonly Stopwatch clock=new Stopwatch();
        readonly DispatcherTimer timer=new DispatcherTimer(DispatcherPriority.Render);
        Forms.NotifyIcon tray;
        bool active, landed, pressed, dragged, closing, ready;
        Point mouseStart, windowStart;
        readonly bool smoke;
        readonly string audioPath;
        public PetWindow(PetArt art, Preferences preferences, bool smokeTest) {
            prefs=preferences; smoke=smokeTest;
            Title="C罗 · SIUUUU 桌宠"; WindowStyle=WindowStyle.None; AllowsTransparency=true;
            Background=Brushes.Transparent; ResizeMode=ResizeMode.NoResize; Topmost=true;
            ShowInTaskbar=false; ShowActivated=false; WindowStartupLocation=WindowStartupLocation.Manual;
            surface=new PetSurface{Art=art, Cursor=Cursors.Hand, ToolTip="点击 Siuuu · 拖动移动 · 右键设置"};
            Content=surface; RenderOptions.SetBitmapScalingMode(surface,BitmapScalingMode.HighQuality);
            SetSize(prefs.Height);
            audioPath=Path.Combine(Program.Folder,"Assets","siuuu.wav");
            if (!File.Exists(audioPath)) audioPath=Path.Combine(Program.Folder,"Assets","siuuu-original.mp3");
            player.MediaOpened += delegate { ready=true; Program.Log("audio ready"); };
            player.MediaFailed += delegate(object o, ExceptionEventArgs e) { Program.Log("audio error: "+e.ErrorException.Message); if(!smoke && tray!=null) tray.ShowBalloonTip(4000,"音频暂时无法播放","请检查 Assets 中的声音文件。",Forms.ToolTipIcon.Warning); };
            if(File.Exists(audioPath)) player.Open(new Uri(audioPath));
            UpdateVolume();
            SourceInitialized+=delegate {
                IntPtr hwnd=new System.Windows.Interop.WindowInteropHelper(this).Handle;
                Native.SetWindowLong(hwnd,-20,Native.GetWindowLong(hwnd,-20)|0x80|0x08000000);
                System.Windows.Interop.HwndSource.FromHwnd(hwnd).AddHook(delegate(IntPtr h,int msg,IntPtr wp,IntPtr lp,ref bool handled) {
                    if(msg==0x21) { handled=true;return new IntPtr(3); }
                    if(msg==0x02E0) Dispatcher.BeginInvoke(new Action(EnsureVisible));
                    return IntPtr.Zero;
                });
            };
            Loaded+=delegate {
                Rect area=SystemParameters.WorkArea;
                Left=double.IsNaN(prefs.X)?area.Right-Width-16:prefs.X;
                Top=double.IsNaN(prefs.Y)?area.Bottom-Height-8:prefs.Y;
                EnsureVisible(); CreateTray();
                if(smoke) RunSmoke();
            };
            timer.Interval=TimeSpan.FromMilliseconds(16);
            timer.Tick+=delegate {
                surface.Time=clock.Elapsed.TotalMilliseconds;
                if(!landed && surface.Time>=Motion.Landing) { landed=true; PlaySound(); }
                if(surface.Time>=Motion.Duration) { timer.Stop(); active=false; surface.Active=false; clock.Stop(); }
                surface.InvalidateVisual();
            };
            surface.MouseLeftButtonDown+=delegate(object o,MouseButtonEventArgs e) {
                mouseStart=Native.CursorDip(this); windowStart=new Point(Left,Top); dragged=false; pressed=surface.CaptureMouse(); e.Handled=true;
            };
            surface.MouseMove+=delegate(object o,MouseEventArgs e) {
                if(!pressed || e.LeftButton!=MouseButtonState.Pressed)return;
                Point p=Native.CursorDip(this); Vector delta=p-mouseStart;
                if(delta.Length>5)dragged=true;
                if(dragged) { Left=windowStart.X+delta.X;Top=windowStart.Y+delta.Y; }
            };
            surface.MouseLeftButtonUp+=delegate(object o,MouseButtonEventArgs e) {
                if(!pressed)return;pressed=false;surface.ReleaseMouseCapture();
                if(dragged) { EnsureVisible();Save(); } else Celebrate(); e.Handled=true;
            };
            surface.LostMouseCapture+=delegate { pressed=false; };
            surface.MouseRightButtonUp+=delegate { ShowMenu(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
            Closing+=delegate { closing=true; Save(); };
            Closed+=delegate {
                timer.Stop(); player.Close(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged;
                if(tray!=null) { tray.Visible=false;tray.Dispose(); }
            };
        }
        void DisplayChanged(object o,EventArgs e) { if(!closing)Dispatcher.BeginInvoke(new Action(EnsureVisible)); }
        void SetSize(double h) {
            double bottom=Top+Height, center=Left+Width/2;
            prefs.Height=h;surface.PetHeight=h;Width=360*h/300;Height=440*h/300;
            if(IsLoaded) { Left=center-Width/2;Top=bottom-Height;EnsureVisible();Save(); }
            surface.InvalidateVisual();
        }
        void EnsureVisible() {
            var source=PresentationSource.FromVisual(this);
            double scale=source!=null?source.CompositionTarget.TransformToDevice.M11:1;
            var screen=Forms.Screen.FromPoint(new System.Drawing.Point((int)((Left+Width/2)*scale),(int)((Top+Height/2)*scale)));
            var a=screen.WorkingArea;
            if(Width>a.Width/scale || Height>a.Height/scale) {
                double fit=Math.Min(a.Width/(scale*Width),a.Height/(scale*Height))*.98;
                prefs.Height*=fit;surface.PetHeight=prefs.Height;Width=360*prefs.Height/300;Height=440*prefs.Height/300;surface.InvalidateVisual();
            }
            Left=Math.Max(a.Left/scale,Math.Min(Left,a.Right/scale-Width));
            Top=Math.Max(a.Top/scale,Math.Min(Top,a.Bottom/scale-Height));
        }
        void Save() { if(smoke)return;prefs.X=Left;prefs.Y=Top;prefs.Save(); }
        bool Celebrate() {
            if(active)return false;
            active=true;landed=false;surface.Active=true;surface.Time=0;
            player.Stop();clock.Restart();timer.Start();surface.InvalidateVisual();return true;
        }
        void PlaySound() {
            if(prefs.Muted)return;
            player.Position=TimeSpan.Zero; UpdateVolume(); player.Play();
            Program.Log("landing audio; opened="+ready);
        }
        void UpdateVolume() { player.Volume=prefs.Muted?0:prefs.Volume; }
        void CreateTray() {
            tray=new Forms.NotifyIcon();tray.Text="C罗 · 点击人物 SIUUUU";
            string icon=Path.Combine(Program.Folder,"Assets","ronaldo.ico");
            tray.Icon=File.Exists(icon)?new System.Drawing.Icon(icon):System.Drawing.SystemIcons.Application;
            tray.Visible=true;
            tray.MouseClick+=delegate(object o,Forms.MouseEventArgs e) { if(e.Button==Forms.MouseButtons.Right) ShowMenu(); else if(e.Button==Forms.MouseButtons.Left) Dispatcher.Invoke(new Action(delegate { EnsureVisible();Celebrate(); })); };
        }
        void ShowMenu() {
            Forms.ContextMenuStrip menu=new Forms.ContextMenuStrip();
            menu.Items.Add("C罗 · SIUUUU").Enabled=false;menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("庆祝一次",null,delegate {Celebrate();});
            Forms.ToolStripMenuItem size=new Forms.ToolStripMenuItem("人物大小");
            foreach(int h in new[]{220,300,380,460}) {
                int value=h;var item=new Forms.ToolStripMenuItem(h+" 像素");item.Checked=Math.Abs(prefs.Height-h)<1;
                item.Click+=delegate{SetSize(value);};size.DropDownItems.Add(item);
            }
            menu.Items.Add(size);
            Forms.ToolStripMenuItem volume=new Forms.ToolStripMenuItem("音量");
            foreach(int n in new[]{25,50,65,80,100}) {
                int value=n;var item=new Forms.ToolStripMenuItem(n+"%");item.Checked=Math.Abs(prefs.Volume*100-n)<1;
                item.Click+=delegate{prefs.Volume=value/100.0;UpdateVolume();Save();};volume.DropDownItems.Add(item);
            }
            menu.Items.Add(volume);
            var mute=new Forms.ToolStripMenuItem("静音");mute.Checked=prefs.Muted;
            mute.Click+=delegate{prefs.Muted=!prefs.Muted;UpdateVolume();Save();};menu.Items.Add(mute);
            menu.Items.Add("回到右下角",null,delegate{Rect a=SystemParameters.WorkArea;Left=a.Right-Width-16;Top=a.Bottom-Height-8;EnsureVisible();Save();});
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出",null,delegate{Close();});
            menu.Closed+=delegate{Dispatcher.BeginInvoke(new Action(delegate{menu.Dispose();}));};
            menu.Show(Forms.Cursor.Position);
        }
        void RunSmoke() {
            var t=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
            t.Tick+=delegate { t.Stop();
                bool first=Celebrate(), second=Celebrate();
                Program.Log("smoke duplicate suppression="+(first && !second));
                var end=new DispatcherTimer{Interval=TimeSpan.FromSeconds(4)};
                end.Tick+=delegate{end.Stop();Program.Log("smoke complete; idle="+!active+"; audioReady="+ready);Close();};end.Start();
            };t.Start();
        }
    }
    static class Native {
        [StructLayout(LayoutKind.Sequential)] public struct POINT {public int X,Y;}
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int n);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h,int n,int v);
        public static Point CursorDip(Visual v) {
            POINT p;GetCursorPos(out p);var s=PresentationSource.FromVisual(v);
            return s==null?new Point(p.X,p.Y):s.CompositionTarget.TransformFromDevice.Transform(new Point(p.X,p.Y));
        }
    }
    // WPF's GIF encoder saves only the first frame. Assemble standard GIF89a
    // frames from Windows' single-image encoder, preserving a palette per frame.
    sealed class AnimatedGif : IDisposable {
        readonly BinaryWriter writer;
        public AnimatedGif(string path,int width,int height) {
            writer=new BinaryWriter(File.Create(path));writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
            writer.Write((ushort)width);writer.Write((ushort)height);writer.Write((byte)0x70);writer.Write((byte)0);writer.Write((byte)0);
            writer.Write(new byte[]{0x21,0xff,11});writer.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));writer.Write(new byte[]{3,1,0,0,0});
        }
        public void Add(BitmapFrame frame,ushort delay) {
            byte[] bytes;
            using(var png=new MemoryStream()) {
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(frame);encoder.Save(png);png.Position=0;
                using(var original=new System.Drawing.Bitmap(png))using(var opaque=new System.Drawing.Bitmap(original.Width,original.Height))using(var gif=new MemoryStream()) {
                    using(var g=System.Drawing.Graphics.FromImage(opaque)){g.Clear(System.Drawing.Color.FromArgb(17,23,31));g.DrawImageUnscaled(original,0,0);}
                    opaque.Save(gif,System.Drawing.Imaging.ImageFormat.Gif);bytes=gif.ToArray();
                }
            }
            int globalLength=(bytes[10]&0x80)!=0?3*(1<<((bytes[10]&7)+1)):0;
            int pos=13+globalLength;
            while(bytes[pos]==0x21) {pos+=2;while(bytes[pos]!=0){int n=bytes[pos];pos+=1+n;}pos++;}
            if(bytes[pos]!=0x2c)throw new Exception("GIF image descriptor missing");
            byte packed=bytes[pos+9];int tableLength=(packed&0x80)!=0?3*(1<<((packed&7)+1)):globalLength;
            int tableStart=(packed&0x80)!=0?pos+10:13;
            byte size=(byte)((packed&0x80)!=0?(packed&7):(bytes[10]&7));
            writer.Write(new byte[]{0x21,0xf9,4,4});writer.Write(delay);writer.Write((byte)0);writer.Write((byte)0);
            writer.Write(bytes,pos,9);writer.Write((byte)(0x80|(packed&0x40)|size));writer.Write(bytes,tableStart,tableLength);
            int data=pos+10+((packed&0x80)!=0?tableLength:0);
            writer.Write(bytes[data++]); // LZW minimum code size
            while(bytes[data]!=0){int n=bytes[data];writer.Write(bytes,data,n+1);data+=n+1;}writer.Write((byte)0);
        }
        public void Dispose(){writer.Write((byte)0x3b);writer.Dispose();}
    }
    static class Program {
        public static readonly string Folder=AppDomain.CurrentDomain.BaseDirectory;
        public static void Log(string s) {try{File.AppendAllText(Path.Combine(Folder,"diagnostics.log"),DateTime.Now.ToString("s")+" "+s+Environment.NewLine);}catch{}}
        [STAThread] static int Main(string[] args) {
            try {
                PetArt art=new PetArt(Folder);
                if(args.Contains("--render")) {Export(art);return 0;}
                if(args.Contains("--self-test")) {SelfTest(art);return 0;}
                bool created;using(Mutex mutex=new Mutex(true,"Local\\CR7SiuuuuDesktopPet",out created)) {
                    if(!created)return 0;
                    Application app=new Application();
                    app.DispatcherUnhandledException+=delegate(object o,DispatcherUnhandledExceptionEventArgs e){Log(e.Exception.ToString());};
                    return app.Run(new PetWindow(art,new Preferences(Folder),args.Contains("--smoke-test")));
                }
            }catch(Exception e){Log(e.ToString());MessageBox.Show("桌宠启动失败："+e.Message,"C罗桌宠");return 1;}
        }
        static BitmapFrame Render(PetArt art,double h,double ms,bool active,bool preview, double dpi=96) {
            double s=h/300;DrawingVisual v=new DrawingVisual();
            using(DrawingContext dc=v.RenderOpen()){art.Draw(dc,h,ms,active,preview);}
            var bmp=new RenderTargetBitmap((int)Math.Ceiling(360*s*dpi/96),(int)Math.Ceiling(440*s*dpi/96),dpi,dpi,PixelFormats.Pbgra32);
            bmp.Render(v);bmp.Freeze();return BitmapFrame.Create(bmp);
        }
        static void Png(BitmapFrame frame,string path) {var encoder=new PngBitmapEncoder();encoder.Frames.Add(frame);using(var fs=File.Create(path))encoder.Save(fs);}
        static void Export(PetArt art) {
            string dir=Path.Combine(Folder,"Preview");Directory.CreateDirectory(dir);
            Png(Render(art,300,0,false,true),Path.Combine(dir,"正面预览.png"));
            Png(Render(art,300,1900,true,true),Path.Combine(dir,"背身庆祝.png"));
            using(var gif=new AnimatedGif(Path.Combine(dir,"庆祝动画.gif"),360,440))for(int i=0;i<82;i++) {
                double t=i<10?0:Math.Min(3500,(i-10)*50);
                var frame=Render(art,300,t,i>=10,true);
                gif.Add(frame,5);
            }
            foreach(int h in new[]{220,300,380,460}) {
                foreach(int t in new[]{0,300,700,1000,1420,1800,2900,3400})Png(Render(art,h,t,true,false),Path.Combine(dir,"check-"+h+"-"+t+".png"));
            }
            Log("rendered preview and size checks");
        }
        static void SelfTest(PetArt art) {
            int count=0;
            for(int t=0;t<=3500;t++) {
                Frame f=Motion.At(t);
                if(f.Pose<0||f.Pose>7||f.Next<0||f.Next>7||f.Blend<0||f.Blend>1||f.Lift<0||f.Lift>82.01)throw new Exception("Invalid animation at "+t);
                if(t>=Motion.Landing && f.Lift!=0)throw new Exception("Feet not landed");count++;
            }
            if(Motion.At(0).Pose!=0||Motion.At(3500).Next!=0||Motion.At(2000).Pose!=6)throw new Exception("Pose sequence mismatch");
            foreach(BitmapSource b in art.Poses)if(b.PixelWidth!=512||b.PixelHeight!=640)throw new Exception("Invalid sprite size");
            string testFolder=Path.Combine(Folder,"Source","settings-test");Directory.CreateDirectory(testFolder);
            Preferences saved=new Preferences(testFolder);saved.Height=380;saved.Volume=.5;saved.Muted=true;saved.X=123;saved.Y=234;saved.Save();
            Preferences loaded=new Preferences(testFolder);
            if(loaded.Height!=380||loaded.Volume!=.5||!loaded.Muted||loaded.X!=123||loaded.Y!=234)throw new Exception("Settings round trip failed");
            File.WriteAllText(Path.Combine(testFolder,"settings.xml"),"<preferences height='NaN' volume='-2' x='Infinity' />");
            Preferences invalid=new Preferences(testFolder);
            if(invalid.Height!=300||invalid.Volume!=0||!double.IsNaN(invalid.X))throw new Exception("Invalid settings recovery failed");
            int renders=0;
            foreach(int h in new[]{220,300,380,460})foreach(int dpi in new[]{96,120,144,192})foreach(int t in new[]{0,300,700,1000,1420,1800,2900,3400}) {
                BitmapFrame b=Render(art,h,t,true,false,dpi);
                byte[] pixels=new byte[b.PixelWidth*b.PixelHeight*4];b.CopyPixels(pixels,b.PixelWidth*4,0);
                for(int x=0;x<b.PixelWidth;x++)if(pixels[x*4+3]!=0||pixels[((b.PixelHeight-1)*b.PixelWidth+x)*4+3]!=0)throw new Exception("Vertical clipping at size="+h+" DPI="+dpi+" time="+t);
                for(int y=0;y<b.PixelHeight;y++)if(pixels[(y*b.PixelWidth)*4+3]!=0||pixels[(y*b.PixelWidth+b.PixelWidth-1)*4+3]!=0)throw new Exception("Horizontal clipping");
                renders++;
            }
            string report="PASS: "+count+" timeline samples; 8 sprites; landing, hold, return.\r\nPASS: "+renders+" transparent boundary checks (4 sizes x 4 DPIs x 8 poses/times).\r\nPASS: settings round trip (position, size, volume, mute), invalid-value recovery.\r\n";
            Log(report);
            File.WriteAllText(Path.Combine(Folder,"Source","test-result.txt"),report);
        }
    }
}
