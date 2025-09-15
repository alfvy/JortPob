using HKLib.hk2018.hkaiCollisionAvoidance;
using JortPob.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static JortPob.Script;
using static SoulsFormats.MQB;

namespace JortPob
{
    public class Dialog
    {
        /* Just a serializiation of the dialog and dialoginfo thingies. We will be iterating through them a lot so may as well do it. */
        public class DialogRecord
        {
            public enum Type
            {
                Greeting, Topic, Journal, Choice,
                Alarm, Attack, Flee, Hello, Hit, Idle, Intruder, Thief,
                AdmireFail, AdmireSuccess, BribeFail, BribeSuccess, InfoRefusal, InfoFail, IntimidateFail, IntimidateSuccess, ServiceRefusal, TauntFail, TauntSuccess
            }

            public readonly Type type;
            public readonly string id;
            public readonly Script.Flag flag; // script flag that determines if this topic is unlocked

            public readonly List<DialogInfoRecord> infos;

            public DialogRecord(Type type, string id, Script.Flag flag)
            {
                this.type = type;
                this.id = id;
                this.flag = flag;

                infos = new();
            }
        }

        public class DialogInfoRecord
        {
            private static uint NEXT_ID = 0;

            public readonly uint id; // generated id used when lookin up wems, not used by elden ring or morrowind
            public readonly DialogRecord.Type type;

            // static requirements for a dialog to be added
            public readonly string speaker, job, faction, cell;
            public readonly int rank;
            public readonly NpcContent.Race race;
            public readonly NpcContent.Sex sex;

            // non-static requirements
            public readonly string playerFaction;
            public readonly int disposition, playerRank;

            public readonly List<DialogFilter> filters;

            public readonly string text; // actual dialog text

            public readonly DialogPapyrus script; // parsed script snippet for this line to execute after playback

            /* Next couple of vars are generated in a second pass, these relate to how dialog lines unlock topics */
            public readonly List<DialogRecord> unlocks; // list of topics this line unlocks, if any

            public DialogInfoRecord(DialogRecord.Type type, JsonNode json)
            {
                id = DialogInfoRecord.NEXT_ID++;
                this.type = type;

                string NullEmpty(string s) { return s.Trim() == "" ? null : s; }

                speaker = NullEmpty(json["speaker_id"].ToString());
                string raceStr = NullEmpty(json["speaker_race"].ToString());
                race = raceStr != null ? (NpcContent.Race)System.Enum.Parse(typeof(NpcContent.Race), raceStr.Replace(" ", "")) : NpcContent.Race.Any;
                job = NullEmpty(json["speaker_class"].ToString());
                faction = NullEmpty(json["speaker_faction"].ToString());
                cell = NullEmpty(json["speaker_cell"].ToString());
                rank = int.Parse(json["data"]["speaker_rank"].ToString());
                Enum.TryParse(json["data"]["speaker_sex"].ToString(), out sex);

                playerFaction = NullEmpty(json["player_faction"].ToString());
                disposition = int.Parse(json["data"]["disposition"].ToString());
                playerRank = int.Parse(json["data"]["player_rank"].ToString());

                filters = new();
                foreach (JsonNode filterNode in json["filters"].AsArray())
                {
                    filters.Add(new(filterNode));
                }

                text = json["text"].ToString();

                if (json["script_text"].ToString() == null || json["script_text"].ToString() == "") { script = null; }
                else
                {
                    DialogPapyrus parsed = new DialogPapyrus(json["script_text"].ToString());
                    script = parsed.calls.Count() > 0 || parsed.choice != null ? parsed : null;  // if we parse the script and find its empty (for example, just a comment) discard it
                }

                unlocks = new();
            }

            /* Generates an ESD condition for this line using the data from its filters */ // used by DialogESD.cs
            private static List<String> debugUnsupportedFiltersLogging = new();
            public string GenerateCondition(ScriptManager scriptManager, NpcContent npcContent)
            {
                List<string> conditions = new();

                // Handle disposition check
                if (disposition > 0)
                {
                    Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.Disposition, npcContent.id);
                    conditions.Add($"GetEventFlagValue({flag.id}, {(int)flag.type}) >= {disposition}");
                }

                if(playerFaction != null)
                {
                    Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, playerFaction);
                    conditions.Add($"GetEventFlag({flag.id}) == True");
                }

                // Handle filters
                for (int i = 0; i < filters.Count(); i++)
                {
                    DialogFilter filter = filters[i];

                    string handleFilter(DialogFilter filter)
                    {
                        switch (filter.type)
                        {
                            case DialogFilter.Type.Function:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.FactionRankDifference:
                                        {
                                            return "False";
                                        }
                                    case DialogFilter.Function.RankRequirement:
                                        {
                                            return "False";
                                        }
                                    case DialogFilter.Function.SameFaction:
                                        {
                                            Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, npcContent.faction);
                                            if (flag == null) { return "False"; }       // another static return. if the npc has no faction it is always false
                                            return $"GetEventFlag({flag.id}) == {filter.value}";
                                        }
                                    case DialogFilter.Function.SameRace:
                                        {
                                            Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.PlayerRace, npcContent.race.ToString());
                                            return $"GetEventFlag({flag.id}) == True";
                                        }
                                    case DialogFilter.Function.TalkedToPc:
                                        {
                                            Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.TalkedToPc, npcContent.id);
                                            return $"GetEventFlag({flag.id}) == False";
                                        }
                                    case DialogFilter.Function.PcLevel:
                                        {
                                            return $"ComparePlayerStat(PlayerStat.RuneLevel, {filter.OperatorString()}, {filter.value})";
                                        }
                                    case DialogFilter.Function.Level:
                                        {
                                            // npcs level can't change so static comparison is fine  @TODO: could uhhh resolve this to just true or false but i'm lazy
                                            return $"{npcContent.level} {filter.OperatorSymbol()} {filter.value}";
                                        }

                                    default: return "False";
                                }

                            case DialogFilter.Type.Journal:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.JournalType:
                                        {
                                            Flag jvar = scriptManager.GetFlag(Script.Flag.Designation.Journal, filter.id); // look for flag, if not found make one
                                            if (jvar == null) { jvar = scriptManager.common.CreateFlag(Flag.Category.Saved, Flag.Type.Byte, Script.Flag.Designation.Journal, filter.id); }
                                            return $"GetEventFlagValue({jvar.id}, {(int)jvar.type}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    default: return "False";
                                }

                            case DialogFilter.Type.Global:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.Global:
                                    case DialogFilter.Function.VariableCompare:
                                        {
                                            // Random 100 handled by rng gen in the esd. Other globals are handled normally
                                            if (filter.id == "Random100")
                                            {
                                                return $"CompareRNGValue({filter.OperatorString()}, {filter.value}) == True";
                                            }

                                            Flag gvar = scriptManager.GetFlag(Script.Flag.Designation.Global, filter.id); // look for flag, if not found make one
                                            if (gvar == null) { gvar = scriptManager.common.CreateFlag(Flag.Category.Saved, Flag.Type.Short, Script.Flag.Designation.Global, filter.id); }
                                            return $"GetEventFlagValue({gvar.id}, {gvar.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }

                                    default: return "False";
                                }

                            case DialogFilter.Type.Dead:
                                {
                                    switch(filter.function)
                                    {
                                        case DialogFilter.Function.DeadType:
                                            {
                                                Flag deadCount = scriptManager.GetFlag(Flag.Designation.DeadCount, filter.id);
                                                if(deadCount == null) { return "False"; } // Only happens if doing a partial build of the game world
                                                return $"GetEventFlagValue({deadCount.id}, {deadCount.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                            }
                                    }
                                    return "False";
                                }

                            case DialogFilter.Type.NotLocal:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.VariableCompare:
                                        {
                                            string localId = $"{npcContent.id}.{filter.id}"; // local vars use the characters id + the var id. many characters can have their own copy of a local
                                            Flag lvar = scriptManager.GetFlag(Script.Flag.Designation.Local, localId); // look for flag
                                            if(lvar == null) { return "False"; } // if we don't find the flag for a local var it doesn't exist
                                            // local vars are set to their maxvalue when uninitialized. check if its maxvalue and return true if it is, false if not
                                            return $"GetEventFlagValue({lvar.id}, {lvar.Bits()}) == {lvar.MaxValue()}";
                                        }

                                    default: return "False";
                                }
                            case DialogFilter.Type.NotId:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.NotIdType:
                                        {
                                            // Checking speaker id, static true/false is fine for this one
                                            if(npcContent.id != filter.id) { return "True"; }
                                            else { return "False"; }
                                        }

                                    default: return "False";
                                }
                            case DialogFilter.Type.NotClass:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.NotClass:
                                        {
                                            // Checking speakers class, static true/false is fine for this as well
                                            if (npcContent.job != filter.id) { return "True"; }
                                            else { return "False"; }
                                        }

                                    default: return "False";
                                }
                            case DialogFilter.Type.NotRace:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.NotRace:
                                        {
                                            // Checking speakers race, static true/false is fine for this as well
                                            if (npcContent.race.ToString().ToLower() != filter.id) { return "True"; }
                                            else { return "False"; }
                                        }

                                    default: return "False";
                                }
                            case DialogFilter.Type.NotFaction:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.NotFaction:
                                        {
                                            // Checking speakers faction, static true/false is fine for this as well
                                            if (npcContent.faction != filter.id) { return "True"; }
                                            else { return "False"; }
                                        }

                                    default: return "False";
                                }
                            case DialogFilter.Type.Local:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.VariableCompare:
                                        {
                                            string localId = $"{npcContent.id}.{filter.id}"; // local vars use the characters id + the var id. many characters can have their own copy of a local
                                            Flag lvar = scriptManager.GetFlag(Script.Flag.Designation.Local, localId); // look for flag, if not found it dosent exist so return false
                                            if (lvar == null) { return "False"; }
                                            // local vars are set to their maxvalue when uninitialized. so when doing comparisons we check the value to see if it is maxvalue first, then do our actual check if that passes. for a short maxvalue is 65536 since eventvalue are unsigned
                                            return $"(GetEventFlagValue({lvar.id}, {lvar.Bits()}) {filter.OperatorSymbol()} {filter.value} and GetEventFlagValue({lvar.id}, {lvar.Bits()}) != {lvar.MaxValue()})";
                                        }

                                    default: return "False";
                                }
                            case DialogFilter.Type.Item:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.ItemType:
                                        {
                                            // Gold specifically handled as souls so its diffo from other item checks
                                            if (filter.id.ToLower() == "gold_001")
                                            {
                                                return $"ComparePlayerStat(PlayerStat.RunesCollected, {filter.OperatorString()}, {filter.value})";
                                            }
                                            // Any other item
                                            return "False"; // not supported yet
                                        }
                                    default: return "False";
                                }

                            default: return "False"; // @TODO: debug thing while we are implementing these functions. if its not implemented it returns false always
                        }
                    }

                    string filterCond = handleFilter(filter);
                    if(filterCond == "False")
                    {
                        string unsupportedFilterType = $"{filter.type}::{filter.function}";
                        if (!debugUnsupportedFiltersLogging.Contains(unsupportedFilterType))
                        {
                            Lort.Log($" ## WARNING ## Unsupported filter type {unsupportedFilterType}", Lort.Type.Debug);
                            debugUnsupportedFiltersLogging.Add(unsupportedFilterType);
                        }
                    }

                    conditions.Add(filterCond);
                }

                // Collapse to string
                string condition = "";
                for (int i = 0; i < conditions.Count(); i++)
                {
                    condition += conditions[i];
                    if (i < conditions.Count() - 1) { condition += " and "; }
                }

                return condition;
            }
        }

        public class DialogPapyrus
        {
            public readonly List<PapyrusCall> calls;
            public readonly PapyrusChoice choice;    // usually null unless the papyrus script had a choice call. choice is always the last call in a script and there can only be 1

            public DialogPapyrus(string script)
            {
                calls = new();
                string[] lines = script.Split("\r\n");
                choice = null;
                foreach (string line in lines)
                {
                    PapyrusCall call = new(line);
                    if (call.type == PapyrusCall.Type.None) { continue; } // discard empty calls
                    if(call.type == PapyrusCall.Type.Choice)  // choice calls are special and are stored differently
                    {
                        choice = new PapyrusChoice(call);
                        continue;
                    }
                    calls.Add(call);
                }
            }

            /* Creates code for a dialog esd to execute when the dialoginfo that this dialogpapyrus is owned by gets played */
            public string GenerateEsdSnippet(ScriptManager scriptManager, NpcContent npcContent, int indent)
            {
                // Takes any mixed numeric parameter and converts it to an esd friendly format. for example  "1 + 2 + crimeGold + 7" or "crimeGold - valueValue" or just "5"
                string ParseParameters(string[] parameters, int startIndex)
                {
                    string parsed = parameters.Length - startIndex > 1 ? "(" : "";
                    for (int i = startIndex; i < parameters.Length; i++)
                    {
                        string p = parameters[i];
                        if (Utility.StringIsNumeric(p)) { parsed += p; }
                        else if (Utility.StringIsOperator(p)) { parsed += p; }
                        else  // its (probably) a variable
                        {
                            Flag pvar = scriptManager.GetFlag(Script.Flag.Designation.Global, p); // look for flag, if not found make one
                            if (pvar == null) { pvar = scriptManager.common.CreateFlag(Flag.Category.Saved, Flag.Type.Short, Script.Flag.Designation.Global, p); }
                            parsed += $"GetEventFlagValue({pvar.id}, {(int)pvar.type})";
                        }
                        if (i < parameters.Length - 1) { parsed += " "; }
                    }
                    if (parsed.StartsWith("(")) { parsed += ")"; }
                    return parsed;
                }

                List<string> lines = new();

                foreach (PapyrusCall call in calls)
                {
                    switch (call.type)
                    {
                        case PapyrusCall.Type.Set:
                            {
                                // This var can be either global or local so check for both. since locals are preprocessed if we dont find either we make a global
                                Flag var = scriptManager.GetFlag(Script.Flag.Designation.Global, call.parameters[0]);
                                if (var == null) { var = scriptManager.GetFlag(Flag.Designation.Local, call.parameters[0]); }
                                if (var == null) { var = scriptManager.common.CreateFlag(Flag.Category.Saved, Flag.Type.Short, Script.Flag.Designation.Global, call.parameters[0]); }

                                string code = $"SetEventFlagValue({var.id}, {var.Bits()}, {ParseParameters(call.parameters, 2)});";

                                lines.Add(code);

                                break;
                            }
                        case PapyrusCall.Type.Journal:
                            {
                                Flag jvar = scriptManager.GetFlag(Script.Flag.Designation.Journal, call.parameters[0]); // look for flag, if not found make one
                                if (jvar == null) { jvar = scriptManager.common.CreateFlag(Flag.Category.Saved, Flag.Type.Byte, Script.Flag.Designation.Journal, call.parameters[0]); }
                                string code = $"SetEventFlagValue({jvar.id}, {jvar.Bits()}, {int.Parse(call.parameters[1])})";
                                lines.Add(code);
                                break;
                            }
                        case PapyrusCall.Type.PcJoinFaction:
                            {
                                Script.Flag fvar = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, npcContent.faction);
                                string code = $"SetEventFlag({fvar.id}, True);";
                                lines.Add(code);
                                break;
                            }
                        case PapyrusCall.Type.ModPcFacRep:
                            {
                                int rep = int.Parse(call.parameters[0]);
                                Script.Flag fvar = scriptManager.GetFlag(Script.Flag.Designation.FactionReputation, call.parameters[1]);
                                string code = $"SetEventFlagValue({fvar.id}, {fvar.Bits()}, ( GetEventFlagValue({fvar.id}, {fvar.Bits()}) + {rep} ))";
                                break;
                            }
                        case PapyrusCall.Type.PcRaiseRank:
                            {
                                // @TODO:
                                break;
                            }
                        case PapyrusCall.Type.PcExpell:
                            {
                                // @TODO:
                                break;
                            }
                        case PapyrusCall.Type.PcClearExpelled:
                            {
                                // @TODO:
                                break;
                            }
                        case PapyrusCall.Type.RemoveItem:
                            {
                                // Gold specifically handled as souls so its diffo from other item checks
                                if (call.target == "player" && call.parameters[0] == "gold_001")
                                {
                                    string code = $"ChangePlayerStat(PlayerStat.RunesCollected, ChangeType.Subtract, {ParseParameters(call.parameters, 1)});";
                                    lines.Add(code);
                                }
                                // Any other item
                                break; // not yet supported
                            }
                        case PapyrusCall.Type.AddItem:
                            {
                                // Gold specifically handled as souls so its diffo from other item checks
                                if (call.target == "player" && call.parameters[0] == "gold_001")
                                {
                                    string code = $"ChangePlayerStat(PlayerStat.RunesCollected, ChangeType.Add, {ParseParameters(call.parameters, 1)});";
                                    lines.Add(code);
                                }
                                // Any other item
                                break; // not yet supported
                            }
                        default: break;
                    }
                }

                if(lines.Count() <= 0) { return ""; } // if empty just return nothing lol lmao

                string space = "";
                for (int i = 0; i < indent; i++)
                {
                    space += " ";
                }

                return $"{space}{string.Join($"\r\n{space}", lines)}\r\n";
            }

            public class PapyrusCall
            {
                public enum Type
                {
                    None, Journal, ModPcFacRep, ModDisposition, AddItem, RemoveItem, SetFight, StartCombat, Goodbye, Choice, AddTopic, RemoveSpell, ModReputation, ShowMap,
                    StartScript, Set, Disable, AddSpell, SetDisposition, PcJoinFaction, PcClearExpelled, PcRaiseRank, ModMercantile, Enable, ClearForceSneak,
                    AiWander, StopCombat, PcExpell, AiFollow, SetPcCrimeLevel, PayFineThief, ModStrength, MessageBox, PositionCell, AiFollowCell, ModFight,
                    ModFactionReaction, AiFollowCellPlayer, PayFine, GotoJail, ModFlee, SetAlarm, AiTravel, PlaceAtPc, ModAxe, ClearInfoActor, Cast, ForceGreeting, RaiseRank
                }

                public readonly Type type;
                public readonly string target;         // can be null, this is set if a papyrus call is on an object like "player->additem cbt 1"
                public readonly string[] parameters;

                public PapyrusCall(string line)
                {
                    string sanitize = line.Trim().ToLower();
                    if (sanitize.StartsWith(";") || sanitize == "") { type = Type.None; target = null; parameters = new string[0]; return; } // line does nothing

                    // Remove trailing comments
                    if (sanitize.Contains(";"))
                    {
                        sanitize = sanitize.Split(";")[0].Trim();
                    }

                    // Remove any multi spaces
                    while (sanitize.Contains("  "))
                    {
                        sanitize = sanitize.Replace("  ", " ");
                    }

                    // Remove any commas as they are not actually needed for papyrus syntax and are used somewhat randomly lol
                    // @TODO: this is fine except on choice calls which have strings with dialog text in them. the dialogs can have commas but we are just erasing them rn. should fix, low prio
                    sanitize = sanitize.Replace(",", "");

                    // Fix a specific single case where a stupid -> has a space in it
                    if (sanitize.Contains("-> ")) { sanitize = sanitize.Replace("-> ", "->"); }

                    // Fix a specific single case of weird syntax
                    if (sanitize.Contains("\"1 ")) { sanitize = sanitize.Replace("\"1 ", "\" 1 "); }

                    // Fix a specific case where a single quote is used at random for no fucking reason
                    if (sanitize.Contains("land deed'")) { sanitize = sanitize.Replace("land deed'", "land deed\""); }

                    // Handle targeted call
                    if (sanitize.Contains("->"))
                    {
                        // Special split because targets can be in quotes and have spaces in them
                        string[] split = sanitize.Split("->");
                        List<string> ps = Regex.Matches(split[1], @"[\""].+?[\""]|[^ ]+")
                            .Cast<Match>()
                            .Select(m => m.Value)
                            .ToList();

                        type = (Type)Enum.Parse(typeof(Type), ps[0], true);
                        target = split[0].Replace("\"", "");
                        ps.RemoveAt(0);
                        parameters = ps.ToArray();
                    }
                    // Handle normal call
                    else
                    {
                        List<string> split = sanitize.Split(" ").ToList();

                        type = (Type)Enum.Parse(typeof(Type), split[0], true);
                        target = null;
                        split.RemoveAt(0);

                        /* Handle special case where you have a call like this :: Set "Manilian Scerius".slaveStatus to 2 */
                        /* Seems to be fairly rare that we have syntax like this but it does happen. */
                        /* Recombine the 2 halves of that "name" and remove the quotes */
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
                                    while(!itrNxt.Contains("\""))
                                    {
                                        itrNxt += $" {split[++i]}";
                                    }
                                    recomb.Add(($"{s} {itrNxt}").Replace("\"", ""));
                                }
                                continue;
                            }

                            recomb.Add(s);
                        }

                        parameters = recomb.ToArray();
                    }
                }
            }

            /* Very specially handled call */
            /* This papyrus function is always singular and last in a dialog script so i can safely store as it's own thing */
            public class PapyrusChoice
            {
                public readonly List<Tuple<int, string>> choices;

                public PapyrusChoice(PapyrusCall call)
                {
                    choices = new();

                    for(int i=0;i<call.parameters.Count();i+=2)
                    {
                        int ind = int.Parse(call.parameters[i + 1]);
                        string text = call.parameters[i];
                        choices.Add(new(ind, text));
                    }
                }
            }
        }

        public class DialogFilter
        {
            public enum Type { NotLocal, Journal, Dead, Item, Function, NotId, Global, Local, NotFaction, NotCell, NotRace, NotClass }
            public enum Operator { Equal, NotEqual, GreaterEqual, LessEqual, Less, Greater }
            public enum Function
            {
                VariableCompare, JournalType, DeadType, ItemType, Choice, NotIdType, PcExpelled, NotFaction, SameFaction, RankRequirement,
                PcSex, SameRace, PcHealthPercent, PcHealth, PcReputation, NotCell, PcVampire, NotRace, PcSpeechcraft, PcLevel, NotClass, PcCrimeLevel,
                SameSex, PcMercantile, PcClothingModifier, FactionRankDifference, PcCorprus, PcPersonality, ShouldAttack, PcAgility, PcSneak, TalkedToPc,
                PcIntelligence, Alarmed, Global, Detected, Attacked, Level, PcBlightDisease, PcCommonDisease, PcBluntWeapon, Reputation, PcStrength,
                CreatureTarget, Weather, ReactionHigh, ReactionLow, HealthPercent, FriendHit
            }

            public readonly Type type;
            public readonly Function function;
            public readonly Operator op;
            public readonly string id;
            public readonly int value;

            public DialogFilter(JsonNode json)
            {
                Enum.TryParse(json["filter_type"].ToString(), out type);
                Enum.TryParse(json["function"].ToString(), out function);
                Enum.TryParse(json["comparison"].ToString(), out op);

                id = json["id"].ToString().ToLower();

                if (json["value"]["type"].ToString() == "Integer")
                {
                    value = int.Parse(json["value"]["data"].ToString());
                }
                else
                {
                    Lort.Log($"## ERROR ## UNSUPPORTED FILTER VALUE TYPE '{json["value"]["type"].ToString()}' DISCARDED IN '{type} {function} {op} {id}'!", Lort.Type.Debug);
                    value = 0;
                }

            }

            /* Returns the esd version of the operator type as a string */
            public string OperatorString()
            {
                switch (op)
                {
                    case Operator.Equal: return "CompareType.Equal";
                    case Operator.NotEqual: return "CompareType.NotEqual";
                    case Operator.GreaterEqual: return "CompareType.GreaterOrEqual";
                    case Operator.Greater: return "CompareType.Greater";
                    case Operator.LessEqual: return "CompareType.LessOrEqual";
                    case Operator.Less: return "CompareType.Less";
                    default: return "DADDY NO PLEASE!!!!";
                }
            }

            /* same as above but symbol instead of string */
            public string OperatorSymbol()
            {
                switch (op)
                {
                    case Operator.Equal: return "==";
                    case Operator.NotEqual: return "!=";
                    case Operator.GreaterEqual: return ">=";
                    case Operator.Greater: return ">";
                    case Operator.LessEqual: return "<=";
                    case Operator.Less: return "<";
                    default: return "DADDY NO PLEASE!!!!";
                }
            }
        }
    }
}
