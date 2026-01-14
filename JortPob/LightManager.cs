using JortPob.Common;
using SoulsFormats;
using System.Linq;
using System.Numerics;


/* Quite literally copy pasted class from the DS3 portjob project. Adapating as I go but uhh blame DS3 if bugs */
namespace JortPob
{
    /* Manages the btl light file for an msb */
    public class LightManager
    {
        private int map, x, y, block;
        private BTL btl;
        //public BTAB btab;
        public LightManager(int map, int x, int y, int block)
        {
            this.map = map;
            this.x = x;
            this.y = y;
            this.block = block;
            btl = new();
            btl.Version = 6;
            btl.Compression = SoulsFormats.DCX.Type.DCX_DFLT_10000_44_9;

            /*btab = new();
            btab.Entries = new();
            btab.BigEndian = false;
            btab.LongFormat = true;
            btab.Compression = SoulsFormats.DCX.Type.DCX_DFLT_10000_44_9;*/
        }

        public LightManager(int map, Int2 coordinate, int block) : this(map, coordinate.x, coordinate.y, block) { }

        /* Returns number of lights this light manager has in it */
        /* Used when writing files, if it's empty we just just don't write this at all */
        public int Count()
        {
            return btl.Lights.Count();
        }

        /* Converts morrowind light reference into btl light. Only used for non physical lights :: LightContent */
        public void CreateLight(LightContent mwl)
        {
            // DEBUG EXAMPLE
            //BTL DEBUG_EXAMPLE = BTL.Read(@"I:\SteamLibrary\steamapps\common\ELDEN RING\Game\map\m60\m60_34_42_00\m60_34_42_00_0000.btl.dcx");

            BTL.Light erl = new();
            erl.Name = mwl.id;

            erl.Type = BTL.LightType.Point;
            erl.Position = mwl.relative + Const.MSB_OFFSET;
            erl.Radius = mwl.radius;
            erl.Rotation = Vector3.Zero;

            erl.DiffuseColor = System.Drawing.Color.FromArgb(255, mwl.color.x, mwl.color.y, mwl.color.z);
            erl.DiffusePower = 2;

            erl.SpecularColor = System.Drawing.Color.FromArgb(255, mwl.color.x, mwl.color.y, mwl.color.z);
            erl.SpecularPower = 2;

            erl.CastShadows = false;
            erl.ShadowColor = System.Drawing.Color.FromArgb(0, 0, 0, 0);

            erl.FlickerBrightnessMult = 1;
            erl.FlickerIntervalMax = 0;
            erl.FlickerIntervalMin = 0;

            erl.Sharpness = 1;
            erl.Width = 0;
            erl.ConeAngle = 0;
            erl.NearClip = 1;

            /* Hardcoded unknown janko, copied from: m38_00.btl -> "動的_冒頭空洞_009" */
            erl.Unk1C = true;
            erl.Unk30 = 0;
            erl.Unk34 = 0;
            erl.Unk50 = 4;
            erl.Unk54 = 2;
            erl.Unk5C = -1;
            erl.Unk64 = new byte[] { 0, 0, 0, 1 };
            erl.Unk68 = 0;
            erl.Unk70 = 0;
            erl.Unk80 = -1;
            erl.Unk84 = new byte[] { 0, 0, 0, 0 };
            erl.Unk88 = 0;
            erl.Unk90 = 0;
            erl.Unk98 = 1;
            erl.UnkA0 = new byte[] { 1, 0, 2, 1 };
            erl.UnkAC = 0;
            erl.UnkBC = 0;
            erl.UnkC0 = new byte[] { 0, 0, 0, 0 };
            erl.UnkC4 = 0;
            erl.UnkC8 = 0;
            erl.UnkCC = 0;
            erl.UnkD0 = 0;
            erl.UnkD4 = 0;
            erl.UnkD8 = 0;
            erl.UnkDC = 0;
            erl.UnkE0 = 0;

            btl.Lights.Add(erl);
        }

        public void Write()
        {
            string path = $"{Const.OUTPUT_PATH}map\\m{map:D2}\\m{map:D2}_{x:D2}_{y:D2}_{block:D2}\\m{map:D2}_{x:D2}_{y:D2}_{block:D2}_0000.btl.dcx";
            btl.Write(path, DCX.Type.DCX_DFLT_10000_44_9);
            //btab.Write($"{path}.btab.dcx", DCX.Type.DCX_DFLT_10000_44_9);
        }
    }
}