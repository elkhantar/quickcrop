using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using QuickCrop;

static class ReviewTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Review regression: "+message); }
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target); }
    static void Pump(Task task) {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted) { Check(DateTime.UtcNow < deadline,"UI test timed out"); Application.DoEvents(); Thread.Sleep(1); }
        task.GetAwaiter().GetResult();
    }
    static void Rejected(Action action, string message) {
        bool rejected = false;
        try { action(); } catch (InvalidDataException) { rejected = true; } catch (IOException) { rejected = true; } catch (UnauthorizedAccessException) { rejected = true; } catch (ArgumentException) { rejected = true; }
        Check(rejected,message);
    }
    public static void Run(string root) {
        string folder = Path.Combine(root,"review"); Directory.CreateDirectory(folder);
        string imagePath = Path.Combine(folder,"photo.png");
        using (Bitmap image = new Bitmap(160,120)) {
            using (Graphics g = Graphics.FromImage(image)) g.Clear(Color.Red);
            image.Save(imagePath,ImageFormat.Png);
        }
        using (Bitmap thumb = Imaging.Thumbnail(imagePath)) {
            // Mutate after decoding, before constructing its card. A later FileInfo must not
            // relabel old thumbnail pixels with the new source's fingerprint.
            File.SetLastWriteTimeUtc(imagePath,DateTime.UtcNow.AddSeconds(2));
            using (Card card = new Card(imagePath,thumb,16.0/9,delegate {})) {
                Rejected(() => new ExportJob(card,true,320,180).Execute(folder),"File changed between decoding and card creation was exported");
            }
        }
        using (FileStream writer = new FileStream(imagePath,FileMode.Open,FileAccess.Write,FileShare.ReadWrite))
            Rejected(() => { using (Bitmap thumb = Imaging.Thumbnail(imagePath)) {} },"Preview read a file being written");
        using (SessionLease first = new SessionLease(folder))
            Rejected(() => { using (SessionLease second = new SessionLease(folder)) {} },"Second writer acquired the same folder");
        using (SessionLease reopened = new SessionLease(folder)) {} // Lock must be released.
        using (MainWindow first = new MainWindow())
        using (MainWindow second = new MainWindow()) {
            first.Show(); Pump(first.LoadFolderAsync(folder));
            Card card = Field<FlowLayoutPanel>(first,"grid").Controls.OfType<Card>().Single();
            Check(card.View.RestoreCrop(Geometry.Fit(160,120,16.0/9,2.5,100,60)),"Could not set crop");
            RectangleF crop = card.View.Crop;
            Button[] buttons = Field<FlowLayoutPanel>(first,"toolbar").Controls.OfType<Button>().ToArray();
            buttons.Single(b => b.Text == "Select all").PerformClick();
            Check(card.Selected.Checked && card.View.Crop == crop,"Select all altered framing or failed to select");
            buttons.Single(b => b.Text == "Select none").PerformClick();
            Check(!card.Selected.Checked && card.View.Crop == crop,"Select none altered framing");
            buttons.Single(b => b.Text == "Select all").PerformClick();
            NumericUpDown width = Field<NumericUpDown>(first,"targetW"), height = Field<NumericUpDown>(first,"targetH");
            width.Value = 3840; height.Value = 2160;
            Check(card.View.Crop == crop && !Field<Button>(first,"process").Enabled,"Unapplied dimensions reset crops or permitted export");
            Check(first.SaveSessionNow(),"Save failed while size fields were pending");
            Check(SessionStore.Load(folder).TargetWidth == 1920,"Pending values were saved as applied target");
            Check(first.ApplyDimensions() && card.View.Crop == crop,"Same-ratio custom dimensions reset framing");
            Check(first.SaveSessionNow() && SessionStore.Load(folder).TargetWidth == 3840,"Applied dimensions were not saved");
            width.Focus(); Application.DoEvents();
            Check(width.ContainsFocus,"Could not focus width input");
            typeof(CropView).GetMethod("OnMouseEnter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(card.View,new object[] { EventArgs.Empty });
            Check(width.ContainsFocus,"Hovering an image stole keyboard focus");
            width.Value = 16384; height.Value = 16384;
            Check(!first.ApplyDimensions() && card.View.Crop == crop,"Oversized target accepted or altered framing");
            width.Value = 3840; height.Value = 2160; Check(first.ApplyDimensions(),"Could not restore valid dimensions");
            first.SetAllSelected(true); Check(first.SaveSessionNow(),"Could not save before second instance test");
            string saved = File.ReadAllText(Path.Combine(folder,SessionStore.FileName));
            second.Show(); Pump(second.LoadFolderAsync(folder)); second.SetAllSelected(false);
            Check(!second.SaveSessionNow(),"Second instance silently overwrote session");
            Check(File.ReadAllText(Path.Combine(folder,SessionStore.FileName)) == saved,"Second instance modified session file");
            // Simulate an external editor: app must detect the revision change before saving.
            FolderSession external = SessionStore.Load(folder); external.CardSize = 500; SessionStore.Save(folder,external);
            first.SetAllSelected(false); Check(!first.SaveSessionNow(),"External session changes were overwritten");
            Check(SessionStore.Load(folder).CardSize == 500,"External changes were lost");
            // Dispose instead of Close to avoid the expected unsaved-work confirmation.
        }
        using (Bitmap image = Imaging.Open(imagePath)) {
            Rejected(() => Imaging.Save(image,new RectangleF(0,0,160,120),16384,16384,folder),"Oversized allocation was permitted");
            Rejected(() => Imaging.Save(image,new RectangleF(-10,0,20,20),10,10,folder),"Out-of-bounds crop was permitted");
            Rejected(() => Imaging.ValidateSize(Int32.MaxValue,Int32.MaxValue,Imaging.MaxSourcePixels),"Overflow bypassed pixel limit");
            image.Tag = imagePath;
            Directory.CreateDirectory(Path.Combine(folder,"photo_crop.png"));
            Imaging.Save(image,new RectangleF(0,0,160,120),80,60,folder);
            Check(File.Exists(Path.Combine(folder,"photo_crop_1.png")),"Output directory-name collision was not handled");
            Check(Directory.GetFiles(folder,"*.tmp").Length == 0,"Export left staging data after success");
        }
        string badFolder = Path.Combine(root,"bad-sessions"); Directory.CreateDirectory(badFolder);
        string statePath = Path.Combine(badFolder,SessionStore.FileName);
        ImageState valid;
        using (Card card = new Card(imagePath,Imaging.Thumbnail(imagePath),16.0/9,delegate {})) valid = card.CaptureState();
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        foreach (int kind in Enumerable.Range(0,5)) {
            ImageState state = serializer.Deserialize<ImageState>(serializer.Serialize(valid));
            FolderSession document = new FolderSession(); document.Images.Add(state);
            if (kind == 0) state.Width = 0;
            if (kind == 1) state.Turns = 4;
            if (kind == 2) state.X = 200;
            if (kind == 3) document.Images.Add(state);
            if (kind == 4) state.Name = "../photo.png";
            string bad = serializer.Serialize(document); File.WriteAllText(statePath,bad);
            Rejected(() => SessionStore.Load(badFolder),"Malformed image entry accepted (case "+kind+")");
            Check(File.ReadAllText(statePath) == bad,"Malformed session was modified");
        }
        using (FileStream oversized = new FileStream(statePath,FileMode.Create,FileAccess.Write)) oversized.SetLength(SessionStore.MaxBytes+1L);
        Rejected(() => SessionStore.Load(badFolder),"Oversized session was deserialized");
        Console.WriteLine("PASS: adversarial regressions (folder writer lock, session conflicts/validation/size, decode identity, locked sources, select all/none, pending dimensions, hover focus, allocation/crop bounds).");
    }
}
