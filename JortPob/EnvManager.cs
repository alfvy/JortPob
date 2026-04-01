using JortPob.Common;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using static JortPob.Paramanager;

namespace JortPob
{
    public class EnvManager
    {
        public enum Rem
        {
            Forest, Mountain, Cave, Tomb, Home, Snowfield
        }

        // returns a list of texture names and bytes for them
        private static byte[] defEnvX, defEnvY, defEnvZ, defEnvW; // function to gen these is slow so hold onto the bytes to reuse
        public static List<Tuple<string, byte[]>> GenerateIrradianceTextures(int map, int x, int y, int block, int id, int time, int size, Rem rem)
        {
            string[] names = new string[]
            {
                $"m{map:D2}_{x:D2}_{y:D2}_{block:D2}_GIIV{id:D4}_{time:D2}_W",
                $"m{map:D2}_{x:D2}_{y:D2}_{block:D2}_GIIV{id:D4}_{time:D2}_X",
                $"m{map:D2}_{x:D2}_{y:D2}_{block:D2}_GIIV{id:D4}_{time:D2}_Y",
                $"m{map:D2}_{x:D2}_{y:D2}_{block:D2}_GIIV{id:D4}_{time:D2}_Z",
                $"m{map:D2}_{x:D2}_{y:D2}_{block:D2}_GILM{id:D4}_{time:D2}_rem",
            };

            if (defEnvX == null || defEnvY == null || defEnvZ == null || defEnvW == null)
            {
                defEnvX = Common.DDS.MakeVolumeTexture(16, 200, 200, 200, 19);
                defEnvY = Common.DDS.MakeVolumeTexture(16, 250, 250, 250, 38);
                defEnvZ = Common.DDS.MakeVolumeTexture(16, 193, 193, 193, 24);
                defEnvW = Common.DDS.MakeVolumeTexture(16, 250, 250, 250, 55);
            }

            List<Tuple<string, byte[]>> textures = new();
            textures.Add(new(names[0], defEnvW));
            textures.Add(new(names[1], defEnvX));
            textures.Add(new(names[2], defEnvY));
            textures.Add(new(names[3], defEnvZ));
            textures.Add(new(names[4], System.IO.File.ReadAllBytes(Utility.ResourcePath($"env\\{rem.ToString().ToLower()}_rem.dds"))));

            return textures;
        }

        public static void CreateEnvMaps(HugeTile tile, int envId)
        {
            string region = tile.GetRegion();
            WeatherData weatherData = GetWeatherData(region);

            string mid = $"{tile.map:D2}_{tile.coordinate.x:D2}_{tile.coordinate.y:D2}_{tile.block:D2}"; // msb full name

            Dictionary<string, int> levels = new();
            levels.Add("high", 64);
            levels.Add("middle", 32);
            levels.Add("low", 16);

            // timeID ::: 0 -> 6 for differnt times of day. if you only have 0 it just uses that for all times of day
            for (int timeId = 0; timeId < 7; timeId++)
            {
                foreach (KeyValuePair<string, int> levelPair in levels)
                {
                    string level = levelPair.Key;
                    int size = levelPair.Value;

                    BND4 bnd = new();
                    bnd.Compression = Compression.KRAK();
                    bnd.Version = "07D7R6";

                    List<Tuple<string, byte[]>> textures = GenerateIrradianceTextures(tile.map, tile.coordinate.x, tile.coordinate.y, tile.block, envId, timeId, size, weatherData.rem);

                    int bndId = 0;
                    foreach (Tuple<string, byte[]> texture in textures)
                    {
                        string name = texture.Item1;
                        byte[] data = texture.Item2;

                        TPF tpf = new TPF();
                        tpf.Compression = Compression.NONE();

                        TPF.Texture tex = new();
                        tex.Flags1 = 128;
                        tex.Format = 102;
                        tex.Mipmaps = 1;
                        tex.Name = name;
                        tex.Bytes = data;
                        tex.Type = TPF.TexType.Volume;

                        tpf.Textures.Add(tex);

                        BinderFile file = new();
                        file.ID = bndId++;
                        file.Name = $"N:\\GR\\data\\INTERROOT_win64\\map\\m{mid}\\tex\\Envmap\\{level}\\{timeId:D2}\\{name}.tpf";
                        file.Bytes = tpf.Write();

                        bnd.Files.Add(file);
                    }

                    bnd.Write(Path.Combine(Const.OUTPUT_PATH, $@"map\m{tile.map:D2}\m{mid}\m{mid}_envmap_{timeId:D2}_{level}_00.tpfbnd.dcx"));
                }
            }

            foreach (KeyValuePair<string, int> levelPair in levels)
            {
                string level = levelPair.Key;

                BND4 ivBnd = new();
                ivBnd.Compression = Compression.KRAK();
                ivBnd.Version = "07D7R6";

                for (int timeId = 0; timeId < 7; timeId++)
                {
                    /* Also make IvInfo */
                    byte[] ivInfoData = System.IO.File.ReadAllBytes(Utility.ResourcePath($"env\\{timeId:D2}.ivInfo"));
                    BinderFile ivFile = new();
                    ivFile.Bytes = ivInfoData;
                    ivFile.ID = timeId;
                    ivFile.Name = $"N:\\GR\\data\\INTERROOT_win64\\map\\m{mid}\\tex\\Envmap\\{level}\\IvInfo\\m{mid}_GIIV{envId}_{timeId:D2}.ivInfo";
                    ivBnd.Files.Add(ivFile);
                }

                ivBnd.Write(Path.Combine(Const.OUTPUT_PATH, $@"map\m{tile.map:D2}\m{mid}\\m{mid}_{level}.ivinfobnd.dcx"));
            }
        }

        public static void CreateEnvMaps(InteriorGroup group, int envId)
        {
            WeatherData weatherData = group.GetWeather();

            string mid = $"{group.map:D2}_{group.area:D2}_{group.unk:D2}_{group.block:D2}"; // msb full name

            Dictionary<string, int> levels = new();
            levels.Add("high", 64);
            levels.Add("middle", 32);
            levels.Add("low", 16);

            foreach (KeyValuePair<string, int> levelPair in levels)
            {
                string level = levelPair.Key;
                int size = levelPair.Value;

                BND4 bnd = new();
                bnd.Compression = Compression.KRAK();
                bnd.Version = "07D7R6";

                List<Tuple<string, byte[]>> textures = GenerateIrradianceTextures(group.map, group.area, group.unk, group.block, envId, 0, size, weatherData.rem);

                int bndId = 0;
                foreach (Tuple<string, byte[]> texture in textures)
                {
                    string name = texture.Item1;
                    byte[] data = texture.Item2;

                    TPF tpf = new TPF();
                    tpf.Compression = Compression.NONE();

                    TPF.Texture tex = new();
                    tex.Flags1 = 128;
                    tex.Format = 102;
                    tex.Mipmaps = 1;
                    tex.Name = name;
                    tex.Bytes = data;
                    tex.Type = TPF.TexType.Volume;

                    tpf.Textures.Add(tex);

                    BinderFile file = new();
                    file.ID = bndId++;
                    file.Name = $"N:\\GR\\data\\INTERROOT_win64\\map\\m{mid}\\tex\\Envmap\\{level}\\{0:D2}\\{name}.tpf";
                    file.Bytes = tpf.Write();

                    bnd.Files.Add(file);
                }

                bnd.Write(Path.Combine(Const.OUTPUT_PATH, $@"map\m{group.map:D2}\m{mid}\m{mid}_envmap_{0:D2}_{level}_00.tpfbnd.dcx"));
            }


            foreach (KeyValuePair<string, int> levelPair in levels)
            {
                string level = levelPair.Key;

                BND4 ivBnd = new();
                ivBnd.Compression = Compression.KRAK();
                ivBnd.Version = "07D7R6";

                /* Also make IvInfo */
                byte[] ivInfoData = System.IO.File.ReadAllBytes(Utility.ResourcePath($"env\\{0:D2}.ivInfo"));
                BinderFile ivFile = new();
                ivFile.Bytes = ivInfoData;
                ivFile.ID = 0;
                ivFile.Name = $"N:\\GR\\data\\INTERROOT_win64\\map\\m{mid}\\tex\\Envmap\\{level}\\IvInfo\\m{mid}_GIIV{envId}_{0:D2}.ivInfo";
                ivBnd.Files.Add(ivFile);

                ivBnd.Write(Path.Combine(Const.OUTPUT_PATH, $@"map\m{group.map:D2}\m{mid}\m{mid}_{level}.ivinfobnd.dcx"));
            }
        }
    }
}
