using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using QuickCrop;
class Tests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void SameCrop(RectangleF a, RectangleF b, string message) {
        Check(Math.Abs(a.X-b.X)<.002 && Math.Abs(a.Y-b.Y)<.002 && Math.Abs(a.Width-b.Width)<.002 && Math.Abs(a.Height-b.Height)<.002,message);
    }
    static void NearColor(Color a, Color b, int tolerance, string message) {
        Check(Math.Abs(a.R-b.R)<=tolerance && Math.Abs(a.G-b.G)<=tolerance && Math.Abs(a.B-b.B)<=tolerance && Math.Abs(a.A-b.A)<=tolerance,message+": "+a+" / "+b);
    }
    static void Pump(Task task) {
        Stopwatch timeout = Stopwatch.StartNew();
        while (!task.IsCompleted) {
            Check(timeout.Elapsed.TotalSeconds < 30,"UI operation timed out");
            Application.DoEvents(); Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
    }
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target); }
    static void Mouse(CropView view, string method, MouseEventArgs args) {
        typeof(CropView).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[] { args });
    }
    static Bitmap Gradient() {
        Bitmap bitmap = new Bitmap(1200,800,PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.WriteOnly,bitmap.PixelFormat);
        try {
            byte[] pixels = new byte[data.Stride*data.Height];
            for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) {
                int p = y*data.Stride+x*4; pixels[p] = 80; pixels[p+1] = (byte)(y*240/bitmap.Height); pixels[p+2] = (byte)(x*240/bitmap.Width); pixels[p+3] = 255;
            }
            Marshal.Copy(pixels,0,data.Scan0,pixels.Length);
        } finally { bitmap.UnlockBits(data); }
        return bitmap;
    }
    static void ExportCorrespondence(string folder) {
        using (Bitmap original = Gradient()) {
            foreach (int dpi in new[] { 72,96,144,300 }) {
                original.SetResolution(dpi,dpi); string path = Path.Combine(folder,"gradient-"+dpi+".png"); original.Save(path,ImageFormat.Png);
                for (int turns = 0; turns < 4; turns++) {
                    using (Card card = new Card(path,Imaging.Thumbnail(path),16.0/9,delegate {}))
                    using (Bitmap expected = (Bitmap)original.Clone()) {
                        for (int n = 0; n < turns; n++) { card.RotateLeft(); expected.RotateFlip(RotateFlipType.Rotate270FlipNone); }
                        card.View.Dock = DockStyle.None; card.View.Bounds = new Rectangle(0,0,640,480);
                        RectangleF chosen = Geometry.Fit(card.View.SourceWidth,card.View.SourceHeight,16.0/9,2.3,card.View.SourceWidth*.68,card.View.SourceHeight*.29);
                        Check(card.View.RestoreCrop(chosen),"Could not set test crop");
                        chosen = card.View.Crop;
                        using (Bitmap visible = new Bitmap(640,480)) {
                            card.View.DrawToBitmap(visible,new Rectangle(0,0,640,480));
                            RectangleF display = (RectangleF)typeof(CropView).GetProperty("ImageRect",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card.View,null);
                            foreach (bool resize in new[] { false,true }) {
                                string output = Path.Combine(folder,"export-"+dpi+"-"+turns+"-"+resize); Directory.CreateDirectory(output);
                                ExportJob job = new ExportJob(card,resize,320,180);
                                // Later UI changes must not change an already-captured export.
                                card.View.Reset(16.0/9); job.Execute(output); Check(card.View.RestoreCrop(chosen),"Could not restore snapshot test crop");
                                using (Bitmap exported = new Bitmap(Path.Combine(output,Path.GetFileNameWithoutExtension(path)+"_crop.png"))) {
                                    Check(exported.Width == (resize ? 320 : (int)Math.Round(chosen.Width)) && exported.Height == (resize ? 180 : (int)Math.Round(chosen.Height)),"Wrong export size");
                                    foreach (double u in new[] { .2,.5,.8 }) foreach (double v in new[] { .2,.5,.8 }) {
                                        int ox = (int)(u*exported.Width), oy = (int)(v*exported.Height);
                                        double sx = chosen.X+(ox+.5)*chosen.Width/exported.Width, sy = chosen.Y+(oy+.5)*chosen.Height/exported.Height;
                                        Color pixel = exported.GetPixel(ox,oy);
                                        NearColor(pixel,expected.GetPixel((int)sx,(int)sy),3,"Export does not match chosen source pixels at DPI "+dpi+", rotation "+turns);
                                        int vx = (int)(display.X+sx/card.View.SourceWidth*display.Width), vy = (int)(display.Y+sy/card.View.SourceHeight*display.Height);
                                        NearColor(pixel,visible.GetPixel(vx,vy),4,"Export does not match visible crop at DPI "+dpi+", rotation "+turns);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    static void Sessions(string folder) {
        string input = Path.Combine(folder,"session-test"); Directory.CreateDirectory(input);
        using (Bitmap image = Gradient()) {
            image.SetResolution(300,300);
            image.Save(Path.Combine(input,"one.png"),ImageFormat.Png);
            image.Save(Path.Combine(input,"two-\u00f1\u4e8c.png"),ImageFormat.Png);
        }
        ImageState[] saved;
        using (MainWindow window = new MainWindow()) {
            window.Show(); Pump(window.LoadFolderAsync(input));
            Field<NumericUpDown>(window,"targetW").Value = 1200; Field<NumericUpDown>(window,"targetH").Value = 800;
            Check(window.ApplyDimensions(),"Could not apply dimensions");
            Field<TrackBar>(window,"size").Value = 480; Field<CheckBox>(window,"resize").Checked = true;
            Card[] cards = Field<FlowLayoutPanel>(window,"grid").Controls.OfType<Card>().ToArray();
            cards[0].Selected.Checked = true;
            Mouse(cards[0].View,"OnMouseWheel",new MouseEventArgs(MouseButtons.None,0,100,100,720));
            RectangleF display = (RectangleF)typeof(CropView).GetProperty("ImageRect",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cards[0].View,null);
            int cx = (int)(display.X+display.Width/2), cy = (int)(display.Y+display.Height/2);
            RectangleF before = cards[0].View.Crop;
            Mouse(cards[0].View,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,cx,cy,0));
            Mouse(cards[0].View,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,cx+30,cy+20,0));
            Mouse(cards[0].View,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,cx+30,cy+20,0));
            Check(cards[0].View.Crop.X > before.X && cards[0].View.Crop.Y > before.Y,"Drag did not move crop");
            cards[1].RotateLeft();
            Check(cards[1].View.RestoreCrop(Geometry.Fit(cards[1].View.SourceWidth,cards[1].View.SourceHeight,1.5,3,250,600)),"Failed to set persisted crop");
            cards[1].Selected.Checked = false;
            saved = cards.Select(c => c.CaptureState()).ToArray();
            // Let the real debounce timer write the session without a manual save.
            Pump(Task.Delay(800));
            FolderSession disk = SessionStore.Load(input);
            Check(disk.Images.Count == 2 && disk.Images[0].Selected && !disk.Images[1].Selected,"Autosave lost selection state");
            Check(disk.TargetWidth == 1200 && disk.TargetHeight == 800 && disk.Resize && disk.CardSize == 480,"Global settings not saved");
            window.Close();
        }
        using (MainWindow reopened = new MainWindow()) {
            reopened.Show(); Pump(reopened.LoadFolderAsync(input));
            Card[] cards = Field<FlowLayoutPanel>(reopened,"grid").Controls.OfType<Card>().ToArray();
            for (int i = 0; i < cards.Length; i++) {
                ImageState actual = cards[i].CaptureState(), expected = saved[i];
                Check(actual.Selected == expected.Selected && actual.Turns == expected.Turns,"Session selection/rotation did not restore");
                SameCrop(cards[i].View.Crop,new RectangleF((float)expected.X,(float)expected.Y,(float)expected.Width,(float)expected.Height),"Session crop did not restore");
            }
            Check(Field<NumericUpDown>(reopened,"targetW").Value == 1200 && Field<NumericUpDown>(reopened,"targetH").Value == 800 && Field<CheckBox>(reopened,"resize").Checked,"Target settings did not restore");
            cards[0].Selected.Checked = false; Check(reopened.SaveSessionNow(),"Manual session flush failed");
            Check(File.Exists(Path.Combine(input,SessionStore.FileName+".bak")),"Atomic save did not preserve previous version");
            // A changed source must be reset instead of applying a stale crop or silently exporting it.
            ExportJob stale = new ExportJob(cards[1],true,300,200);
            File.SetLastWriteTimeUtc(cards[1].FilePath,DateTime.UtcNow.AddSeconds(2));
            bool rejected = false;
            try { stale.Execute(input); } catch (IOException) { rejected = true; }
            Check(rejected,"Changed source was silently exported");
            reopened.Close();
        }
        using (MainWindow changed = new MainWindow()) {
            changed.Show(); Pump(changed.LoadFolderAsync(input));
            Card second = Field<FlowLayoutPanel>(changed,"grid").Controls.OfType<Card>().Last();
            Check(second.Turns == 0 && second.View.Zoom == 1 && !second.Selected.Checked,"Changed image restored stale state");
            changed.Close();
        }
        string file = Path.Combine(input,SessionStore.FileName);
        string intact = File.ReadAllText(file); bool lockedRejected = false;
        FolderSession previous = SessionStore.Load(input);
        using (FileStream locked = new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)) {
            try { SessionStore.Save(input,new FolderSession()); } catch (IOException) { lockedRejected = true; }
        }
        Check(lockedRejected && File.ReadAllText(file) == intact,"Failed save damaged the previous session");
        Check(Directory.GetFiles(input,"*.tmp").Length == 0,"Failed save left a partial session behind");
        Check(previous.Images.Any(s => s.Name.Contains("\u00f1\u4e8c")),"Unicode filenames did not round-trip");
        File.WriteAllText(file,"invalid JSON");
        bool corruptRejected = false;
        try { SessionStore.Load(input); } catch { corruptRejected = true; }
        Check(corruptRejected && File.ReadAllText(file) == "invalid JSON","Corrupt session was overwritten");
        using (MainWindow corrupt = new MainWindow()) {
            corrupt.Show(); Pump(corrupt.LoadFolderAsync(input));
            Field<FlowLayoutPanel>(corrupt,"grid").Controls.OfType<Card>().First().Selected.Checked = true;
            Check(!corrupt.SaveSessionNow() && File.ReadAllText(file) == "invalid JSON","UI overwrote unreadable session");
            // Dispose without Close to avoid the intentional unsaved-changes confirmation.
        }
    }
    static void DpiFixture(string folder) {
        foreach (int dpi in new[] { 72,96,144,300 }) {
            string path = Path.Combine(folder,"dpi-"+dpi+".png");
            using (Bitmap original = new Bitmap(1200,800)) {
                original.SetResolution(dpi,dpi);
                using (Graphics g = Graphics.FromImage(original)) {
                    g.Clear(Color.Blue); g.FillRectangle(Brushes.Red,600,0,600,800);
                }
                original.Save(path,ImageFormat.Png);
                using (Bitmap full = Imaging.Open(path)) {
                    Color expected = original.GetPixel(1000,600), actual = full.GetPixel(1000,600);
                    Check(actual.R > 240 && actual.B < 10 && actual.A == 255,"Image DPI changed source pixels at "+dpi+" DPI: "+actual);
                }
            }
        }
    }
    static void OrientationFixtures(string folder) {
        byte[] jpeg;
        using (Bitmap image = new Bitmap(120,80)) {
            using (Graphics g = Graphics.FromImage(image)) {
                g.FillRectangle(Brushes.Red,0,0,60,40); g.FillRectangle(Brushes.Green,60,0,60,40);
                g.FillRectangle(Brushes.Blue,0,40,60,40); g.FillRectangle(Brushes.Yellow,60,40,60,40);
            }
            using (MemoryStream stream = new MemoryStream()) { image.Save(stream,ImageFormat.Jpeg); jpeg = stream.ToArray(); }
        }
        for (int orientation = 1; orientation <= 8; orientation++) {
            string path = Path.Combine(folder,"orientation-"+orientation+".jpg");
            using (BinaryWriter writer = new BinaryWriter(File.Create(path))) {
                writer.Write(jpeg,0,2); writer.Write(new byte[] { 255,225,0,34,69,120,105,102,0,0,73,73,42,0,8,0,0,0 });
                writer.Write((ushort)1); writer.Write((ushort)0x112); writer.Write((ushort)3); writer.Write(1);
                writer.Write((ushort)orientation); writer.Write((ushort)0); writer.Write(0);
                writer.Write(jpeg,2,jpeg.Length-2);
            }
            using (Bitmap full = Imaging.Open(path))
            using (Bitmap thumb = Imaging.Thumbnail(path)) {
                Check(full.Size == (orientation >= 5 ? new Size(80,120) : new Size(120,80)),"EXIF axes incorrect");
                Check(full.Size == Imaging.SourceSize(thumb) && full.Size == thumb.Size,"EXIF thumbnail dimensions incorrect");
                foreach (int x in new[] { full.Width/4,full.Width*3/4 }) foreach (int y in new[] { full.Height/4,full.Height*3/4 }) {
                    Color a = full.GetPixel(x,y), b = thumb.GetPixel(x,y);
                    Check(Math.Abs(a.R-b.R)<8 && Math.Abs(a.G-b.G)<8 && Math.Abs(a.B-b.B)<8,"EXIF preview/export mismatch for orientation "+orientation);
                }
            }
        }
    }
    static Bitmap LegacyThumbnail(string path) {
        using (Bitmap full = Imaging.Open(path)) {
            double scale = Math.Min(1,720.0/Math.Max(full.Width,full.Height));
            Bitmap thumb = new Bitmap(Math.Max(1,(int)(full.Width*scale)),Math.Max(1,(int)(full.Height*scale)));
            using (Graphics g = Graphics.FromImage(thumb)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(full,0,0,thumb.Width,thumb.Height); }
            return thumb;
        }
    }
    static void Benchmark(string folder) {
        string[] files = Enumerable.Range(0,12).Select(i => Path.Combine(folder,"large-"+i+".jpg")).ToArray();
        using (Bitmap large = new Bitmap(6000,4000)) {
            using (Graphics g = Graphics.FromImage(large)) {
                using (LinearGradientBrush brush = new LinearGradientBrush(new Rectangle(0,0,6000,4000),Color.DarkBlue,Color.Orange,35)) g.FillRectangle(brush,0,0,6000,4000);
                for (int n = 0; n < 200; n++) g.DrawEllipse(Pens.White,n*29,n*17,1000,900);
            }
            foreach (string path in files) large.Save(path,ImageFormat.Jpeg);
        }
        using (Bitmap warmup = Imaging.Thumbnail(files[0])) {}
        Stopwatch timer = Stopwatch.StartNew();
        foreach (string path in files) using (Bitmap thumb = LegacyThumbnail(path)) {}
        double old = timer.Elapsed.TotalSeconds;
        timer.Restart();
        foreach (string path in files) using (Bitmap thumb = Imaging.Thumbnail(path)) {}
        double single = timer.Elapsed.TotalSeconds;
        timer.Restart();
        for (int i = 0; i < files.Length; i += ThumbnailLoader.Workers) {
            ThumbnailResult[] batch = ThumbnailLoader.LoadBatch(files.Skip(i).Take(ThumbnailLoader.Workers).ToArray()).GetAwaiter().GetResult();
            foreach (ThumbnailResult result in batch) { Check(result.Error == null,"Benchmark decode failed: "+result.Error); result.Dispose(); }
        }
        double parallel = timer.Elapsed.TotalSeconds;
        Console.WriteLine("BENCHMARK: 12 x 24MP JPEG, legacy {0:0.00}s, new single worker {1:0.00}s, new {2} workers {3:0.00}s ({4:0.0}x faster than legacy).",old,single,ThumbnailLoader.Workers,parallel,old/parallel);
    }
    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Exception failure = null;
        using (Form host = new Form { ShowInTaskbar = false, Opacity = 0, Width = 1, Height = 1 }) {
            host.Shown += delegate {
                host.BeginInvoke(new Action(delegate {
                    try { Run(args); } catch (Exception ex) { failure = ex; }
                    finally { host.Close(); }
                }));
            };
            // Exercise real WinForms async dispatch, including multiple simultaneously open forms.
            Application.Run(host);
        }
        if (failure != null) { Console.Error.WriteLine(failure); Environment.ExitCode = 1; }
    }
    static void Run(string[] args) {
        Random random = new Random(42);
        for (int i = 0; i < 10000; i++) {
            int w = random.Next(1,16000), h = random.Next(1,16000);
            double ratio = Math.Pow(10, random.NextDouble()*4-2), zoom = 1+random.NextDouble()*19;
            RectangleF c = Geometry.Fit(w,h,ratio,zoom,random.Next(-32000,32000),random.Next(-32000,32000));
            Check(c.X >= -.01 && c.Y >= -.01 && c.Right <= w+.01 && c.Bottom <= h+.01,"Crop outside source");
            Check(Math.Abs(c.Width/c.Height/ratio-1) < .00001,"Aspect ratio changed");
        }
        RectangleF initial = Geometry.Fit(400,300,16.0/9,1,200,150);
        Check(initial.Width == 400 && initial.Height == 225,"Initial crop must be maximal");
        string temp = Path.Combine(Path.GetTempPath(),"QuickCrop-test-"+Guid.NewGuid()); Directory.CreateDirectory(temp);
        try {
            if (args.Contains("--benchmark")) { Benchmark(temp); return; }
            OrientationFixtures(temp);
            DpiFixture(temp);
            ExportCorrespondence(temp);
            Sessions(temp);
            ReviewTests.Run(temp);
            using (Bitmap source = new Bitmap(400,300)) {
                using (Graphics g = Graphics.FromImage(source)) { g.Clear(Color.Blue); g.FillRectangle(Brushes.Red,200,0,200,300); }
                source.Tag = "sample.png";
                Imaging.Save(source,new RectangleF(200,0,200,300),100,150,temp);
                Imaging.Save(source,new RectangleF(200,0,200,300),100,150,temp);
                Check(File.Exists(Path.Combine(temp,"sample_crop_1.png")),"Collision protection failed");
                using (Bitmap output = Imaging.Open(Path.Combine(temp,"sample_crop.png"))) {
                    Check(output.Width == 100 && output.Height == 150,"Resize dimensions incorrect");
                    Check(output.GetPixel(50,75).R > 250,"Exported wrong crop");
                }
                source.Tag = "sample.jpg";
                Imaging.Save(source,new RectangleF(0,0,200,300),200,300,temp);
                using (Bitmap jpeg = Imaging.Open(Path.Combine(temp,"sample_crop.jpg"))) Check(jpeg.GetPixel(100,150).B > 240,"JPEG export failed");
                source.RotateFlip(System.Drawing.RotateFlipType.Rotate270FlipNone);
                Check(source.Width == 300 && source.Height == 400,"Rotation dimensions incorrect");
                source.Tag = "rotated.png";
                Imaging.Save(source,new RectangleF(0,0,300,200),300,200,temp);
                using (Bitmap rotated = Imaging.Open(Path.Combine(temp,"rotated_crop.png"))) Check(rotated.GetPixel(150,100).R > 250,"Left rotation mapped wrong pixels");
            }
            // Preview pixels and original dimensions must agree with full-resolution export.
            foreach (string file in Directory.GetFiles(temp).Where(p => p.EndsWith(".png") || p.EndsWith(".jpg"))) {
                using (Bitmap thumb = Imaging.Thumbnail(file))
                using (Bitmap full = Imaging.Open(file)) {
                    Check(Imaging.SourceSize(thumb) == full.Size,"Thumbnail lost original dimensions");
                    Check(thumb.Width <= 720 && thumb.Height <= 720,"Thumbnail exceeds decode cap");
                    Color a = thumb.GetPixel(thumb.Width/2,thumb.Height/2), b = full.GetPixel(full.Width/2,full.Height/2);
                    Check(Math.Abs(a.R-b.R)<8 && Math.Abs(a.B-b.B)<8,"Thumbnail pixels disagree with export");
                }
            }
            ThumbnailResult[] results = ThumbnailLoader.LoadBatch(new[] { Path.Combine(temp,"sample_crop.png"), Path.Combine(temp,"missing.jpg"), Path.Combine(temp,"sample_crop.jpg") }).GetAwaiter().GetResult();
            Check(results[0].Image != null && results[1].Error != null && results[2].Image != null,"Bad file must not break the batch");
            foreach (ThumbnailResult result in results) result.Dispose();
            using (MainWindow window = new MainWindow()) {
                FlowLayoutPanel grid = window.Controls.OfType<FlowLayoutPanel>().First(p => p.Dock == DockStyle.Fill);
                Bitmap preview = new Bitmap(400,300);
                using (Graphics g = Graphics.FromImage(preview)) { g.Clear(Color.DarkRed); g.FillEllipse(Brushes.Goldenrod,100,40,200,200); }
                preview.Tag = new Size(4000,3000);
                Card card = new Card("Example.png",preview,16.0/9,delegate {}); card.Size = new Size(360,288); grid.Controls.Add(card);
                window.Show(); Application.DoEvents();
                typeof(CropView).GetMethod("OnMouseWheel",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(card.View,new object[] { new MouseEventArgs(MouseButtons.None,0,100,100,120) });
                Check(card.View.Zoom > 1 && card.Selected.Checked,"Wheel must zoom and select");
                card.Selected.Checked = false;
                Panel side = card.Controls.OfType<Panel>().First();
                side.Controls.OfType<Button>().First(b => b.AccessibleName == "Rotate left 90 degrees").PerformClick();
                Check(card.Turns == 1 && card.View.SourceWidth == 3000 && card.View.SourceHeight == 4000,"Card rotation must swap source axes");
                Check(card.View.Zoom == 1 && card.Selected.Checked,"Rotation must reset crop and select");
                Check(Math.Abs(card.View.Crop.Width/card.View.Crop.Height-16.0/9) < .00001,"Card must use original aspect geometry");
                foreach (bool dark in new[] { false,true }) {
                    AppTheme.Apply(window,dark);
                    Check(dark ? card.View.ForeColor.GetBrightness() > .8 : card.View.ForeColor.GetBrightness() < .3,"Card text theme contrast");
                    foreach (NumericUpDown field in window.Controls.OfType<FlowLayoutPanel>().SelectMany(p => p.Controls.OfType<NumericUpDown>()))
                        Check(Math.Abs(field.ForeColor.GetBrightness()-field.BackColor.GetBrightness()) > .6,"Resolution field contrast");
                    using (Bitmap capture = new Bitmap(window.Width,window.Height)) {
                        window.DrawToBitmap(capture,new Rectangle(0,0,window.Width,window.Height));
                        capture.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,dark ? "preview-dark.png" : "preview-light.png"));
                    }
                }
                using (Bitmap capture = new Bitmap(window.Width,window.Height)) {
                    window.DrawToBitmap(capture,new Rectangle(0,0,window.Width,window.Height));
                    capture.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview.png"));
                }
                window.Close();
            }
        } finally { Directory.Delete(temp,true); }
        Console.WriteLine("PASS: DPI regression (72/96/144/300), preview/export correspondence across 32 crop/resize/rotation cases, immutable export snapshots, autosave/reopen/settings, changed-source checks, atomic backups/corrupt sessions, 10,000 geometry cases, EXIF, PNG/JPEG export, interaction and theme checks.");
    }
}
