using JortPob.Common;
using JortPob.Worker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static JortPob.Dialog;
using static SoulsFormats.DRB.Shape;

namespace JortPob
{
    public class SoundManager
    {
        private volatile int nextBankId;
        private readonly List<SoundBankInfo> banks;
        private readonly SoundBankGlobals globals;

        private readonly List<SAMData> samQueue;

        public class SAMData
        {
            public readonly Dialog.DialogRecord dialog;
            public readonly Dialog.DialogInfoRecord info;
            public readonly string line;
            public readonly string hashName;
            public readonly CharacterContent npc;
            public SAMData(Dialog.DialogRecord dialog, Dialog.DialogInfoRecord info, string line, string hashName, CharacterContent npc)
            {
                this.dialog = dialog;
                this.info = info;
                this.line = line;
                this.hashName = hashName;
                this.npc = npc;
            }
        }

        public SoundManager()
        {
            nextBankId = 100;
            banks = new();
            globals = new();
            samQueue = new();
        }

        /* Either returns an existing bank meeting the requirements, or makes a new one */
        public SoundBankInfo GetBank(CharacterContent npc)
        {
            bool useCustom = Override.CheckCustomVoice(npc.id);
            bool isCreature = npc.race == CharacterContent.Race.Creature;

            foreach (SoundBankInfo bankInfo in banks)
            {
                if(useCustom && bankInfo.race == CharacterContent.Race.Custom && bankInfo.custom == npc.id && bankInfo.uses <= Const.MAX_ESD_PER_VCBNK)
                {
                    return bankInfo;
                }
                else if (isCreature && bankInfo.race == CharacterContent.Race.Creature && bankInfo.custom == npc.id && bankInfo.uses <= Const.MAX_ESD_PER_VCBNK)
                {
                    return bankInfo;
                }
                else if (bankInfo.race == npc.race && bankInfo.sex == npc.sex && bankInfo.uses <= Const.MAX_ESD_PER_VCBNK)
                {
                    return bankInfo;
                }
            }
            SoundBankInfo bnk;
            if (useCustom) { bnk = new(nextBankId++, CharacterContent.Race.Custom, npc.sex, new SoundBank(globals), npc.id); }
            else if(isCreature) { bnk = new(nextBankId++, CharacterContent.Race.Creature, npc.sex, new SoundBank(globals), npc.id); }
            else { bnk = new(nextBankId++, npc.race, npc.sex, new SoundBank(globals)); }
            banks.Add(bnk);
            return bnk;
        }

        public SoundBank.Sound FindSound(CharacterContent npc, int dialogInfo)
        {
            bool useCustom = Override.CheckCustomVoice(npc.id);
            bool isCreature = npc.race == CharacterContent.Race.Creature;

            if (useCustom)
            {
                return banks.Where(b => b.race == CharacterContent.Race.Custom && b.custom == npc.id)
                            .SelectMany(b => b.bank.sounds)
                            .FirstOrDefault(s => s.dialogInfo == dialogInfo, null);
            }
            else if (isCreature)
            {
                return banks.Where(b => b.race == CharacterContent.Race.Creature && b.custom == npc.id)
                            .SelectMany(b => b.bank.sounds)
                            .FirstOrDefault(s => s.dialogInfo == dialogInfo, null);
            }
            else
            {
                return banks.Where(b => b.race == npc.race && b.sex == npc.sex)
                    .SelectMany(b => b.bank.sounds)
                    .FirstOrDefault(s => s.dialogInfo == dialogInfo, null);
            }
        }

        /* Adds lines to a queue so we can do multithreaded tts gen on them */
        public string GenerateLine(DialogRecord dialog, DialogInfoRecord info, string line, string hashName, CharacterContent npc)
        {
            bool useCustom = Override.CheckCustomVoice(npc.id);
            bool isCreature = npc.race == CharacterContent.Race.Creature;

            SAMData dat = new(dialog, info, line, hashName, npc);
            samQueue.Add(dat);

            if (useCustom) { return Path.Combine(Const.CACHE_PATH, @$"dialog\{CharacterContent.Race.Custom}\{npc.id}\{dialog.id}\{hashName}\{hashName}.wem"); }
            else if(isCreature) { return Path.Combine(Const.CACHE_PATH, @$"dialog\{CharacterContent.Race.Creature}\{npc.id}\{dialog.id}\{hashName}\{hashName}.wem"); }
            else { return Path.Combine(Const.CACHE_PATH, @$"dialog\{npc.race}\{npc.sex}\{dialog.id}\{hashName}\{hashName}.wem"); }
        }

        /* Writes all soundbanks to given dir */
        public void Write(string dir)
        {
            SamWorker.Go(samQueue); // actually generate and convert wems

            Lort.Log($"Writing {banks.Count()} BNKs...", Lort.Type.Main);
            Lort.NewTask("Writing BNKs", banks.Count);

            foreach (SoundBankInfo bankInfo in banks)
            {
                bankInfo.bank.Write(dir, bankInfo.id);
                Lort.TaskIterate();
            }
        }

        public class SoundBankGlobals
        {
            private readonly object bnkIdLock = new(), headerIdLock = new(), sourceIdLock = new();
            private readonly uint[] usedHeaderIds, usedBnkIds, usedSourceIds;  // list of every single used bnk id (of the multiple id types) in stock elden ring. bnk ids are global so we want to avoid collisions
            private readonly List<uint> bnkCallIds; // list of every generating "play" or "stop" bnk id, these are not sequential like other ids so we track them here
            private volatile uint nextBnkId, nextHeaderId, nextSourceId;  // do not use directly, call NextID()
            private volatile uint nextRowId;  // increments by 10

            public SoundBankGlobals()
            {
                uint[] LoadIdList(string path)
                {
                    string[] lines = File.ReadAllLines(path);
                    return lines.Select(uint.Parse).ToArray();
                }

                usedBnkIds = LoadIdList(Utility.ResourcePath(@"sound\all_used_bnk_ids.txt"));
                usedHeaderIds = LoadIdList(Utility.ResourcePath(@"sound\all_used_bnk_header_ids.txt"));
                usedSourceIds = LoadIdList(Utility.ResourcePath(@"sound\all_used_source_ids.txt"));

                bnkCallIds = new();

                nextHeaderId = 100;
                nextSourceId = 100000000;
                nextBnkId = 1000;
                nextRowId = 20000000;
            }

            public uint[] GetEventBnkId()
            {
                uint[] TryGetNextCallIds(uint rowId)
                {
                    byte[] playCallBytes = Encoding.ASCII.GetBytes($"Play_v{rowId.ToString("D8")}0".ToLower());
                    byte[] stopCallBytes = Encoding.ASCII.GetBytes($"Stop_v{rowId.ToString("D8")}0".ToLower());

                    uint playCallId = Utility.FNV1_32(playCallBytes);
                    uint stopCallId = Utility.FNV1_32(stopCallBytes);

                    return [rowId, playCallId, stopCallId];
                }

                uint[] ids = TryGetNextCallIds(NextRowId());
                while (usedBnkIds.Contains(ids[1]) || usedBnkIds.Contains(ids[2]))
                {
                    ids = TryGetNextCallIds(NextRowId());
                }

                bnkCallIds.Add(ids[1]);
                bnkCallIds.Add(ids[2]);

                return ids;
            }

            public uint NextBnkId()
            {
                lock (bnkIdLock)
                {
                    while (bnkCallIds.Contains(nextBnkId) || usedBnkIds.Contains(nextBnkId))
                    {
                        nextBnkId++;
                    }

                    return nextBnkId++;
                }
            }

            public uint NextHeaderId()
            {
                lock (headerIdLock)
                {
                    while (usedHeaderIds.Contains(nextHeaderId))
                    {
                        nextHeaderId++;
                    }

                    return nextHeaderId++;
                }
            }

            public uint NextSourceId()
            {
                lock (sourceIdLock)
                {
                    while (usedSourceIds.Contains(nextSourceId))
                    {
                        nextSourceId++;
                    }

                    return nextSourceId++;
                }
            }

            public uint NextRowId()
            {
                return Interlocked.Add(ref nextRowId, 10);
            }
        }

        public record SoundBankInfo
        (
            int id,               // vc###.bnk id
            NpcContent.Race race, // race of npcs that use this bank
            NpcContent.Sex sex,
            SoundBank bank,
            string custom = null  // (usually null)!  If we use the Race enum 'Creature' or 'Custom' then this string is the id of the custom voice role used here
        )
        {
            public int uses { get; set; }
        }
    }
}
