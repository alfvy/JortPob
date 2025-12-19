using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JortPob
{
    internal class MenuTexManager : IDisposable
    {
        public IconManager icon;
        public LoadingImagesManager images;

        public MenuTexManager(ESM esm)
        {
            icon = new(esm);
            images = new();
        }

        public void Write()
        {
            (var hiBxf, var lowBxf) = icon.Write();
            images.Write(hiBxf, lowBxf);
        }
        public void Dispose()
        {
            icon.Dispose();
            images = null;
        }
    }
}
