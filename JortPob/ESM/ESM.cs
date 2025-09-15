using HKLib.hk2018.hkaiCollisionAvoidance.Solver;
using HKLib.hk2018.hke;
using JortPob.Common;
using JortPob.Worker;
using SoulsFormats;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static JortPob.Dialog;
using static JortPob.NpcContent;
using static JortPob.NpcManager.TopicData;
using static JortPob.Script;

namespace JortPob
{
    public class ESM
    {
        /* Types of records in the ESM */
        public enum Type
        {
            Header, GameSetting, GlobalVariable, Class, Faction, Race, Sound, Skill, MagicEffect, Script, Region, Birthsign, LandscapeTexture, Spell, Static, Door,
            MiscItem, Weapon, Container, Creature, Bodypart, Light, Enchantment, Npc, Armor, Clothing, RepairTool, Activator, Apparatus, Lockpick, Probe, Ingredient,
            Book, Alchemy, LeveledItem, LeveledCreature, Cell, Landscape, PathGrid, SoundGen, Dialogue, DialogueInfo
        }

        private readonly Dictionary<Type, List<JsonNode>> unidentifiedRecordsByType;
        private readonly Dictionary<Type, Dictionary<string, JsonNode>> recordsByType;
        private readonly ConcurrentDictionary<Int2, Landscape> landscapesByCoordinate;
        public List<DialogRecord> dialog;
        public List<Faction> factions;
        public List<Cell> exterior, interior;

        public ESM(string path, ScriptManager scriptManager)
        {
            Lort.Log($"Loading '{path}' ...", Lort.Type.Main);

            string tempRawJson = File.ReadAllText(path);
            JsonArray json = JsonNode.Parse(tempRawJson).AsArray();

            recordsByType = new Dictionary<Type, Dictionary<string, JsonNode>>();
            unidentifiedRecordsByType = new Dictionary<Type, List<JsonNode>>();
            var enumNames = Enum.GetNames(typeof(Type)).ToHashSet();

            foreach (string name in Enum.GetNames(typeof(Type)))
            {
                Enum.TryParse(name, out Type type);
                if (type == Type.Dialogue || type == Type.DialogueInfo) { continue; } // special records, need to be handled specially
                recordsByType.Add(type, new Dictionary<string, JsonNode>());
                unidentifiedRecordsByType.Add(type, []);
            }

            foreach (var record in json)
            {
                if (record?["type"] == null)
                {
                    continue;
                }

                var rawRecordType = record["type"].ToString();
                if (!enumNames.Contains(rawRecordType))
                {
                    continue;
                }
                if (!Enum.TryParse(rawRecordType, out Type type))
                {
                    continue;
                }

                // special records, need to be handled specially
                if (type is Type.Dialogue or Type.DialogueInfo)
                {
                    continue;
                }

                if (record["id"] == null)
                {
                    unidentifiedRecordsByType[type].Add(record);
                }
                else
                {
                    recordsByType[type].Add(record["id"].ToString(), record);
                }
            }

            /* Handle dialog stuff now */
            dialog = new();
            DialogRecord current = null;
            for (int i = 0; i < json.Count; i++)
            {
                JsonNode record = json[i];
                Enum.TryParse(record["type"].ToString(), out Type type);

                if (type == Type.Dialogue)
                {
                    string idstr = record["id"].ToString();
                    string typestr = idstr.Replace(" ", "");
                    string diatype = record["dialogue_type"].ToString();
                    typestr = new String(typestr.Where(c => c != '-' && (c < '0' || c > '9')).ToArray());
                    if (!Enum.TryParse(typestr, out DialogRecord.Type dtype)) { dtype = DialogRecord.Type.Topic; }
                    if (diatype.ToLower() == "journal") { dtype = DialogRecord.Type.Journal; }

                    if (current != null && current.type == DialogRecord.Type.Greeting && dtype == DialogRecord.Type.Greeting) { continue; } // skip so we can merge all 9 greeting levels into a single thingy

                    current = new(dtype, idstr, scriptManager.common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.TopicEnabled, idstr));
                    dialog.Add(current);
                }
                else if (type == Type.DialogueInfo)
                {
                    // check for a "choice" filter and mark this as a Choice type dialoginforecord if that's the case
                    // choice type dialoginfo are only accessed through a choice papyrus call and have to be handled differently than other dialoginfos
                    bool isChoice = false;
                    foreach (JsonNode filterNode in record["filters"].AsArray())
                    {
                        if (filterNode["filter_type"].ToString() == "Function" && filterNode["function"].ToString() == "Choice") { isChoice = true; break; }
                    }

                    DialogInfoRecord dialogInfoRecord = new(isChoice ? DialogRecord.Type.Choice : current.type, record);
                    current.infos.Add(dialogInfoRecord);
                }
            }

            /* Post process, looking for topic unlocks */
            foreach (DialogRecord topic in dialog)
            {
                foreach (DialogInfoRecord info in topic.infos)
                {
                    foreach (DialogRecord otherTopic in dialog)
                    {
                        if (info.text.ToLower().Contains(otherTopic.id.ToLower()))
                        {
                            if (topic == otherTopic) { continue; } // prevent self succ
                            info.unlocks.Add(otherTopic);
                        }
                    }
                }
            }

            /* Multi threading to speed this up... */
            List<List<Cell>> cells = CellWorker.Go(this);
            exterior = cells[0];
            interior = cells[1];
            landscapesByCoordinate = new();

            /* Post processing of local variables. */
            /* Local variables need to be created and initialized as a fixed "unset" value */
            /* We cannot simply instance local vars on the fly as some contexts that are looking for them need to know if they exists (filters for examlpe) */
            /* So we do a quick scan through all papyrus dialog snippets and papyrus scripts (@TODO that part) to find them and create them now */
            foreach (DialogRecord topic in dialog)
            {
                foreach (DialogInfoRecord info in topic.infos)
                {
                    if (info.script != null)
                    {
                        foreach (DialogPapyrus.PapyrusCall call in info.script.calls)
                        {
                            if (call.type == DialogPapyrus.PapyrusCall.Type.Set)
                            {
                                if(!call.parameters[0].Contains(".")) { continue; } // if the variable name doesn't contain a '.' then it's a global not a local
                                Script.Flag lvar = scriptManager.GetFlag(Flag.Designation.Local, call.parameters[0]);
                                if(lvar == null) { scriptManager.common.CreateFlag(Flag.Category.Saved, Flag.Type.Short, Flag.Designation.Local, call.parameters[0], (uint)Utility.Pow(2, (uint)Flag.Type.Short) - 1); }
                            }
                        }
                    }
                }
            }

            /* Load faction info from esm */
            factions = new();
            List<JsonNode> factionJson = [.. GetAllRecordsByType(ESM.Type.Faction)];
            foreach (JsonNode jsonNode in factionJson)
            {
                Faction faction = new(jsonNode);
                factions.Add(faction);
            }
        }

        /* List of types that we should search for references */
        // more const values we should move somewhere. @TODO
        public readonly Type[] VALID_CONTENT_TYPES = {
            Type.Static, Type.Container, Type.Light, Type.Sound, Type.Skill, Type.Region, Type.Door, Type.MiscItem, Type.Weapon,  Type.Creature, Type.Bodypart, Type.Npc,
            Type.Armor, Type.Clothing, Type.RepairTool, Type.Activator, Type.Apparatus, Type.Lockpick, Type.Probe, Type.Ingredient, Type.Book, Type.Alchemy, Type.LeveledItem,
            Type.LeveledCreature, Type.PathGrid, Type.SoundGen
        };

        /* References don't contain any explicit 'type' data so... we just gotta go find it lol */
        /* @TODO: well actually i think the 'flags' int value in some records is useed as a 32bit boolean array and that may specify record types possibly. Look into it? */
        public Record FindRecordById(string id)
        {
            foreach (var type in VALID_CONTENT_TYPES)
            {
                var recordsById = recordsByType[type];
                if (recordsById.TryGetValue(id, out var value))
                {
                    return new Record(type, value);
                }
            }
            return null; // Not found!
        }

        public IEnumerable<JsonNode> GetAllRecordsByType(Type type)
        {
            return recordsByType[type].Values.Concat(unidentifiedRecordsByType[type]);
        }

        public Cell GetCellByGrid(Int2 position)
        {
            foreach (Cell cell in exterior)
            {
                if (cell.coordinate == position) { return cell; }
            }
            return null;
        }

        public Cell GetCellByName(string name)
        {
            foreach (Cell cell in exterior)
            {
                if (cell.name == name) { return cell; }
            }
            foreach (Cell cell in interior)
            {
                if (cell.name == name) { return cell; }
            }
            return null;
        }

        public Landscape GetLandscape(Int2 coordinate)
        {
            if (GetCellByGrid(coordinate) == null) { return null; } // Performance hack.

            if (landscapesByCoordinate.TryGetValue(coordinate, out var existingLandscape))
            {
                return existingLandscape;
            }

            var matchingRecord = GetAllRecordsByType(Type.Landscape)
                .FirstOrDefault(
                    json => int.Parse(json["grid"][0].ToString()) == coordinate.x &&
                            int.Parse(json["grid"][1].ToString()) == coordinate.y
                );

            if (matchingRecord == null)
            {
                return null;
            }

            var landscape = new Landscape(this, coordinate, matchingRecord);
            landscapesByCoordinate[coordinate] = landscape;
            return landscape;
        }

        /* Same as above but only returns a landscape if its already fully loaded. Returns null if its not loaded */
        public Landscape GetLoadedLandscape(Int2 coordinate)
        {
            return landscapesByCoordinate.GetValueOrDefault(coordinate);
        }

        /* Load all landscapes, single threaded */
        public void LoadLandscapes()
        {
            Lort.Log($"Processing {exterior.Count} landscapes...", Lort.Type.Main);
            Lort.NewTask("Processing Landscape", exterior.Count);
            foreach (Cell cell in exterior)
            {
                GetLandscape(cell.coordinate);
                Lort.TaskIterate();
            }
        }

        /* Get dialog and character data for building esd */
        public List<Tuple<DialogRecord, List<DialogInfoRecord>>> GetDialog(NpcContent npc)
        {
            List<Tuple<DialogRecord, List<DialogInfoRecord>>> ds = new();  // i am really sorry about this type
            foreach(DialogRecord dialogRecord in dialog)
            {
                if (dialogRecord.type == DialogRecord.Type.Journal) { continue; } // obviously skip these lmao

                // Check if the npc meets requirements for any lines in this topic
                List<DialogInfoRecord> infos = new();
                foreach(DialogInfoRecord info in dialogRecord.infos)
                {
                    // Check if the npc meets all static requirements for this dialog line
                    if (info.speaker != null && info.speaker != npc.id) { continue; }
                    if (info.race != NpcContent.Race.Any && info.race != npc.race) { continue; }
                    if (info.job != null && info.job != npc.job) { continue; }
                    if (info.faction != null && info.faction != npc.faction) { continue; }
                    if (info.rank > npc.rank) { continue; }
                    if (info.cell != null && npc.cell.name != null && !npc.cell.name.ToLower().StartsWith(info.cell.ToLower())) { continue; }
                    if (info.sex != NpcContent.Sex.Any && info.sex != npc.sex) { continue; }

                    infos.Add(info);

                    // If this line has no filters it means that anything below it is unreachable, so we just break in that case
                    if (info.filters.Count() <= 0 && info.playerFaction == null && info.playerRank <= 0 && info.disposition <= 0) { break; }
                }

                if (infos.Count() > 0) { ds.Add(new(dialogRecord, infos)); } // discard if no valid lines
            }

            return ds;
        }
    }

    public class Faction
    {
        public readonly string id, name;
        public readonly List<Rank> ranks;

        public Faction(JsonNode json)
        {
            id = json["id"].GetValue<string>();
            name = json["name"].GetValue<string>();
            ranks = new();

            JsonArray rankNames = json["rank_names"].AsArray();
            JsonArray rankRequirements = json["data"]["requirements"].AsArray();

            for (int i=0;i< rankNames.Count();i++)
            {
                string rankName = rankNames[i].GetValue<string>();
                JsonNode rankRequiremnt = rankRequirements[i];
                int reputation = rankRequiremnt["reputation"].GetValue<int>();
                Rank rank = new(rankName, i, reputation);
                ranks.Add(rank);
            }
        }

        public class Rank
        {
            public readonly string name;
            public readonly int level, reputation; // required reputation to reach this rank
            public Rank(string name, int level, int reputation)
            {
                this.name = name;
                this.level = level;
                this.reputation = reputation;
            }
        }
    }

    public class Record
    {
        public readonly ESM.Type type;
        public readonly JsonNode json;
        public Record(ESM.Type type, JsonNode json)
        {
            this.type = type;
            this.json = json;
        }
    }
}
