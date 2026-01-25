using IronPython.Compiler;
using JortPob.Common;
using SharpAssimp.Unmanaged;
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
                this.id = id.StartsWith("Greeting") ? "Greeting" : id; // change 'Greeting #' to just 'Greeting' for sanity reasons
                this.flag = flag;

                infos = new();
            }
        }

        public class DialogInfoRecord
        {
            private static int NEXT_ID = 0;

            public readonly int id; // generated id used when lookin up wems, not used by elden ring or morrowind
            public readonly DialogRecord.Type type;

            // static requirements for a dialog to be added
            public readonly string speaker, job, faction, cell;
            public readonly int rank;
            public readonly CharacterContent.Race race;
            public readonly CharacterContent.Sex sex;

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
                id = DialogInfoRecord.NEXT_ID+=10;  // increment by 10 so we can use the 9 values between each id as the split text ids (guh)
                this.type = type;

                string NullEmpty(string s) { return s.Trim() == "" ? null : s; }

                speaker = NullEmpty(json["speaker_id"].ToString());
                string raceStr = NullEmpty(json["speaker_race"].ToString());
                race = raceStr != null ? Enum.Parse<CharacterContent.Race>(raceStr.Replace(" ", "")) : CharacterContent.Race.Any;
                job = NullEmpty(json["speaker_class"].ToString());
                faction = NullEmpty(json["speaker_faction"].ToString());
                cell = NullEmpty(json["speaker_cell"].ToString());
                rank = int.Parse(json["data"]["speaker_rank"].ToString());
                Enum.TryParse(json["data"]["speaker_sex"].ToString(), out sex);

                playerFaction = NullEmpty(json["player_faction"].ToString());
                disposition = int.Parse(json["data"]["disposition"].ToString());
                playerRank = playerFaction!=null?int.Parse(json["data"]["player_rank"].ToString()):-1;  // minor MW bug fix. some dialogs have a mistake where they have a required rank set but no faction

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

            /* Very special function for optimization */
            // So basically, this function is dedicated to determining if any filters in this DialogRecord cause it to be completely unreachable for a given npc
            // Most filters require runtime information to determine if they will resolve true or false, but many can be statically determined based on the npcs data
            // Good examples of statically determined filters are things like NotRace or NotLocal. Or if a character has no faction then any faction related filters.
            // It is important to be careful here though, we don't want to accidentally discard dialog lines that could resolve true at some point
            private static uint DISCARD_COUNT = 0; // some tracking to see how effective the filter discards are
            public bool IsUnreachableFor(ScriptManager scriptManager, CharacterContent npc)
            {
                if (speaker != null && speaker != npc.id) { return true; }
                if (race != CharacterContent.Race.Any && race != npc.race) { return true; }
                if (job != null && job != npc.job) { return true; }
                if (faction != null && faction != npc.faction) { return true; }
                if (rank > npc.rank || (rank >= 0 && npc.faction == null)) { return true; }  // unsure if this is correct, i *think* it is but haven't verified
                if ((cell != null && npc.cell.name == null) || (cell != null && npc.cell.name != null && !npc.cell.name.ToLower().StartsWith(cell.ToLower()))) { return true; }
                if (sex != CharacterContent.Sex.Any && sex != npc.sex) { return true; }

                DISCARD_COUNT++;
                foreach (DialogFilter filter in filters)
                {
                    switch (filter.type)
                    {
                        case DialogFilter.Type.Function:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.FactionRankDifference:
                                    {
                                        if (npc.faction == null) { return true; } break;
                                    }
                                case DialogFilter.Function.RankRequirement:
                                    {
                                        if (npc.faction == null) { return true; }
                                        break;
                                    }
                                case DialogFilter.Function.SameFaction:
                                    {
                                        if (npc.faction == null) { return true; }
                                        break;
                                    }
                                case DialogFilter.Function.PcExpelled:
                                    {
                                        if (npc.faction == null) { return true; }
                                        break;
                                    }
                                case DialogFilter.Function.Reputation:
                                    {
                                        if(!filter.ResolveOperator(npc.reputation)) { return true; }
                                        break;
                                    }
                                case DialogFilter.Function.Level:
                                    {
                                        if (!filter.ResolveOperator(npc.level)) { return true; }
                                        break;
                                    }
                                default: break;
                            }
                            break;

                        case DialogFilter.Type.NotLocal:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.VariableCompare:
                                    {
                                        string localId = $"{npc.id}.{filter.id}"; // local vars use the characters id + the var id. many characters can have their own copy of a local
                                        Flag lvar = scriptManager.GetFlag(Script.Flag.Designation.Local, localId); // look for flag
                                        if (lvar != null) { return true; } // local vars are preprocessed so we can just check if the local var exists or not
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        case DialogFilter.Type.Local:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.VariableCompare:
                                    {
                                        string localId = $"{npc.id}.{filter.id}"; // local vars use the characters id + the var id. many characters can have their own copy of a local
                                        Flag lvar = scriptManager.GetFlag(Script.Flag.Designation.Local, localId); // look for flag
                                        if (lvar == null) { return true; } // local vars are preprocessed so we can just check if the local var exists or not
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        case DialogFilter.Type.NotCell:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.NotCell:
                                    {
                                        // static check, characters in elden ring can't really travel around so it's fine for now. may need to change at some point tho
                                        if (npc.cell.name != null && npc.cell.name.ToLower().StartsWith(filter.id.ToLower())) { return true; }
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        case DialogFilter.Type.NotId:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.NotIdType:
                                    {
                                        if (npc.id == filter.id) { return true; }
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        case DialogFilter.Type.NotClass:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.NotClass:
                                    {
                                        if (npc.job == filter.id) { return true; }
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        case DialogFilter.Type.NotRace:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.NotRace:
                                    {
                                        if (npc.race.ToString().ToLower() == filter.id) { return true; }
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        case DialogFilter.Type.NotFaction:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.NotFaction:
                                    {
                                        // Checking speakers faction, static true/false is fine for this as well
                                        if (npc.faction == filter.id) { return true; }
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        default: break;
                    }
                }
                DISCARD_COUNT--;
                return false;
            }

            /* Generates an ESD condition for this line using the data from its filters */ // used by DialogESD.cs
            private static List<String> debugUnsupportedFiltersLogging = new();
            public string GenerateCondition(ItemManager itemManager, ScriptManager scriptManager, CharacterContent npcContent)
            {
                List<string> conditions = new();

                // Handle disposition check
                if (disposition > 0)
                {
                    Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.Disposition, npcContent.entity.ToString());
                    conditions.Add($"GetEventFlagValue({flag.id}, {(int)flag.type}) >= {disposition}");
                }

                if(playerFaction != null)
                {
                    Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, playerFaction);
                    conditions.Add($"GetEventFlag({flag.id}) == True");
                }

                if (playerRank > -1)
                {
                    Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.FactionRank, playerFaction);
                    conditions.Add($"GetEventFlagValue({flag.id}, {flag.Bits()}) >= {playerRank + 1}"); // the +1 is because i made the first rank 1 and morrowind assumes 0
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
                                            if (npcContent.faction == null) { return "False"; } // static false return if npc is not in a faction
                                            Script.Flag rvar = scriptManager.GetFlag(Script.Flag.Designation.FactionRank, npcContent.faction);
                                            return $"(GetEventFlagValue({rvar.id}, {rvar.Bits()}) - {rank+1}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.RankRequirement:
                                        {
                                            if (npcContent.faction == null) { return "False"; } // static false return if npc is not in a faction
                                            Script.Flag retVal = scriptManager.GetFlag(Script.Flag.Designation.ReturnValueRankReq, npcContent.entity.ToString());
                                            return $"GetEventFlagValue({retVal.id}, {retVal.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.SameFaction:
                                        {
                                            if (npcContent.faction == null) { return "False"; } // static false return if npc is not in a faction
                                            Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, npcContent.faction);
                                            if (flag == null) { return "False"; }       // another static return. if the npc has no faction it is always false
                                            return $"GetEventFlag({flag.id}) == {filter.value}";
                                        }
                                    case DialogFilter.Function.SameRace:
                                        {
                                            Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.PlayerRace, npcContent.race.ToString());
                                            return $"GetEventFlag({flag.id}) == True";
                                        }
                                    case DialogFilter.Function.SameSex:
                                        {
                                            int sexVal = npcContent.sex == CharacterContent.Sex.Male ? 1 : 0; // elden ring values are :: male = 1, female = 0 
                                            return $"ComparePlayerStat(PlayerStat.Gender, CompareType.Equal, {sexVal}) == {filter.value}";
                                        }
                                    case DialogFilter.Function.TalkedToPc:
                                        {
                                            Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.TalkedToPc, npcContent.entity.ToString());
                                            return $"GetEventFlag({flag.id}) == False";
                                        }
                                    case DialogFilter.Function.PcLevel:
                                        {
                                            return $"ComparePlayerStat(PlayerStat.RuneLevel, {filter.OperatorString()}, {filter.value})";
                                        }
                                    case DialogFilter.Function.PcSex:
                                        {
                                            return $"ComparePlayerStat(PlayerStat.Gender, CompareType.Equal, 0) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.PcExpelled:
                                        {
                                            if (npcContent.faction == null) { return "False"; } // static false return if npc is not in a faction
                                            Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.FactionExpelled, npcContent.faction);
                                            return $"GetEventFlag({flag.id}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.Reputation:
                                        {
                                            return $"{npcContent.reputation} {filter.OperatorSymbol()} {filter.value}"; // could be statically resolved
                                        }
                                    case DialogFilter.Function.PcReputation:
                                        {
                                            Flag rvar = scriptManager.GetFlag(Script.Flag.Designation.Reputation, "Reputation");  // grab player reputation flag
                                            return $"GetEventFlagValue({rvar.id}, {rvar.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.PcCrimeLevel:
                                        {
                                            Flag cvar = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel"); // grab crime gold flag
                                            return $"GetEventFlagValue({cvar.id}, {cvar.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.Level:
                                        {
                                            // npcs level can't change so static comparison is fine
                                            return $"{npcContent.level} {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.HealthPercent:
                                        {
                                            return $"(GetSelfHP() / 10) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.FriendHit:
                                        {
                                            Script.Flag hflag = scriptManager.GetFlag(Flag.Designation.Hostile, npcContent.entity.ToString());
                                            Script.Flag fvar = scriptManager.GetFlag(Script.Flag.Designation.FriendHitCounter, npcContent.entity.ToString());
                                            Script.Flag dvar = scriptManager.GetFlag(Script.Flag.Designation.Disposition, npcContent.entity.ToString());
                                            return $"(not GetEventFlag({hflag.id}) and GetEventFlagValue({dvar.id}, {dvar.Bits()}) > 60 and GetEventFlagValue({fvar.id}, {fvar.Bits()}) {filter.OperatorSymbol()} {filter.value - 1})";
                                        }
                                    case DialogFilter.Function.Choice:
                                        {
                                            return $"True"; // choice commands are handled differently. this "true" is just to prevent breaking choices that have filters
                                        }

                                    default: return null;
                                }

                            case DialogFilter.Type.Journal:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.JournalType:
                                        {
                                            Flag jvar = scriptManager.GetFlag(Script.Flag.Designation.Journal, filter.id); // look for flag, if not found make one
                                            if (jvar == null) { jvar = scriptManager.common.CreateFlag(Flag.Category.Saved, Flag.Type.Byte, Script.Flag.Designation.Journal, filter.id); }
                                            return $"GetEventFlagValue({jvar.id}, {jvar.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    default: return null;
                                }

                            case DialogFilter.Type.Global:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.Global:
                                    case DialogFilter.Function.VariableCompare:
                                        {
                                            // Random 100 handled by rng gen in the esd. Other globals are handled normally
                                            if (filter.id == "random100")
                                            {
                                                return $"CompareRNGValue({filter.OperatorString()}, {filter.value}) == True";
                                            }

                                            Flag gvar = scriptManager.GetFlag(Script.Flag.Designation.Global, filter.id); // look for flag. if not found return a static 'False' as it's probably a float variable
                                            if(gvar == null) { return "False"; }
                                            return $"GetEventFlagValue({gvar.id}, {gvar.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }

                                    default: return null;
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
                                    return null;
                                }

                            case DialogFilter.Type.NotLocal:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.VariableCompare:
                                        {
                                            string localId = $"{npcContent.id}.{filter.id}"; // local vars use the characters id + the var id. many characters can have their own copy of a local
                                            Flag lvar = scriptManager.GetFlag(Script.Flag.Designation.Local, localId); // look for flag
                                            if(lvar == null) { return "True"; } // if we don't find the flag for a local var it doesn't exist
                                            return $"False";
                                        }

                                    default: return null;
                                }
                            case DialogFilter.Type.NotCell:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.NotCell:
                                        {
                                            // static check, characters in elden ring can't really travel around so it's fine for now. may need to change at some point tho
                                            if(npcContent.cell.name == null) { return "True"; }
                                            if (npcContent.cell.name.ToLower().StartsWith(filter.id.ToLower())) { return "False"; }
                                            return "True";
                                        }

                                    default: return null;
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

                                    default: return null;
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

                                    default: return null;
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

                                    default: return null;
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

                                    default: return null;
                                }
                            case DialogFilter.Type.Local:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.VariableCompare:
                                        {
                                            string localId = $"{npcContent.id}.{filter.id}"; // local vars use the characters id + the var id. many characters can have their own copy of a local
                                            Flag lvar = scriptManager.GetFlag(Script.Flag.Designation.Local, localId); // look for flag, if not found it dosent exist so return false
                                            if (lvar == null) { return "False"; }
                                            return $"GetEventFlagValue({lvar.id}, {lvar.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }

                                    default: return null;
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
                                            else
                                            {
                                                ItemManager.ItemInfo itemInfo = itemManager.GetItem(filter.id.ToLower());
                                                if (itemInfo == null) { throw new Exception("Script failed to find referenced item! This should not happen!"); }
                                                return $"ComparePlayerInventoryNumber({(int)itemInfo.type}, {itemInfo.row}, {filter.OperatorString()}, {filter.value}, False)";
                                            }
                                        }
                                    default: return null;
                                }

                            default: return null; // @TODO: debug thing while we are implementing these functions. if its not implemented it returns null which we convert to a "false" below
                        }
                    }

                    string filterCond = handleFilter(filter);
                    if(filterCond == null)
                    {
                        string unsupportedFilterType = $"{filter.type}::{filter.function}";
                        if (!debugUnsupportedFiltersLogging.Contains(unsupportedFilterType))
                        {
                            Lort.Log($" ## WARNING ## Unsupported filter type {unsupportedFilterType}", Lort.Type.Debug);
                            debugUnsupportedFiltersLogging.Add(unsupportedFilterType);
                        }

                        filterCond = "False";
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
            public readonly List<Papyrus.Call> calls;
            public readonly PapyrusChoice choice;    // usually null unless the papyrus script had a choice call. choice is always the last call in a script and there can only be 1

            public DialogPapyrus(string script)
            {
                calls = new();
                string[] lines = script.Split("\r\n");
                choice = null;
                foreach (string line in lines)
                {
                    Papyrus.Call call = new(line);
                    if (call.type == Papyrus.Call.Type.None) { continue; } // discard empty calls
                    if(call.type == Papyrus.Call.Type.Choice)  // choice calls are special and are stored differently
                    {
                        choice = new PapyrusChoice(call);
                        continue;
                    }
                    calls.Add(call);
                }
            }

            /* Creates code for a dialog esd to execute when the dialoginfo that this dialogpapyrus is owned by gets played */
            private static List<String> debugUnsupportedPapyrusCallLogging = new();
            public string GenerateEsdSnippet(Paramanager paramanager, ItemManager itemManager, ScriptManager scriptManager, CharacterContent npcContent, uint esdId, int indent)
            {
                // Takes any mixed numeric parameter and converts it to an esd friendly format. for example  "1 + 2 + crimeGold + 7" or "crimeGold - valueValue" or just "5"
                string ParseParameters(string[] parameters, int startIndex)
                {
                    string parsed = parameters.Length - startIndex > 1 ? "(" : "";
                    for (int i = startIndex; i < parameters.Length; i++)
                    {
                        string p = parameters[i];
                        if (Utility.StringIsInteger(p)) { parsed += p; }
                        else if (Utility.StringIsOperator(p)) { parsed += p; }
                        else  // its (probably) a variable
                        {
                            Flag pvar = GetFlagByVariable(p); // get variable flag
                            if (pvar == null) { parsed += "0"; } // @TODO: discarding function calls rn, should support them properly (like in papyrusemevd.cs)
                            else { parsed += $"GetEventFlagValue({pvar.id}, {(int)pvar.type})"; }
                        }
                        if (i < parameters.Length - 1) { parsed += " "; }
                    }
                    if (parsed.StartsWith("(")) { parsed += ")"; }
                    return parsed;
                }

                // Little function to resolve a variable to a flag
                Script.Flag GetFlagByVariable(string varName)
                {
                    Script.Flag retFlag = null;
                    if (!varName.Contains("."))  // probably a local var of this object
                    {
                        retFlag = scriptManager.GetFlag(Script.Flag.Designation.Local, $"{npcContent.id}.{varName}");
                    }
                    else         // looks like it's actually a local var of a different object
                    {
                        retFlag = scriptManager.GetFlag(Script.Flag.Designation.Local, varName); // look for it, if we dont find it we create it
                        if (retFlag == null) { retFlag = scriptManager.common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Short, Script.Flag.Designation.Local, varName); }
                    }
                    if (retFlag == null) { retFlag = scriptManager.GetFlag(Script.Flag.Designation.Global, varName); } // maybe its a global var!
                    return retFlag;
                }

                List<string> lines = new();

                foreach (Papyrus.Call call in calls)
                {
                    switch (call.type)
                    {
                        case Papyrus.Call.Type.Set:
                            {
                                // This var can be either global or local so check for both
                                Flag var = GetFlagByVariable(call.parameters[0]);
                                if (var == null) { break; } // if we fail to find the variable just discard for now. this only really happens if a papyrus script is discarded and fails to setup a local var
                                string code = $"SetEventFlagValue({var.id}, {var.Bits()}, {ParseParameters(call.parameters, 2)})";

                                lines.Add(code);

                                break;
                            }
                        case Papyrus.Call.Type.Journal:
                            {
                                Flag jvar = scriptManager.GetFlag(Script.Flag.Designation.Journal, call.parameters[0]); // look for flag, if not found make one
                                if (jvar == null) { jvar = scriptManager.common.CreateFlag(Flag.Category.Saved, Flag.Type.Byte, Script.Flag.Designation.Journal, call.parameters[0]); }
                                string code = $"SetEventFlagValue({jvar.id}, {jvar.Bits()}, {int.Parse(call.parameters[1])})";
                                lines.Add(code);
                                break;
                            }
                        case Papyrus.Call.Type.AddTopic:
                            {
                                Flag tvar = scriptManager.GetFlag(Script.Flag.Designation.TopicEnabled, call.parameters[0]);
                                string code = $"SetEventFlag({tvar.id}, FlagState.On)";
                                lines.Add(code);
                                break;
                            }
                        case Papyrus.Call.Type.PcJoinFaction:
                            {
                                Script.Flag fvar = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, npcContent.faction);
                                string code = $"SetEventFlag({fvar.id}, FlagState.On)";
                                lines.Add(code);
                                break;
                            }
                        case Papyrus.Call.Type.ModPcFacRep:
                            {
                                int rep = int.Parse(call.parameters[0]);
                                Script.Flag fvar = scriptManager.GetFlag(Script.Flag.Designation.FactionReputation, call.parameters[1]);
                                string code = $"assert t{esdId:D9}_x{Const.ESD_STATE_HARDCODE_MODFACREP}(facrepflag={fvar.id}, value={call.parameters[0]})";
                                lines.Add(code);
                                break;
                            }
                        case Papyrus.Call.Type.PcRaiseRank:
                            {
                                Script.Flag jvar = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, npcContent.faction);
                                Script.Flag rvar = scriptManager.GetFlag(Script.Flag.Designation.FactionRank, npcContent.faction);
                                string joinFactionCode = $"SetEventFlag({jvar.id}, True);";
                                string raiseRankCode = $"SetEventFlagValue({rvar.id}, {rvar.Bits()}, ( GetEventFlagValue({rvar.id}, {rvar.Bits()}) + {1} ))";
                                lines.Add(joinFactionCode);
                                lines.Add(raiseRankCode);
                                break;
                            }
                        case Papyrus.Call.Type.PcExpell:
                            {
                                Script.Flag fvar = scriptManager.GetFlag(Script.Flag.Designation.FactionExpelled, npcContent.faction);
                                string code = $"SetEventFlag({fvar.id}, FlagState.On)";
                                lines.Add(code);
                                break;
                            }
                        case Papyrus.Call.Type.PcClearExpelled:
                            {
                                Script.Flag fvar = scriptManager.GetFlag(Script.Flag.Designation.FactionExpelled, npcContent.faction);
                                string code = $"SetEventFlag({fvar.id}, FlagState.Off)";
                                lines.Add(code);
                                break;
                            }
                        case Papyrus.Call.Type.MessageBox:
                            {
                                scriptManager.common.GetOrRegisterMessage(paramanager, "Message", call.parameters[0]);
                                break;
                            }
                        case Papyrus.Call.Type.RemoveItem:
                            {
                                // only supporting items/gold added to player rn. will eventually support other stuff
                                if (call.target == "player")
                                {
                                    // Gold specifically handled as souls so its diffo from other item checks
                                    if (call.parameters[0] == "gold_001")
                                    {
                                        string code = $"ChangePlayerStat(PlayerStat.RunesCollected, ChangeType.Subtract, {ParseParameters(call.parameters, 1)})";
                                        lines.Add(code);
                                    }
                                    // Any other item
                                    else
                                    {
                                        ItemManager.ItemInfo itemInfo = itemManager.GetItem(call.parameters[0].ToLower());
                                        if (itemInfo == null) { throw new Exception("Script failed to find referenced item! This should not happen!"); }
                                        Script.Flag removeItemFlag = scriptManager.common.GetOrRegisterRemoveItem(itemInfo, int.Parse(call.parameters[1]));
                                        string code = $"SetEventFlag({removeItemFlag.id}, FlagState.On)";
                                        lines.Add(code);
                                    }
                                }
                                break;
                            }
                        case Papyrus.Call.Type.AddItem:
                            {
                                // only supporting items/gold added to player rn. will eventually support other stuff
                                if (call.target == "player")
                                {
                                    // Gold specifically handled as souls so its diffo from other item checks
                                    if (call.parameters[0] == "gold_001")
                                    {
                                        string code = $"ChangePlayerStat(PlayerStat.RunesCollected, ChangeType.Add, {ParseParameters(call.parameters, 1)})";
                                        lines.Add(code);
                                    }
                                    // Any other item
                                    else
                                    {
                                        ItemManager.ItemInfo itemInfo = itemManager.GetItem(call.parameters[0].ToLower());
                                        if (itemInfo == null) { throw new Exception("Script failed to find referenced item! This should not happen!"); }
                                        int row = paramanager.GenerateAddItemLot(itemInfo);
                                        string code = $"AwardItemLot({row})";
                                        lines.Add(code);
                                    }
                                }
                                break;
                            }
                        case Papyrus.Call.Type.ModDisposition:
                            {
                                Script.Flag dvar = scriptManager.GetFlag(Script.Flag.Designation.Disposition, npcContent.entity.ToString());
                                string code = $"assert t{esdId:D9}_x{Const.ESD_STATE_HARDCODE_MODDISPOSITION}(dispositionflag={dvar.id}, value={call.parameters[0]})";
                                lines.Add(code);
                                break;
                            }
                        case Papyrus.Call.Type.SetDisposition:
                            {
                                Script.Flag dvar = scriptManager.GetFlag(Script.Flag.Designation.Disposition, npcContent.entity.ToString());
                                string code = $"SetEventFlagValue({dvar.id}, {dvar.Bits()}, {call.parameters[0]})";
                                lines.Add(code);
                                break;
                            }
                        case Papyrus.Call.Type.ModReputation:
                            {
                                Script.Flag rvar = scriptManager.GetFlag(Script.Flag.Designation.Reputation, "Reputation");
                                string code = $"SetEventFlagValue({rvar.id}, {rvar.Bits()}, ( GetEventFlagValue({rvar.id}, {rvar.Bits()}) + {call.parameters[0]} ))";
                                lines.Add(code);
                                break;
                            }
                        case Papyrus.Call.Type.SetPcCrimeLevel:
                            {
                                Script.Flag cvar = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
                                string code = $"SetEventFlagValue({cvar.id}, {cvar.Bits()}, {call.parameters[0]})";
                                lines.Add(code);
                                break;
                            }
                        case Papyrus.Call.Type.PayFine:
                            {
                                Script.Flag aflag = scriptManager.GetFlag(Script.Flag.Designation.CrimeAbsolved, "CrimeAbsolved");
                                Script.Flag crimeLevel = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
                                string absolveCode = $"SetEventFlag({aflag.id}, FlagState.On);";  // setting this flag triggers a common event that clears all crime values
                                string fineCode = $"SetEventFlagValue({crimeLevel.id}, {crimeLevel.Bits()}, 0)"; // seting crimelevel to zero here since if this value isnt cleared immidieatly it can cause guards to re-engage you
                                lines.Add(absolveCode);
                                lines.Add(fineCode);
                                break;
                            }
                        case Papyrus.Call.Type.GoToJail:
                            {
                                Script.Flag aflag = scriptManager.GetFlag(Script.Flag.Designation.CrimeAbsolved, "CrimeAbsolved");
                                Script.Flag crimeLevel = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
                                string absolveCode = $"SetEventFlag({aflag.id}, FlagState.On);";  // setting this flag triggers a common event that clears all crime values
                                string fineCode = $"SetEventFlagValue({crimeLevel.id}, {crimeLevel.Bits()}, 0)"; // seting crimelevel to zero here since if this value isnt cleared immidieatly it can cause guards to re-engage you
                                lines.Add(absolveCode);
                                lines.Add(fineCode);
                                break;
                            }
                        case Papyrus.Call.Type.StartCombat:
                            {
                                if (call.parameters[0].Trim() == "player")
                                {
                                    Flag hvar; // if a guard starts combat with a player its a crime, if its anyone else it's just them being angy at you
                                    if (npcContent.IsGuard()) { hvar = scriptManager.GetFlag(Flag.Designation.CrimeEvent, npcContent.entity.ToString()); }
                                    else { hvar = scriptManager.GetFlag(Flag.Designation.Hostile, npcContent.entity.ToString()); }
                                    string code = $"SetEventFlag({hvar.id}, FlagState.On)";
                                    lines.Add(code);
                                    break;
                                }

                                // @TODO: startcombat with anything else than player is not supported yet
                                break;
                            }
                        case Papyrus.Call.Type.Goodbye:
                            {
                                // End conversation promptly
                                string code = $"return 0";
                                lines.Add(code);
                                break;
                            }
                        default:
                            { 
                                if(!debugUnsupportedPapyrusCallLogging.Contains(call.type.ToString()))
                                {
                                    Lort.Log($" ## WARNING ## Unsupported papyrus call {call.type}", Lort.Type.Debug);
                                    debugUnsupportedPapyrusCallLogging.Add(call.type.ToString());
                                }
                                break;
                            }
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


            /* Very specially handled call */
            /* This papyrus function is always singular and last in a dialog script so i can safely store as it's own thing */
            public class PapyrusChoice
            {
                public readonly List<Tuple<int, string>> choices;

                public PapyrusChoice(Papyrus.Call call)
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

            /* Actually resolve a comparison operation from this filter with a given value */
            public bool ResolveOperator(int leftValue)
            {
                switch (op)
                {
                    case Operator.Equal: return leftValue == value;
                    case Operator.NotEqual: return leftValue != value;
                    case Operator.GreaterEqual: return leftValue >= value;
                    case Operator.Greater: return leftValue > value;
                    case Operator.LessEqual: return leftValue <= value;
                    case Operator.Less: return leftValue < value;
                    default: return false;
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
                    case Operator.Greater: return "CompareType.Greater";             // for the comparetype operators the mismatch only applys to the Less and LessOrEqual ops
                    case Operator.Less: return "CompareType.LessOrEqual";
                    case Operator.LessEqual: return "CompareType.Less";
                    default: throw new Exception("Invalid operator type! This should not happen!");
                }
            }

            /* same as above but symbol instead of string */
            public string OperatorSymbol()
            {
                switch (op)
                {
                    case Operator.Equal: return "==";
                    case Operator.NotEqual: return "!=";
                    case Operator.GreaterEqual: return ">";    // due to an issue with esd compiling >= and > are swapped. same issue with < and <= as well
                    case Operator.Greater: return ">=";
                    case Operator.LessEqual: return "<=";
                    case Operator.Less: return "<";
                    default: throw new Exception("Invalid operator type! This should not happen!");
                }
            }
        }
    }
}
