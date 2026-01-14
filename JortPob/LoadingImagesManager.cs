using HKLib.hk2018.hkAtomic;
using IronPython.Runtime.Operations;
using JortPob.Common;
using SoulsFormats;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JortPob
{
    public class LoadingImagesManager
    {
        // there are 34 loading menu images, so that's the limit
        // its technically to bypass this limit
        // but until then just add images in the format 'customLoad##`
        // its recommended that the image be 4096 x 2048
        // but having smaller images is tollerable
        public (BXF4 hiBxf, BXF4 lowBxf) Write(BXF4 hiBxf, BXF4 lowBxf)
        {
            if (Const.DEBUG_SKIP_CUSTOM_LOADING_IMAGES)
                return (hiBxf, lowBxf);

            Lort.NewTask("Binding Loading Menu Images", 2);
            Lort.Log("Writting Loading Menu Images...", Lort.Type.Main);

            var hiFiles = hiBxf.Files
                .Where(f => f.Name.ToLower().Contains("menu_load") && !f.Name.ToLower().Contains("ps5"))
                .ToList();

            byte[] defaultDDS;
            using (var defaultImage =
                Bitmap.FromFile(Utility.ResourcePath("menu\\loading images\\customLoadDeafult.png")) as Bitmap)
            {
                defaultDDS = Common.DDS.BitmapToDDS(
                    defaultImage, DirectXTexNet.DXGI_FORMAT.BC1_UNORM);
            }

            // DDS bytes indexed by number
            var customImages = new Dictionary<int, byte[]>();

            foreach (var customImageFile in Directory.EnumerateFiles(
                        Utility.ResourcePath("menu\\loading images\\")))
            {
                if (!customImageFile.ToLower().Contains("customload"))
                    continue;

                var digits = new string(
                    Path.GetFileNameWithoutExtension(customImageFile)
                        .Where(char.IsDigit)
                        .ToArray());

                if (!int.TryParse(digits, out var index))
                    continue;

                using var image = Bitmap.FromFile(customImageFile) as Bitmap;
                var dds = Common.DDS.BitmapToDDS(
                    image, DirectXTexNet.DXGI_FORMAT.BC1_UNORM);

                customImages[index] = dds;
            }

            foreach (var file in hiFiles)
            {
                var tpf = new TPF();
                var texture = new TPF.Texture();

                var fileName = file.Name.Split('.')[0].Split('\\')[1];

                var digits = new string(
                    Path.GetFileNameWithoutExtension(fileName)
                        .Where(char.IsDigit)
                        .ToArray());

                if (!int.TryParse(digits, out var index))
                    continue;

                var ddsBytes = customImages.ContainsKey(index) ? customImages[index] : defaultDDS;

                texture.Name = fileName;
                texture.Bytes = ddsBytes;
                texture.Format = (byte)Common.DDS.GetTpfFormatFromDdsBytes(ddsBytes);
                tpf.Compression = DCX.Type.DCX_KRAK;
                tpf.Textures.Add(texture);
                file.Bytes = tpf.Write();
            }

            Lort.TaskIterate();

            var lowFiles = lowBxf.Files
                .Where(f => f.Name.ToLower().Contains("menu_load") && !f.Name.ToLower().Contains("ps5"))
                .ToList();

            foreach (var file in lowFiles)
            {
                var tpf = new TPF();
                var texture = new TPF.Texture();

                var fileName = file.Name.Split('.')[0].Split('\\')[1];

                var digits = new string(
                    Path.GetFileNameWithoutExtension(fileName)
                        .Where(char.IsDigit)
                        .ToArray());

                if (!int.TryParse(digits, out var index))
                    continue;

                var ddsBytes = customImages.ContainsKey(index) ? customImages[index] : defaultDDS;

                texture.Name = fileName;
                texture.Bytes = ddsBytes;
                texture.Format = (byte)Common.DDS.GetTpfFormatFromDdsBytes(ddsBytes);
                tpf.Compression = DCX.Type.DCX_KRAK;
                tpf.Textures.Add(texture);
                file.Bytes = tpf.Write();
            }

            hiBxf.Files = hiBxf.Files.OrderBy(file => file.ID).ToList();
            lowBxf.Files = lowBxf.Files.OrderBy(file => file.ID).ToList();

            Lort.TaskIterate();
            return (hiBxf, lowBxf);
        }
    }
}
