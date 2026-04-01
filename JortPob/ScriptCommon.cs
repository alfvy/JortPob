using JortPob.Common;
using SoulsFormats;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using static JortPob.Script;
using static JortPob.Script.Flag;

namespace JortPob
{
    using ScriptFlagLookupKey = (Script.Flag.Designation, string); 

    /* Handles CommonEvent and CommonFunc EMEVD. These are different from map scripts so I decided to give them a seperate class */

    public class ScriptCommon : BaseScript
    {
        public readonly EMEVD func;

        public List<Flag> flags;
        private Dictionary<Flag.Category, uint> flagUsedCounts;
        private Dictionary<EntityType, uint> entityUsedCounts;

        public enum Event
        {
            SpawnHandler, SpawnHandlerDisableable, SpawnHandlerPhased, IntSpawnHandler, IntSpawnHandlerDisableable, IntSpawnHandlerPhased, Halt,
            LoadDoor, NpcHostilityHandler, Message, Essential, DeadBody, 
            ItemAsset, OwnedItemAsset, ItemAssetWithDisable, OwnedItemAssetWithDisable, OwnedContainer, TravelWarp, RemoveItem, PermanentSpeff,
            StaticDisable, PlaySE, TriggerEnable, TriggerDisable, NpcModStat, NpcInfight, GetSecondsPassed
        }
        public readonly Dictionary<Event, uint> events;
        public readonly Dictionary<int, Flag> messages;  // hash of message text as key, value is flag that when set to true triggers a message to display

        /**
         * This is just used to speed up searches for flags. It is a 1:1 mapping, so duplicate designated/named
         * flags will result in us just using the first one. This is okay (for now), because that is the same logic
         * that GetFlag already uses.
         */
        private readonly Dictionary<ScriptFlagLookupKey, Flag> flagsByLookupKey;

        public ScriptCommon(ScriptManager manager) : base(manager)
        {
            func = EMEVD.Read(Utility.ResourcePath(@"script\common_func.emevd.dcx"));

            messages = new();

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

            events = new();

            /* Create an event for going through load doors */
            Flag doorEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:DoorLoad");
            EMEVD.Event loadDoor = new(doorEventFlag.id);

            int pc = 0;
            string NextParameterName()
            {
                return $"X{pc++ * 4}_4";
            }

            string[] loadDoorEventRaw = new string[]
            {
                $"IfActionButtonInArea(MAIN, {NextParameterName()}, {NextParameterName()});",
                $"RotateCharacter(10000, {NextParameterName()}, 60000, false);",
                $"WaitFixedTimeSeconds(0.25);",
                $"PlaySE({NextParameterName()}, SoundType.Asset, 200);",
                $"WaitFixedTimeSeconds(0.75);",
                $"WarpPlayer({NextParameterName()}, {NextParameterName()}, {NextParameterName()}, {NextParameterName()}, {NextParameterName()}, -1);",
                $"EndUnconditionally(EventEndType.End);"
            };

            for (int i = 0; i < loadDoorEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(loadDoorEventRaw[i], i);
                loadDoor.Parameters.AddRange(newPs);
                loadDoor.Instructions.Add(instr);
            }

            func.Events.Add(loadDoor);
            events.Add(Event.LoadDoor, doorEventFlag.id);

            /* Create an event for handling creature/npc spawn/respawn*/
            Flag spawnEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:SpawnHandler");
            EMEVD.Event spawnHandler = new(spawnEventFlag.id);

            pc = 0;

            string[] spawnHandlerEventRaw = new string[]
            {
                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",   // check dead flag
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"IfCharacterHPValue(MAIN, {NextParameterName()}, 5, 0, 0, 1);", // check if hp is less or equal to 0. comparison values are in byte format so 5 is <= and 4 is >=
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, ON);",  // set dead
                $"IncrementEventValue({NextParameterName()}, {NextParameterName()}, {NextParameterName()});", // count on kill record id flag
                $"EndUnconditionally(EventEndType.End);"
            };

            for (int i = 0; i < spawnHandlerEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(spawnHandlerEventRaw[i], i);
                spawnHandler.Parameters.AddRange(newPs);
                spawnHandler.Instructions.Add(instr);
            }

            func.Events.Add(spawnHandler);
            events.Add(Event.SpawnHandler, spawnEventFlag.id);

            /* Create an event for handling npc halting */
            Flag haltEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:Halt");
            EMEVD.Event haltEvent = new(haltEventFlag.id);

            pc = 0;

            string[] haltEventRaw = new string[]
            {
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",  // if npc is dead ...
                $"EndUnconditionally(EventEndType.End);",                                           // kill event

                $"IfEntityInoutsideRadiusOfEntity(AND_01, InsideOutsideState.Inside, 10000, {NextParameterName()}, {Const.NPC_HELLO_DIST_IN}, 1);",   // blocking wait distance check for player close enough AND ...
                $"IfEventFlag(AND_01, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",                                                  // ... blocking wait until hostile flag is off
                $"IfConditionGroup(MAIN, PASS, AND_01);",
                $"SetCharacterAIState({NextParameterName()}, Disabled);",                                                                          // disable ai
                $"RotateCharacter({NextParameterName()}, 10000, -1, false)",                                                                      // rotate to face player

                $"IfEntityInoutsideRadiusOfEntity(OR_01, InsideOutsideState.Outside, 10000, {NextParameterName()}, {Const.NPC_HELLO_DIST_OUT}, 1);",  // blocking wait distance check for player far enough OR...
                $"IfEventFlag(OR_01, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",                                                    // ... blocking wait until hostile flag is on
                $"IfConditionGroup(MAIN, PASS, OR_01);",
                $"SetCharacterAIState({NextParameterName()}, Enabled);",                            // enable ai
                $"RequestCharacterAIReplan({NextParameterName()});",                               // make brain work good

                $"EndUnconditionally(EventEndType.Restart);",     // restart event
            };

            for (int i = 0; i < haltEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(haltEventRaw[i], i);
                haltEvent.Parameters.AddRange(newPs);
                haltEvent.Instructions.Add(instr);
            }

            func.Events.Add(haltEvent);
            events.Add(Event.Halt, haltEventFlag.id);

            /* Create an event for handling creature/npc spawn/respawn and disable/enable */
            Flag spawnHandlerWithDisableEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:SpawnHandlerDisableable");
            EMEVD.Event spawnHandlerWithDisableEvent = new(spawnHandlerWithDisableEventFlag.id);

            pc = 0;

            string[] spawnHandlerWithDisableRaw = new string[]
            {
                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",   // if dead flag is on disable and end event
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",   // if disable flag is on ...
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",                     // disable character

                $"IfCharacterHPValue(MAIN, {NextParameterName()}, 5, 0, 0, 1);", // check if hp is less or equal to 0. comparison values are in byte format so 5 is <= and 4 is >=
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, ON);",  // set dead
                $"IncrementEventValue({NextParameterName()}, {NextParameterName()}, {NextParameterName()});", // count on kill record id flag
                $"EndUnconditionally(EventEndType.End);"
            };

            for (int i = 0; i < spawnHandlerWithDisableRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(spawnHandlerWithDisableRaw[i], i);
                spawnHandlerWithDisableEvent.Parameters.AddRange(newPs);
                spawnHandlerWithDisableEvent.Instructions.Add(instr);
            }

            func.Events.Add(spawnHandlerWithDisableEvent);
            events.Add(Event.SpawnHandlerDisableable, spawnHandlerWithDisableEventFlag.id);

            /* Create an event for handling creature/npc spawn/respawn and disable/enable */
            Flag spawnHandlerPhasedEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:SpawnHandlerPhased");
            EMEVD.Event spawnHandlerPhasedEvent = new(spawnHandlerPhasedEventFlag.id);

            pc = 0;

            string[] spawnHandlerPhasedRaw = new string[]
            {
                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",   // if dead flag is on disable and end event
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"ChangeCharacterEnableState({NextParameterName()}, 0);",                                                  // phased character starts disabled
                $"IfEventFlag(AND_01, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",                       // not disabled AND...
                $"IfEventValue(AND_01, {NextParameterName()}, {NextParameterName()}, 0, {NextParameterName()});",        // phase value matches this npcs phase
                $"IfConditionGroup(MAIN, PASS, AND_01);",                                                               // blocking wait...
                $"ChangeCharacterEnableState({NextParameterName()}, 1);",                                              // enable phased character

                $"IfCharacterHPValue(MAIN, {NextParameterName()}, 5, 0, 0, 1);", // check if hp is less or equal to 0. comparison values are in byte format so 5 is <= and 4 is >=
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, ON);",  // set dead
                $"IncrementEventValue({NextParameterName()}, {NextParameterName()}, {NextParameterName()});", // count on kill record id flag
                $"EndUnconditionally(EventEndType.End);"
            };

            for (int i = 0; i < spawnHandlerPhasedRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(spawnHandlerPhasedRaw[i], i);
                spawnHandlerPhasedEvent.Parameters.AddRange(newPs);
                spawnHandlerPhasedEvent.Instructions.Add(instr);
            }

            func.Events.Add(spawnHandlerPhasedEvent);
            events.Add(Event.SpawnHandlerPhased, spawnHandlerPhasedEventFlag.id);

            /* Create an event for handling creature/npc spawn/respawn in interiors */
            Flag intSpawnHandlerEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:IntSpawnHandler");
            EMEVD.Event intSpawnHandlerEvent = new(intSpawnHandlerEventFlag.id);

            pc = 0;

            string[] intSpawnHandlerEventRaw = new string[]
            {
                $"SkipIfInoutsideArea(2, InsideOutsideState.Inside, 10000, {NextParameterName()}, 1);", // check if inside cell, disable and exit if not
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",   // check dead flag
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"IfCharacterHPValue(MAIN, {NextParameterName()}, 5, 0, 0, 1);", // check if hp is less or equal to 0. comparison values are in byte format so 5 is <= and 4 is >=
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, ON);",  // set dead
                $"IncrementEventValue({NextParameterName()}, {NextParameterName()}, {NextParameterName()});", // count on kill record id flag
                $"EndUnconditionally(EventEndType.End);"
            };

            for (int i = 0; i < intSpawnHandlerEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(intSpawnHandlerEventRaw[i], i);
                intSpawnHandlerEvent.Parameters.AddRange(newPs);
                intSpawnHandlerEvent.Instructions.Add(instr);
            }

            func.Events.Add(intSpawnHandlerEvent);
            events.Add(Event.IntSpawnHandler, intSpawnHandlerEventFlag.id);

            /* Create an event for handling creature/npc spawn/respawn and disable/enable */
            Flag intSpawnHandlerWithDisableEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:IntSpawnHandlerDisableable");
            EMEVD.Event intSpawnHandlerWithDisableEvent = new(intSpawnHandlerWithDisableEventFlag.id);

            pc = 0;

            string[] intSpawnHandlerWithDisableRaw = new string[]
            {
                $"SkipIfInoutsideArea(2, InsideOutsideState.Inside, 10000, {NextParameterName()}, 1);", // check if inside cell, disable and exit if not
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",   // if dead flag is on disable and end event
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",   // if disable flag is on ...
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",                     // disable character

                $"IfCharacterHPValue(MAIN, {NextParameterName()}, 5, 0, 0, 1);", // check if hp is less or equal to 0. comparison values are in byte format so 5 is <= and 4 is >=
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, ON);",  // set dead
                $"IncrementEventValue({NextParameterName()}, {NextParameterName()}, {NextParameterName()});", // count on kill record id flag
                $"EndUnconditionally(EventEndType.End);"
            };

            for (int i = 0; i < intSpawnHandlerWithDisableRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(intSpawnHandlerWithDisableRaw[i], i);
                intSpawnHandlerWithDisableEvent.Parameters.AddRange(newPs);
                intSpawnHandlerWithDisableEvent.Instructions.Add(instr);
            }

            func.Events.Add(intSpawnHandlerWithDisableEvent);
            events.Add(Event.IntSpawnHandlerDisableable, intSpawnHandlerWithDisableEventFlag.id);

            /* Create an event for handling creature/npc spawn/respawn and disable/enable */
            Flag intSpawnHandlerPhasedEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:IntSpawnHandlerPhased");
            EMEVD.Event intSpawnHandlerPhasedEvent = new(intSpawnHandlerPhasedEventFlag.id);

            pc = 0;

            string[] intSpawnHandlerPhasedRaw = new string[]
            {
                $"SkipIfInoutsideArea(2, InsideOutsideState.Inside, 10000, {NextParameterName()}, 1);", // check if inside cell, disable and exit if not
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",   // if dead flag is on disable and end event
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"ChangeCharacterEnableState({NextParameterName()}, 0);",                                                  // phased character starts disabled
                $"IfEventFlag(AND_01, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",                       // not disabled AND...
                $"IfEventValue(AND_01, {NextParameterName()}, {NextParameterName()}, 0, {NextParameterName()});",        // phase value matches this npcs phase
                $"IfConditionGroup(MAIN, PASS, AND_01);",                                                               // blocking wait...
                $"ChangeCharacterEnableState({NextParameterName()}, 1);",                                              // enable phased character

                $"IfCharacterHPValue(MAIN, {NextParameterName()}, 5, 0, 0, 1);", // check if hp is less or equal to 0. comparison values are in byte format so 5 is <= and 4 is >=
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, ON);",  // set dead
                $"IncrementEventValue({NextParameterName()}, {NextParameterName()}, {NextParameterName()});", // count on kill record id flag
                $"EndUnconditionally(EventEndType.End);"
            };

            for (int i = 0; i < intSpawnHandlerPhasedRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(intSpawnHandlerPhasedRaw[i], i);
                intSpawnHandlerPhasedEvent.Parameters.AddRange(newPs);
                intSpawnHandlerPhasedEvent.Instructions.Add(instr);
            }

            func.Events.Add(intSpawnHandlerPhasedEvent);
            events.Add(Event.IntSpawnHandlerPhased, intSpawnHandlerPhasedEventFlag.id);

            /* Create an event for handling friendly npc hostility */
            Flag hostileEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:NpcHostilityHandler");
            EMEVD.Event hostileEvent = new(hostileEventFlag.id);

            pc = 0;

            string[] hostileEventRaw = new string[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});", 
                $"SetCharacterTeamType({NextParameterName()}, 27);",   // hostile flag on, hostile   >:(     // 27: TeamType.HostileNPC
                $"RequestCharacterAIReplan({NextParameterName()});",  // replan so we realize we are now enemies
                $"IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",
                $"SetCharacterTeamType({NextParameterName()}, 26);",   // hostile flag off, friendly :D       //  26: TeamType.FriendlyNPC
                $"RequestCharacterAIReplan({NextParameterName()});",  // replan so we realize we are now friends
                $"EndUnconditionally(EventEndType.Restart);",    // restart because it's possible for this to happen more than once
            };

            for (int i = 0; i < hostileEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(hostileEventRaw[i], i);
                hostileEvent.Parameters.AddRange(newPs);
                hostileEvent.Instructions.Add(instr);
            }

            func.Events.Add(hostileEvent);
            events.Add(Event.NpcHostilityHandler, hostileEventFlag.id);

            /* Create an event for handling messages */
            Flag messageEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:Message");
            EMEVD.Event messageEvent = new(messageEventFlag.id);

            pc = 0;

            string[] messageEventRaw = new string[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",  // wait for flag to trigger this popup to be set to true
                $"ShowTutorialPopup({NextParameterName()}, true, true);",   // show popup
                $"SetEventFlag(0, {NextParameterName()}, OFF)",              // set flag back to false
                $"EndUnconditionally(EventEndType.Restart);",    // restart so it's ready to go again if needed
            };

            for (int i = 0; i < messageEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(messageEventRaw[i], i);
                messageEvent.Parameters.AddRange(newPs);
                messageEvent.Instructions.Add(instr);
            }

            func.Events.Add(messageEvent);
            events.Add(Event.Message, messageEventFlag.id);

            /* Create an event for displaying a message when the player kills an essential npc */
            Flag essentialEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:Essential");
            EMEVD.Event essentialEvent = new(essentialEventFlag.id);

            pc = 0;

            string[] essentialEventRaw = new string[]
            {
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",    // if npc is already dead...
                $"EndUnconditionally(EventEndType.End);",                                            // end event
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",    // otherwise blocking wait for the dead flag to change
                $"ShowTutorialPopup({NextParameterName()}, true, true);",                          // then let the player know he's fucked
            };

            for (int i = 0; i < essentialEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(essentialEventRaw[i], i);
                essentialEvent.Parameters.AddRange(newPs);
                essentialEvent.Instructions.Add(instr);
            }

            func.Events.Add(essentialEvent);
            events.Add(Event.Essential, essentialEventFlag.id);

            /* Create an event for intitializing dead bodys. To be specific, any NPC in morrowind that has the "dead" flag and is just a lootable body */
            Flag deadBodyEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:DeadBody");
            EMEVD.Event deadBodyEvent = new(deadBodyEventFlag.id);

            pc = 0;

            string[] deadBodyEventRaw = new string[]
            {
                $"ForceAnimationPlayback({NextParameterName()}, 90100, false, false, false, 0, 1);",    // laying on ground dead animation (0 is Equals)
                $"ChangeCharacterCollisionState({NextParameterName()}, Disabled);",    // no-collide
                $"SetCharacterTeamType({NextParameterName()}, 26);",               // friendly npc team = 26
            };

            for (int i = 0; i < deadBodyEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(deadBodyEventRaw[i], i);
                deadBodyEvent.Parameters.AddRange(newPs);
                deadBodyEvent.Instructions.Add(instr);
            }

            func.Events.Add(deadBodyEvent);
            events.Add(Event.DeadBody, deadBodyEventFlag.id);

            /* Create an event for making itemcontent assets placed on the map dissapear when the item is actually taken by the player */
            Flag itemAssetWithDisableEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:ItemAssetWithDisable");
            EMEVD.Event itemAssetWithDisableEvent = new(itemAssetWithDisableEventFlag.id);

            pc = 0;

            string[] itemAssetWithDisableEventRaw = new string[]
            {
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",   // if disable flag is on ...
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",                     // disable static
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",
                $"ChangeAssetEnableState({NextParameterName()}, 0);"
            };

            for (int i = 0; i < itemAssetWithDisableEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(itemAssetWithDisableEventRaw[i], i);
                itemAssetWithDisableEvent.Parameters.AddRange(newPs);
                itemAssetWithDisableEvent.Instructions.Add(instr);
            }

            func.Events.Add(itemAssetWithDisableEvent);
            events.Add(Event.ItemAssetWithDisable, itemAssetWithDisableEventFlag.id);

            /* Same as above but also triggers a crime on the player when the item is taken */
            Flag ownedItemAssetWithDisableEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:OwnedItemAssetWithDisable");
            EMEVD.Event ownedItemAssetWithDisableEvent = new(ownedItemAssetWithDisableEventFlag.id);

            pc = 0;

            string[] ownedItemAssetWithDisableEventRaw = new string[]
            {
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",   // if disable flag is on ...
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",                     // disable static

                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",  // if item is already taken
                $"ChangeAssetEnableState({NextParameterName()}, 0);",                              // hide asset
                $"EndUnconditionally(EventEndType.End);",                                      // end event early to preven crime retriggering

                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",    // wait till item picked up
                $"ChangeAssetEnableState({NextParameterName()}, 0);",                               // hide asset
                $"SkipIfEventFlag(3, ON, TargetEventFlagType.EventFlag, {NextParameterName()});", // skip if the owner is dead
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, ON);", // flag this crime as thievery
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, ON);", // flag crime comitted
                $"EventValueOperation({NextParameterName()}, {NextParameterName()}, {NextParameterName()}, 0, 1, 0);", // add to bounty (last 0 is ADD operation type)
                $"SetSpEffect(10000, {(int)SpeffManager.Functional.Alarming});"           // add alarming speff to player since they did a crime
            };

            for (int i = 0; i < ownedItemAssetWithDisableEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(ownedItemAssetWithDisableEventRaw[i], i);
                ownedItemAssetWithDisableEvent.Parameters.AddRange(newPs);
                ownedItemAssetWithDisableEvent.Instructions.Add(instr);
            }

            func.Events.Add(ownedItemAssetWithDisableEvent);
            events.Add(Event.OwnedItemAssetWithDisable, ownedItemAssetWithDisableEventFlag.id);


            /* Create an event for making itemcontent assets placed on the map dissapear when the item is actually taken by the player */
            Flag itemAssetEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:ItemAsset");
            EMEVD.Event itemAssetEvent = new(itemAssetEventFlag.id);

            pc = 0;

            string[] itemAssetEventRaw = new string[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",
                $"ChangeAssetEnableState({NextParameterName()}, 0);"
            };

            for (int i = 0; i < itemAssetEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(itemAssetEventRaw[i], i);
                itemAssetEvent.Parameters.AddRange(newPs);
                itemAssetEvent.Instructions.Add(instr);
            }

            func.Events.Add(itemAssetEvent);
            events.Add(Event.ItemAsset, itemAssetEventFlag.id);

            /* Same as above but also triggers a crime on the player when the item is taken */
            Flag ownedItemAssetEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:OwnedItemAsset");
            EMEVD.Event ownedItemAssetEvent = new(ownedItemAssetEventFlag.id);

            pc = 0;

            string[] ownedItemAssetEventRaw = new string[]
            {
                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",  // if item is already taken
                $"ChangeAssetEnableState({NextParameterName()}, 0);",                              // hide asset
                $"EndUnconditionally(EventEndType.End);",                                      // end event early to preven crime retriggering

                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",    // wait till item picked up
                $"ChangeAssetEnableState({NextParameterName()}, 0);",                               // hide asset
                $"SkipIfEventFlag(3, ON, TargetEventFlagType.EventFlag, {NextParameterName()});", // skip if the owner is dead
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, ON);", // flag this crime as thievery
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, ON);", // flag crime comitted
                $"EventValueOperation({NextParameterName()}, {NextParameterName()}, {NextParameterName()}, 0, 1, 0);", // add to bounty (last 0 is ADD operation type)
                $"SetSpEffect(10000, {(int)SpeffManager.Functional.Alarming});"           // add alarming speff to player since they did a crime
            };

            for (int i = 0; i < ownedItemAssetEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(ownedItemAssetEventRaw[i], i);
                ownedItemAssetEvent.Parameters.AddRange(newPs);
                ownedItemAssetEvent.Instructions.Add(instr);
            }

            func.Events.Add(ownedItemAssetEvent);
            events.Add(Event.OwnedItemAsset, ownedItemAssetEventFlag.id);

            /* Same as above but for containers instead of items */
            Flag ownedContainerEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:OwnedContainer");
            EMEVD.Event ownedContainerEvent = new(ownedContainerEventFlag.id);

            pc = 0;

            string[] ownedContainerEventRaw = new string[]
            {
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",  // if continer is already looted 
                $"EndUnconditionally(EventEndType.End);",                                      // end event early to prevent crime retriggering
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",    // wait till container is looted
                $"SkipIfEventFlag(3, ON, TargetEventFlagType.EventFlag, {NextParameterName()});", // skip if the owner is dead
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, ON);", // flag this crime as thievery
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, ON);", // flag crime comitted
                $"EventValueOperation({NextParameterName()}, {NextParameterName()}, {NextParameterName()}, 0, 1, 0);", // add to bounty (last 0 is ADD operation type)
            };

            for (int i = 0; i < ownedContainerEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(ownedContainerEventRaw[i], i);
                ownedContainerEvent.Parameters.AddRange(newPs);
                ownedContainerEvent.Instructions.Add(instr);
            }

            func.Events.Add(ownedContainerEvent);
            events.Add(Event.OwnedContainer, ownedContainerEventFlag.id);

            /* Create an event for travel npc warps */
            Flag travelWarpEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:TravelWarp");
            EMEVD.Event travelWarpEvent = new(travelWarpEventFlag.id);

            pc = 0;

            string[] travelWarpEventRaw = new string[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",
                $"WarpPlayer({NextParameterName()}, {NextParameterName()}, {NextParameterName()}, {NextParameterName()}, {NextParameterName()}, -1);"
            };

            for (int i = 0; i < travelWarpEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(travelWarpEventRaw[i], i);
                travelWarpEvent.Parameters.AddRange(newPs);
                travelWarpEvent.Instructions.Add(instr);
            }

            func.Events.Add(travelWarpEvent);
            events.Add(Event.TravelWarp, travelWarpEventFlag.id);

            /* Create an event for removing an item from the player (you cant do this from ESD so a trigger event like this is fine */
            Flag removeItemEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:RemoveItem");
            EMEVD.Event removeItemEvent = new(removeItemEventFlag.id);

            pc = 0;

            string[] removeItemEventRaw = new string[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",
                $"RemoveItemFromPlayer({NextParameterName()}, {NextParameterName()}, {NextParameterName()});",
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, OFF);",
                $"EndUnconditionally(EventEndType.Restart);",    // restart so it's ready to go again if needed
            };

            for (int i = 0; i < removeItemEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(removeItemEventRaw[i], i);
                removeItemEvent.Parameters.AddRange(newPs);
                removeItemEvent.Instructions.Add(instr);
            }

            func.Events.Add(removeItemEvent);
            events.Add(Event.RemoveItem, removeItemEventFlag.id);

            /* Create an event for handling a permanent speff on the player */
            Flag permanentSpeffEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:PermanentSpeff");
            EMEVD.Event permanentSpeffEvent = new(permanentSpeffEventFlag.id);

            pc = 0;

            string[] permanentSpeffEventRaw = new string[]
            {
                $"IfEventFlag(AND_01, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",    // if flag is true
                $"IfCharacterHasSpEffect(AND_01, 10000, {NextParameterName()}, false, 0, 1);",        // and player does not have the speff
                $"IfConditionGroup(MAIN, PASS, AND_01);",
                $"SetSpEffect(10000, {NextParameterName()});",   // add speff to player
                $"IfEventFlag(AND_01, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",    // if flag is false
                $"IfCharacterHasSpEffect(AND_01, 10000, {NextParameterName()}, true, 0, 1);",        // and player does have the speff
                $"IfConditionGroup(MAIN, PASS, AND_01);",
                $"ClearSpEffect(10000, {NextParameterName()});",   // remove speff from player
                $"EndUnconditionally(EventEndType.Restart);",    // restart so it's ready to go again if needed
            };

            for (int i = 0; i < permanentSpeffEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(permanentSpeffEventRaw[i], i);
                permanentSpeffEvent.Parameters.AddRange(newPs);
                permanentSpeffEvent.Instructions.Add(instr);
            }

            func.Events.Add(permanentSpeffEvent);
            events.Add(Event.PermanentSpeff, permanentSpeffEventFlag.id);

            /* Create an event for handling startcombat and stopcombat calls */
            Flag npcInfightEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:NpcInfight");
            EMEVD.Event npcInfightEvent = new(npcInfightEventFlag.id);

            pc = 0;

            string[] npcInfightEventRaw = new string[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",
                $"SetCharacterTeamType({NextParameterName()}, 29);",   // hostile flag on, hostile   >:(     // 29: TeamType.Indiscriminate
                $"SetSpEffect({NextParameterName()}, {(int)SpeffManager.Functional.VoidMurder});",
                $"IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",
                $"SetCharacterTeamType({NextParameterName()}, 26);",  // hostile flag off, friendly :D       //  26: TeamType.FriendlyNPC
                $"ClearSpEffect({NextParameterName()}, {(int)SpeffManager.Functional.VoidMurder});",
                $"EndUnconditionally(EventEndType.Restart);",    // restart because it's possible for this to happen more than once
            };

            for (int i = 0; i < npcInfightEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(npcInfightEventRaw[i], i);
                npcInfightEvent.Parameters.AddRange(newPs);
                npcInfightEvent.Instructions.Add(instr);
            }

            func.Events.Add(npcInfightEvent);
            events.Add(Event.NpcInfight, npcInfightEventFlag.id);

            /* Create an event for handling a permanent mod stat on an npcs */
            Flag npcModStatEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:NpcInFight");
            EMEVD.Event npcModStatEvent = new(npcModStatEventFlag.id);

            pc = 0;

            string[] npcModStatEventRaw = new string[]
            {
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});", // if flag is on...
                $"SetSpEffect({NextParameterName()}, {NextParameterName()});",                    // apply speff to npc
                $"EndUnconditionally(EventEndType.End);",                                        // and that's all!
            };

            for (int i = 0; i < npcModStatEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(npcModStatEventRaw[i], i);
                npcModStatEvent.Parameters.AddRange(newPs);
                npcModStatEvent.Instructions.Add(instr);
            }

            func.Events.Add(npcModStatEvent);
            events.Add(Event.NpcModStat, npcModStatEventFlag.id);

            /* Create an event for handling disable/enable of statics */
            Flag staticDisableEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:StaticDisable");
            EMEVD.Event staticDisableEvent = new(staticDisableEventFlag.id);

            pc = 0;

            string[] staticDisableEventRaw = new string[]
            {
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",   // if disable flag is on ...
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",                     // disable static
            };

            for (int i = 0; i < staticDisableEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(staticDisableEventRaw[i], i);
                staticDisableEvent.Parameters.AddRange(newPs);
                staticDisableEvent.Instructions.Add(instr);
            }

            func.Events.Add(staticDisableEvent);
            events.Add(Event.StaticDisable, staticDisableEventFlag.id);

            /* Create an event for playing an SE. Used by ESD to trigger sound effects */
            Flag playSEEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:PlaySE");
            EMEVD.Event playSEEvent = new(playSEEventFlag.id);

            pc = 0;

            string[] playSEEventRaw = new string[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",      // if play sound flag is set
                $"PlaySE({NextParameterName()}, {NextParameterName()}, {NextParameterName()});",      // play sound
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, OFF);",          // turn flag back off
                $"EndUnconditionally(EventEndType.Restart);"     // restart!
            };

            for (int i = 0; i < playSEEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(playSEEventRaw[i], i);
                playSEEvent.Parameters.AddRange(newPs);
                playSEEvent.Instructions.Add(instr);
            }

            func.Events.Add(playSEEvent);
            events.Add(Event.PlaySE, playSEEventFlag.id);

            /* Create an event for esd to trigger an object enable*/
            Flag triggerEnableEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:TriggerEnable");
            EMEVD.Event triggerEnableEvent = new(triggerEnableEventFlag.id);

            pc = 0;

            string[] triggerEnableEventRaw = new string[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",        // blocking wait until flag set...
                $"ChangeCharacterEnableState({NextParameterName()}, Enabled);",                        // enable object
                $"ChangeAssetEnableState({NextParameterName()}, Enabled);",                           // @TODO: Fuck ass hack. please seperate functions for character/asset
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, OFF);",         // turn flag back off
                $"EndUnconditionally(EventEndType.Restart);"     // restart!
            };

            for (int i = 0; i < triggerEnableEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(triggerEnableEventRaw[i], i);
                triggerEnableEvent.Parameters.AddRange(newPs);
                triggerEnableEvent.Instructions.Add(instr);
            }

            func.Events.Add(triggerEnableEvent);
            events.Add(Event.TriggerEnable, triggerEnableEventFlag.id);

            /* Create an event for esd to trigger an object disable*/
            Flag triggerDisableEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:TriggerDisable");
            EMEVD.Event triggerDisableEvent = new(triggerDisableEventFlag.id);

            pc = 0;

            string[] triggerDisableEventRaw = new string[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",        // blocking wait until flag set...
                $"ChangeCharacterEnableState({NextParameterName()}, Disabled);",                        // disable object
                $"ChangeAssetEnableState({NextParameterName()}, Disabled);",                           // @TODO: Fuck ass hack. please seperate functions for character/asset
                $"SetEventFlag(TargetEventFlagType.EventFlag, {NextParameterName()}, OFF);",         // turn flag back off
                $"EndUnconditionally(EventEndType.Restart);"     // restart!
            };

            for (int i = 0; i < triggerDisableEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(triggerDisableEventRaw[i], i);
                triggerDisableEvent.Parameters.AddRange(newPs);
                triggerDisableEvent.Instructions.Add(instr);
            }

            func.Events.Add(triggerDisableEvent);
            events.Add(Event.TriggerDisable, triggerDisableEventFlag.id);

            /* Create event for emulating the GetSecondsPassed papyrus call */
            Flag getSecondsPassedFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:GetSecondsPassed");
            EMEVD.Event getSecondsPassed = new(getSecondsPassedFlag.id);

            pc = 0;

            string[] getSecondsPassedRaw = new string[]
            {
                $"WaitFixedTimeSeconds(1);", // wait 1 second
                $"EventValueOperation({NextParameterName()}, {NextParameterName()}, 1, 0, 1, 0);", // increment timer by 1
                $"EndUnconditionally(EventEndType.Restart);"     // restart!
            };

            for (int i = 0; i < getSecondsPassedRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(getSecondsPassedRaw[i], i);
                getSecondsPassed.Parameters.AddRange(newPs);
                getSecondsPassed.Instructions.Add(instr);
            }

            func.Events.Add(getSecondsPassed);
            events.Add(Event.GetSecondsPassed, getSecondsPassedFlag.id);
        }

        /* Register a tutorial popup message with given text */
        /* Returns a flag that when set to true shows the message */
        /* Stores a mapping of texthashes to prevent duplicates. */
        public Flag GetOrRegisterMessage(Paramanager paramanager, string title, string text)
        {
            int textHash = (title+text).GetHashCode();
            if (messages.ContainsKey(textHash)) { return messages[textHash]; }

            Flag messageFlag = CreateFlag(Flag.Category.Temporary, Flag.Type.Bit, Flag.Designation.Message, text);
            int param = paramanager.GenerateMessage(title, text);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeCommonEvent(0, {events[ScriptCommon.Event.Message]}, {messageFlag.id}, {param}, {messageFlag.id});"));
            messages.Add(textHash, messageFlag);
            return messageFlag;
        }

        /* Register a right side of the screen non pausing message with given text */
        /* Returns a flag that when set to true shows the notification */
        /* Stores a mapping of texthashes to prevent duplicates. */
        public Flag GetOrRegisterNotification(Paramanager paramanager, string text)
        {
            int textHash = text.GetHashCode();
            if (messages.ContainsKey(textHash)) { return messages[textHash]; }

            Flag messageFlag = CreateFlag(Flag.Category.Temporary, Flag.Type.Bit, Flag.Designation.Message, text);
            int param = paramanager.GenerateNotification(text);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeCommonEvent(0, {events[ScriptCommon.Event.Message]}, {messageFlag.id}, {param}, {messageFlag.id});"));
            messages.Add(textHash, messageFlag);
            return messageFlag;
        }

        /* Create an event for travel npcs to warp the player to a specific location. Returns the flag that when set to ON will trigger this event */
        public Flag GetOrRegisterTravelWarp(CharacterContent.Travel travel)
        {
            string flagName = $"{travel.name}:{(int)travel.position.X},{(int)travel.position.X}";
            Flag warpToFlag = manager.GetFlag(Designation.TravelWarp, flagName);
            if (warpToFlag != null) { return warpToFlag; }

            warpToFlag = CreateFlag(Category.Temporary, Flag.Type.Bit, Designation.TravelWarp, flagName);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeCommonEvent(0, {events[ScriptCommon.Event.TravelWarp]}, {warpToFlag.id}, {travel.map}, {travel.x}, {travel.y}, {travel.block}, {travel.entity});"));
            return warpToFlag;
        }

        /* Create an event for removing an item from the player */
        public Flag GetOrRegisterRemoveItem(ItemManager.ItemInfo itemInfo, int quantity)
        {
            string flagName = $"{itemInfo.type}:{itemInfo.row}:{quantity}";
            Flag removeItemFlag = manager.GetFlag(Designation.RemoveItem, flagName);
            if (removeItemFlag != null) { return removeItemFlag; }

            removeItemFlag = CreateFlag(Category.Temporary, Flag.Type.Bit, Designation.RemoveItem, flagName);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeCommonEvent(0, {events[ScriptCommon.Event.RemoveItem]}, {removeItemFlag.id}, {(int)itemInfo.type}, {(int)itemInfo.row}, {quantity}, {removeItemFlag.id});"));
            return removeItemFlag;
        }

        /* Handler that maintains a permanent SPEFF on the player. Used for things that persist like Diseases or Abilities */
        public Flag CreatePermanentSpeff(SpeffManager.SpeffSpell spell)
        {
            Script.Flag speffFlag = CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.PermanentSpeff, spell.id);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeCommonEvent(0, {events[ScriptCommon.Event.PermanentSpeff]}, {speffFlag.id}, {spell.row}, {spell.row}, {speffFlag.id}, {spell.row}, {spell.row});"));
            return speffFlag;
        }

        /* Return a Random papyrus call handler */
        public Flag GetOrRegisterRandom(int max)
        {
            Script.Flag randomFlag = manager.GetFlag(Designation.Random, max.ToString());
            if (randomFlag != null) { return randomFlag; }
            randomFlag = CreateFlag(Category.Temporary, Type.Short, Designation.Random, max.ToString());

            EMEVD.Event randomEvent = new();
            Flag randomEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"RandomHandlerEvent");
            randomEvent.ID = randomEventFlag.id;

            List<int> randomValues = Enumerable.Range(0, max).ToList();
            randomValues.Shuffle();

            foreach (int i in randomValues) {
                randomEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({randomFlag.id}, {randomFlag.Bits()}, {i}, 0, 1, 5);")); // assign random value to flag
                randomEvent.Instructions.Add(AUTO.ParseAdd($"WaitFixedTimeFrames(1);"));  // wait 1 frame then repeat
            }
            randomEvent.Instructions.Add(AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));  // restart

            emevd.Events.Add(randomEvent);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeEvent(0, {randomEvent.ID}, 0);"));

            return randomFlag;
        }

        /* Create a fixed common event that handles the players ability to use the crafting menu based on what alchemy equipment they have */
        public void CreateAlchemyHandler(List<ItemManager.ItemInfo> items)
        {
            EMEVD.Event alchemyEvent = new();
            Flag alchemyEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"AlchemyHandlerEvent");
            alchemyEvent.ID = alchemyEventFlag.id;

            alchemyEvent.Instructions.Add(AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, 60120, OFF);"));  // initialize as crafting disabled

            foreach(ItemManager.ItemInfo item in items)
            {
                alchemyEvent.Instructions.Add(AUTO.ParseAdd($"IfPlayerHasdoesntHaveItem(OR_01, ItemType.Goods, {item.row}, OwnershipState.Owns);"));  // does player have alchemy tool
                alchemyEvent.Instructions.Add(AUTO.ParseAdd($"SkipIfConditionGroupStateUncompiled(1, FAIL, OR_01);"));
                alchemyEvent.Instructions.Add(AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, 60120, ON);"));                    // if they do then enable crafting
                alchemyEvent.Instructions.Add(AUTO.ParseAdd($"IfElapsedSeconds(MAIN, 0);"));                                                 // reset condition group
            }

            alchemyEvent.Instructions.Add(AUTO.ParseAdd($"WaitFixedTimeSeconds(3);"));  // only do this check every few seconds as its not high priority
            alchemyEvent.Instructions.Add(AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));  // restart

            emevd.Events.Add(alchemyEvent);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeEvent(0, {alchemyEvent.ID}, 0);"));
        }

        /* Create time handler. These events track minutes, seconds, days, months, and years */
        /* In order to simplify coding I'm making all months have 30 days */
        public void TimeHandler()
        {
            Flag hour = GetOrCreateFlag(Category.Saved, Type.Short, Designation.Global, "GameHour");
            Flag day = GetOrCreateFlag(Category.Saved, Type.Short, Designation.Global, "Day");
            Flag month = GetOrCreateFlag(Category.Saved, Type.Short, Designation.Global, "Month");
            Flag year = GetOrCreateFlag(Category.Saved, Type.Short, Designation.Global, "Year");
            Flag daysPassedFlag = GetOrCreateFlag(Category.Saved, Type.Short, Designation.Global, "DaysPassed");

            /* Hour handler */
            EMEVD.Event hourEvent = new();
            Flag hourEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"TimeHourEvent");
            hourEvent.ID = hourEventFlag.id;

            for (int i = 0; i < 24; i++)
            {
                hourEvent.Instructions.Add(AUTO.ParseAdd($"IfElapsedSeconds(MAIN, 0);"));                                                 // reset condition group
                hourEvent.Instructions.Add(AUTO.ParseAdd($"IfTimeOfDayInRange(OR_01, {i}, 0, 0, {i}, 59, 59);"));                         // check a time range...
                hourEvent.Instructions.Add(AUTO.ParseAdd($"SkipIfConditionGroupStateUncompiled(1, FAIL, OR_01);"));
                hourEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({hour.id}, {hour.Bits()}, {i}, 0, 1, 5);"));               // set gamehour global value
            }

            hourEvent.Instructions.Add(AUTO.ParseAdd($"WaitFixedTimeSeconds(1);"));  // update once a second
            hourEvent.Instructions.Add(AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));  // restart

            emevd.Events.Add(hourEvent);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeEvent(0, {hourEvent.ID}, 0);"));

            /* Day, month, year handler */
            EMEVD.Event dateEvent = new();
            Flag dateEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"TimeDateEvent");
            dateEvent.ID = dateEventFlag.id;

            dateEvent.Instructions.Add(AUTO.ParseAdd($"IfTimeOfDayInRange(MAIN, 12, 0, 0, 23, 59, 59);"));  // if we are in the latter half of a day
            dateEvent.Instructions.Add(AUTO.ParseAdd($"IfTimeOfDayInRange(MAIN, 0, 0, 0, 11, 59, 59);"));   // and the clock rolls over to 0 (12:00AM~)  // Both of these are blocking waits
            dateEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({day.id}, {day.Bits()}, 1, 0, 1, 0);"));  // a day has passed
            dateEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({daysPassedFlag.id}, {daysPassedFlag.Bits()}, 1, 0, 1, 0);"));

            dateEvent.Instructions.Add(AUTO.ParseAdd($"IfEventValue(OR_01, {day.id}, {day.Bits()}, 2, 29);"));   // if the day is the 30th
            dateEvent.Instructions.Add(AUTO.ParseAdd($"SkipIfConditionGroupStateUncompiled(2, FAIL, OR_01);"));
            dateEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({month.id}, {month.Bits()}, 1, 0, 1, 0);"));  // a month has passed
            dateEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({day.id}, {day.Bits()}, 0, 0, 1, 5);"));  // the day is 0 again

            dateEvent.Instructions.Add(AUTO.ParseAdd($"IfEventValue(OR_02, {month.id}, {month.Bits()}, 2, 11);"));   // if the month is the 12th
            dateEvent.Instructions.Add(AUTO.ParseAdd($"SkipIfConditionGroupStateUncompiled(2, FAIL, OR_02);"));
            dateEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({year.id}, {year.Bits()}, 1, 0, 1, 0);"));  // a year has passed
            dateEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({month.id}, {month.Bits()}, 0, 0, 1, 5);"));  // the month is 0 again

            dateEvent.Instructions.Add(AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));  // restart

            emevd.Events.Add(dateEvent);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeEvent(0, {dateEvent.ID}, 0);"));
        }

        /* Create a simple common event that tracks the current weather and writes it to a flag for dialog filter conditions to read from */
        public enum WeatherEMEVD
        {
            None = -1, Default = 0, Rain = 1, Snow = 2, WindyRain = 3, Fog = 4, Cloudless = 5, FlatClouds = 6, PuffyClouds = 7, RainyClouds = 8, WindyFog = 9, HeavySnow = 10,
            HeavyFog = 11, WindyPuffyClouds = 12, Default2 = 13, Default3 = 14, RainyHeavyFog = 15, SnowyHeavyFog = 16, ScatteredRain = 17, Unknown18 = 18, Unknown19 = 19,
            Unknown20 = 20, Unknown21 = 21, Unknown22 = 22, Unknown23 = 23
        }

        public enum WeatherPapyrus
        {
            Clear = 0, Cloudy = 1, Foggy = 2, Overcast = 3, Rain = 4, Thunder = 5, Ash = 6, Blight = 7, Snow = 8, Blizzard = 9
        }

        public void CreateWeatherTracker()
        {
            EMEVD.Event weatherEvent = new();
            Flag weatherEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"WeatherTracker");
            weatherEvent.ID = weatherEventFlag.id;

            Flag weatherValue = CreateFlag(Category.Temporary, Flag.Type.Byte, Designation.CurrentWeather, "CurrentWeather");

            List<(WeatherEMEVD emevd, WeatherPapyrus papyrus)> weatherRemaps = [
                (WeatherEMEVD.None, WeatherPapyrus.Clear),
                (WeatherEMEVD.Default, WeatherPapyrus.Clear),
                (WeatherEMEVD.Rain, WeatherPapyrus.Rain),
                (WeatherEMEVD.Snow, WeatherPapyrus.Snow),
                (WeatherEMEVD.WindyRain, WeatherPapyrus.Thunder),
                (WeatherEMEVD.Fog, WeatherPapyrus.Foggy),
                (WeatherEMEVD.Cloudless, WeatherPapyrus.Clear),
                (WeatherEMEVD.FlatClouds, WeatherPapyrus.Overcast),
                (WeatherEMEVD.PuffyClouds, WeatherPapyrus.Cloudy),
                (WeatherEMEVD.RainyClouds, WeatherPapyrus.Rain),
                (WeatherEMEVD.WindyFog, WeatherPapyrus.Foggy),
                (WeatherEMEVD.HeavySnow, WeatherPapyrus.Blizzard),
                (WeatherEMEVD.HeavyFog, WeatherPapyrus.Foggy),
                (WeatherEMEVD.WindyPuffyClouds, WeatherPapyrus.Cloudy),
                (WeatherEMEVD.Default2, WeatherPapyrus.Clear),
                (WeatherEMEVD.Default3, WeatherPapyrus.Clear),
                (WeatherEMEVD.RainyHeavyFog, WeatherPapyrus.Thunder),
                (WeatherEMEVD.SnowyHeavyFog, WeatherPapyrus.Blizzard),
                (WeatherEMEVD.ScatteredRain, WeatherPapyrus.Rain),
                (WeatherEMEVD.Unknown18, WeatherPapyrus.Clear),
                (WeatherEMEVD.Unknown19, WeatherPapyrus.Clear),
                (WeatherEMEVD.Unknown20, WeatherPapyrus.Clear),
                (WeatherEMEVD.Unknown21, WeatherPapyrus.Clear),
                (WeatherEMEVD.Unknown22, WeatherPapyrus.Clear),
                (WeatherEMEVD.Unknown23, WeatherPapyrus.Clear),
            ];

            foreach(var remap in weatherRemaps)
            {
                weatherEvent.Instructions.Add(AUTO.ParseAdd($"IfWeatherActive(OR_01, {(int)remap.emevd}, 0, 0);"));         // if weather is active
                weatherEvent.Instructions.Add(AUTO.ParseAdd($"SkipIfConditionGroupStateUncompiled(1, FAIL, OR_01);"));
                weatherEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({weatherValue.id}, {weatherValue.Bits()}, {(int)remap.papyrus}, 0, 1, 5);"));  // set flag value for that weather
                weatherEvent.Instructions.Add(AUTO.ParseAdd($"IfElapsedSeconds(MAIN, 0);"));                                                 // reset condition group
            }

            weatherEvent.Instructions.Add(AUTO.ParseAdd($"WaitFixedTimeSeconds(10);"));  // only do this check every few seconds as its not high priority
            weatherEvent.Instructions.Add(AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));  // restart

            emevd.Events.Add(weatherEvent);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeEvent(0, {weatherEvent.ID}, 0);"));
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

        /* There are some bugs with this system. It defo wastes some flag space. We have lots tho. Maybe fix later */
        private static readonly uint[] COMMON_FLAG_BASES = new uint[]  // using flags from every msb slot along the bottom most edge of the world
        {
            1030290000, 1031290000, 1032290000, 1033290000, 1034290000, 1035290000, 1036290000, 1037290000, 1038290000, 1039290000 // if we run out of flag space it will throw an exception. adding more is easy tho
        };
        private static readonly Dictionary<Flag.Category, uint[]> FLAG_TYPE_OFFSETS = new()
        {
            { Flag.Category.Event, new uint[] { 1000, 3000, 6000 } },
            { Flag.Category.Saved, new uint[] { 0, 4000, 7000, 8000, 9000 } },
            { Flag.Category.Temporary, new uint[] { 2000, 5000 } }
        };

        public override Flag CreateFlag(Flag.Category category, Flag.Type type, Flag.Designation designation, Content content, uint value = 0, bool allowPhased = false)
        {
            if (content is PhasedNpcContent) { throw new System.Exception("Cannot create flags for phased content in ScriptCommon!"); }
            return CreateFlag(category, type, designation, content.entity.ToString(), value);
        }

        public override Flag CreateFlagLocal(Content content, string name, uint value = 0)
        {
            throw new System.Exception("CreateFlagLocal cannot be called from ScriptCommon.cs! Please use Script.CreateFlagLocal()");
        }

        public override Flag CreateFlag(Flag.Category category, Flag.Type type, Flag.Designation designation, string name, uint value = 0)
        {
            /* Cap off a group of 1000 flags if it's near full. For example: This is to prevent us adding a multi bit flag like a byte when there is only 3 flags left */
            uint rawCount = flagUsedCounts[category];
            if ((rawCount % 1000) + ((uint)type) >= 1000)
            {
                flagUsedCounts[category] += 1000 - (rawCount % 1000);
                rawCount = flagUsedCounts[category];
            }

            /* Calculate next flag */
            uint perThou = (rawCount / 1000) % (uint)(FLAG_TYPE_OFFSETS[category].Length);
            uint perMsb = (rawCount / 1000) / (uint)(FLAG_TYPE_OFFSETS[category].Length);
            uint mod = rawCount % 1000;
            uint mapOffset = COMMON_FLAG_BASES[perMsb];
            uint id = mapOffset + FLAG_TYPE_OFFSETS[category][perThou] + mod;
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

        public Flag FindFlagByLookupKey(ScriptFlagLookupKey key)
        {
            return flagsByLookupKey.GetValueOrDefault(key);
        }

        /* Create a unique entity id, this is primarily used as an overflow for other msbs when they run out of room. */
        public uint CreateEntity(EntityType type, string name)
        {
            uint rawCount = entityUsedCounts[type]++;
            uint newid = COMMON_FLAG_BASES[(rawCount / 1000)] + ((uint)type) + rawCount;

            return newid;
        }

        public void Write()
        {
            emevd.Write(Path.Combine(Const.OUTPUT_PATH, "event", "common.emevd.dcx"));
            func.Write(Path.Combine(Const.OUTPUT_PATH, "event", "common_func.emevd.dcx"));
        }
    }
}
