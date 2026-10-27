using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Web.Script.Serialization;
using System.Security.Cryptography;

namespace QuickCrop {
    public static class Geometry {
        public static RectangleF Fit(float width, float height, double ratio, double zoom, double cx, double cy) {
            double w = Math.Min(width, height * ratio) / zoom, h = w / ratio;
            return new RectangleF((float)Math.Max(0, Math.Min(width-w, cx-w/2)),
                (float)Math.Max(0, Math.Min(height-h, cy-h/2)), (float)w, (float)h);
        }
    }
    public static class Imaging {
        public const long MaxSourcePixels = 100000000, MaxOutputPixels = 64000000;
        public static void ValidateSize(int width, int height, long maximumPixels) {
            if (width < 1 || height < 1) throw new InvalidDataException("Image dimensions must be positive.");
            if ((long)width*height > maximumPixels)
                throw new InvalidDataException("Image exceeds the supported limit of "+(maximumPixels/1000000)+" megapixels.");
        }
        public static Size SourceSize(Bitmap preview) {
            PreviewInfo info = preview.Tag as PreviewInfo;
            return info != null ? info.Size : preview.Tag is Size ? (Size)preview.Tag : preview.Size;
        }
        static readonly RotateFlipType[] Orientations = { RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipNone,
            RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate180FlipNone, RotateFlipType.Rotate180FlipX,
            RotateFlipType.Rotate90FlipX, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate270FlipNone };
        static int Orientation(Image image) {
            if (!image.PropertyIdList.Contains(0x112)) return 1;
            byte[] value = image.GetPropertyItem(0x112).Value;
            return value.Length > 0 && value[0] >= 1 && value[0] <= 8 ? value[0] : 1;
        }
        public static Bitmap Thumbnail(string path) {
            // Keep one read handle open throughout header inspection and decoding. Denying writes
            // and deletion ties the preview and its fingerprint to the very same source file.
            int width, height, orientation;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                PreviewInfo info = new PreviewInfo { FileLength = stream.Length, LastWriteUtcTicks = File.GetLastWriteTimeUtc(path).Ticks };
                using (Image header = Image.FromStream(stream,false,false)) {
                    width = header.Width; height = header.Height; orientation = Orientation(header);
                }
                ValidateSize(width,height,MaxSourcePixels); stream.Position = 0;
                BitmapImage decoded = new BitmapImage();
                decoded.BeginInit();
                decoded.CacheOption = BitmapCacheOption.OnLoad;
                decoded.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                decoded.StreamSource = stream;
                if (width >= height) decoded.DecodePixelWidth = Math.Min(720, width);
                else decoded.DecodePixelHeight = Math.Min(720, height);
                decoded.EndInit();
                FormatConvertedBitmap pixels = new FormatConvertedBitmap(decoded, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                Bitmap thumb = new Bitmap(pixels.PixelWidth, pixels.PixelHeight, PixelFormat.Format32bppArgb);
                try {
                    BitmapData data = thumb.LockBits(new Rectangle(0,0,thumb.Width,thumb.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try { pixels.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride*data.Height, data.Stride); }
                    finally { thumb.UnlockBits(data); }
                    thumb.RotateFlip(Orientations[orientation]);
                    info.Size = orientation >= 5 ? new Size(height,width) : new Size(width,height); thumb.Tag = info;
                    return thumb;
                } catch { thumb.Dispose(); throw; }
            }
        }
        public static Bitmap Open(string path, long? expectedLength = null, long? expectedTimestamp = null) {
            using (FileStream stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                if ((expectedLength.HasValue && stream.Length != expectedLength.Value) ||
                    (expectedTimestamp.HasValue && File.GetLastWriteTimeUtc(path).Ticks != expectedTimestamp.Value))
                    throw new IOException("Source changed since loading. Reopen the folder before exporting this image.");
              using (Image source = Image.FromStream(stream,false,false)) {
                ValidateSize(source.Width,source.Height,MaxSourcePixels);
                source.RotateFlip(Orientations[Orientation(source)]);
                Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                try {
                using (Graphics g = Graphics.FromImage(result)) {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(source, new Rectangle(0,0,source.Width,source.Height), 0,0,source.Width,source.Height,GraphicsUnit.Pixel);
                }
                return result;
                } catch { result.Dispose(); throw; }
              }
            }
        }
        public static void Save(Bitmap source, RectangleF crop, int width, int height, string output) {
            ValidateSize(width,height,MaxOutputPixels);
            if (float.IsNaN(crop.X) || float.IsInfinity(crop.X) || float.IsNaN(crop.Y) || float.IsInfinity(crop.Y) ||
                float.IsNaN(crop.Width) || float.IsInfinity(crop.Width) || float.IsNaN(crop.Height) || float.IsInfinity(crop.Height) ||
                crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 || crop.Right > source.Width+.01 || crop.Bottom > source.Height+.01)
                throw new InvalidDataException("Crop rectangle must lie inside the source image.");
            using (Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb)) {
                using (Graphics g = Graphics.FromImage(result)) {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (ImageAttributes attributes = new ImageAttributes()) {
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(source, new Rectangle(0, 0, width, height), crop.X, crop.Y, crop.Width, crop.Height, GraphicsUnit.Pixel, attributes);
                    }
                }
                string stem = Path.Combine(output, Path.GetFileNameWithoutExtension(source.Tag as string) + "_crop");
                bool png = String.Equals(Path.GetExtension(source.Tag as string), ".png", StringComparison.OrdinalIgnoreCase);
                string ext = png ? ".png" : ".jpg";
                string temp = Path.Combine(output,".quickcrop-export-"+Guid.NewGuid().ToString("N")+".tmp");
                try {
                        using (FileStream stream = new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                            if (png) result.Save(stream, ImageFormat.Png);
                            else {
                                using (Bitmap rgb = new Bitmap(width, height, PixelFormat.Format24bppRgb)) {
                                    using (Graphics g = Graphics.FromImage(rgb)) {
                                        g.Clear(Color.White);
                                        g.DrawImage(result,new Rectangle(0,0,width,height),0,0,width,height,GraphicsUnit.Pixel);
                                    }
                                    ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
                                    using (EncoderParameters options = new EncoderParameters(1)) {
                                        options.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                                        rgb.Save(stream, codec, options);
                                    }
                                }
                            }
                            stream.Flush(true);
                        }
                    // Publish only a complete encoding. Move never replaces an existing file.
                    for (int n = 0; ; n++) {
                        string path = stem+(n == 0 ? "" : "_"+n)+ext;
                        try { File.Move(temp,path); break; }
                        catch (IOException) { if (File.Exists(path) || Directory.Exists(path)) continue; throw; }
                    }
                } finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
    }
    public sealed class PreviewInfo {
        public Size Size;
        public long FileLength, LastWriteUtcTicks;
    }
    public sealed class ImageState {
        public string Name;
        public long FileLength, LastWriteUtcTicks;
        public bool Selected;
        public int Turns, SourceWidth, SourceHeight;
        public double X, Y, Width, Height;
    }
    public sealed class FolderSession {
        public int Version = 1;
        public int TargetWidth = 1920, TargetHeight = 1080, CardSize = 360;
        public bool Resize;
        public List<ImageState> Images = new List<ImageState>();
    }
    public static class SessionStore {
        public const string FileName = ".quickcrop.json";
        public const int MaxBytes = 32*1024*1024;
        static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = MaxBytes }; }
        static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        static void Validate(FolderSession session) {
            if (session == null || session.Version != 1 || session.Images == null ||
                session.TargetWidth < 1 || session.TargetWidth > 16384 || session.TargetHeight < 1 || session.TargetHeight > 16384 ||
                (long)session.TargetWidth*session.TargetHeight > Imaging.MaxOutputPixels || session.CardSize < 240 || session.CardSize > 720)
                throw new InvalidDataException("Invalid or unsupported QuickCrop session.");
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ImageState s in session.Images) {
                if (s == null || String.IsNullOrEmpty(s.Name) || s.Name == "." || s.Name == ".." || s.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !names.Add(s.Name) ||
                    s.Turns < 0 || s.Turns > 3 || s.FileLength < 0 || s.LastWriteUtcTicks < 0 || s.LastWriteUtcTicks > DateTime.MaxValue.Ticks ||
                    s.SourceWidth < 1 || s.SourceHeight < 1 || (long)s.SourceWidth*s.SourceHeight > Imaging.MaxSourcePixels ||
                    !Finite(s.X) || !Finite(s.Y) || !Finite(s.Width) || !Finite(s.Height) || s.X < 0 || s.Y < 0 || s.Width <= 0 || s.Height <= 0 ||
                    s.X+s.Width > s.SourceWidth+.01 || s.Y+s.Height > s.SourceHeight+.01 ||
                    Math.Min(s.SourceWidth,s.SourceHeight*s.Width/s.Height)/s.Width > 20.001)
                    throw new InvalidDataException("Invalid image entry in QuickCrop session. Existing data has been kept.");
            }
        }
        public static string Revision(string folder) {
            string path = Path.Combine(folder,FileName);
            if (!File.Exists(path)) return null;
            using (FileStream stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                if (stream.Length > MaxBytes) throw new InvalidDataException("Session file exceeds 32 MB.");
                using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(stream));
            }
        }
        public static FolderSession Load(string folder) {
            string path = Path.Combine(folder,FileName);
            if (!File.Exists(path)) return new FolderSession();
            string json;
            using (FileStream stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                if (stream.Length > MaxBytes) throw new InvalidDataException("Session file exceeds 32 MB.");
                using (StreamReader reader = new StreamReader(stream)) json = reader.ReadToEnd();
            }
            FolderSession session = Serializer().Deserialize<FolderSession>(json);
            Validate(session);
            return session;
        }
        public static void Save(string folder, FolderSession session) {
            SaveCore(folder,session,null,false);
        }
        public static string Save(string folder, FolderSession session, string expectedRevision) {
            return SaveCore(folder,session,expectedRevision,true);
        }
        static string SaveCore(string folder, FolderSession session, string expectedRevision, bool checkRevision) {
            Validate(session);
            string path = Path.Combine(folder,FileName), temp = path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(Serializer().Serialize(session));
                if (bytes.Length > MaxBytes) throw new InvalidDataException("Session file exceeds 32 MB.");
                using (FileStream stream = new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
                if (checkRevision && Revision(folder) != expectedRevision)
                    throw new IOException("Session changed outside this app. Reopen the folder to load those changes; this save was not written.");
                if (File.Exists(path)) File.Replace(temp,path,path+".bak",true);
                else File.Move(temp,path);
                using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(bytes));
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public sealed class SessionLease : IDisposable {
        readonly FileStream stream;
        public SessionLease(string folder) {
            // The empty lock file persists. Deleting it on close would race another opener.
            stream = new FileStream(Path.Combine(folder,".quickcrop.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);
        }
        public void Dispose() { stream.Dispose(); }
    }
    public sealed class ExportJob {
        readonly string path;
        readonly long length, timestamp;
        readonly int turns, sourceWidth, sourceHeight, outputWidth, outputHeight;
        public readonly RectangleF Crop;
        public string Name { get { return Path.GetFileName(path); } }
        public ExportJob(Card card, bool resize, int width, int height) {
            path = card.FilePath; turns = card.Turns; sourceWidth = card.View.SourceWidth; sourceHeight = card.View.SourceHeight;
            length = card.FileLength; timestamp = card.LastWriteUtcTicks;
            Crop = card.View.Crop;
            outputWidth = resize ? width : Math.Max(1,(int)Math.Round(Crop.Width));
            outputHeight = resize ? height : Math.Max(1,(int)Math.Round(Crop.Height));
        }
        public void Execute(string output) {
            using (Bitmap full = Imaging.Open(path,length,timestamp)) {
                for (int i = 0; i < turns; i++) full.RotateFlip(RotateFlipType.Rotate270FlipNone);
                if (full.Width != sourceWidth || full.Height != sourceHeight) throw new InvalidDataException("Source dimensions no longer match the preview.");
                // The same oriented source-pixel rectangle is used for preview, persistence, and export.
                full.Tag = path; Imaging.Save(full,Crop,outputWidth,outputHeight,output);
            }
        }
    }
    public sealed class ThumbnailResult : IDisposable {
        public string Path;
        public Bitmap Image;
        public string Error;
        public void Dispose() { if (Image != null) { Image.Dispose(); Image = null; } }
    }
    public static class ThumbnailLoader {
        // Bounded chunks avoid queuing thousands of decodes and cap transient memory usage.
        public static readonly int Workers = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
        public static Task<ThumbnailResult[]> LoadBatch(string[] paths) {
            return Task.Run(() => {
                ThumbnailResult[] results = new ThumbnailResult[paths.Length];
                Parallel.For(0, paths.Length, new ParallelOptions { MaxDegreeOfParallelism = Workers }, i => {
                    ThumbnailResult result = new ThumbnailResult { Path = paths[i] };
                    try { result.Image = Imaging.Thumbnail(paths[i]); }
                    catch (Exception ex) { result.Error = ex.Message; }
                    results[i] = result;
                });
                return results;
            });
        }
    }
    public static class AppTheme {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        public static bool IsDark {
            get {
                if (SystemInformation.HighContrast) return false;
                try {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                        return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme",1)) == 0;
                } catch { return false; }
            }
        }
        public static void Apply(Control root, bool dark) {
            bool highContrast = SystemInformation.HighContrast;
            Color background = highContrast ? SystemColors.Control : dark ? Color.FromArgb(29,33,40) : Color.FromArgb(241,243,247);
            Color surface = highContrast ? SystemColors.Window : dark ? Color.FromArgb(43,48,57) : Color.White;
            Color foreground = highContrast ? SystemColors.ControlText : dark ? Color.FromArgb(242,244,248) : Color.FromArgb(28,32,40);
            ApplyControl(root, background, surface, foreground);
            Form form = root as Form;
            if (form != null && form.IsHandleCreated) {
                int enabled = dark && !highContrast ? 1 : 0;
                try { if (DwmSetWindowAttribute(form.Handle,20,ref enabled,4) != 0) DwmSetWindowAttribute(form.Handle,19,ref enabled,4); }
                catch (DllNotFoundException) {} catch (EntryPointNotFoundException) {}
            }
        }
        static void ApplyControl(Control control, Color background, Color surface, Color foreground) {
            control.ForeColor = foreground;
            control.BackColor = control is CropView ? Color.FromArgb(24,27,32) :
                control is Card || control is Button || control is ComboBox || control is NumericUpDown || control is ToolbarNumberHost ? surface : background;
            Button button = control as Button;
            if (button != null) { button.UseVisualStyleBackColor = false; button.FlatAppearance.BorderColor = foreground; }
            ThemeButton themedButton = control as ThemeButton;
            if (themedButton != null) themedButton.DarkTheme = foreground.GetBrightness() > .5 && !SystemInformation.HighContrast;
            foreach (Control child in control.Controls) ApplyControl(child, control.BackColor, surface, foreground);
            control.Invalidate();
        }
    }
    public class ThemeButton : Button {
        public bool DarkTheme;
        protected override void OnPaint(PaintEventArgs e) {
            if (Enabled || !DarkTheme || SystemInformation.HighContrast) { base.OnPaint(e); return; }
            // Native disabled-button painting ignores ForeColor. Keep light text while a
            // muted border distinguishes disabled actions; normal Button behavior is retained.
            e.Graphics.Clear(Color.FromArgb(35,39,47));
            ControlPaint.DrawBorder(e.Graphics,ClientRectangle,Color.FromArgb(89,97,112),ButtonBorderStyle.Solid);
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ForeColor,flags);
        }
    }
    public class ToolbarNumberHost : Panel {
        readonly NumericUpDown input;
        public ToolbarNumberHost(NumericUpDown input) {
            this.input = input; Width = 86; Height = 32; Margin = new Padding(3);
            DoubleBuffered = true; input.BorderStyle = BorderStyle.None; Controls.Add(input);
        }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);
            if (input != null) input.SetBounds(5,Math.Max(1,(ClientSize.Height-input.Height)/2),Math.Max(1,ClientSize.Width-10),input.Height);
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); ControlPaint.DrawBorder(e.Graphics,ClientRectangle,ForeColor,ButtonBorderStyle.Solid);
        }
    }
    public sealed class CropWheelRouter : IMessageFilter {
        [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativePoint point);
        readonly Form owner;
        public CropWheelRouter(Form owner) { this.owner = owner; }
        public bool PreFilterMessage(ref Message message) {
            if (message.Msg != 0x020A || owner.IsDisposed || !owner.Visible || !owner.ContainsFocus) return false;
            Point cursor = Cursor.Position;
            CropView view = Control.FromHandle(WindowFromPoint(new NativePoint { X = cursor.X, Y = cursor.Y })) as CropView;
            if (view == null || !view.Enabled || view.FindForm() != owner) return false;
            int delta = unchecked((short)((message.WParam.ToInt64() >> 16) & 0xffff));
            Point local = view.PointToClient(cursor);
            view.HandleWheel(new HandledMouseEventArgs(MouseButtons.None,0,local.X,local.Y,delta));
            return true;
        }
    }
    public class CropView : Control {
        public Bitmap Preview;
        public int SourceWidth, SourceHeight;
        public double Ratio = 16.0/9, Zoom = 1, CX, CY;
        public event Action Interacted;
        public event Action Changed;
        private bool dragging;
        private Point last;
        readonly bool redDominant;
        public CropView(Bitmap preview) {
            Preview = preview; Size original = Imaging.SourceSize(preview);
            SourceWidth = original.Width; SourceHeight = original.Height; CX = SourceWidth/2.0; CY = SourceHeight/2.0;
            DoubleBuffered = true; TabStop = true; BackColor = Color.FromArgb(24, 27, 32);
            SetStyle(ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            redDominant = RedDominant();
        }
        public RectangleF Crop { get { return Geometry.Fit(SourceWidth, SourceHeight, Ratio, Zoom, CX, CY); } }
        void NotifyChanged() { Invalidate(); if (Changed != null) Changed(); }
        public void Reset(double ratio) { Ratio = ratio; Zoom = 1; CX = SourceWidth/2.0; CY = SourceHeight/2.0; NotifyChanged(); }
        public bool RestoreCrop(RectangleF crop) {
            double maximumWidth = Math.Min(SourceWidth,SourceHeight*Ratio);
            if (float.IsNaN(crop.X) || float.IsInfinity(crop.X) || float.IsNaN(crop.Y) || float.IsInfinity(crop.Y) ||
                float.IsNaN(crop.Width) || float.IsInfinity(crop.Width) || float.IsNaN(crop.Height) || float.IsInfinity(crop.Height) ||
                crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 || crop.Right > SourceWidth+.01 || crop.Bottom > SourceHeight+.01 ||
                Math.Abs(crop.Width/crop.Height/Ratio-1) > .0001 || maximumWidth/crop.Width > 20.001 || maximumWidth/crop.Width < .9999) return false;
            Zoom = Math.Max(1,Math.Min(20,maximumWidth/crop.Width)); CX = crop.X+crop.Width/2; CY = crop.Y+crop.Height/2;
            NotifyChanged(); return true;
        }
        private RectangleF ImageRect {
            get { float scale = Math.Min((Width-16f)/SourceWidth, (Height-16f)/SourceHeight);
                return new RectangleF((Width-SourceWidth*scale)/2, (Height-SourceHeight*scale)/2, SourceWidth*scale, SourceHeight*scale); }
        }
        private bool RedDominant() {
            int red = 0, count = 0;
            for (int y = 0; y < Preview.Height; y += Math.Max(1, Preview.Height/24))
                for (int x = 0; x < Preview.Width; x += Math.Max(1, Preview.Width/24)) {
                    Color c = Preview.GetPixel(x,y); count++;
                    if (c.R > 70 && c.R > c.G*1.25 && c.R > c.B*1.25) red++;
                }
            return red > count * .25;
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); if (Width < 20 || Height < 20) return;
            RectangleF image = ImageRect, crop = Crop;
            float s = image.Width/SourceWidth;
            RectangleF box = new RectangleF(image.X+crop.X*s, image.Y+crop.Y*s, crop.Width*s, crop.Height*s);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(Preview, image);
            using (Region mask = new Region(image)) {
                mask.Exclude(box);
                using (Brush shade = new SolidBrush(Color.FromArgb(115, 0, 0, 0))) e.Graphics.FillRegion(shade, mask);
            }
            using (Pen outline = new Pen(redDominant ? Color.Yellow : Color.FromArgb(255, 40, 55), 3))
                e.Graphics.DrawRectangle(outline, box.X, box.Y, box.Width, box.Height);
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e); Focus();
            if (e.Button != MouseButtons.Left || !ImageRect.Contains(e.Location)) return;
            RectangleF r = ImageRect, c = Crop; float s = r.Width/SourceWidth;
            if (!new RectangleF(r.X+c.X*s,r.Y+c.Y*s,c.Width*s,c.Height*s).Contains(e.Location)) return;
            dragging = true; last = e.Location; Capture = true; if (Interacted != null) Interacted();
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e); if (!dragging) return;
            RectangleF c = Crop; double s = ImageRect.Width/SourceWidth;
            CX = c.X+c.Width/2+(e.X-last.X)/s; CY = c.Y+c.Height/2+(e.Y-last.Y)/s;
            c = Crop; CX = c.X+c.Width/2; CY = c.Y+c.Height/2;
            last = e.Location; NotifyChanged();
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
        protected override void OnMouseWheel(MouseEventArgs e) {
            if (!ImageRect.Contains(e.Location)) return;
            HandledMouseEventArgs handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true;
            RectangleF c = Crop; CX = c.X+c.Width/2; CY = c.Y+c.Height/2;
            Zoom = Math.Max(1, Math.Min(20, Zoom * Math.Pow(1.12, e.Delta/120.0)));
            NotifyChanged(); if (Interacted != null) Interacted();
        }
        public void HandleWheel(MouseEventArgs e) { OnMouseWheel(e); }
        protected override void Dispose(bool disposing) { if (disposing && Preview != null) Preview.Dispose(); base.Dispose(disposing); }
    }
    public class Card : Panel {
        public readonly string FilePath;
        public readonly CropView View;
        public readonly CheckBox Selected = new CheckBox();
        public readonly long FileLength, LastWriteUtcTicks;
        public int Turns;
        public Card(string path, Bitmap preview, double ratio, Action changed) {
            FilePath = path; Margin = new Padding(8); Padding = new Padding(8); BackColor = Color.FromArgb(43,48,57);
            PreviewInfo identity = preview.Tag as PreviewInfo;
            if (identity != null) { FileLength = identity.FileLength; LastWriteUtcTicks = identity.LastWriteUtcTicks; }
            else {
                FileInfo info = new FileInfo(path);
                if (info.Exists) { FileLength = info.Length; LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks; }
            }
            View = new CropView(preview); View.Ratio = ratio; View.Dock = DockStyle.Fill;
            Panel side = new Panel { Dock = DockStyle.Right, Width = 45 };
            Selected.Text = ""; Selected.SetBounds(15, 12, 22, 24); Selected.AccessibleName = "Select " + Path.GetFileName(path);
            Selected.CheckedChanged += delegate { changed(); };
            Button rotate = new ThemeButton { Text = "\u21b6", Width = 38, Height = 36, Left = 5, Top = 48, FlatStyle = FlatStyle.Flat, AccessibleName = "Rotate left 90 degrees" };
            rotate.Click += delegate { RotateLeft(); };
            Button reset = new ThemeButton { Text = "Fit", Width = 38, Height = 30, Left = 5, Top = 94, FlatStyle = FlatStyle.Flat, AccessibleName = "Reset crop" };
            reset.Click += delegate { View.Reset(View.Ratio); Selected.Checked = true; };
            side.Controls.AddRange(new Control[] { Selected, rotate, reset });
            Label name = new Label { Text = Path.GetFileName(path), AutoEllipsis = true, Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(View); Controls.Add(side); Controls.Add(name);
            View.Interacted += delegate { Selected.Checked = true; };
            View.Changed += changed;
        }
        public void RotateLeft() {
            Turns = (Turns+1)%4; View.Preview.RotateFlip(RotateFlipType.Rotate270FlipNone);
            int oldWidth = View.SourceWidth; View.SourceWidth = View.SourceHeight; View.SourceHeight = oldWidth;
            View.Reset(View.Ratio); Selected.Checked = true;
        }
        public ImageState CaptureState() {
            RectangleF crop = View.Crop;
            return new ImageState { Name = Path.GetFileName(FilePath), FileLength = FileLength, LastWriteUtcTicks = LastWriteUtcTicks,
                Selected = Selected.Checked, Turns = Turns, SourceWidth = View.SourceWidth, SourceHeight = View.SourceHeight,
                X = crop.X, Y = crop.Y, Width = crop.Width, Height = crop.Height };
        }
        public bool Restore(ImageState state) {
            if (!String.Equals(state.Name,Path.GetFileName(FilePath),StringComparison.OrdinalIgnoreCase) ||
                state.FileLength != FileLength || state.LastWriteUtcTicks != LastWriteUtcTicks || state.Turns < 0 || state.Turns > 3) return false;
            int w = state.Turns%2 == 0 ? View.SourceWidth : View.SourceHeight;
            int h = state.Turns%2 == 0 ? View.SourceHeight : View.SourceWidth;
            if (state.SourceWidth != w || state.SourceHeight != h) return false;
            int oldWidth = View.SourceWidth, oldHeight = View.SourceHeight;
            View.SourceWidth = w; View.SourceHeight = h;
            if (!View.RestoreCrop(new RectangleF((float)state.X,(float)state.Y,(float)state.Width,(float)state.Height))) {
                View.SourceWidth = oldWidth; View.SourceHeight = oldHeight; return false;
            }
            for (int i = 0; i < state.Turns; i++) View.Preview.RotateFlip(RotateFlipType.Rotate270FlipNone);
            Turns = state.Turns; Selected.Checked = state.Selected; View.Invalidate(); return true;
        }
    }
    public class MainWindow : Form {
        readonly FlowLayoutPanel grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
        readonly FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), WrapContents = true };
        readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(15,8,0,0) };
        readonly NumericUpDown targetW = new NumericUpDown { Minimum = 1, Maximum = 16384, Value = 1920, Width = 80 };
        readonly NumericUpDown targetH = new NumericUpDown { Minimum = 1, Maximum = 16384, Value = 1080, Width = 80 };
        readonly CheckBox resize = new CheckBox { Text = "Resize output", AutoSize = true, Padding = new Padding(8,5,8,0) };
        readonly TrackBar size = new TrackBar { Minimum = 240, Maximum = 720, Value = 360, Width = 135, Height = 32, TickStyle = TickStyle.None, SmallChange = 20, LargeChange = 80 };
        readonly Button process;
        readonly Button applySize;
        readonly ComboBox presets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        readonly Label sessionNotice = new Label { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(15,3,0,0), AutoEllipsis = true };
        readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 500 };
        Dictionary<string,ImageState> remembered = new Dictionary<string,ImageState>(StringComparer.OrdinalIgnoreCase);
        string currentFolder;
        string sessionRevision;
        SessionLease sessionLease;
        readonly CropWheelRouter wheelRouter;
        int appliedWidth = 1920, appliedHeight = 1080;
        bool busy, updating, restoring, bulkEditing, sessionWritable = true, sessionDirty, dimensionsDirty;
        double Ratio { get { return (double)appliedWidth/appliedHeight; } }
        IEnumerable<Card> Cards { get { return grid.Controls.OfType<Card>(); } }
        public MainWindow() {
            Text = "QuickCrop"; Width = 1250; Height = 850; MinimumSize = new Size(760,500); StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(29,33,40); ForeColor = Color.FromArgb(235,239,245);
            Button folder = MakeButton("Open folder...", async delegate { await OpenFolder(); });
            presets.Items.AddRange(new object[] { "1920 x 1080 (FHD)", "2560 x 1440 (QHD)", "3840 x 2160 (4K)", "1920 x 1200 (16:10)", "1080 x 1920 (portrait)", "1080 x 1080 (square)", "Custom" });
            presets.SelectedIndex = 0;
            presets.SelectedIndexChanged += delegate {
                if (updating) return;
                int[,] values = { {1920,1080},{2560,1440},{3840,2160},{1920,1200},{1080,1920},{1080,1080} };
                if (presets.SelectedIndex >= 0 && presets.SelectedIndex < 6) {
                    updating = true; targetW.Value = values[presets.SelectedIndex,0]; targetH.Value = values[presets.SelectedIndex,1]; updating = false; ApplyDimensions();
                }
            };
            EventHandler dimensions = delegate {
                if (!updating) { presets.SelectedIndex = 6; dimensionsDirty = true; UpdateStatus(); }
            };
            targetW.ValueChanged += dimensions; targetH.ValueChanged += dimensions;
            process = MakeButton("Process selected...", async delegate { await Process(); });
            applySize = MakeButton("Apply size",delegate { ApplyDimensions(); });
            Button selectAll = MakeButton("Select all",delegate { SetAllSelected(true); });
            Button selectNone = MakeButton("Select none",delegate { SetAllSelected(false); });
            presets.DrawMode = DrawMode.OwnerDrawFixed;
            presets.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (Brush brush = new SolidBrush(selected ? SystemColors.Highlight : presets.BackColor)) e.Graphics.FillRectangle(brush, e.Bounds);
                if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, presets.Items[e.Index].ToString(), e.Font, e.Bounds,
                    selected ? SystemColors.HighlightText : presets.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                e.DrawFocusRectangle();
            };
            toolbar.Controls.AddRange(new Control[] { folder, selectAll, selectNone, presets, new ToolbarNumberHost(targetW), new Label { Text = "x", AutoSize = true, Padding = new Padding(0,8,0,0) }, new ToolbarNumberHost(targetH), applySize, resize,
                new Label { Text = "Card size", AutoSize = true, Padding = new Padding(10,5,0,0) }, size, process });
            FlowLayoutPanel help = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(16,8,0,0) };
            help.Controls.Add(new Label { Text = "Drag the crop to frame an image. Wheel up: tighter crop. Wheel down: wider crop. Editing selects the image.", AutoSize = true });
            Controls.Add(grid); Controls.Add(help); Controls.Add(toolbar); Controls.Add(sessionNotice); Controls.Add(status);
            size.ValueChanged += delegate { grid.SuspendLayout(); foreach (Card card in Cards) SizeCard(card); grid.ResumeLayout(); QueueSave(); };
            resize.CheckedChanged += delegate { UpdateStatus(); QueueSave(); };
            saveTimer.Tick += delegate { SaveSessionNow(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { e.Cancel = true; MessageBox.Show(this,"Please wait for the current operation to finish.","QuickCrop"); }
                else if (!SaveSessionNow()) e.Cancel = MessageBox.Show(this,"Selections could not be saved. Close without saving?\n\n"+sessionNotice.Text,"Unsaved session",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2) != DialogResult.Yes;
            };
            UpdateStatus();
            AppTheme.Apply(this, AppTheme.IsDark);
            Shown += delegate { AlignToolbarControls(); };
            wheelRouter = new CropWheelRouter(this); Application.AddMessageFilter(wheelRouter);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); AppTheme.Apply(this, AppTheme.IsDark); }
        protected override void WndProc(ref Message m) {
            base.WndProc(ref m);
            if ((m.Msg == 0x001A || m.Msg == 0x031A) && IsHandleCreated && !IsDisposed) AppTheme.Apply(this, AppTheme.IsDark);
        }
        Button MakeButton(string text, EventHandler action) {
            Button b = new ThemeButton { Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, Padding = new Padding(6,2,6,2) }; b.Click += action; return b;
        }
        void AlignToolbarControls() {
            int height = toolbar.Controls.OfType<Button>().Max(b => b.Height);
            int comboChrome = presets.Height-presets.ItemHeight;
            toolbar.SuspendLayout();
            try {
                presets.ItemHeight = Math.Max(Font.Height,height-comboChrome);
                foreach (ToolbarNumberHost host in toolbar.Controls.OfType<ToolbarNumberHost>()) host.Height = height;
                resize.Padding = new Padding(8,Math.Max(0,(height-resize.Font.Height-4)/2),8,0);
                size.Height = height;
            } finally { toolbar.ResumeLayout(); }
        }
        void SizeCard(Card card) { card.Size = new Size(size.Value, (int)(size.Value*.70)+36); }
        public bool ApplyDimensions() {
            // Reading Value validates any still-edited numeric text before applying both fields together.
            int width = (int)targetW.Value, height = (int)targetH.Value;
            try { Imaging.ValidateSize(width,height,Imaging.MaxOutputPixels); }
            catch (InvalidDataException ex) { dimensionsDirty = true; UpdateStatus(); status.Text = ex.Message; return false; }
            appliedWidth = width; appliedHeight = height; dimensionsDirty = false;
            ResetRatios(); return true;
        }
        public void SetAllSelected(bool selected) {
            bulkEditing = true;
            try { foreach (Card card in Cards) card.Selected.Checked = selected; }
            finally { bulkEditing = false; }
            CardChanged();
        }
        void ResetRatios() {
            foreach (Card card in Cards) if (Math.Abs(card.View.Ratio/Ratio-1) > .000000001) card.View.Reset(Ratio);
            UpdateStatus(); QueueSave();
        }
        void CardChanged() { if (bulkEditing) return; UpdateStatus(); QueueSave(); }
        void QueueSave() {
            if (restoring || currentFolder == null) return;
            sessionDirty = true;
            if (!sessionWritable) return;
            sessionNotice.Text = "Saving selections..."; saveTimer.Stop(); saveTimer.Start();
        }
        public bool SaveSessionNow() {
            saveTimer.Stop();
            if (!sessionDirty || currentFolder == null) return true;
            if (!sessionWritable) return false;
            try {
                foreach (Card card in Cards) remembered[Path.GetFileName(card.FilePath)] = card.CaptureState();
                sessionRevision = SessionStore.Save(currentFolder,new FolderSession { TargetWidth = appliedWidth, TargetHeight = appliedHeight,
                    Resize = resize.Checked, CardSize = size.Value, Images = remembered.Values.OrderBy(s => s.Name,StringComparer.OrdinalIgnoreCase).ToList() },sessionRevision);
                sessionDirty = false; sessionNotice.Text = "Selections and crop boxes saved."; return true;
            } catch (Exception ex) { sessionNotice.Text = "Could not save selections: "+ex.Message; return false; }
        }
        protected override void Dispose(bool disposing) {
            if (disposing) {
                saveTimer.Stop(); saveTimer.Dispose();
                if (sessionLease != null) { sessionLease.Dispose(); sessionLease = null; }
                if (wheelRouter != null) Application.RemoveMessageFilter(wheelRouter);
            }
            base.Dispose(disposing);
        }
        void UpdateStatus() {
            if (process != null) process.Enabled = !busy && !dimensionsDirty && Cards.Any();
            if (applySize != null) applySize.Enabled = !busy && dimensionsDirty;
            status.Text = dimensionsDirty ? "Click Apply size to use the edited dimensions. Existing crop boxes are unchanged until applied." :
                Cards.Count() == 0 ? "Open a folder to begin. Supported: JPG, PNG, BMP, GIF, TIFF." : Cards.Count()+" images  |  "+Cards.Count(c => c.Selected.Checked)+" selected  |  "+(resize.Checked ? "Resize" : "Crop only");
        }
        void SetBusy(bool value, bool loading = false) {
            busy = value; toolbar.Enabled = !value; grid.Enabled = !value || loading;
            UseWaitCursor = value && !loading;
            UpdateStatus();
        }
        async Task OpenFolder() {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "Choose an input folder", ShowNewFolderButton = false }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { await LoadFolderAsync(dialog.SelectedPath); }
                catch (Exception ex) { MessageBox.Show(this,ex.Message,"Unable to open folder"); }
            }
        }
        public async Task LoadFolderAsync(string folder) {
                if (busy) throw new InvalidOperationException("Another operation is in progress.");
                string[] files;
                try { string[] extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };
                    files = Directory.EnumerateFiles(folder).Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Unable to open folder"); return; }
                if (!SaveSessionNow()) throw new IOException("Current selections could not be saved. Keep this folder open or close without saving before switching folders.\n"+sessionNotice.Text);
                if (sessionLease != null) { sessionLease.Dispose(); sessionLease = null; }
                grid.SuspendLayout();
                try { foreach (Control c in grid.Controls.Cast<Control>().ToArray()) c.Dispose(); }
                finally { grid.ResumeLayout(); }
                currentFolder = folder; sessionDirty = false; sessionWritable = true; saveTimer.Stop(); sessionNotice.Text = "";
                try { sessionLease = new SessionLease(folder); }
                catch (IOException ex) { sessionWritable = false; sessionNotice.Text = "Session is read-only (folder may be open in another QuickCrop): "+ex.Message; }
                catch (UnauthorizedAccessException ex) { sessionWritable = false; sessionNotice.Text = "Session is read-only: "+ex.Message; }
                FolderSession session;
                try {
                    // A revision change while reading must not pair old data with a new revision.
                    sessionRevision = SessionStore.Revision(folder); session = SessionStore.Load(folder);
                    if (sessionRevision != SessionStore.Revision(folder)) throw new IOException("Session changed while loading. Reopen this folder to retry.");
                }
                catch (Exception ex) {
                    session = new FolderSession(); sessionWritable = false;
                    sessionNotice.Text = "Could not read .quickcrop.json; existing file kept. Autosave disabled: "+ex.Message;
                }
                remembered = session.Images.GroupBy(s => s.Name,StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key,g => g.Last(),StringComparer.OrdinalIgnoreCase);
                restoring = true; updating = true;
                try {
                    targetW.Value = session.TargetWidth; targetH.Value = session.TargetHeight;
                    appliedWidth = session.TargetWidth; appliedHeight = session.TargetHeight; dimensionsDirty = false;
                    resize.Checked = session.Resize; size.Value = session.CardSize;
                    presets.SelectedIndex = 6;
                    for (int p = 0; p < 6; p++) if (presets.Items[p].ToString().StartsWith(session.TargetWidth+" x "+session.TargetHeight+" ",StringComparison.Ordinal)) { presets.SelectedIndex = p; break; }
                } finally { updating = false; restoring = false; }
                SetBusy(true, true); List<string> errors = new List<string>();
                Stopwatch timer = Stopwatch.StartNew();
                try {
                    for (int i = 0; i < files.Length; i += ThumbnailLoader.Workers) {
                        status.Text = "Loading "+Math.Min(i+ThumbnailLoader.Workers,files.Length)+" / "+files.Length;
                        ThumbnailResult[] batch = await ThumbnailLoader.LoadBatch(files.Skip(i).Take(ThumbnailLoader.Workers).ToArray());
                        grid.SuspendLayout();
                        try {
                            foreach (ThumbnailResult result in batch) {
                                if (result.Error != null) { errors.Add(Path.GetFileName(result.Path)+": "+result.Error); continue; }
                                Card card = null;
                                try {
                                    card = new Card(result.Path, result.Image, Ratio, CardChanged);
                                    result.Image = null; // Card now owns the thumbnail.
                                    ImageState state;
                                    restoring = true;
                                    try { if (remembered.TryGetValue(Path.GetFileName(result.Path),out state)) card.Restore(state); }
                                    finally { restoring = false; }
                                    SizeCard(card); AppTheme.Apply(card, AppTheme.IsDark); grid.Controls.Add(card);
                                } catch (Exception ex) {
                                    if (card != null) card.Dispose(); errors.Add(Path.GetFileName(result.Path)+": "+ex.Message);
                                }
                            }
                        } finally {
                            foreach (ThumbnailResult result in batch) result.Dispose();
                            grid.ResumeLayout();
                        }
                    }
                    Text = "QuickCrop - "+folder;
                } finally { SetBusy(false); UpdateStatus(); }
                status.Text += "  |  Loaded in "+timer.Elapsed.TotalSeconds.ToString("0.0")+" s";
                if (sessionWritable) QueueSave();
                if (errors.Count > 0) MessageBox.Show(this, "Skipped "+errors.Count+" unreadable images:\n"+String.Join("\n", errors.Take(12)), "Loading report");
        }
        async Task Process() {
            // Commit pending text validation, but require the user to apply changed dimensions
            // and inspect their framing before allowing an export.
            int editedWidth = (int)targetW.Value, editedHeight = (int)targetH.Value;
            if (dimensionsDirty || editedWidth != appliedWidth || editedHeight != appliedHeight) { dimensionsDirty = true; UpdateStatus(); return; }
            Card[] selected = Cards.Where(c => c.Selected.Checked).ToArray();
            if (selected.Length == 0) { MessageBox.Show(this,"Select at least one image, or drag its crop rectangle.","Nothing selected"); return; }
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "Choose an output folder (originals are preserved)" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string output = dialog.SelectedPath; bool doResize = resize.Checked; int w = appliedWidth, h = appliedHeight;
                SaveSessionNow();
                ExportJob[] jobs = selected.Select(c => new ExportJob(c,doResize,w,h)).ToArray();
                SetBusy(true); List<string> errors = new List<string>(); int saved = 0;
                try {
                    foreach (ExportJob job in jobs) {
                        status.Text = "Exporting "+(saved+errors.Count+1)+" / "+selected.Length+" - "+job.Name;
                        try {
                            await Task.Run(() => job.Execute(output)); saved++;
                        } catch (Exception ex) { errors.Add(job.Name+": "+ex.Message); }
                    }
                } finally { SetBusy(false); UpdateStatus(); }
                MessageBox.Show(this, saved+" images saved to:\n"+output+(errors.Count == 0 ? "" : "\n\n"+errors.Count+" failed:\n"+String.Join("\n",errors.Take(12))), "Export complete");
            }
        }
    }
    static class Program {
        [STAThread] static void Main() {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow());
        }
    }
}
