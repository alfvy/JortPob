using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JortPob.Common;

namespace JortPob
{
    internal class MenuTextureManager : IDisposable
    {
        public IconManager icon;
        public LoadingImagesManager images;

        public MenuTextureManager(ESM esm)
        {
            icon = new(esm);
            images = new();
        }

        public void Write()
        {
            (var hiBxf, var lowBxf) = icon.Write();
            images.Write(hiBxf, lowBxf);

            hiBxf.Write($"{Const.OUTPUT_PATH}menu\\hi\\00_solo.tpfbhd", $"{Const.OUTPUT_PATH}menu\\hi\\00_solo.tpfbdt");
            lowBxf.Write($"{Const.OUTPUT_PATH}menu\\low\\00_solo.tpfbhd", $"{Const.OUTPUT_PATH}menu\\low\\00_solo.tpfbdt");
        }
        public void Dispose()
        {
            icon.Dispose();
            images = null;
        }
    }
}
