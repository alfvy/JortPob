using JortPob.Common;
using JortPob.Worker;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static JortPob.Dialog;

namespace JortPob
{
    public class NpcManager
    {
        /* This class is responsible for managing the creation of ``Enemy`` objects in an MSB */
        /* In morrowind this is both the NPC and Creature record types */
        /* This class handles both the creation and assignment of params (NPCPARAM, NPCTHINKPARAM, CHARAINITPARAM) and manages ESDs and data for dialog generation and compiling */
        /* Morrowind makes a distinction between these 2 types of characters but ER does not */

        private readonly ESM esm;
        private readonly SoundManager soundManager;
        private readonly Paramanager paramanager;
        private readonly TextManager textManager;
        private readonly ItemManager itemManager;
        private readonly ScriptManager scriptManager;

        private readonly Dictionary<string, int> topicText; // topic text id map
        private readonly List<EsdInfo> esds;

        private readonly Dictionary<string, int> npcParamMap, npcThinkParamMap;

        private int nextNpcParamId, nextNpcThinkParamId;  // increment by 10

        public NpcManager(ESM esm, SoundManager sound, Paramanager param, TextManager text, ItemManager item, ScriptManager scriptManager)
        {
            this.esm = esm;
            this.soundManager = sound;
            this.paramanager = param;
            this.textManager = text;
            this.itemManager = item;
            this.scriptManager = scriptManager;

            esds = new();
            npcParamMap = new();
            npcThinkParamMap = new();
            topicText = new();

            nextNpcParamId = 544900010;
            nextNpcThinkParamId = 544900010;
        }

        public (int npc, int think) GetParams(ItemManager itemManager, Script script, NpcContent content)
        {
            int npcRow = GetNpcParam(itemManager, script, content);
            int thinkRow = GetThinkParam(itemManager, script, content);

            return (npcRow, thinkRow);
        }

        private int GetNpcParam(ItemManager itemManager, Script script, NpcContent content)
        {
            // First check if we already generated one for this npc record. If we did return that one. Some npcs like guards and dreamers have multiple placements
            if (npcParamMap.ContainsKey(content.id)) { return npcParamMap[content.id]; }

            int id = nextNpcParamId += 10;
            paramanager.GenerateNpcParam(itemManager, script, content, id);
            npcParamMap.Add(content.id, id);
            return id;
        }

        private int GetThinkParam(ItemManager itemManager, Script script, NpcContent content)
        {
            // First check if we already generated one for this npc record. If we did return that one. Some npcs like guards and dreamers have multiple placements
            if (npcThinkParamMap.ContainsKey(content.id)) { return npcThinkParamMap[content.id]; }

            int id = nextNpcThinkParamId += 10;
            paramanager.GenerateThinkParam(itemManager, script, content, id);
            npcThinkParamMap.Add(content.id, id);
            return id;
        }

        public (int npc, int think) GetParams(ItemManager itemManager, Script script, CreatureContent content, Override.EnemyRemap remap)
        {
            int npcRow = GetNpcParam(itemManager, script, content, remap);
            int thinkRow = GetThinkParam(itemManager, script, content, remap);

            return (npcRow, thinkRow);
        }

        private int GetNpcParam(ItemManager itemManager, Script script, CreatureContent content, Override.EnemyRemap remap)
        {
            if (npcParamMap.ContainsKey(content.id)) { return npcParamMap[content.id]; }

            int id = nextNpcParamId += 10;
            paramanager.GenerateNpcParam(itemManager, script, content, id, remap);
            npcParamMap.Add(content.id, id);
            return id;
        }

        private int GetThinkParam(ItemManager itemManager, Script script, CreatureContent content, Override.EnemyRemap remap)
        {
            if (npcThinkParamMap.ContainsKey(content.id)) { return npcThinkParamMap[content.id]; }

            int id = nextNpcThinkParamId += 10;
            paramanager.GenerateThinkParam(itemManager, script, content, id, remap);
            npcThinkParamMap.Add(content.id, id);
            return id;
        }

        public int GetESD(BaseTile tile, CharacterContent content) { return GetESD(tile.IdList(), content); }
        public int GetESD(InteriorGroup group, CharacterContent content) { return GetESD(group.IdList(), content); }

        /* Creates an ESD for the given instance of a npc */
        /* ESDs are generally 1 to 1 with characters but there are some exceptions like guards */
        // @TODO: THIS SYSTEM USING AN ARRAY OF INTS IS FUCKING SHIT PLEASE GOD REFACTOR THIS TO JUST USE THE ACTUAL TILE OR INTERIOR GROUP
        public int GetESD(int[] msbIdList, CharacterContent content)
        {
            if (Const.DEBUG_SKIP_ESD) { return 0; } // debug skip

            if (content.race == CharacterContent.Race.Creature && !esm.HasDialog((CreatureContent)content)) { return 0; } // if this is a creature, verify it has dialog lines to build dialog for

            // First check if we even need one, hostile or dead npcs dont' get talk data for now
            if (content.dead || content.hostile) { return 0; }

            /* There used to be a check here that looked for an esd tied to the record id of the npc, i'm removing this */
            /* Every instance of an npc needs its own esd. Sharing esd's will only lead to horrible bugs long term */
            /* ESDs and event flags now use the entity id of the individual npc as their unique identifying value NOT THE RECORD ID */

            List<Tuple<DialogRecord, List<DialogInfoRecord>>> dialog = esm.GetDialog(scriptManager, content);
            SoundManager.SoundBankInfo bankInfo = soundManager.GetBank(content);

            List<TopicData> data = [];
            foreach ((DialogRecord dia, List<DialogInfoRecord> infos) in dialog)
            {
                int topicId = 20000000; // generic "talk" as default, should never actually end up being used
                if (dia.type == DialogRecord.Type.Topic)
                {
                    if (!topicText.TryGetValue(dia.id, out topicId))
                    {
                        topicId = textManager.AddTopic(dia.id);
                        topicText.Add(dia.id, topicId);
                    }
                }

                TopicData topicData = new(dia, topicId);

                foreach (DialogInfoRecord info in infos)
                {
                    /* If this dialog is too long for a single subtitle we split it into pieces */
                    List<string> lines = Utility.CivilizedSplit(info.text);
                    List<int> talkRows = new();
                    int baseRow = -1;
                    for (int i=0;i<lines.Count();i++)
                    {
                        string line = lines[i];
                        /* Search existing soundbanks for the specific dialoginfo we are about to generate. if it exists just yoink it instead of generating a new one */
                        /* If we generate a new talkparam row for every possible line we run out of talkparam rows entirely and the project fails to build */
                        /* This sharing is required, and unfortunately it had to be added in at the end so its not a great implementation */
                        SoundBank.Sound snd = soundManager.FindSound(content, info.id + i); // look for a generated wem sound that matches the npc (race/sex) and dialog line (dialoginforecord id)

                        // Use an existing wem and talkparam we already generated because it's a match
                        if (snd != null) { talkRows.Add(bankInfo.bank.AddSound(snd)); continue; } // and continue here

                        /* Debug voice acting using SAM */
                        string wemFile;
                        uint nxtid = (uint)(info.id + i);
                        string hashName = $"{info.text.GetMD5Hash()}+{i}"; // Get the hash of the actual text string for this line, it will be our unique identier and filename for the cached wav/wem
                        if (Const.USE_SAM) { wemFile = soundManager.GenerateLine(dia, info, line, hashName, content); }
                        else { wemFile = Const.DEFAULT_DIALOG_WEM; }

                        // If this is not the first line in a talkparam group we must generate with sequential ids!
                        if (baseRow >= 0)
                        {
                            talkRows.Add(bankInfo.bank.AddSound(wemFile, info.id + i, line, (uint)(baseRow + i)));
                        }
                        // Make a new sound and talkparam row because no suitable match was found!
                        else
                        {
                            baseRow = bankInfo.bank.AddSound(wemFile, info.id + i, line);
                            talkRows.Add(baseRow);
                        }
                    }
                    // The parmanager function will automatically skip duplicates when addign talkparam rows so we don't need to do anything here. the esd gen needs those dupes so ye
                    topicData.talks.Add(new(info, talkRows, lines));
                }

                if (topicData.talks.Count > 0) { data.Add(topicData); } // if no valid lines for a topic, discard
            }
            paramanager.GenerateTalkParam(data);

            int esdId = int.Parse($"{bankInfo.id.ToString("D3")}{bankInfo.uses++.ToString("D2")}{msbIdList[0]:D2}{(msbIdList[0]==60?0:msbIdList[1]):D2}");  // i know guh guhhhhh

            Script areaScript = scriptManager.GetScript(msbIdList[0], msbIdList[1], msbIdList[2], msbIdList[3]); // get area script for this npc

            areaScript.RegisterNpcHostility(content);  // setup hostility flag/event
            areaScript.RegisterNpcHello(content);      // setup hello flags and turntoplayer script

            DialogESD dialogEsd = new(esm, scriptManager, paramanager, textManager, itemManager, areaScript, (uint)esdId, content, data);
            string pyPath = $"{Const.CACHE_PATH}esd\\t{esdId}.py";
            string esdPath = $"{Const.CACHE_PATH}esd\\t{esdId}.esd";
            dialogEsd.Write(pyPath);

            EsdInfo esdInfo = new(pyPath, esdPath, content.id, esdId);
            esds.Add(esdInfo);

            return esdId;
        }

        /* ESDs are now 1 to 1 with individual placements of enemies/creatures so the file writing has been simplified */
        public void Write()
        {
            EsdWorker.Go(esds);

            Lort.Log($"Binding {esds.Count()} ESDs...", Lort.Type.Main);
            Lort.NewTask($"Binding ESDs", esds.Count());

            /* Create all needed bnds */
            Dictionary<(int, int), BND4> bnds = new();
            foreach(EsdInfo esd in esds)
            {
                if(!bnds.ContainsKey((esd.map, esd.area)))
                {
                    BND4 bnd = new();
                    bnd.Compression = SoulsFormats.DCX.Type.DCX_KRAK;
                    bnd.Version = "07D7R6";
                    bnds.Add((esd.map, esd.area), bnd);
                }
            }

            /* Write esds to bnds */
            foreach(EsdInfo esd in esds)
            {
                BND4 bnd = bnds[(esd.map, esd.area)];
                BinderFile file = new();
                file.Bytes = System.IO.File.ReadAllBytes(esd.esd);
                file.Name = $"N:\\GR\\data\\INTERROOT_win64\\script\\talk\\m{esd.map:D2}_{esd.area:D2}_00_00\\{Utility.PathToFileName(esd.esd)}.esd";
                file.ID = bnd.Files.Count();

                bnd.Files.Add(file);
                Lort.TaskIterate();
            }

            /* Write bnds to file */
            Lort.Log($"Writing {bnds.Count} Binded ESDs... ", Lort.Type.Main);
            Lort.NewTask($"Writing {bnds.Count} Binded ESDs... ", bnds.Count);
            foreach (KeyValuePair<(int, int), BND4> kvp in bnds)
            {
                int map = kvp.Key.Item1;
                int area = kvp.Key.Item2;
                BND4 bnd = kvp.Value;

                bnd.Write($"{Const.OUTPUT_PATH}script\\talk\\m{map:D2}_{area:D2}_00_00.talkesdbnd.dcx");
                Lort.TaskIterate();
            }
        }

        public class EsdInfo
        {
            public readonly string py, esd, content;
            public readonly int id;  // esd id
            public readonly int map, area; // msb ids

            public EsdInfo(string py, string esd, string content, int id)
            {
                this.py = py;                                // path to the python source file
                this.esd = esd;        // path to compiled esd
                this.content = content;
                this.id = id;
                string m = id.ToString().Substring(5, 2);
                string a = id.ToString().Substring(7, 2);
                map = int.Parse(m);
                area = int.Parse(a);
            }
        }

        public class TopicData
        {
            public readonly DialogRecord dialog;
            public readonly int topicText;
            public readonly List<TalkData> talks;

            public TopicData(DialogRecord dialog, int topicText)
            {
                this.dialog = dialog;
                this.topicText = topicText;
                this.talks = new();
            }

            /* Blank constructor only used by creature dialog generation for blank topics like voice */
            public TopicData()
            {
                dialog = null;
                topicText = -1;
                talks = new();
            }

            /* Special case where a topic contains only infos with the filter type "choice" making it unreachable */
            public bool IsOnlyChoice()
            {
                foreach(TalkData talk in talks)
                {
                    if(talk.dialogInfo.type != DialogRecord.Type.Choice) { return false; }
                }
                return true;
            }

            /* Check if a rank requirment filter is used anywhere in this topicdata */
            public bool HasRankRequirementFilter()
            {
                foreach (TalkData talk in talks)
                {
                    foreach (DialogFilter filter in talk.dialogInfo.filters)
                    {
                        if (filter.type == DialogFilter.Type.Function && filter.function == DialogFilter.Function.RankRequirement)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            public class TalkData
            {
                public readonly DialogInfoRecord dialogInfo;
                public readonly int primaryTalkRow;  // first row for this talk, all that really matters game engine automatically plays subsequent rows in order
                public readonly List<int> talkRows;
                public readonly List<string> splitText;

                public TalkData(DialogInfoRecord dialogInfo, List<int> talkRows, List<string> splitText)
                {
                    this.dialogInfo = dialogInfo;
                    this.primaryTalkRow = talkRows[0];
                    this.talkRows = talkRows;
                    this.splitText = splitText;
                }

                public bool IsChoice()
                {
                    return dialogInfo.type == DialogRecord.Type.Choice;
                }
            }
        }


    }
}
