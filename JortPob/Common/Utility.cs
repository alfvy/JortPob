using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using WitchyFormats;
using Xbrz;
using static Community.CsharpSqlite.Sqlite3;

namespace JortPob.Common
{
    public static class Utility
    {
        private static readonly char[] _dirSep = { '\\', '/' };

        /* Take a full file path and returns just a file name without directory or extensions */
        public static string PathToFileName(string fileName)
        {
            if (fileName.EndsWith("\\") || fileName.EndsWith("/"))
                fileName = fileName.TrimEnd(_dirSep);

            if (fileName.Contains("\\") || fileName.Contains("/"))
                fileName = fileName.Substring(fileName.LastIndexOfAny(_dirSep) + 1);

            if (fileName.Contains("."))
                fileName = fileName.Substring(0, fileName.LastIndexOf('.'));

            return fileName;
        }

        public static string ResourcePath(string path)
        {
            return $"{AppDomain.CurrentDomain.BaseDirectory}Resources\\{path}";
        }

        public static uint FNV1_32(byte[] data)
        {
            const uint FNV_PRIME = 16777619;
            const uint FNV_OFFSET_BASIS = 2166136261;

            uint hash = FNV_OFFSET_BASIS;

            foreach (byte b in data)
            {
                hash *= FNV_PRIME;
                hash ^= b;
            }

            return hash;
        }

        /* Try to split a piece of text on punctuation to fit within the max size of a subtitle in Elden Ring */
        public static List<string> CivilizedSplit(string text)
        {
            // Break apart all text by punctuation
            List<string> lines = new();
            string concat = "";
            for(int i=0;i<text.Length;i++)
            {
                char c = text[i];
                concat += c;

                bool isPunc = c == '.' || c == ';' || c == '?' || c == '!';
                if (isPunc) { lines.Add(concat); concat = ""; }
            }
            if (concat.Trim() != "") { lines.Add(concat.Trim()); }

            // See if any lines desperately need to be split on a comma or in the middle of a sentence
            for(int i=0;i<lines.Count();i++)
            {
                string line = lines[i];
                if(line.Length > Const.MAX_CHAR_PER_TALK)
                {
                    // search from center for a place split
                    int bestComma = -1;
                    int bestSpace = -1;
                    int middle = line.Length / 2;
                    for(int j=0;j<line.Length/2;j++)
                    {
                        int left = Math.Max(0, middle - j);
                        int right = Math.Min(middle + j, line.Length-1);

                        if (line[left] == ' ' && bestSpace == -1) { bestSpace = left; }
                        if (line[right] == ' ' && bestSpace == -1) { bestSpace = right; }
                        if (line[left] == ',' && bestComma == -1) { bestComma = left; }
                        if (line[right] == ',' && bestComma == -1) { bestComma = right; }
                    }

                    int splitAt; bool onSpace;
                    // Split on a comma
                    if (bestComma != -1) { splitAt = bestComma; onSpace = false; }
                    // Split on a space
                    else { splitAt = bestSpace; onSpace = true; }

                    string a = $"{line.Substring(0, splitAt+1).Trim()}{(onSpace?"--":"")}";
                    string b = $"{(onSpace ? "--" : "")}{line.Substring(splitAt+1, line.Length - splitAt - 1).Trim()}";

                    // Add lines back in the right spot
                    lines.RemoveAt(i);
                    lines.Insert(i, b);
                    lines.Insert(i, a);
                }
            }

            // Reassemble within tolerence of a single subtitle
            List<string> recombined = new();
            concat = "";
            foreach(string line in lines)
            {
                if(line.Length > Const.MAX_CHAR_PER_TALK)
                {
                    if(concat != "") { recombined.Add(concat); }
                    concat = line;
                    //Lort.Log($"## WARNING ## Line exceeds character limit: {text}", Lort.Type.Debug);
                }
                else if(concat.Length + line.Length <= Const.MAX_CHAR_PER_TALK)
                {
                    concat += $" {line}";
                }
                else
                {
                    recombined.Add(concat);
                    concat = line;
                }
            }
            recombined.Add(concat);

            if (recombined.Count() >= 8) { Lort.Log($"## WARNING ## Line exceeds split limit: {text}", Lort.Type.Debug); }

            return recombined;
        }

        public static string StringAwareLower(string s)
        {
            bool inQuote = false;
            string text = s;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"') { inQuote = !inQuote; }
                else if (char.IsUpper(c) && !inQuote) { text = text.Remove(i, 1).Insert(i, $"{char.ToLower(c)}"); }
            }
            return text;
        }

        public static string[] StringAwareSplit(string text, char on)
        {
            if(text.Trim() == "") { return new string[0]; } // empty string = emptry array

            List<string> split = text.Split(on).ToList();
            List<string> recomb = new();
            for (int i = 0; i < split.Count(); i++)
            {
                string s = split[i];
                if (s.StartsWith("\""))
                {
                    if (s.Split("\"").Length - 1 == 2) { recomb.Add(s.Replace("\"", "")); }
                    else
                    {
                        string itrNxt = split[++i];
                        while (!itrNxt.Contains("\""))
                        {
                            itrNxt += $" {split[++i]}";
                        }
                        recomb.Add(($"{s} {itrNxt}").Replace("\"", ""));
                    }
                    continue;
                }

                recomb.Add(s);
            }
            return recomb.ToArray();
        }

        public static string StringAwareReplace(string s, char find, char replace)
        {
            bool inQuote = false;
            string text = s;
            for(int i=0;i<text.Length;i++)
            {
                char c = text[i];
                if (c == '"') { inQuote = !inQuote; }
                else if (c == find && !inQuote) { text = text.Remove(i, 1).Insert(i, $"{replace}"); }
            }
            return text;
        }
      
        public static string SanitizeTextForComment(string text)
        {
            return text.Replace("\r", "").Replace("\n", "");
        }

        public static bool StringIsInteger(string text)
        {
            return int.TryParse(text, out _);
        }

        public static bool StringIsFloat(string text)
        {
            return float.TryParse(text, out _);
        }

        public static bool StringIsOperator(string text)
        {
            string allowableLetters = "+=<>!-*/";

            foreach (char c in text)
            {
                // This is using String.Contains for .NET 2 compat.,
                //   hence the requirement for ToString()
                if (!allowableLetters.Contains(c.ToString()))
                    return false;
            }

            return true;
        }

        private static Random random;
        public static int RandomRange(int min, int max)
        {
            if(random == null) { random = new(Const.RANDOM_SEED); }

            return random.Next(min, max);
        }

        public static Bitmap XbrzUpscale(Bitmap bitmap, int factor)
        {
            // Convert to ARGB int[] array
            int width = bitmap.Width, height = bitmap.Height;
            int[] srcPixels = new int[width * height];
            var rect = new Rectangle(0, 0, width, height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, srcPixels, 0, srcPixels.Length);
            bitmap.UnlockBits(data);

            // Perform scaling
            var scaler = new XbrzScaler(factor, withAlpha: true);
            int[] scaledPixels = scaler.ScaleImage(srcPixels, null, width, height);

            // Convert back to Bitmap
            int scaledWidth = width * factor, scaledHeight = height * factor;
            var scaledBitmap = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppArgb);
            var scaledRect = new Rectangle(0, 0, scaledWidth, scaledHeight);
            var scaledData = scaledBitmap.LockBits(scaledRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            System.Runtime.InteropServices.Marshal.Copy(scaledPixels, 0, scaledData.Scan0, scaledPixels.Length);
            scaledBitmap.UnlockBits(scaledData);

            return scaledBitmap;
        }

        /* Converts a linear color space to SRGB */
        public static Bitmap LinearToSRGB(Bitmap bitmap)
        {
            Bitmap linearBitmap = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);

            float ConvertValue(float colorValue)
            {
                if (colorValue <= 0.0031308f)
                {
                    return colorValue * 12.92f;
                }
                else
                {
                    return 1.055f * ((float)Math.Pow(colorValue, 1.0f / 2.4f)) - 0.055f;
                }
            }

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color srgbColor = bitmap.GetPixel(x, y);
                    // Convert each channel from sRGB to linear
                    float r = ConvertValue(srgbColor.R / 255f);
                    float g = ConvertValue(srgbColor.G / 255f);
                    float b = ConvertValue(srgbColor.B / 255f);
                    float a = srgbColor.A / 255f; // Alpha remains the same
                    Color linearColor = Color.FromArgb(
                        (int)(a * 255),
                        (int)(r * 255),
                        (int)(g * 255),
                        (int)(b * 255)
                    );
                    linearBitmap.SetPixel(x, y, linearColor);
                }
            }
            return linearBitmap;
        }

        public static unsafe Bitmap LinearToSRGBAlt(Bitmap bitmap)
        {
            Bitmap srgbBitmap = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);

            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);

            // technically this can be done in place, but better safe than sorry
            // due to bit manipulation shenanigens, bit locking is required
            BitmapData bmpDataIn = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);
            BitmapData bmpDataOut = srgbBitmap.LockBits(rect, ImageLockMode.WriteOnly, srgbBitmap.PixelFormat);

            try
            {
                byte* ptrIn = (byte*)bmpDataIn.Scan0;
                byte* ptrOut = (byte*)bmpDataOut.Scan0;

                // LUT calculation
                float[] sRGB_LUT = new float[256];
                for (int i = 0; i < 256; i++)
                {
                    float linearValue = i / 255.0f;
                    sRGB_LUT[i] = Value(linearValue);
                }

                int totalBytes = bmpDataIn.Stride * bitmap.Height;

                for (int i = 0; i < totalBytes; i += 4)
                {
                    // since colors are big endian, they're reversed as b g r a
                    byte linearB = ptrIn[i];
                    byte linearG = ptrIn[i + 1];
                    byte linearR = ptrIn[i + 2];
                    byte alphaA = ptrIn[i + 3];

                    // lut to the save
                    float srgbR_float = sRGB_LUT[linearR];
                    float srgbG_float = sRGB_LUT[linearG];
                    float srgbB_float = sRGB_LUT[linearB];

                    // squish the results back to range
                    ptrOut[i] = (byte)Math.Round(srgbB_float * 255f);
                    ptrOut[i + 1] = (byte)Math.Round(srgbG_float * 255f);
                    ptrOut[i + 2] = (byte)Math.Round(srgbR_float * 255f);

                    ptrOut[i + 3] = alphaA;
                }
            }
            finally
            {
                // MUST unlock the bits after processing
                bitmap.UnlockBits(bmpDataIn);
                srgbBitmap.UnlockBits(bmpDataOut);
            }

            return srgbBitmap;

            float Value(float linearValue)
            {
                linearValue = Math.Max(0f, Math.Min(1f, linearValue));

                if (linearValue <= 0.0031308f)
                {
                    return linearValue * 12.92f;
                }
                else
                {
                    return 1.055f * ((float)FastPow(linearValue, 1.0f / 2.4f)) - 0.055f;
                }
            }
        }

        // source: trust me bro
        public static double FastPow(double a, double b)
        {
            long tmp = BitConverter.DoubleToInt64Bits(a);
            long tmp2 = (long)(b * (tmp - 4606921280493453312L)) + 4606921280493453312L;
            return BitConverter.Int64BitsToDouble(tmp2);
        }

        /* Code borrowed from https://stackoverflow.com/questions/1922040/how-to-resize-an-image-c-sharp */
        public static Bitmap ResizeBitmap(Image image, int width, int height)
        {
            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);

            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }

            return destImage;
        }

        /* Sort binderfiles by id */
        public static void SortBND4(BND4 bnd)
        {
            bnd.Files = bnd.Files.OrderBy(file => file.ID).ToList();
        }

        public static void SortFsParam(FsParam param)
        {
            param.Rows = param.Rows.AsParallel().OrderBy(row => row.ID).ToList();
        }

        public static void SortPARAM(SoulsFormats.PARAM param)
        {
            param.Rows = param.Rows.AsParallel().OrderBy(row => row.ID).ToList();
        }

        public static void SortFMG(FMG fmg)
        {
            fmg.Entries = fmg.Entries.AsParallel().OrderBy(entry => entry.ID).ToList();
        }

        public static long Pow(int x, uint pow)
        {
            int ret = 1;
            while (pow != 0)
            {
                if ((pow & 1) == 1)
                    ret *= x;
                x *= x;
                pow >>= 1;
            }
            return ret;
        }
    }


    public static class IListExtensions
    {
        /// <summary>
        /// Shuffles the element order of the specified list.
        /// </summary>
        public static void Shuffle<T>(this IList<T> ts)
        {
            var count = ts.Count;
            var last = count - 1;
            Random rand = new Random();
            for (var i = 0; i < last; ++i)
            {
                var r = rand.Next(i, count);
                var tmp = ts[i];
                ts[i] = ts[r];
                ts[r] = tmp;
            }
        }
    }
}
