using JortPob.Common;
using SoulsFormats;
using SoulsIds;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using static JortPob.Script.Flag;

/* Individual script for an msb. */
/* managed by ScriptManager 
/* When using the word "entity" in this code i am refering to entity id. i just like shorter names */

/* Using this research as a base for conventions here https://docs.google.com/spreadsheets/d/17sE1a1h87BhpiUwKUyJ9ZjKTeehXA4OuLwmQvTfwo_M/edit?gid=1770617590#gid=1770617590 */

namespace JortPob
{
    using ScriptFlagLookupKey = (Script.Flag.Designation, string);

    public abstract class BaseScript
    {
        public readonly ScriptManager manager;

        public readonly Events AUTO;

        public readonly EMEVD emevd;
        public readonly EMEVD.Event init;

        public BaseScript(ScriptManager manager)
        {
            this.manager = manager;
            AUTO = new(Utility.ResourcePath(@"script\er-common.emedf.json"), true, true);

            emevd = new EMEVD();
            emevd.Compression = Compression.KRAK();
            emevd.Format = SoulsFormats.EMEVD.Game.Sekiro;

            // Linked file offsets are stored as bytes of a UTF16 string pointing to the commonfunc and macro emevd files
            byte[] file1 = System.Text.Encoding.Unicode.GetBytes(@"N:\GR\data\Param\event\common_func.emevd" + "\0");
            byte[] file2 = System.Text.Encoding.Unicode.GetBytes(@"N:\GR\data\Param\event\common_macro.emevd" + "\0");
            List<byte> combined = new();
            combined.AddRange(file1);
            combined.AddRange(file2);
            emevd.StringData = combined.ToArray();
            emevd.LinkedFileOffsets = new() { 0, file1.Length };

            init = new EMEVD.Event(0);
            emevd.Events.Add(init);
        }

        public abstract Script.Flag CreateFlagLocal(Content content, string name, uint value = 0);
        public abstract Script.Flag CreateFlag(Script.Flag.Category category, Script.Flag.Type type, Script.Flag.Designation designation, Content content, uint value = 0, bool allowPhased = false);
        public abstract Script.Flag CreateFlag(Script.Flag.Category category, Script.Flag.Type type, Script.Flag.Designation designation, string name, uint value = 0);
        public abstract Script.Flag GetOrCreateFlag(Script.Flag.Category category, Script.Flag.Type type, Script.Flag.Designation designation, Content content, uint value = 0, bool allowPhased = false);
        public abstract Script.Flag GetOrCreateFlag(Script.Flag.Category category, Script.Flag.Type type, Script.Flag.Designation designation, string name, uint value = 0);
    }

    public class Script : BaseScript
    {
        public readonly int map, x, y, block;

        public readonly List<CharacterContent> npcs; // list of npcs that are registered in this areascript, used to do some script generation
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

        public Script(ScriptManager manager, int map, int x, int y, int block) : base(manager)
        {
            this.map = map;
            this.x = x;
            this.y = y;
            this.block = block;

            entityIdMapping = new();

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

        /* Registers bed as a "bonfire" and creates and returns the specail entity ids for a bed and its respawn point */
        private uint bedCount = 0;
        public (uint bed, uint respawn) RegisterBed()
        {
            if(bedCount > Const.MAX_BEDS_PER_MSB) { Lort.Log($"## ERROR ## Failed to register respawn for bed in m{map:D2}_{x:D2}_{y:D2}_{block:D2} due to 19 bed limit!", Lort.Type.Debug); return (0, 0); }

            uint mapOffset;
            if (map == 60) { mapOffset = uint.Parse($"10{x:D2}{y:D2}0000"); }
            else { mapOffset = uint.Parse($"{map:D2}{x:D2}0000"); }

            uint bedEntity = mapOffset + 950 + bedCount;
            uint respawnEntity = bedEntity + 30;
            bedCount++;

            Flag bedFlag = CreateFlag(Flag.Category.Saved, Flag.Type.Bit, Script.Flag.Designation.RegisterBed, bedEntity.ToString());
            init.Instructions.Add(AUTO.ParseAdd($"RegisterBonfire({bedFlag.id}, {bedEntity}, 0, 0, 0, 5);"));
            return (bedEntity, respawnEntity);
        }

        public void RegisterLoadDoor(Paramanager paramanager, DoorContent door, ModelInfo modelInfo)
        {
            int actionParamId = paramanager.GenerateActionButtonDoorParam(modelInfo, door.warp.prompt);
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.LoadDoor]}, {actionParamId}, {door.entity}, {door.entity}, {1000}, {door.warp.map}, {door.warp.x}, {door.warp.y}, {door.warp.block}, {door.warp.entity});"));
        }

        public void RegisterItemAsset(ItemContent item)
        {
            CharacterContent owner;
            if(item.ownerNpc != null) { owner = GetAreaNpcById(item.ownerNpc); }
            else { owner = null; }

            Script.Flag disableFlag = manager.GetFlag(Script.Flag.Designation.Disabled, item);

            // Unowned item free for the taking
            if (owner == null)
            {
                if (disableFlag == null)
                {
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.ItemAsset]}, {item.treasure.id}, {item.entity});"));
                }
                else
                {
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.ItemAssetWithDisable]}, {disableFlag.id}, {item.entity}, {item.treasure.id}, {item.entity});"));
                }
            }
            // Item owned by an npc that counts as stealing if you take it
            else
            {
                Flag ownerDead = manager.GetFlag(Designation.Dead, owner);
                Flag crimeLevel = manager.GetFlag(Designation.CrimeLevel, "CrimeLevel");
                Flag crimeFlag = manager.GetFlag(Designation.CrimeEvent, owner);
                Flag thiefFlag = manager.GetFlag(Designation.ThiefCrime, owner);

                List<string> parameters = new()
                {
                    item.treasure.id.ToString(),
                    item.entity.ToString(),
                    item.treasure.id.ToString(),
                    item.entity.ToString(),
                    ownerDead.id.ToString(),
                    owner.witness == CharacterContent.Witness.Guard ? thiefFlag.id.ToString() : crimeFlag.id.ToString(),  // minor hack. if guard witness then dont trigger hostility so guard can arrest player.
                    thiefFlag.id.ToString(),
                    crimeLevel.id.ToString(),
                    crimeLevel.Bits().ToString(),
                    item.value.ToString()
                };
                if (disableFlag == null)
                {
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.OwnedItemAsset]}, {string.Join(", ", parameters)});"));
                }
                else
                {
                    parameters.Insert(0, item.entity.ToString());
                    parameters.Insert(0, disableFlag.id.ToString());
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.OwnedItemAssetWithDisable]}, {string.Join(", ", parameters)});"));
                }
                ownedContent.Add(item);
            }
        }

        public void RegisterContainerAsset(ContainerContent container, int totalValue)
        {
            CharacterContent owner;
            if (container.ownerNpc != null) { owner = GetAreaNpcById(container.ownerNpc); }
            else { owner = null; }

            if(owner != null)
            {
                Flag ownerDead = manager.GetFlag(Designation.Dead, owner);
                Flag crimeLevel = manager.GetFlag(Designation.CrimeLevel, "CrimeLevel");
                Flag crimeFlag = manager.GetFlag(Designation.CrimeEvent, owner);
                Flag thiefFlag = manager.GetFlag(Designation.ThiefCrime, owner);

                List<string> parameters = new()
                {
                    container.treasure.id.ToString(),
                    container.treasure.id.ToString(),
                    ownerDead.id.ToString(),
                    owner.witness == CharacterContent.Witness.Guard ? thiefFlag.id.ToString() : crimeFlag.id.ToString(),  // minor hack. if guard witness then dont trigger hostility so guard can arrest player.
                    thiefFlag.id.ToString(),
                    crimeLevel.id.ToString(),
                    crimeLevel.Bits().ToString(),
                    totalValue.ToString()
                };

                init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.OwnedContainer]}, {string.Join(", ", parameters)});"));
                ownedContent.Add(container);
            }
        }

        public void RegisterNpcHostility(CharacterContent npc)
        {
            GetOrCreateFlag(Flag.Category.Temporary, Flag.Type.Nibble, Flag.Designation.FriendHitCounter, npc); // setup friendly hit counter
            Flag hostileFlag = GetOrCreateFlag(Flag.Category.Saved, Flag.Type.Bit, Flag.Designation.Hostile, npc, npc.IsHostile() ? 1u : 0u);
            Flag crimeFlag = GetOrCreateFlag(Flag.Category.Saved, Flag.Type.Bit, Flag.Designation.CrimeEvent, npc);
            Flag hostileQuipFlag = GetOrCreateFlag(Flag.Category.Temporary, Flag.Type.Bit, Flag.Designation.HostileQuip, npc);
            Flag hasBeenAttackedFlag = GetOrCreateFlag(Flag.Category.Saved, Flag.Type.Bit, Script.Flag.Designation.HasBeenAttacked, npc);
            Flag helloFlag = GetOrCreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.Hello, npc);
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.NpcHostilityHandler]}, {hostileFlag.id}, {npc.entity}, {npc.entity}, {hostileFlag.id}, {npc.entity}, {npc.entity});"));
            npcs.Add(npc);
        }

        /* Dead body */
        public void RegisterDeadNpc(NpcContent npc)
        {
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.DeadBody]}, {npc.entity}, {npc.entity}, {npc.entity});"));
        }

        public void RegisterCharacter(Paramanager paramanager, CharacterContent npc, Flag count)
        {
            Flag deadFlag = GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Dead, npc);
            Flag disableFlag = manager.GetFlag(Script.Flag.Designation.Disabled, npc);

            if (IsInterior())
            {
                uint cellAreaEntityId = manager.areas[npc.cell];

                // NPC spawn handler for phased npcs
                if (npc is PhasedNpcContent)
                {
                    PhasedNpcContent pnpc = (PhasedNpcContent)npc;
                    Flag phaseFlag = manager.GetFlag(Designation.Phase, pnpc);
                    List<string> parameters = new()
                    {
                        cellAreaEntityId.ToString(),
                        pnpc.entity.ToString(),
                        deadFlag.id.ToString(),
                        pnpc.entity.ToString(),
                        pnpc.entity.ToString(),
                        disableFlag.id.ToString(),
                        phaseFlag.id.ToString(),
                        phaseFlag.Bits().ToString(),
                        pnpc.phase.ToString(),
                        pnpc.entity.ToString(),
                        pnpc.entity.ToString(),
                        deadFlag.id.ToString(),
                        count.id.ToString(),
                        count.Bits().ToString(),
                        count.MaxValue().ToString()
                    };
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.IntSpawnHandlerPhased]}, {string.Join(", ", parameters)});"));
                }
                // NPC spawn handler for NPCS that can't be disabled
                else if (disableFlag == null)
                {
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.IntSpawnHandler]}, {cellAreaEntityId}, {npc.entity}, {deadFlag.id}, {npc.entity}, {npc.entity}, {deadFlag.id}, {count.id}, {count.Bits()}, {count.MaxValue()});"));
                }
                // NPC spawn handler for NPCS that can be disabled
                else
                {
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.IntSpawnHandlerDisableable]}, {cellAreaEntityId}, {npc.entity}, {deadFlag.id}, {npc.entity}, {disableFlag.id}, {npc.entity}, {npc.entity}, {deadFlag.id}, {count.id}, {count.Bits()}, {count.MaxValue()});"));
                }
            }
            else
            {
                // NPC spawn handler for phased npcs
                if (npc is PhasedNpcContent)
                {
                    PhasedNpcContent pnpc = (PhasedNpcContent)npc;
                    Flag phaseFlag = manager.GetFlag(Designation.Phase, pnpc);
                    List<string> parameters = new()
                    {
                        deadFlag.id.ToString(),
                        pnpc.entity.ToString(),
                        pnpc.entity.ToString(),
                        disableFlag.id.ToString(),
                        phaseFlag.id.ToString(),
                        phaseFlag.Bits().ToString(),
                        pnpc.phase.ToString(),
                        pnpc.entity.ToString(),
                        pnpc.entity.ToString(),
                        deadFlag.id.ToString(),
                        count.id.ToString(),
                        count.Bits().ToString(),
                        count.MaxValue().ToString()
                    };
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.SpawnHandlerPhased]}, {string.Join(", ", parameters)});"));
                }
                // NPC spawn handler for NPCS that can't be disabled
                else if (disableFlag == null)
                {
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.SpawnHandler]}, {deadFlag.id}, {npc.entity}, {npc.entity}, {deadFlag.id}, {count.id}, {count.Bits()}, {count.MaxValue()});"));
                }
                // NPC spawn handler for NPCS that can be disabled
                else
                {
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.SpawnHandlerDisableable]}, {deadFlag.id}, {npc.entity}, {disableFlag.id}, {npc.entity}, {npc.entity}, {deadFlag.id}, {count.id}, {count.Bits()}, {count.MaxValue()});"));
                }
            }

            if (npc.essential)
            {
                int tutorialPopupId = paramanager.GenerateMessage("", "With this character's death, the thread of prophecy is severed. You are doomed.");
                init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.Essential]}, {deadFlag.id}, {deadFlag.id}, {tutorialPopupId});"));
            }
        }

        public void RegisterHaltEvent(CharacterContent npc)
        {
            Flag deadFlag = manager.GetFlag(Script.Flag.Designation.Dead, npc);
            Flag hostileFlag = manager.GetFlag(Script.Flag.Designation.Hostile, npc);

            List<string> parameters = new()
            {
                deadFlag.id.ToString(),
                npc.entity.ToString(),
                hostileFlag.id.ToString(),
                npc.entity.ToString(),
                npc.entity.ToString(),
                npc.entity.ToString(),
                hostileFlag.id.ToString(),
                npc.entity.ToString(),
                npc.entity.ToString(),
            };
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.Halt]}, {string.Join(", ", parameters)});"));
        }

        public Flag RegisterStaticDisable(StaticContent content)
        {
            Script.Flag disableFlag = manager.GetFlag(Script.Flag.Designation.Disabled, content);
            if(disableFlag == null) { return null; } // disable flags only get created for objects that have disable calls referencing them.
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.StaticDisable]}, {disableFlag.id}, {content.entity});"));
            return disableFlag;
        }

        /* Used by ESD to disable an object via a flag */
        public Flag GetOrRegisterTriggerDisable(Content content)
        {
            Flag triggerDisableFlag = manager.GetFlag(Script.Flag.Designation.TriggerDisable, content);
            if (triggerDisableFlag == null)
            {
                triggerDisableFlag = CreateFlag(Flag.Category.Temporary, Flag.Type.Bit, Flag.Designation.TriggerDisable, content.id);
                init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.TriggerDisable]}, {triggerDisableFlag.id}, {content.entity}, {content.entity}, {triggerDisableFlag.id});"));
            }
            return triggerDisableFlag;
        }

        /* Used by ESD to enable an object via a flag */
        public Flag GetOrRegisterTriggerEnable(Content content)
        {
            Flag triggerEnableFlag = manager.GetFlag(Script.Flag.Designation.TriggerEnable, content);
            if (triggerEnableFlag == null)
            {
                triggerEnableFlag = CreateFlag(Flag.Category.Temporary, Flag.Type.Bit, Flag.Designation.TriggerEnable, content.id);
                init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.TriggerEnable]}, {triggerEnableFlag.id}, {content.entity}, {content.entity}, {triggerEnableFlag.id});"));
            }
            return triggerEnableFlag;
        }

        /* Can't call PlaySE from ESD so we are using an EMEVD event triggered by a flag to do it. Returned flag is the trigger for playing a sound. */
        public Script.Flag GetOrRegisterPlaySE(uint entity, int seId)
        {
            /* See if this SE already has an event registered for it */
            string playId = $"{entity}->{seId}";
            Flag playFlag = manager.GetFlag(Designation.PlaySE, playId);

            /* If not create one and return */
            if (playFlag == null)
            {
                playFlag = CreateFlag(Category.Saved, Flag.Type.Bit, Designation.PlaySE, playId);
                init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.PlaySE]}, {playFlag.id}, {entity}, 5, {seId}, {playFlag.id});"));  // 5 is SFX type
            }
            return playFlag;
        }

        /* Register a modStat call here so that it is permanently applied to npc. Flag returned is the trigger for it to be on. */
        public Flag RegisterModStat(uint entityId, int speffId)
        {
            Flag modStatFlag = CreateFlag(Category.Saved, Type.Bit, Designation.NpcModStat, $"{entityId}->{speffId}");
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.NpcModStat]}, {modStatFlag.id}, {entityId}, {speffId});"));
            return modStatFlag;
        }

        /* Register NpcInfight event for StartCombat and StopCombat calls */
        public Flag GetOrRegisterInfight(CharacterContent content)
        {
            Script.Flag fightFlag = manager.GetFlag(Designation.NpcInfight, content);
            if(fightFlag != null) { return fightFlag; } // already exists, return flag

            fightFlag = CreateFlag(Category.Saved, Type.Bit, Designation.NpcInfight, content, 0, true);
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.NpcInfight]}, {fightFlag.id}, {content.entity}, {content.entity}, {fightFlag.id}, {content.entity}, {content.entity});"));
            return fightFlag;
        }

        /* Crime events are charcters reactions to being attacked or stolen from */
        /* These events are generated before Write(). What this does is look for any npcs near an npc and if the player commits a crime against an npc we trigger all nearby npcs to get mad at the player */
        /* Additionally if this event is triggered we also set all guards hostile and mark guards to force greet the player */
        public void GenerateCrimeEvents()
        {
            foreach(CharacterContent npc in npcs)
            {
                Flag eventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"{npc.entity}->CrimeEvent");
                EMEVD.Event crimeEvent = new EMEVD.Event();
                crimeEvent.ID = eventFlag.id;
                // If the player commits a crime agains this npc, their crime flag flips, we then go hostile
                Flag hvar = manager.GetFlag(Flag.Designation.Hostile, npc);
                Flag cvar = manager.GetFlag(Flag.Designation.CrimeEvent, npc);
                crimeEvent.Instructions.Add(AUTO.ParseAdd($"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {cvar.id});"));  // if crime flag on
                crimeEvent.Instructions.Add(AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {hvar.id}, ON);"));       // go hostile

                // Look for nearby npcs and see if any of them are nearby, if they are they will also turn hostile if their alarm value is high enough
                // @TODO: minor concern but this only searches by distance within this msb. in the overworld if an npc was near a border it would not look for nearby npcs in the next tile over. very minor issue but noting it here anyways
                foreach (CharacterContent other in npcs)
                {
                    if (!other.IsGuard()) // if you are a guard, go full aggro, otherwise its conditional
                    {
                        if (other.alarm < 50) { continue; } // no nearby crime aggro if low alarm
                        if (System.Numerics.Vector3.Distance(npc.position, other.position) > 10) { continue; } // no nearby crime aggro if far away
                    }
                    Flag otherhvar = manager.GetFlag(Flag.Designation.Hostile, other);
                    crimeEvent.Instructions.Add(AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {otherhvar.id}, ON);"));       // go hostile as well
                }

                // Apply the 'alarming' functional speff to the player. Used by dialog filters
                crimeEvent.Instructions.Add(AUTO.ParseAdd($"SetSpEffect(10000, {(int)SpeffManager.Functional.Alarming});"));

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

            Flag playerIsSneakingFlag = manager.GetFlag(Designation.PlayerIsSneaking, "PlayerIsSneaking");

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

        private CharacterContent GetAreaNpcById(string id)
        {
            foreach(CharacterContent npc in npcs)
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

        public override Flag CreateFlag(Flag.Category category, Flag.Type type, Flag.Designation designation, Content content, uint value = 0, bool allowPhased = false)
        {
            if(content is PhasedNpcContent && !allowPhased) { throw new System.Exception("Cannot create flags for phased content in this manner! See CreateFlagLocal or use allowePhased if you are certain it's okay."); }
            else if(content is PhasedNpcContent) { return CreateFlag(category, type, designation, manager.routing[(PhasedNpcContent)content], value); }
            return CreateFlag(category, type, designation, content.entity.ToString(), value);
        }

        public override Flag CreateFlagLocal(Content content, string name, uint value = 0)
        {
            if (content is PhasedNpcContent)
            {
                PhasedNpcContent pnpc = (PhasedNpcContent)content;
                return GetOrCreateFlag(Category.Saved, Type.Short, Designation.Local, $"{manager.routing[pnpc]}.{name}", value); // this is the only place where a phased npc creates new flags
            }
            return CreateFlag(Category.Saved, Type.Short, Designation.Local, $"{content.entity.ToString()}.{name}", value);
        }

        public override Flag CreateFlag(Flag.Category category, Flag.Type type, Flag.Designation designation, string name, uint value = 0)
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

        public override Flag GetOrCreateFlag(Category category, Type type, Designation designation, Content content, uint value = 0, bool allowPhased = false)
        {
            Flag flag = manager.GetFlag(designation, content);
            if (flag != null) { return flag; }
            return CreateFlag(category, type, designation, content, value, allowPhased);
        }

        public override Flag GetOrCreateFlag(Flag.Category category, Flag.Type type, Flag.Designation designation, string name, uint value = 0)
        {
            Flag flag = manager.GetFlag(designation, name);
            if (flag != null) { return flag; }
            return CreateFlag(category, type, designation, name, value);
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
            if (rawCount >= 950) { newid = manager.common.CreateEntity(type, name); }
            else { newid = mapOffset + ((uint)type) + rawCount; }

            entityIdMapping.Add(newid, name);

            return newid;
        }

        public Flag FindFlagByLookupKey(ScriptFlagLookupKey key)
        {
            return flagsByLookupKey.GetValueOrDefault(key);
        }

        // map 60 and 61 are the main overworld and dlc overworld respectively. All other map ids are interior areas like caves/forts. Important disction as there are differences in how they are handled by the game engine.
        public bool IsInterior()
        {
            return !(map == 60 || map == 61);
        }

        public void Write()
        {
            emevd.Write(Path.Combine(Const.OUTPUT_PATH, "event", $"m{map:D2}_{x:D2}_{y:D2}_{block:D2}.emevd.dcx"));
        }

        [DebuggerDisplay("Flag :: {category} {type} {designation} {name}")]
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
                HasBeenAttacked, // used by a specific filter condition. if the npc is ever hit by the player this is permanently true
                TopicEnabled, TalkedToPc, Disposition, PlayerRace,
                FactionJoined, FactionReputation, FactionRank, FactionExpelled,    // faction stuff
                GuardIsGreeting, PlayerIsTalking, PlayerIsSneaking, PlayerRuneCount, PlayerStat, PlayerItemCount,
                ReturnValueRankReq, ReturnReactionHigh, ReturnReactionLow,          // these are temp values used by ESD to store variables
                CurrentWeather,
                Arrest,                // Flag for determining if guards have attempted an arrest or if they are just going to attack you
                CrimeAbsolved,            // temp value, setting it to 1 triggers a common emevd event that clears all crime and hostility flags
                ResetHostility,           // similar to above but doesnt clear crime, just resets hostility
                HostileQuip, Hello,    // temp value that is flagged when a guard is gretting the player, if the player has a bounty and trys to leave dialog without paying they get dunked on
                OnActivate, OnDeath, CellChanged, GetButtonPassBit, GetButtonFailBit, GetButtonPressedValue, // used by papyrus to emulate mw script behaviours
                PermanentSpeff, NpcModStat,  // Used for maintaining speffs on the player/npcs permanently
                NpcInfight,   // Used to make npcs fight each other. papyrus StartCombat/StopCombat calls
                AiPackage,   // Index of what default aipacakge we are running
                SwitchAiPackage, // Very special event flag that creates a function with a single parameter that kills all ai package events and starts a new one after
                AiPackageDone,  // Set to 1 when "SwitchAiPackage" is called. Reading from this value in a script sets it back to 0
                Wander,      // Index of wander position used by Wander AiPackage
                RunSubscript, // Flag created for StartScript, StopScript, ScriptRunning papyrus calls. when true subscripts run, when false they stop
                Phase,      // Used by phased npcs to determine what location they are at
                Message,    // Flag to trigger a popmessage or notification
                PlaySE,     // Flat to trigger playing a sound effect
                TravelWarp, // Flag to trigger warping the player from travel npcs
                RemoveItem,  // Flag to trigger removing an item from the player
                Random,      // Flag for EMEVD papyrus to get values from Random calls
                SecondsPassed, // Timer flag value used to emulate GetSecondsPassed papyrus call
                TriggerEnable, TriggerDisable,  // Flags set by ESD to trigger an EMEVD event to enable or disable an object
                DiscoverLocation,  // marks location on your map when set
                RegisterBed,      // For register bonfire calls in EMEVD
                Hardcode     // Used by any jank hardcoding I end up doing
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
