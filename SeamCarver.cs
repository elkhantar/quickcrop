using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuickCrop {
    // Independent implementation of minimum-energy, connected seam removal (Avidan/Shamir).
    // The JS Image Carver demo inspired this feature; no JS runtime or third-party code is bundled.
    public static class SeamCarver {
        public const long MaxCarvingPixels = 24000000;

        public static Size AspectSize(int width, int height, int targetWidth, int targetHeight) {
            if (width < 1 || height < 1 || targetWidth < 1 || targetHeight < 1) throw new ArgumentOutOfRangeException("Dimensions must be positive.");
            if ((long)width*targetHeight > (long)height*targetWidth)
                return new Size(Math.Max(1,Math.Min(width,(int)Math.Round(height*(double)targetWidth/targetHeight))),height);
            return new Size(width,Math.Max(1,Math.Min(height,(int)Math.Round(width*(double)targetHeight/targetWidth))));
        }

        static int Difference(int first, int second) {
            int a = (int)((uint)first >> 24), b = (int)((uint)second >> 24), value = Math.Abs(a-b);
            // Ignore invisible RGB when evaluating energy; retain original RGBA for output.
            for (int shift = 0; shift <= 16; shift += 8) {
                int x = (((first >> shift)&255)*a+255*(255-a)+127)/255;
                int y = (((second >> shift)&255)*b+255*(255-b)+127)/255;
                value += Math.Abs(x-y);
            }
            return value;
        }
        static int Energy(int[] pixels, int stride, int width, int height, int x, int y) {
            return Difference(pixels[y*stride+Math.Max(0,x-1)],pixels[y*stride+Math.Min(width-1,x+1)])+
                Difference(pixels[Math.Max(0,y-1)*stride+x],pixels[Math.Min(height-1,y+1)*stride+x]);
        }

        public static Bitmap Carve(Bitmap source, int targetWidth, int targetHeight,
            CancellationToken cancellation = default(CancellationToken), IProgress<int> progress = null) {
            cancellation.ThrowIfCancellationRequested();
            Size output = AspectSize(source.Width,source.Height,targetWidth,targetHeight);
            bool transpose = output.Height < source.Height;
            int stride = transpose ? source.Height : source.Width, height = transpose ? source.Width : source.Height;
            int width = stride, target = transpose ? output.Height : output.Width, seams = width-target;
            if (progress != null) progress.Report(0);
            if (seams == 0) {
                Bitmap copy = source.Clone(new Rectangle(0,0,source.Width,source.Height),PixelFormat.Format32bppArgb);
                try {
                    cancellation.ThrowIfCancellationRequested();
                    copy.Tag = source.Tag; if (progress != null) progress.Report(100); return copy;
                } catch { copy.Dispose(); throw; }
            }
            if ((long)source.Width*source.Height > MaxCarvingPixels)
                throw new InvalidOperationException("Experimental seam carving supports up to 24 megapixels. Use normal cropping or a smaller input image.");
            int[] pixels = new int[checked(stride*height)], energy = new int[pixels.Length];
            sbyte[] parents = new sbyte[pixels.Length];
            int[] row = new int[source.Width], seam = new int[height];
            long[] previous = new long[stride], current = new long[stride];
            BitmapData data = source.LockBits(new Rectangle(0,0,source.Width,source.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {
                for (int y = 0; y < source.Height; y++) {
                    cancellation.ThrowIfCancellationRequested();
                    Marshal.Copy(new IntPtr(data.Scan0.ToInt64()+(long)y*data.Stride),row,0,row.Length);
                    if (transpose) for (int x = 0; x < row.Length; x++) pixels[x*stride+y] = row[x];
                    else Array.Copy(row,0,pixels,y*stride,row.Length);
                }
            } finally { source.UnlockBits(data); }
            for (int y = 0; y < height; y++) {
                cancellation.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++) energy[y*stride+x] = Energy(pixels,stride,width,height,x,y);
            }
            int percent = 0;
            for (int removed = 0; removed < seams; removed++) {
                cancellation.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++) previous[x] = energy[x];
                for (int y = 1; y < height; y++) {
                    if ((y&31) == 0) cancellation.ThrowIfCancellationRequested();
                    int offset = y*stride;
                    for (int x = 0; x < width; x++) {
                        int parent = x; long best = previous[x];
                        if (x > 0 && previous[x-1] < best) { best = previous[x-1]; parent = x-1; }
                        if (x+1 < width && previous[x+1] < best) { best = previous[x+1]; parent = x+1; }
                        current[x] = best+energy[offset+x]; parents[offset+x] = (sbyte)(parent-x);
                    }
                    long[] swap = previous; previous = current; current = swap;
                }
                int end = 0;
                for (int x = 1; x < width; x++) if (previous[x] < previous[end]) end = x;
                for (int y = height-1; y >= 0; y--) { seam[y] = end; if (y > 0) end += parents[y*stride+end]; }
                for (int y = 0; y < height; y++) {
                    if ((y&31) == 0) cancellation.ThrowIfCancellationRequested();
                    int offset = y*stride+seam[y], count = width-seam[y]-1;
                    Array.Copy(pixels,offset+1,pixels,offset,count); Array.Copy(energy,offset+1,energy,offset,count);
                }
                width--;
                // Connected seams move at most one pixel per row. Only this narrow band
                // changes neighbors; all other energy values remain valid after shifting.
                for (int y = 0; y < height; y++) {
                    if ((y&31) == 0) cancellation.ThrowIfCancellationRequested();
                    for (int x = Math.Max(0,seam[y]-2); x <= Math.Min(width-1,seam[y]+2); x++)
                        energy[y*stride+x] = Energy(pixels,stride,width,height,x,y);
                }
                int updated = (int)((long)(removed+1)*100/seams);
                if (progress != null && updated != percent) { percent = updated; progress.Report(percent); }
            }
            cancellation.ThrowIfCancellationRequested();
            Bitmap result = new Bitmap(output.Width,output.Height,PixelFormat.Format32bppArgb);
            try {
                BitmapData destination = result.LockBits(new Rectangle(0,0,result.Width,result.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                try {
                    int[] outputRow = new int[output.Width];
                    for (int y = 0; y < output.Height; y++) {
                        cancellation.ThrowIfCancellationRequested();
                        if (transpose) for (int x = 0; x < output.Width; x++) outputRow[x] = pixels[x*stride+y];
                        else Array.Copy(pixels,y*stride,outputRow,0,output.Width);
                        Marshal.Copy(outputRow,0,new IntPtr(destination.Scan0.ToInt64()+(long)y*destination.Stride),outputRow.Length);
                    }
                } finally { result.UnlockBits(destination); }
                result.Tag = source.Tag; return result;
            } catch { result.Dispose(); throw; }
        }
    }
}
