using JortPob.Common;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Drawing;
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
        public static readonly List<uint> DO_NOT_USE_FLAGS = new();

        public ScriptCommon common;
        public List<Script> scripts; // map scripts
        public ScriptManager()
        {
            common = new();
            scripts = new();

            // I wrote a little baby program to scan the common.emevd script and extract every number used in it.
            // I have these used numbers in a txt file and we parse that into a list
            // We avoid using any of these numbers as flag ids because it can cause collisions with base game common functions
            // This is technically a temporary measure @TODO: !! eventualy we will rewrite common.emevd and replace it with all custom code
            // That will be tough though and not a high priority task for dev.
            string[] lines = System.IO.File.ReadAllLines(Utility.ResourcePath(@"script\common_event_used_values.txt"));            
            foreach(string line in lines)
            {
                DO_NOT_USE_FLAGS.Add(uint.Parse(line));
            }
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
            var lookupKey = Script.FormatFlagLookupKey(designation, name.ToLower());

            Flag f = common.FindFlagByLookupKey(lookupKey);
            if (f != null) { return f; }

            foreach (Script script in scripts)
            {
                f = script.FindFlagByLookupKey(lookupKey);
                if (f != null) { return f; }
            }

            return null;
        }

        /* Sets up race and faction flags that are used globally and interact with specific papyrus calls */
        /* Also some other globalish vars we need for scripts like Reputation and CrimeLevel */
        public void SetupSpecialFlags(ESM esm)
        {
            List<JsonNode> raceJson = [.. esm.GetAllRecordsByType(ESM.Type.Race)];

            // A short for reputation, maybe could fit in a byte but lets just be safe here
            common.CreateFlag(Flag.Category.Saved, Flag.Type.Short, Flag.Designation.Reputation, "Reputation");

            // Crime gold to be paid to guards
            common.CreateFlag(Flag.Category.Saved, Flag.Type.Short, Flag.Designation.CrimeLevel, "CrimeLevel");

            // Crime absolved flag
            common.CreateFlag(Flag.Category.Saved, Flag.Type.Bit, Flag.Designation.CrimeAbsolved, "CrimeAbsolved"); // not temp since load screen happens if going to jail

            // Temp flag that is set when a guard is talking to the player, used to control some guard aggro stuff
            common.CreateFlag(Flag.Category.Temporary, Flag.Type.Bit, Flag.Designation.GuardIsGreeting, "GuardIsGreeting");

            // Temp flag that is set true when a player is talking with an npc, used to prevent idle/hello lines from nearby npcs while you are talking with someone
            common.CreateFlag(Flag.Category.Temporary, Flag.Type.Bit, Flag.Designation.PlayerIsTalking, "PlayerIsTalking");

            // Temp flag that is set to the players current soul/rune count. For use when comparing your cash dosh money count in EMEVD
            Script.Flag playerRuneCount = common.CreateFlag(Flag.Category.Temporary, Flag.Type.Int, Flag.Designation.PlayerRuneCount, "PlayerRuneCount");

            // Temp flag that is set true when a player is sneaking
            Script.Flag playerIsSneakingFlag = common.CreateFlag(Flag.Category.Temporary, Flag.Type.Bit, Flag.Designation.PlayerIsSneaking, "PlayerIsSneaking");

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
            string hksJankEnd = "\t\tact(WritePointerChain, CHR_INS_BASE, UNSIGNED_BYTE, 0, PLAYER_GAME_DATA, BURN_SCAR)\r\n\tend\r\nend\r\n";

            string hksJankGen = "";
            foreach(Script.Flag flag in raceFlags)
            {
                NpcContent.Race raceEnum = (Race)System.Enum.Parse(typeof(Race), flag.name);

                hksJankGen += $"\t\tif BURN_SCAR_VALUE == {(int)raceEnum} then\r\n\t\t\tact(DEBUG_PRINT, \"{raceEnum.ToString()}\")\r\n\t\t\tact(SetEventFlag, \"{flag.id}\", 1)\r\n\t\tend\r\n";
            }

            string hksSneakShitcode = $""""

                                          -- literally just writing if the player is sneaking or not to an emevd flag
                                          if env(IsCOMPlayer) == FALSE then
                                              if c_IsStealth == TRUE then
                                                  act(10003, "{playerIsSneakingFlag.id}", 1)
                                              else
                                                  act(10003, "{playerIsSneakingFlag.id}", 0)
                                              end
                                          end


                                      """";

            string playerRuneCountBase = playerRuneCount.id.ToString()[..7];
            string playerRuneCountOffset = playerRuneCount.id.ToString()[7..];
            string hksSoulCounterShitCode = $""""

                                            	-- writing the players rune count to a 32bit flag so emevd can look at It
                                                if env(IsCOMPlayer) == FALSE then
                                                    local DEBUG_PRINT = 10001
                                                    local TraversePointerChain = 10000
                                                    local SetEventFlag = 10003
                                                    local GAME_DATA_MAN = 0x3D5DF38
                                                    local PLAYER_GAME_INFO = 0x8
                                                    local SOUL_COUNT = 0x6c
                                            		local UNSIGNED_INT = 4
                                                    local currentRunes = env(TraversePointerChain, 0, UNSIGNED_INT, GAME_DATA_MAN, PLAYER_GAME_INFO, SOUL_COUNT)
                                            		for i = 0, 31 do
                                            			local flagBit = tostring("{playerRuneCountBase}".. string.format("%03d", i + {playerRuneCountOffset})) -- kill me
                                            			act(SetEventFlag, flagBit, value_of_bit(currentRunes, i))
                                            		end
                                                end


                                            """";

            string hksBitwiseShitCode =    $""""

                                            -- gets bit n of a number. bitwise ops dont' exist in this version of lua. code yoinked from google
                                            function value_of_bit(num, n)
                                                -- Calculate 2^n
                                                local power_of_two = 2^n 

                                                -- Shift the desired bit to the least significant position
                                                local shifted_num = math.floor(num / power_of_two)

                                                -- Get the value of the least significant bit
                                                local bit_value = shifted_num % 2

                                                -- Return value of that bit
                                            	return bit_value
                                            end


                                            """";

            hksFile = hksFile.Replace("-- $$ INJECT JANK UPDATE FUNCTION HERE $$ --", $"{hksJankStart}{hksJankGen}{hksJankEnd}{hksBitwiseShitCode}");
            hksFile = hksFile.Replace("-- $$ INJECT JANK UPDATE CALL HERE $$ --", $"{hksSneakShitcode}{hksSoulCounterShitCode}{hksJankCall}");
            string hksOutPath = $"{Const.OUTPUT_PATH}action\\script\\c0000.hks";
            if (System.IO.File.Exists(hksOutPath)) { System.IO.File.Delete(hksOutPath); }
            if (!System.IO.Directory.Exists(Path.GetDirectoryName(hksOutPath))) { System.IO.Directory.CreateDirectory(Path.GetDirectoryName(hksOutPath)); }
            System.IO.File.WriteAllText(hksOutPath, hksFile);

            // Max rep seems to be 120, may need to cap it incase you can somehow overflow that
            foreach (FactionInfo faction in esm.factions)
            {
                common.CreateFlag(Flag.Category.Saved, Flag.Type.Bit, Flag.Designation.FactionJoined, faction.id, 0);
                common.CreateFlag(Flag.Category.Saved, Flag.Type.Byte, Flag.Designation.FactionReputation, faction.id, 0);
                common.CreateFlag(Flag.Category.Saved, Flag.Type.Byte, Flag.Designation.FactionRank, faction.id, 0);
                common.CreateFlag(Flag.Category.Saved, Flag.Type.Bit, Flag.Designation.FactionExpelled, faction.id, 0);
            }
        }

        /* This event is triggered when player goes to jail or pays fines to a guard. Resets all crime stuff like npc hostility and crime gold */
        public void GenerateGlobalCrimeAbsolvedEvent()
        {
            List<Script.Flag> allFlags = new();
            allFlags.AddRange(common.flags);
            foreach (Script script in scripts)
            {
                allFlags.AddRange(script.flags);
            }

            Script.Flag eventFlag = common.CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, "GlobalAbsolveCrimeEvent");
            EMEVD.Event absolveEvent = new();
            absolveEvent.ID = eventFlag.id;

            Script.Flag absolveFlag = GetFlag(Script.Flag.Designation.CrimeAbsolved, "CrimeAbsolved");
            absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {absolveFlag.id});"));  // if absolve flag set

            Script.Flag crimeLevel = GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
            absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"EventValueOperation({crimeLevel.id}, {crimeLevel.Bits()}, 0, 0, 1, 5);")); // 5 is CalculationType.Assign

            int delayCounter = 0; // if you do to much in a single frame the game crashes so every hundred flags we wait a frame
            foreach (Script.Flag flag in allFlags)
            {
                if (flag.designation != Script.Flag.Designation.Hostile) { continue; }
                absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {flag.id}, OFF);"));

                if(delayCounter++ > 100)
                {
                    absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"WaitFixedTimeFrames(1);"));
                    delayCounter = 0;
                }
            }

            absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {absolveFlag.id}, OFF);"));
            absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);")); // restart so its ready to go again when the player fucks up

            common.emevd.Events.Add(absolveEvent);
            common.init.Instructions.Add(common.AUTO.ParseAdd($"InitializeEvent(0, {eventFlag.id})"));  // initialize in common
        }

        public void GenerateAreaEvents()
        {
            foreach(Script script in scripts)
            {
                script.GenerateCrimeEvents();
                script.GenerateThieveryEvent();
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
                /* If the description of a flag looks like it's a number, its probably an entity id, search the entityIdMappings and see if we have some info on it to include in this file */
                string desc = null;
                foreach (Script script in scripts)
                {
                    if (!Utility.StringIsInteger(flag.name)) { break; } // dont bother checking unless flag name appears to be an entityid
                    if (script.entityIdMapping.ContainsKey(uint.Parse(flag.name))) { desc = script.entityIdMapping[uint.Parse(flag.name)]; break; }
                }
                /* Write */
                flagInfo.Add($"{flag.category.ToString().PadRight(16)} {flag.type.ToString().PadRight(16)} {flag.designation.ToString().PadRight(24)} {flag.name.ToString().PadRight(48)} {flag.value.ToString().PadRight(6)} {flag.id.ToString().PadRight(18)} {(desc!=null?desc:"")}");
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
