using JortPob.Common;
using System.Drawing;
using System.Drawing.Imaging;

namespace JortPob.Tests
{
    [TestClass]
    public sealed class DescribeUtility
    {
        [TestMethod]
        public void LinearToSRGBShouldConvertCorrectly()
        {
            Utility.InitSRGBCache();

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

            Color ConvertColor(Color color)
            {
                byte alpha = color.A;
                byte red = (byte)(ConvertValue(color.R / 255f) * 255);
                byte green = (byte)(ConvertValue(color.G / 255f) * 255);
                byte blue = (byte)(ConvertValue(color.B / 255f) * 255);
                return Color.FromArgb(alpha, red, green, blue);
            }

            Color[][] inputColors = [[Color.Red, Color.Green, Color.Blue],
                                    [Color.FromArgb(0x7F1A3B01), Color.FromArgb(0x0113ABFF), Color.FromArgb(0x300F3012)],
                                    [Color.ForestGreen, Color.DarkSalmon, Color.Chocolate]];
            Color[][] expectedOutputColor = inputColors.Select(cArr => cArr.Select(ConvertColor).ToArray()).ToArray();

            var inputBitmap = new Bitmap(3, 3, PixelFormat.Format32bppArgb);
            for (var h = 0; h < inputColors.Length; h++)
            {
                for (var w = 0; w < inputColors[h].Length; w++)
                {
                    inputBitmap.SetPixel(w, h, inputColors[h][w]);
                }
            }

            var convertedBitmap = Utility.LinearToSRGB(inputBitmap);
            for (var h = 0; h < inputColors.Length; h++)
            {
                for (var w = 0; w < inputColors[h].Length; w++)
                {
                    var newColor = convertedBitmap.GetPixel(w, h);
                    Assert.AreEqual(expectedOutputColor[h][w], newColor);
                }
            }
        }
    }
}
