using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuickCrop;

static class SeamCarverTests {
    sealed class ImmediateProgress : IProgress<int> {
        readonly Action<int> action;
        public ImmediateProgress(Action<int> action) { this.action = action; }
        public void Report(int value) { action(value); }
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception("Seam carving: "+message); }
    static T Field<T>(MainWindow window, string name) { return (T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window); }
    static void Pump(Task task) {
        Stopwatch timer = Stopwatch.StartNew();
        while (!task.IsCompleted) { Check(timer.Elapsed.TotalSeconds < 30,"UI task timed out"); Application.DoEvents(); Thread.Sleep(1); }
        task.GetAwaiter().GetResult();
    }
    static int Distance(Color a, Color b) {
        int[] left = { a.R,a.G,a.B }, right = { b.R,b.G,b.B }; int value = Math.Abs(a.A-b.A);
        for (int c = 0; c < 3; c++) value += Math.Abs((left[c]*a.A+255*(255-a.A)+127)/255-(right[c]*b.A+255*(255-b.A)+127)/255);
        return value;
    }
    // Slow reference: rebuild every energy value and every row after each seam.
    // Compare against the optimized cache/shift/transpose path on random RGBA images.
    static Bitmap Reference(Bitmap image, int targetW, int targetH) {
        Size size = SeamCarver.AspectSize(image.Width,image.Height,targetW,targetH);
        bool horizontal = size.Height != image.Height;
        int h = horizontal ? image.Width : image.Height, w = horizontal ? image.Height : image.Width;
        Color[][] pixels = new Color[h][];
        for (int y = 0; y < h; y++) {
            pixels[y] = new Color[w];
            for (int x = 0; x < w; x++) pixels[y][x] = horizontal ? image.GetPixel(y,x) : image.GetPixel(x,y);
        }
        int destination = horizontal ? size.Height : size.Width;
        while (w > destination) {
            long[,] cost = new long[h,w]; int[,] previous = new int[h,w];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
                int value = Distance(pixels[y][Math.Max(0,x-1)],pixels[y][Math.Min(w-1,x+1)])+
                    Distance(pixels[Math.Max(0,y-1)][x],pixels[Math.Min(h-1,y+1)][x]);
                if (y == 0) { cost[y,x] = value; continue; }
                int best = x;
                if (x > 0 && cost[y-1,x-1] < cost[y-1,best]) best = x-1;
                if (x < w-1 && cost[y-1,x+1] < cost[y-1,best]) best = x+1;
                previous[y,x] = best; cost[y,x] = value+cost[y-1,best];
            }
            int last = 0;
            for (int x = 1; x < w; x++) if (cost[h-1,x] < cost[h-1,last]) last = x;
            for (int y = h-1; y >= 0; y--) {
                Color[] reduced = new Color[w-1];
                for (int x = 0, i = 0; x < w; x++) if (x != last) reduced[i++] = pixels[y][x];
                pixels[y] = reduced; if (y > 0) last = previous[y,last];
            }
            w--;
        }
        Bitmap result = new Bitmap(size.Width,size.Height);
        for (int y = 0; y < result.Height; y++) for (int x = 0; x < result.Width; x++) result.SetPixel(x,y,horizontal ? pixels[x][y] : pixels[y][x]);
        return result;
    }
    public static void Run(string root) {
        Random random = new Random(8201);
        for (int n = 0; n < 40; n++) {
            using (Bitmap source = new Bitmap(random.Next(5,14),random.Next(5,12))) {
                for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
                    source.SetPixel(x,y,Color.FromArgb(random.Next(4) == 0 ? 0 : random.Next(256),random.Next(256),random.Next(256),random.Next(256)));
                int tw = n%2 == 0 ? 1 : 3, th = n%2 == 0 ? 2 : 1;
                using (Bitmap expected = Reference(source,tw,th))
                using (Bitmap carved = SeamCarver.Carve(source,tw,th)) {
                    Check(carved.Size == expected.Size,"Unexpected aspect dimensions");
                    for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
                        Check(carved.GetPixel(x,y).ToArgb() == expected.GetPixel(x,y).ToArgb(),"Cached energy or transposed removal differs from full recomputation");
                }
            }
        }
        using (Bitmap source = new Bitmap(80,40)) {
            using (Graphics g = Graphics.FromImage(source)) { g.Clear(Color.White); g.FillRectangle(Brushes.Black,30,10,20,20); }
            // Leave background margin: severe reductions can distort even strong subjects.
            foreach (Size target in new[] { new Size(1,1), new Size(3,1) }) {
                using (Bitmap result = SeamCarver.Carve(source,target.Width,target.Height)) {
                    int black = 0;
                    for (int y = 0; y < result.Height; y++) for (int x = 0; x < result.Width; x++) if (result.GetPixel(x,y).R == 0) black++;
                    Check(black == 400,"Low-energy background removal damaged the high-contrast subject fixture");
                }
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource()) {
                bool cancelled = false;
                try {
                    using (Bitmap result = SeamCarver.Carve(source,1,1,cancellation.Token,new ImmediateProgress(p => { if (p > 0) cancellation.Cancel(); }))) {}
                } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && source.Width == 80 && source.GetPixel(35,15).R == 0,"Cancellation ignored or source modified");
            }
        }
        foreach (Size edge in new[] { new Size(1,8),new Size(8,1),new Size(1,1) }) {
            using (Bitmap source = new Bitmap(edge.Width,edge.Height))
            using (Bitmap result = SeamCarver.Carve(source,1,1)) Check(result.Size == new Size(1,1),"Single-row/column case failed");
        }
        string order = Path.Combine(root,"seam-order"); Directory.CreateDirectory(order);
        string subjectPath = Path.Combine(order,"subject.png");
        using (Bitmap source = new Bitmap(80,40)) {
            using (Graphics g = Graphics.FromImage(source)) { g.Clear(Color.White); g.FillRectangle(Brushes.Black,30,10,20,20); }
            source.Save(subjectPath,ImageFormat.Png);
        }
        using (Card subject = new Card(subjectPath,Imaging.Thumbnail(subjectPath),1,delegate {})) {
            new ExportJob(subject,false,1,1,true).Execute(order);
            using (Bitmap natural = new Bitmap(Path.Combine(order,"subject_crop.png"))) Check(natural.Size == new Size(40,40),"Carved output was rescaled despite Resize being off");
            new ExportJob(subject,true,10,10,true).Execute(order);
            using (Bitmap resized = new Bitmap(Path.Combine(order,"subject_crop_1.png"))) {
                int left = 10, top = 10, right = -1, bottom = -1;
                for (int y = 0; y < 10; y++) for (int x = 0; x < 10; x++) if (resized.GetPixel(x,y).R < 128) {
                    left = Math.Min(left,x); right = Math.Max(right,x); top = Math.Min(top,y); bottom = Math.Max(bottom,y);
                }
                Check(resized.Size == new Size(10,10) && right-left >= 3 && Math.Abs((right-left)-(bottom-top)) <= 1,"Rescaling happened before carving or distorted the preserved subject");
            }
        }
        string folder = Path.Combine(root,"seam-session"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder,"whole.png");
        using (Bitmap source = new Bitmap(160,80)) {
            using (Graphics g = Graphics.FromImage(source)) { g.Clear(Color.Blue); g.FillRectangle(Brushes.Red,0,0,80,80); }
            source.Save(path,ImageFormat.Png);
        }
        using (Card card = new Card(path,Imaging.Thumbnail(path),2,delegate {})) {
            Check(card.View.RestoreCrop(new RectangleF(100,20,40,20)),"Could not set manual test crop");
            ExportJob snapshot = new ExportJob(card,false,2,1,true);
            card.View.Reset(2);
            string output = Path.Combine(folder,"original-size"); Directory.CreateDirectory(output); snapshot.Execute(output);
            using (Bitmap result = new Bitmap(Path.Combine(output,"whole_crop.png"))) {
                Check(result.Size == new Size(160,80) && result.GetPixel(10,40).R > 240 && result.GetPixel(150,40).B > 240,"Seam export used the crop instead of the whole image");
            }
            card.RotateLeft();
            string rotated = Path.Combine(folder,"rotated"); Directory.CreateDirectory(rotated);
            new ExportJob(card,true,20,40,true).Execute(rotated);
            using (Bitmap result = new Bitmap(Path.Combine(rotated,"whole_crop.png"))) {
                Check(result.Size == new Size(20,40) && result.GetPixel(10,3).B > 240 && result.GetPixel(10,36).R > 240,"Rotation or final rescaling failed");
            }
            string cancelledFolder = Path.Combine(folder,"cancelled"); Directory.CreateDirectory(cancelledFolder);
            using (CancellationTokenSource cancellation = new CancellationTokenSource()) {
                bool cancelled = false;
                try { new ExportJob(card,false,1,1,true).Execute(cancelledFolder,cancellation.Token,new ImmediateProgress(p => { if (p > 0) cancellation.Cancel(); })); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && Directory.GetFiles(cancelledFolder).Length == 0,"Cancelled export published a partial file");
            }
        }
        RectangleF remembered;
        using (MainWindow window = new MainWindow()) {
            window.Show(); Pump(window.LoadFolderAsync(folder));
            Card card = Field<FlowLayoutPanel>(window,"grid").Controls.OfType<Card>().Single();
            Check(card.View.RestoreCrop(new RectangleF(80,10,64,36)),"Could not set preserved manual crop"); remembered = card.View.Crop;
            Field<CheckBox>(window,"seamCarving").Checked = true;
            Check(card.View.SeamCarving && !card.Selected.Checked,"Mode switch changed selection or did not enable whole-image mode");
            Check(!card.Controls.OfType<Panel>().Single().Controls.OfType<Button>().Single(b => b.Text == "Fit").Visible,"Fit control remained visible in seam mode");
            card.View.HandleWheel(new HandledMouseEventArgs(MouseButtons.None,0,100,100,120));
            Check(card.View.Crop == remembered,"Wheel changed a hidden manual crop");
            using (Bitmap preview = new Bitmap(card.View.Width,card.View.Height)) {
                card.View.DrawToBitmap(preview,card.View.ClientRectangle);
                RectangleF image = (RectangleF)typeof(CropView).GetProperty("ImageRect",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card.View,null);
                Check(preview.GetPixel((int)image.X+5,(int)image.Y+5).R > 240,"Whole image remained shaded by the manual crop overlay");
            }
            Check(window.SaveSessionNow() && SessionStore.Load(folder).SeamCarving,"Mode did not persist");
            using (Bitmap capture = new Bitmap(window.Width,window.Height)) {
                window.DrawToBitmap(capture,new Rectangle(0,0,window.Width,window.Height));
                capture.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-seam-mode.png"));
            }
            window.Close();
        }
        using (MainWindow reopened = new MainWindow()) {
            reopened.Show(); Pump(reopened.LoadFolderAsync(folder));
            Card card = Field<FlowLayoutPanel>(reopened,"grid").Controls.OfType<Card>().Single();
            Check(Field<CheckBox>(reopened,"seamCarving").Checked && card.View.SeamCarving,"Whole-image mode did not restore");
            Field<CheckBox>(reopened,"seamCarving").Checked = false;
            Check(!card.View.SeamCarving && card.View.Crop == remembered,"Manual crop was lost after toggling back");
            using (CancellationTokenSource cancellation = new CancellationTokenSource()) {
                typeof(MainWindow).GetField("processCancellation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(reopened,cancellation);
                typeof(MainWindow).GetMethod("SetBusy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(reopened,new object[] { true,false });
                try {
                    Button cancel = Field<Button>(reopened,"cancelProcessing");
                    Check(cancel.Enabled && cancel.Visible,"Cancel action was disabled by its toolbar");
                    cancel.PerformClick(); Check(cancellation.IsCancellationRequested,"Cancel action did not signal the worker");
                } finally {
                    typeof(MainWindow).GetField("processCancellation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(reopened,null);
                    typeof(MainWindow).GetMethod("SetBusy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(reopened,new object[] { false,false });
                }
            }
            reopened.Close();
        }
        string legacy = Path.Combine(root,"seam-legacy"); Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy,SessionStore.FileName),"{\"Version\":1,\"TargetWidth\":1920,\"TargetHeight\":1080,\"Images\":[]}");
        Check(!SessionStore.Load(legacy).SeamCarving,"Old sessions unexpectedly enabled experimental carving");
        using (Bitmap benchmark = new Bitmap(600,400)) {
            using (Graphics g = Graphics.FromImage(benchmark)) { g.Clear(Color.SkyBlue); g.FillEllipse(Brushes.DarkRed,240,100,120,200); }
            Stopwatch timer = Stopwatch.StartNew();
            using (Bitmap result = SeamCarver.Carve(benchmark,16,9)) Check(result.Size == new Size(600,338),"Benchmark output size wrong");
            Console.WriteLine("Seam carving sample: 600x400 -> 600x338 in "+timer.Elapsed.TotalSeconds.ToString("0.00")+" s.");
        }
        Console.WriteLine("PASS: seam carving (40 RGBA reference comparisons, both axes, subject preservation, narrow images, whole-source export, rotation, rescale, cancellation, session/mode restore).");
    }
}
