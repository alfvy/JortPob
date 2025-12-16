using ESDLang.Script;
using JortPob.Common;
using SoulsFormats;
using SoulsIds;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static HKLib.hk2018.hknpShape.MassConfig;
using static JortPob.ItemManager;
using static JortPob.NpcContent;
using static JortPob.Papyrus;
using static JortPob.Script;
using static JortPob.Script.Flag;

namespace JortPob
{
    using ScriptFlagLookupKey = (Script.Flag.Designation, string); 

    /* Handles CommonEvent and CommonFunc EMEVD. These are different from map scripts so I decided to give them a seperate class */

    public class ScriptCommon
    {
        public Events AUTO;

        public readonly EMEVD emevd, func;
        public readonly EMEVD.Event init;

        public List<Flag> flags;
        private Dictionary<Flag.Category, uint> flagUsedCounts;
        private Dictionary<EntityType, uint> entityUsedCounts;

        public enum Event
        {
            LoadDoor, SpawnHandler, NpcHostilityHandler, Message, Hello, Essential, DeadBody, ItemAsset, OwnedItemAsset, OwnedContainer, TravelWarp, RemoveItem
        }
        public readonly Dictionary<Event, uint> events;
        public readonly Dictionary<int, Flag> messages;  // hash of message text as key, value is flag that when set to true triggers a message to display

        /**
         * This is just used to speed up searches for flags. It is a 1:1 mapping, so duplicate designated/named
         * flags will result in us just using the first one. This is okay (for now), because that is the same logic
         * that GetFlag already uses.
         */
        private readonly Dictionary<ScriptFlagLookupKey, Flag> flagsByLookupKey;

        public ScriptCommon()
        {
            AUTO = new(Utility.ResourcePath(@"script\\er-common.emedf.json"), true, true);

            emevd = EMEVD.Read(Utility.ResourcePath(@"script\common.emevd.dcx"));
            func = EMEVD.Read(Utility.ResourcePath(@"script\common_func.emevd.dcx"));
            init = emevd.Events[0];

            // Bytes here are raw string data that points to the filenames of common_func and common_macro
            emevd.StringData = new byte[] { 78, 0, 58, 0, 92, 0, 71, 0, 82, 0, 92, 0, 100, 0, 97, 0, 116, 0, 97, 0, 92, 0, 80, 0, 97, 0, 114, 0, 97, 0, 109, 0, 92, 0, 101, 0, 118, 0, 101, 0, 110, 0, 116, 0, 92, 0, 99, 0, 111, 0, 109, 0, 109, 0, 111, 0, 110, 0, 95, 0, 102, 0, 117, 0, 110, 0, 99, 0, 46, 0, 101, 0, 109, 0, 101, 0, 118, 0, 100, 0, 0, 0, 78, 0, 58, 0, 92, 0, 71, 0, 82, 0, 92, 0, 100, 0, 97, 0, 116, 0, 97, 0, 92, 0, 80, 0, 97, 0, 114, 0, 97, 0, 109, 0, 92, 0, 101, 0, 118, 0, 101, 0, 110, 0, 116, 0, 92, 0, 99, 0, 111, 0, 109, 0, 109, 0, 111, 0, 110, 0, 95, 0, 109, 0, 97, 0, 99, 0, 114, 0, 111, 0, 46, 0, 101, 0, 109, 0, 101, 0, 118, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            emevd.LinkedFileOffsets = new() { 0, 82 };

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

            /* Create an event for handling creature/npc spawn/respawn and disable/enable */
            Flag spawnEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:SpawnHandler");
            EMEVD.Event spawnHandler = new(spawnEventFlag.id);

            pc = 0;

            string[] spawnHandlerEventRaw = new string[]
            {
                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",   // check disabled flag
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

            for (int i = 0; i < spawnHandlerEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(spawnHandlerEventRaw[i], i);
                spawnHandler.Parameters.AddRange(newPs);
                spawnHandler.Instructions.Add(instr);
            }

            func.Events.Add(spawnHandler);
            events.Add(Event.SpawnHandler, spawnEventFlag.id);

            /* Create an event for handling friendly npc hostility */
            Flag hostileEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:NpcHostilityHandler");
            EMEVD.Event hostileEvent = new(hostileEventFlag.id);

            pc = 0;

            string[] hostileEventRaw = new string[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});", 
                $"SetCharacterTeamType({NextParameterName()}, 27);",   // hostile flag on, hostile   >:(     // 27: TeamType.HostileNPC
                $"IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",
                $"SetCharacterTeamType({NextParameterName()}, 26);",  // hostile flag off, friendly :D       //  26: TeamType.FriendlyNPC
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

            /* Create an event for handling npc hello turntoplayer from esd */
            Flag helloEventFlag = CreateFlag(Flag.Category.Event, Flag.Type.Bit, Flag.Designation.Event, $"CommonFunc:Hello");
            EMEVD.Event helloEvent = new(helloEventFlag.id);

            pc = 0;

            string[] helloEventRaw = new string[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",  // wait for flag to trigger
                //$"RotateCharacter({NextParameterName()}, 10000, -1, false);",   // turn character to face the player
                $"IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",  // wait for flag to go back to off
                $"EndUnconditionally(EventEndType.Restart);",    // restart so it's ready to go again if needed
            };

            for (int i = 0; i < helloEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(helloEventRaw[i], i);
                helloEvent.Parameters.AddRange(newPs);
                helloEvent.Instructions.Add(instr);
            }

            func.Events.Add(helloEvent);
            events.Add(Event.Hello, helloEventFlag.id);

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
                $"IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, {NextParameterName()});",    // dead flag starts off
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {NextParameterName()});",    // dead flag changes to on. meaning dood is dead
                $"ShowTutorialPopup({NextParameterName()}, true, true);",                          // let the player know he's fucked
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
        public Flag GetOrRegisterTravelWarp(NpcContent.Travel travel)
        {
            string flagName = $"{travel.name}:{(int)travel.position.X},{(int)travel.position.X}";
            Flag warpToFlag = GetFlag(Designation.TravelWarp, flagName);
            if (warpToFlag != null) { return warpToFlag; }

            warpToFlag = CreateFlag(Category.Temporary, Flag.Type.Bit, Designation.TravelWarp, flagName);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeCommonEvent(0, {events[ScriptCommon.Event.TravelWarp]}, {warpToFlag.id}, {travel.map}, {travel.x}, {travel.y}, {travel.block}, {travel.entity});"));
            return warpToFlag;
        }

        /* Create an event for removing an item from the player */
        public Flag GetOrRegisterRemoveItem(ItemManager.ItemInfo itemInfo, int quantity)
        {
            string flagName = $"{itemInfo.type}:{itemInfo.row}:{quantity}";
            Flag removeItemFlag = GetFlag(Designation.RemoveItem, flagName);
            if (removeItemFlag != null) { return removeItemFlag; }

            removeItemFlag = CreateFlag(Category.Temporary, Flag.Type.Bit, Designation.RemoveItem, flagName);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeCommonEvent(0, {events[ScriptCommon.Event.RemoveItem]}, {removeItemFlag.id}, {(int)itemInfo.type}, {(int)itemInfo.row}, {quantity}, {removeItemFlag.id});"));
            return removeItemFlag;
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

        public Script.Flag GetFlag(Designation designation, string name)
        {
            var lookupKey = Script.FormatFlagLookupKey(designation, name.ToLower());

            return FindFlagByLookupKey(lookupKey);
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
        public Flag CreateFlag(Flag.Category category, Flag.Type type, Flag.Designation designation, string name, uint value = 0)
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
            emevd.Write($"{Const.OUTPUT_PATH}\\event\\common.emevd.dcx");
            func.Write($"{Const.OUTPUT_PATH}\\event\\common_func.emevd.dcx");
        }
    }
}
