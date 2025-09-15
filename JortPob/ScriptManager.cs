using JortPob.Common;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static JortPob.NpcContent;
using static JortPob.Script;
using static JortPob.Script.Flag;
using static SoulsFormats.MSBAC4.Event;

namespace JortPob
{
    public class ScriptManager
    {
        public ScriptCommon common;
        public List<Script> scripts; // map scripts

        public ScriptManager()
        {
            common = new();
            scripts = new();
        }

        public Script GetScript(int map, int x, int y, int block)
        {
            foreach (Script script in scripts)
            {
                if (script.map == map && script.x == x && script.y == y && script.block == block)
                {
                    return script;
                }
            }

            Script s = new(common, map, x, y, block);
            scripts.Add(s);
            return s;
        }

        public Script GetScript(BaseTile tile)
        {
            if (tile.GetType() != typeof(Tile)) { return null; } // big/huge tiles don't need scripts

            return GetScript(tile.map, tile.coordinate.x, tile.coordinate.y, tile.block);
        }

        public Script GetScript(InteriorGroup group)
        {
            return GetScript(group.map, group.area, group.unk, group.block);
        }

        public Script.Flag GetFlag(Designation designation, string name)
        {
            Script.Flag FindFlag(List<Script.Flag> flags, Designation designation, string name)
            {
                foreach (Script.Flag flag in flags)
                {
                    if(flag.designation == designation && flag.name.ToLower() == name.ToLower())
                    {
                        return flag;
                    }
                }
                return null;
            }

            Script.Flag f = FindFlag(common.flags, designation, name);
            if(f != null) { return f; }

            foreach (Script script in scripts)
            {
                f = FindFlag(script.flags, designation, name);
                if (f != null) { return f; }
            }

            return null;
        }

        /* Sets up race and faction flags that are used globally and interact with specific papyrus calls */
        public void SetupSpecialFlags(ESM esm)
        {
            List<JsonNode> raceJson = [.. esm.GetAllRecordsByType(ESM.Type.Race)];

            // One flag for each race. Single bit. Name of the flag to identify it by is the same as the enum name from NpcContent.Race
            // Reason for doing 10 bits instead of a single byte is because I don't want to set an eventvalueflag from HKS becasue lua is a cursed language
            List<Script.Flag> raceFlags = new();
            foreach(JsonNode json in raceJson)
            {
                raceFlags.Add(common.CreateFlag(Flag.Category.Saved, Flag.Type.Bit, Flag.Designation.PlayerRace, json["id"].GetValue<string>().Replace(" ", ""), 0));
            }

            // Crete the HKS file that will set the correct raceflag after character creation
            // We do this by setting an unused value during character creation based on race and then reading that value in hks. Then we set a flag from that value and donezo.
            // This is defo some shitcode and maybe later we can improve this system in some kind of meaningful way
            string hksFile = System.IO.File.ReadAllText(Utility.ResourcePath(@"script\c0000.hks"));
            string hksJankCall = "\t-- Jank auto-generated code: calls jank function above\r\n\tif not JankRaceInitDone then\r\n\t\tJankRaceInitDone = true\r\n\t\tJankRaceInit()\r\n\tend\r\n\t-- End of jank\r\n";
            string hksJankStart = "-- Jank auto-generated code: function to check burnscars value and set the correct race flag\r\nlocal JankRaceInitDone = false\r\nfunction JankRaceInit()\r\n\tlocal WritePointerChain = 10000\r\n\tlocal TraversePointerChain = 10000\r\n\tlocal SetEventFlag = 10003\r\n\tlocal CHR_INS_BASE = 1\r\n\tlocal PLAYER_GAME_DATA = 0x580\r\n    local BURN_SCAR = 0x876\r\n\tlocal UNSIGNED_BYTE = 0\r\n\tlocal DEBUG_PRINT = 10001\r\n\tlocal BURN_SCAR_VALUE = env(TraversePointerChain, CHR_INS_BASE, UNSIGNED_BYTE, PLAYER_GAME_DATA, BURN_SCAR)\r\n\tif BURN_SCAR_VALUE > 0 then\r\n";
            string hksJankEnd = "\t\tact(WritePointerChain, CHR_INS_BASE, UNSIGNED_BYTE, 0, PLAYER_GAME_DATA, BURN_SCAR)\r\n\tend\r\nend\r\n-- End of jank\r\n";

            string hksJankGen = "";
            foreach(Script.Flag flag in raceFlags)
            {
                NpcContent.Race raceEnum = (Race)System.Enum.Parse(typeof(Race), flag.name);

                hksJankGen += $"\t\tif BURN_SCAR_VALUE == {(int)raceEnum} then\r\n\t\t\tact(DEBUG_PRINT, \"{raceEnum.ToString()}\")\r\n\t\t\tact(SetEventFlag, \"{flag.id}\", 1)\r\n\t\tend\r\n";
            }

            hksFile = hksFile.Replace("-- $$ INJECT JANK UPDATE FUNCTION HERE $$ --", $"{hksJankStart}{hksJankGen}{hksJankEnd}");
            hksFile = hksFile.Replace("-- $$ INJECT JANK UPDATE CALL HERE $$ --", $"{hksJankCall}");
            string hksOutPath = $"{Const.OUTPUT_PATH}action\\script\\c0000.hks";
            if (System.IO.File.Exists(hksOutPath)) { System.IO.File.Delete(hksOutPath); }
            if (!System.IO.Directory.Exists(Path.GetDirectoryName(hksOutPath))) { System.IO.Directory.CreateDirectory(Path.GetDirectoryName(hksOutPath)); }
            System.IO.File.WriteAllText(hksOutPath, hksFile);

            // Max rep seems to be 120, may need to cap it incase you can somehow overflow that
            foreach (Faction faction in esm.factions)
            {
                common.CreateFlag(Flag.Category.Saved, Flag.Type.Bit, Flag.Designation.FactionJoined, faction.id, 0);
                common.CreateFlag(Flag.Category.Saved, Flag.Type.Byte, Flag.Designation.FactionReputation, faction.id, 0);
                common.CreateFlag(Flag.Category.Saved, Flag.Type.Byte, Flag.Designation.FactionRank, faction.id, 0);
            }
        }

        /* Write all EMEVD scripts this class has created */
        public void Write()
        {
            /* Debuggy thing */
            List<Flag> allFlags = new();
            allFlags.AddRange(common.flags);
            foreach (Script script in scripts)
            {
                allFlags.AddRange(script.flags);
            }

            /* Output a cheatsheet with every flag and it's id and starting value */
            List<string> flagInfo = new();
            foreach (Flag flag in allFlags)
            {
                flagInfo.Add($"{flag.category.ToString().PadRight(16)} {flag.type.ToString().PadRight(16)} {flag.designation.ToString().PadRight(24)} {flag.name.ToString().PadRight(48)} {flag.value.ToString().PadRight(6)} {flag.id.ToString()}");
            }
            System.IO.File.WriteAllLines($"{Const.OUTPUT_PATH}flag information.txt", flagInfo.ToArray());

            Lort.Log($"Writing {scripts.Count + 1} EMEVDs...", Lort.Type.Main);
            common.Write();
            foreach(Script script in scripts)
            {
                script.Write();
            }
        }
    }
}
