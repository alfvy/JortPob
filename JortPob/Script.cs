using HKLib.hk2018.hk;
using HKX2;
using JortPob.Common;
using SoulsFormats;
using SoulsIds;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using static JortPob.Script.Flag;
using static SoulsFormats.MSB1.Event;
using static SoulsFormats.MSBAC4.Event;

/* Individual script for an msb. */
/* managed by ScriptManager 
/* When using the word "entity" in this code i am refering to entity id. i just like shorter names */

/* Using this research as a base for conventions here https://docs.google.com/spreadsheets/d/17sE1a1h87BhpiUwKUyJ9ZjKTeehXA4OuLwmQvTfwo_M/edit?gid=1770617590#gid=1770617590 */

namespace JortPob
{
    using ScriptFlagLookupKey = (Script.Flag.Designation, string);

    public class Script
    {

        public Events AUTO;

        public readonly int map, x, y, block;

        public readonly ScriptCommon common; // commonevent and commonfunc emevds
        public readonly EMEVD emevd;
        public readonly EMEVD.Event init;

        public readonly List<NpcContent> npcs; // list of npcs that are registered in this areascript, used to do some script generation
        public readonly List<Content> ownedContent; // list of all items/containers that have an npc owner. this is used to generate a thievery script after main gen finishes

        public readonly Dictionary<uint, string> entityIdMapping; // used for debuggin, just records a string (usually a record id) as a description for created entity ids

        public enum EntityType
        {
            Enemy = 0, Asset = 1000, Region = 2000, Event = 3000, Collision = 4000, Group = 5000
        }

        public List<Flag> flags;

        /**
         * This is just used to speed up searches for flags. It is a 1:1 mapping, so duplicate designated/named
         * flags will result in us just using the first one. This is okay (for now), because that is the same logic
         * that GetFlag already uses elsewhere.
         */
        private readonly Dictionary<ScriptFlagLookupKey, Flag> flagsByLookupKey;
        private Dictionary<Flag.Category, uint> flagUsedCounts;
        private Dictionary<EntityType, uint> entityUsedCounts;

        public Script(ScriptCommon common, int map, int x, int y, int block)
        {
            this.common = common;
            this.map = map;
            this.x = x;
            this.y = y;
            this.block = block;

            entityIdMapping = new();

            AUTO = new(Utility.ResourcePath(@"script\\er-common.emedf.json"), true, true);

            EMEVD DEBUGTESTDELETE = EMEVD.Read($"{Const.ELDEN_PATH}\\game\\event\\m60_42_36_00.emevd.dcx");

            emevd = new EMEVD();
            emevd.Compression = SoulsFormats.DCX.Type.DCX_KRAK;
            emevd.Format = SoulsFormats.EMEVD.Game.Sekiro;

            // Bytes here are raw string data that points to the filenames of common_func and common_macro
            emevd.StringData = new byte[] { 78, 0, 58, 0, 92, 0, 71, 0, 82, 0, 92, 0, 100, 0, 97, 0, 116, 0, 97, 0, 92, 0, 80, 0, 97, 0, 114, 0, 97, 0, 109, 0, 92, 0, 101, 0, 118, 0, 101, 0, 110, 0, 116, 0, 92, 0, 99, 0, 111, 0, 109, 0, 109, 0, 111, 0, 110, 0, 95, 0, 102, 0, 117, 0, 110, 0, 99, 0, 46, 0, 101, 0, 109, 0, 101, 0, 118, 0, 100, 0, 0, 0, 78, 0, 58, 0, 92, 0, 71, 0, 82, 0, 92, 0, 100, 0, 97, 0, 116, 0, 97, 0, 92, 0, 80, 0, 97, 0, 114, 0, 97, 0, 109, 0, 92, 0, 101, 0, 118, 0, 101, 0, 110, 0, 116, 0, 92, 0, 99, 0, 111, 0, 109, 0, 109, 0, 111, 0, 110, 0, 95, 0, 109, 0, 97, 0, 99, 0, 114, 0, 111, 0, 46, 0, 101, 0, 109, 0, 101, 0, 118, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            emevd.LinkedFileOffsets = new() { 0, 82 };

            init = new EMEVD.Event(0);
            emevd.Events.Add(init);

            flags = new();
            flagsByLookupKey = new();

            flagUsedCounts = new()
            {
                { Flag.Category.Event, 0 },
                { Flag.Category.Saved, 0 },
                { Flag.Category.Temporary, 0 }
            };

            entityUsedCounts = new()
            {
                { EntityType.Enemy, 0 },
                { EntityType.Asset, 0 },
                { EntityType.Region, 0 },
                { EntityType.Event, 0 },
                { EntityType.Collision, 0 },
                { EntityType.Group, 0 }
            };

            npcs = new();
            ownedContent = new();
        }

        public void RegisterLoadDoor(Paramanager paramanager, DoorContent door, ModelInfo modelInfo)
        {
            int actionParamId = paramanager.GenerateActionButtonDoorParam(modelInfo, door.warp.prompt);
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {common.events[ScriptCommon.Event.LoadDoor]}, {actionParamId}, {door.entity}, {door.entity}, {1000}, {door.warp.map}, {door.warp.x}, {door.warp.y}, {door.warp.block}, {door.warp.entity});"));
        }

        public void RegisterItemAsset(ItemContent item)
        {
            NpcContent owner;
            if(item.ownerNpc != null) { owner = GetAreaNpcById(item.ownerNpc); }
            else { owner = null; }

            // Unowned item free for the taking
            if (owner == null)
            {
                init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {common.events[ScriptCommon.Event.ItemAsset]}, {item.treasure.id}, {item.entity});"));
            }
            // Item owned by an npc that counts as stealing if you take it
            else
            {
                Flag ownerDead = GetFlag(Designation.Dead, owner.entity.ToString());
                Flag crimeLevel = common.GetFlag(Designation.CrimeLevel, "CrimeLevel");
                Flag crimeFlag = GetFlag(Designation.CrimeEvent, owner.entity.ToString());
                Flag thiefFlag = GetFlag(Designation.ThiefCrime, owner.entity.ToString());

                List<string> parameters = new()
                {
                    item.treasure.id.ToString(),
                    item.entity.ToString(),
                    item.treasure.id.ToString(),
                    item.entity.ToString(),
                    ownerDead.id.ToString(),
                    crimeFlag.id.ToString(),
                    thiefFlag.id.ToString(),
                    crimeLevel.id.ToString(),
                    crimeLevel.Bits().ToString(),
                    item.value.ToString()
                };

                init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {common.events[ScriptCommon.Event.OwnedItemAsset]}, {string.Join(", ", parameters)});"));
                ownedContent.Add(item);
            }
        }

        public void RegisterContainerAsset(ContainerContent container)
        {
            NpcContent owner;
            if (container.ownerNpc != null) { owner = GetAreaNpcById(container.ownerNpc); }
            else { owner = null; }

            if(owner != null)
            {
                Flag ownerDead = GetFlag(Designation.Dead, owner.entity.ToString());
                Flag crimeLevel = common.GetFlag(Designation.CrimeLevel, "CrimeLevel");
                Flag crimeFlag = GetFlag(Designation.CrimeEvent, owner.entity.ToString());
                Flag thiefFlag = GetFlag(Designation.ThiefCrime, owner.entity.ToString());

                List<string> parameters = new()
                {
                    container.treasure.id.ToString(),
                    container.treasure.id.ToString(),
                    ownerDead.id.ToString(),
                    crimeFlag.id.ToString(),
                    thiefFlag.id.ToString(),
                    crimeLevel.id.ToString(),
                    crimeLevel.Bits().ToString(),
                    500.ToString() // @TODO: just setting a random value for this. we should calculate value of items in a container in the future and set that as the crimegold for stealing
                };

                init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {common.events[ScriptCommon.Event.OwnedContainer]}, {string.Join(", ", parameters)});"));
                ownedContent.Add(container);
            }
        }

        public void RegisterNpcHostility(NpcContent npc)
        {
            CreateFlag(Flag.Category.Temporary, Flag.Type.Nibble, Flag.Designation.FriendHitCounter, npc.entity.ToString()); // setup friendly hit counter
            Flag hostileFlag = CreateFlag(Flag.Category.Saved, Flag.Type.Bit, Flag.Designation.Hostile, npc.entity.ToString());
            Flag crimeFlag = CreateFlag(Flag.Category.Saved, Flag.Type.Bit, Flag.Designation.CrimeEvent, npc.entity.ToString());
            Flag hostileQuipFlag = CreateFlag(Flag.Category.Temporary, Flag.Type.Bit, Flag.Designation.HostileQuip, npc.entity.ToString());
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {common.events[ScriptCommon.Event.NpcHostilityHandler]}, {hostileFlag.id}, {npc.entity}, {hostileFlag.id}, {npc.entity});"));
            npcs.Add(npc);
        }

        public void RegisterNpcHello(NpcContent npc)
        {
            /* Hello event: npc turns to player when player enters a certain radius and the esd sets a flag and says a hello line */
            Flag helloFlag = CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.Hello, npc.entity.ToString());
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {common.events[ScriptCommon.Event.Hello]}, {helloFlag.id}, {npc.entity}, {helloFlag.id});"));
        }

        /* Dead body */
        public void RegisterDeadNpc(NpcContent npc)
        {
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {common.events[ScriptCommon.Event.DeadBody]}, {npc.entity}, {npc.entity}, {npc.entity});"));
        }

        public void RegisterNpc(Paramanager paramanager, NpcContent npc, Flag count)
        {
            /* Dead/disable spawn event */
            Flag deadFlag = CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Dead, npc.entity.ToString());
            Flag disableFlag = CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Disabled, npc.entity.ToString());
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {common.events[ScriptCommon.Event.SpawnHandler]}, {disableFlag.id}, {npc.entity}, {deadFlag.id}, {npc.entity}, {npc.entity}, {deadFlag.id}, {count.id}, {count.Bits()}, {count.MaxValue()});"));
            if(npc.essential)
            {
                int tutorialPopupId = paramanager.GenerateMessage("", "With this character's death, the thread of prophecy is severed. You are doomed.");
                init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {common.events[ScriptCommon.Event.Essential]}, {deadFlag.id}, {deadFlag.id}, {tutorialPopupId});"));
            }
        }

        public void RegisterCreature(CreatureContent creature, Flag count)
        {
            /* Dead/disable spawn event */
            Flag deadFlag = CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Dead, creature.entity.ToString());
            Flag disableFlag = CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Disabled, creature.entity.ToString());
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {common.events[ScriptCommon.Event.SpawnHandler]}, {disableFlag.id}, {creature.entity}, {deadFlag.id}, {creature.entity}, {creature.entity}, {deadFlag.id}, {count.id}, {count.Bits()}, {count.MaxValue()});"));
        }

        /* Crime events are charcters reactions to being attacked or stolen from */
        /* These events are generated before Write(). What this does is look for any npcs near an npc and if the player commits a crime against an npc we trigger all nearby npcs to get mad at the player */
        /* Additionally if this event is triggered we also set all guards hostile and mark guards to force greet the player */
        public void GenerateCrimeEvents()
        {
            foreach(NpcContent npc in npcs)
            {
                Flag eventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, npc.entity.ToString());
                EMEVD.Event crimeEvent = new EMEVD.Event();
                crimeEvent.ID = eventFlag.id;
                // If the player commits a crime agains this npc, their crime flag flips, we then go hostile
                Flag hvar = FindFlagByLookupKey(Script.FormatFlagLookupKey(Flag.Designation.Hostile, npc.entity.ToString()));
                Flag cvar = FindFlagByLookupKey(Script.FormatFlagLookupKey(Flag.Designation.CrimeEvent, npc.entity.ToString()));
                crimeEvent.Instructions.Add(AUTO.ParseAdd($"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {cvar.id});"));  // if crime flag on
                crimeEvent.Instructions.Add(AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {hvar.id}, ON);"));       // go hostile

                // Look for nearby npcs and see if any of them are nearby, if they are they will also turn hostile if their alarm value is high enough
                // @TODO: minor concern but this only searches by distance within this msb. in the overworld if an npc was near a border it would not look for nearby npcs in the next tile over. very minor issue but noting it here anyways
                foreach (NpcContent other in npcs)
                {
                    if (!other.IsGuard()) // if you are a guard, go full aggro, otherwise its conditional
                    {
                        if (other.alarm < 50) { continue; } // no nearby crime aggro if low alarm
                        if (System.Numerics.Vector3.Distance(npc.position, other.position) > 10) { continue; } // no nearby crime aggro if far away
                    }
                    Flag otherhvar = FindFlagByLookupKey(Script.FormatFlagLookupKey(Flag.Designation.Hostile, other.entity.ToString()));
                    crimeEvent.Instructions.Add(AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {otherhvar.id}, ON);"));       // go hostile as well
                }

                // Lastly, flip the crime event flag back to 0
                crimeEvent.Instructions.Add(AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {cvar.id}, OFF);"));

                emevd.Events.Add(crimeEvent);
                init.Instructions.Add(AUTO.ParseAdd($"InitializeEvent(0, {crimeEvent.ID}, 0);"));
            }
        }

        /* Generate thievery event */
        /* Geneartes a big event that hides all owned item pickups UNLESS the player is sneaking. This is to make accidentally stealing stuff not a problem */
        public void GenerateThieveryEvent()
        {
            EMEVD.Event thieveryEvent = new();
            Flag thieveryEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"ThieveryEvent::{map:D2}_{x:D2}_{y:D2}_{block:D2}");
            thieveryEvent.ID = thieveryEventFlag.id;

            Flag playerIsSneakingFlag = common.GetFlag(Designation.PlayerIsSneaking, "PlayerIsSneaking");

            thieveryEvent.Instructions.Add(AUTO.ParseAdd($"IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, {playerIsSneakingFlag.id});")); // if not sneaking
            foreach (Content content in ownedContent)
            {
                thieveryEvent.Instructions.Add(AUTO.ParseAdd($"SetAssetTreasureState({content.entity}, Disabled);"));
            }
            thieveryEvent.Instructions.Add(AUTO.ParseAdd($"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {playerIsSneakingFlag.id});")); // if sneaking
            foreach (Content content in ownedContent)
            {
                thieveryEvent.Instructions.Add(AUTO.ParseAdd($"SetAssetTreasureState({content.entity}, Enabled);"));
            }
            thieveryEvent.Instructions.Add(AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));

            emevd.Events.Add(thieveryEvent);
            init.Instructions.Add(AUTO.ParseAdd($"InitializeEvent(0, {thieveryEventFlag.id}, 0);"));
        }

        private NpcContent GetAreaNpcById(string id)
        {
            foreach(NpcContent npc in npcs)
            {
                if (npc.id == id) { return npc; }
            }
            return null;
        }

        /* Create an EMEVD flag for this MSB */
        private static readonly Dictionary<Flag.Category, uint[]> FLAG_TYPE_OFFSETS = new()
        {
            { Flag.Category.Event, new uint[] { 1000, 3000, 6000 } },
            { Flag.Category.Saved, new uint[] { 0, 4000, 7000, 8000, 9000 } },
            { Flag.Category.Temporary, new uint[] { 2000, 5000 } }
        };

        public static ScriptFlagLookupKey GetLookupKeyForFlag(Flag flag)
        {
            return FormatFlagLookupKey(flag.designation, flag.name.ToLower());
        }

        public static ScriptFlagLookupKey FormatFlagLookupKey(Flag.Designation designation, string name)
        {
            return (designation, name.ToLower());
        }

        public Flag CreateFlag(Flag.Category category, Flag.Type type, Flag.Designation designation, string name, uint value = 0)
        {
            uint rawCount = flagUsedCounts[category];
            uint perThou = rawCount / 1000;
            uint mod = rawCount % 1000;
            uint mapOffset;
            if(map == 60) { mapOffset = uint.Parse($"10{x:D2}{y:D2}0000"); }
            else { mapOffset = uint.Parse($"{map:D2}{x:D2}0000"); }

            uint id = mapOffset + FLAG_TYPE_OFFSETS[category][perThou] + mod;  // if we run out of flags this will throw an out of bounds exception. that situation would be bad but should't happen.
            flagUsedCounts[category] += ((uint)type);

            // Check for a collision with a common event flag, if we find a collision we recursviely try making another flag
            if (ScriptManager.DO_NOT_USE_FLAGS.Contains(id))
            {
                Lort.Log($" ## WARNING ## Flag collision with commonevent found: {id}", Lort.Type.Debug);
                return CreateFlag(category, type, designation, name, value);
            }

            Flag flag = new(category, type, designation, name, id, value);
            flags.Add(flag);
            flagsByLookupKey.TryAdd(GetLookupKeyForFlag(flag), flag);
            return flag;
        }

        /* Create a unique entity id for this MSB */
        public uint CreateEntity(EntityType type, string name)
        {
            uint rawCount = entityUsedCounts[type]++;
            uint mapOffset;
            if (map == 60)
            {
                mapOffset = uint.Parse($"10{x:D2}{y:D2}0000");
            }
            else
            {
                mapOffset = uint.Parse($"{map:D2}{x:D2}0000");
            }


            //if (rawCount >= 1000) { throw new Exception($" Entity ID overflow in m{map:D2}_{x:D2}_{y:D2}"); }

            uint newid;
            if (rawCount >= 1000) { newid = common.CreateEntity(type, name); }
            else { newid = mapOffset + ((uint)type) + rawCount; }

            entityIdMapping.Add(newid, name);

            return newid;
        }

        private Script.Flag GetFlag(Designation designation, string name)
        {
            var lookupKey = Script.FormatFlagLookupKey(designation, name.ToLower());

            return FindFlagByLookupKey(lookupKey);
        }

        public Flag FindFlagByLookupKey(ScriptFlagLookupKey key)
        {
            return flagsByLookupKey.GetValueOrDefault(key);
        }

        public void Write()
        {
            emevd.Write($"{Const.OUTPUT_PATH}\\event\\m{map:D2}_{x:D2}_{y:D2}_{block:D2}.emevd.dcx");
        }

        public class Flag
        {
            public enum Category
            {
                Event, Saved, Temporary
            }

            public enum Type
            {
                Bit = 1, Nibble = 4, Byte = 8, Short = 16, Int = 32
            }

            public enum Designation
            {
                Event,                                          // Flag is an event ID
                Item,                                           // ItemLot awarded flag
                ItemVisibility,                                 // Flag that determines if an item in a shop is visible for the player to buy
                Global, Local, Reputation, Journal, CrimeLevel,          // CrimeLevel is gold owed to guards, the Crime below is a per npc variable for if you comitted a crime against them
                Dead, DeadCount, Disabled, Hostile, CrimeEvent, FriendHitCounter, Pickpocketed, ThiefCrime,      // hostile flag exists for friendly npcs, if you piss em off they stab you
                TopicEnabled, TalkedToPc, Disposition, PlayerRace,
                FactionJoined, FactionReputation, FactionRank, FactionExpelled,    // faction stuff
                GuardIsGreeting, PlayerIsTalking, PlayerIsSneaking, PlayerRuneCount,
                ReturnValueRankReq,                              // these are temp values used by ESD to store variables
                CrimeAbsolved,            // temp value, setting it to 1 triggers a common emevd event that clears all crime and hostility flags
                HostileQuip, Hello,    // temp value that is flagged when a guard is gretting the player, if the player has a bounty and trys to leave dialog without paying they get dunked on
                OnActivate, CellChanged, GetButtonPressedBit, GetButtonPressedValue, // used by papyrus to emulate mw script behaviours
                Message,    // Flag to trigger a popmessage or notification
                TravelWarp, // Flag to trigger warping the player from travel npcs
                RemoveItem  // Flag to trigger removing an item from the player
            }

            public readonly Category category;
            public readonly Type type;
            public readonly Designation designation;
            public readonly string name;  // general purpose string to identify this flag. for example, if this is a papyrus global variable, it would be that variables name
            public readonly uint id, value;   // id is flag, value is the default initial value. usually 0

            public Flag(Category category, Type type, Designation designation, string name, uint id, uint value)
            {
                this.category = category;
                this.type = type;
                this.designation = designation;
                this.name = name;
                this.id = id;
                this.value = value;
            }

            public uint Bits()
            {
                return (uint)type;
            }

            public uint MaxValue()
            {
                return (uint)Utility.Pow(2, (uint)type) - 1;
            }
        }
    }
}
