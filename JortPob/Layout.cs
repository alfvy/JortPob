using JortPob.Common;
using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Numerics;
using static IronPython.Modules._ast;
using static JortPob.InteriorGroup;
using static JortPob.NpcContent;

namespace JortPob
{

    /* Takes the Morrowind ESM cell grid and re-subdivides it into the Elden Ring tile grid */
    public class Layout
    {
        public List<BaseTile> all;
        public List<HugeTile> huges;
        public List<BigTile> bigs;
        public List<Tile> tiles;

        public List<InteriorGroup> interiors;

        public Layout(Cache cache, ESM esm, Paramanager param, TextManager text, ScriptManager scriptManager)
        {
            all = new();
            huges = new();
            bigs = new();
            tiles = new();

            interiors = new();

            /* Generate tiles based off base game msb info... */
            string msbdata = File.ReadAllText(Utility.ResourcePath(@"msb\msblist.txt"));
            string[] msblist = msbdata.Split(";");

            Lort.Log("Generating layout...", Lort.Type.Main);
            Lort.NewTask("Generating Layout", msblist.Length+esm.exterior.Count+esm.interior.Count);

            foreach (string msb in msblist)
            {
                string[] split = msb.Split(",");
                int m = int.Parse(split[0]);
                int x = int.Parse(split[1]);
                int y = int.Parse(split[2]);
                int b = int.Parse(split[3]);

                if(m == 60 && b == 0)
                {
                    Tile tile = new Tile(m, x, y, b);
                    tiles.Add(tile);
                    all.Add(tile);
                }

                Lort.TaskIterate(); // Progress bar update
            }

            /* Generate BigTiles... */
            foreach (string msb in msblist)
            {
                string[] split = msb.Split(",");
                int m = int.Parse(split[0]);
                int x = int.Parse(split[1]);
                int y = int.Parse(split[2]);
                int b = int.Parse(split[3]);

                if (m == 60 && b == 1)
                {
                    BigTile big = new BigTile(m, x, y, b);

                    foreach (Tile tile in tiles)
                    {
                        int x1 = x * 2;
                        int y1 = y * 2;
                        int x2 = x1 + 2;
                        int y2 = y1 + 2;
                        if (tile.coordinate.x >= x1 && tile.coordinate.x < x2 && tile.coordinate.y >= y1 && tile.coordinate.y < y2)
                        {
                            big.AddTile(tile);
                        }
                    }

                    bigs.Add(big);
                    all.Add(big);
                }

                Lort.TaskIterate(); // Progress bar update
            }

            /* Generate HugeTiles... */
            foreach (string msb in msblist)
            {
                string[] split = msb.Split(",");
                int m = int.Parse(split[0]);
                int x = int.Parse(split[1]);
                int y = int.Parse(split[2]);
                int b = int.Parse(split[3]);

                if (m == 60 && b == 2)
                {
                    HugeTile huge = new HugeTile(m, x, y, b);

                    foreach(BigTile big in bigs)
                    {
                        int x1 = x * 2;
                        int y1 = y * 2;
                        int x2 = x1 + 2;
                        int y2 = y1 + 2;
                        if (big.coordinate.x >= x1 && big.coordinate.x < x2 && big.coordinate.y >= y1 && big.coordinate.y < y2)
                        {
                            huge.AddBig(big);
                        }
                    }

                    foreach(Tile tile in tiles)
                    {
                        int x1 = x * 4;
                        int y1 = y * 4;
                        int x2 = x1 + 4;
                        int y2 = y1 + 4;
                        if(tile.coordinate.x >= x1 && tile.coordinate.x < x2 && tile.coordinate.y >= y1 && tile.coordinate.y < y2)
                        {
                            huge.AddTile(tile);
                        }
                    }

                    huges.Add(huge);
                    all.Add(huge);
                }

                Lort.TaskIterate(); // Progress bar update
            }

            /* Generate Interior Groups */
            foreach (string msb in msblist)
            {
                string[] split = msb.Split(",");
                int m = int.Parse(split[0]);
                int a = int.Parse(split[1]);
                int u = int.Parse(split[2]);
                int b = int.Parse(split[3]);

                int[] validMaps = new int[]
                {
                    12, 13, 14, 15, 16, 19, 20, 21, 22, 25, 28, 30, 31, 32, 34, 35, 39, 40, 41, 42, 43
                };

                if (validMaps.Contains(m) && u == 0 && b == 0)
                {
                    InteriorGroup group = new InteriorGroup(m, a, u, b);
                    interiors.Add(group);
                }

                Lort.TaskIterate(); // Progress bar update
            }

            Content EmitterConversionCheck(Content content)
            {
                if(content.GetType() != typeof(AssetContent)) { return content; }
                AssetContent assetContent = content as AssetContent;

                /* If an assetcontent has emitter nodes, we convert it to an emittercontent */
                /* We can't really do this earlier than this point sadly because we need both the ESM loaded an cache built to be able to catch this corner case */
                /* So we do it here */
                ModelInfo modelInfo = cache.GetModel(assetContent.mesh);
                if (!modelInfo.HasEmitter()) { return content; }

                EmitterContent emitterContent = assetContent.ConvertToEmitter();
                cache.AddConvertedEmitter(emitterContent);

                return emitterContent;
            }

            /* Subdivide all cell content into tiles */
            foreach (Cell cell in esm.exterior)
            {
                HugeTile huge = GetHugeTile(cell.center);
                TerrainInfo terrain = cache.GetTerrain(cell.coordinate);
                if (terrain != null)
                {
                    if (huge != null) { huge.AddTerrain(cell.center, terrain); }
                    else { Lort.Log($" ## WARNING ## Terrain fell outside of reality [{cell.coordinate.x}, {cell.coordinate.y}] -- {cell.region} :: B02", Lort.Type.Debug); }
                }

                if (huge != null)
                {
                    huge.AddCell(cell);

                    foreach (Content content in cell.contents)
                    {
                        Content c = EmitterConversionCheck(content); // checks if we need to convert an assetcontent into an emittercontent due to it having emitter nodes but no light data

                        huge.AddContent(cache, cell, c);
                    }
                }
                else { Lort.Log($" ## WARNING ## Cell fell outside of reality [{cell.coordinate.x}, {cell.coordinate.y}] -- {cell.name} :: B02", Lort.Type.Debug); }
                Lort.TaskIterate(); // Progress bar update
            }

            /* Render an ASCII image of the tiles for verification! */
            Lort.Log("Drawing ASCII art of worldspace map...", Lort.Type.Debug);
            for (int y = 28; y < 66; y++)
            {
                string line = "";
                for (int x = 30; x < 64; x++)
                {
                    Tile tile = GetTile(new Int2(x, y));
                    if (tile == null) { line += "-"; }
                    else
                    {
                        line += tile.assets.Count > 0 ? "X" : "~";
                    }
                }
                Lort.Log(line, Lort.Type.Debug);
            }


            /* Subdivide all interior cells into groups */
            int partition = (int)Math.Ceiling(esm.interior.Count / (float)interiors.Count);
            int start = 0, end = partition;
            foreach (InteriorGroup group in interiors)
            {
                for(int i=start; i<Math.Min(end, esm.interior.Count); i++)
                {
                    Cell cell = esm.interior[i];
                    group.AddCell(cell);

                    Lort.TaskIterate(); // Progress bar update
                }

                start += partition;
                end += partition;
            }

            /* Resolve load doors and travel npcs */
            InteriorGroup.Chunk FindChunk(string name) // find a chunk that contains the named cell
            {
                foreach(InteriorGroup group in interiors)
                {
                    foreach (InteriorGroup.Chunk chunk in group.chunks)
                    {
                        if (chunk.cell.name == name) { return chunk; }
                    }
                }

                return null; // may happen if debug options are enabled to build only some cells
            }

            Tile FindTile(Vector3 position) // find a tile based on coords
            {
                foreach (Tile tile in tiles)
                {
                    if(tile.PositionInside(position))
                    {
                        return tile;
                    }
                }

                return null; // may happen if debug options are enabled to build only some cells
            }

            Cell FindCell(Vector3 position)  // getting an ext cell by morrowind coordinates. used to find exterior cell name
            {
                int x = (int)Math.Floor(position.X / Const.CELL_SIZE);
                int y = (int)Math.Floor(position.Z / Const.CELL_SIZE);
                return esm.GetCellByGrid(new Int2(x,y));
            }

            void HandleDoor(DoorContent door)
            {
                if (door.warp != null)
                {
                    // Door goes to interior cell
                    if (door.warp.cell != null)
                    {
                        InteriorGroup.Chunk to = FindChunk(door.warp.cell);
                        if (to == null) { door.warp = null; return; }      // caused by debug sometimes
                        door.warp.map = to.group.map;
                        door.warp.x = to.group.area;
                        door.warp.y = to.group.unk;
                        door.warp.block = to.group.block;
                        door.warp.entity = scriptManager.GetScript(to.group).CreateEntity(Script.EntityType.Region, $"DoorExit::{door.cell.name}->{door.warp.cell}");
                        string areaName;
                        if (to.cell.name.Contains(",")) { areaName = to.cell.name.Split(",")[^1].Trim(); }
                        else { areaName = to.cell.name; }
                        door.warp.prompt = $"Enter {areaName}";
                        to.AddWarp(door.warp);
                    }
                    // Door goes to exterior cell
                    else
                    {
                        Tile to = FindTile(door.warp.position);  // does not respect cell borders in tile msbs. likely a non-issue but kinda sketch ... @TODO:
                        if (to == null) { door.warp = null; return; }     // caused by debug sometimes
                        door.warp.map = to.map;
                        door.warp.x = to.coordinate.x;
                        door.warp.y = to.coordinate.y;
                        door.warp.block = to.block;
                        door.warp.entity = scriptManager.GetScript(to).CreateEntity(Script.EntityType.Region, $"DoorExit::{door.cell.name}->exterior[{door.warp.x},{door.warp.y}]");
                        door.warp.prompt = $"Exit";
                        to.AddWarp(door.warp);
                    }
                }
            }

            void HandleTravel(NpcContent npc, Tile from = null)
            {
                if (npc.travel.Count() > 0)
                {
                    // Travel goes to interior cell
                    for (int i = 0; i < npc.travel.Count; i++)
                    {
                        CharacterContent.Travel travel = npc.travel[i];
                        if (travel.cell != null)
                        {
                            InteriorGroup.Chunk to = FindChunk(travel.cell);
                            if (to == null) { npc.travel.RemoveAt(i--); continue; }      // caused by debug sometimes
                            travel.map = to.group.map;
                            travel.x = to.group.area;
                            travel.y = to.group.unk;
                            travel.block = to.group.block;
                            travel.entity = scriptManager.GetScript(to.group).CreateEntity(Script.EntityType.Region, $"TravelDestination::{npc.cell.name}->{travel.cell}");
                            travel.name = to.cell.name;
                            travel.cost = Const.TRAVEL_DEFAULT_COST;

                            to.AddWarp(travel);
                        }
                        // Travel goes to exterior cell
                        else
                        {
                            Tile to = FindTile(travel.position);  // does not respect cell borders in tile msbs. likely a non-issue but kinda sketch ... @TODO:
                            Cell cell = FindCell(travel.position);
                            if (to == null || cell == null) { npc.travel.RemoveAt(i--); continue; }     // caused by debug sometimes
                            travel.map = to.map;
                            travel.x = to.coordinate.x;
                            travel.y = to.coordinate.y;
                            travel.block = to.block;
                            travel.entity = scriptManager.GetScript(to).CreateEntity(Script.EntityType.Region, $"TravelDestination::{npc.cell.name}->exterior[{travel.x},{travel.y}]");
                            travel.name = cell.name;
                            // calculate distance for cost of travel
                            if (from != null)
                            {
                                Vector2 a = new(from.coordinate.x, from.coordinate.y);
                                Vector2 b = new(to.coordinate.x, to.coordinate.y);
                                travel.cost = (int)(Const.TRAVEL_DISTANCE_COST * Math.Max(1, Vector2.Distance(a, b)));
                            }
                            else { travel.cost = Const.TRAVEL_DEFAULT_COST; }
                                to.AddWarp(travel);
                        }
                    }
                }
            }

            foreach (InteriorGroup group in interiors)
            {
                foreach(InteriorGroup.Chunk chunk in group.chunks)
                {
                    foreach(DoorContent door in chunk.doors)
                    {
                        HandleDoor(door);
                        if(door.warp != null) { door.entity = scriptManager.GetScript(group).CreateEntity(Script.EntityType.Asset, $"DoorEntry::{door.cell.name}->{door.warp.cell}"); }
                    }

                    foreach(NpcContent npc in chunk.npcs)
                    {
                        HandleTravel(npc);
                    }
                }
            }

            foreach (Tile tile in tiles)
            {
                foreach (DoorContent door in tile.doors)
                {
                    HandleDoor(door);
                    if (door.warp != null) { door.entity = scriptManager.GetScript(tile).CreateEntity(Script.EntityType.Asset, $"DoorExit::{door.cell.name}->exterior[{door.warp.x},{door.warp.y}]"); }
                }

                foreach (NpcContent npc in tile.npcs)
                {
                    HandleTravel(npc, tile);
                }
            }

            // default location name value for interiors
            foreach(InteriorGroup group in interiors)
            {
                int textId = int.Parse($"{group.map:D2}{group.area:D2}0");
                text.SetLocation(textId, "Interior");
            }

            /* Handle npc hasWitness flag */ // this check is very similar to and patially entwined with Script.GenerateCrimeEvents() and the ESD state HANDLECRIME
            /* We can't determine witnesses at runtime so we just do some test now and determine if an npc has witneses to report crimes to */
            void CheckWitnesses(List<NpcContent> npcs)
            {
                foreach (NpcContent npc in npcs)
                {
                    if (npc.alarm >= 50 || npc.IsGuard()) { npc.hasWitness = true; continue; } // will report crimes themselves
                    foreach (NpcContent other in npcs)
                    {
                        if (npc == other) { continue; } // dont' self succ

                        // guards get bonus range because I said so
                        if (other.IsGuard() && System.Numerics.Vector3.Distance(npc.position, other.position) < 23) { npc.hasWitness = true; break; }
                        if (other.alarm >= 50 && System.Numerics.Vector3.Distance(npc.position, other.position) < 12) { npc.hasWitness = true; break; }
                    }
                }
            }

            foreach (Tile tile in tiles) { CheckWitnesses(tile.npcs); }
            foreach (InteriorGroup group in interiors) {
                foreach (InteriorGroup.Chunk chunk in group.chunks) { CheckWitnesses(chunk.npcs); }
            }

            /* Statically resolve shop inventories for npcs (also creatures too) */
            void ResolveShop(CharacterContent npc)
            {
                if (!npc.HasBarter()) { return; } // nope!

                bool WillBarter(CharacterContent npc, ESM.Type type)
                {
                    switch(type)
                    {
                        case ESM.Type.Armor:
                            return npc.services.Contains(CharacterContent.Service.BartersArmor);
                        case ESM.Type.Book:
                            return npc.services.Contains(CharacterContent.Service.BartersBooks);
                        case ESM.Type.Clothing:
                            return npc.services.Contains(CharacterContent.Service.BartersClothing);
                        case ESM.Type.Ingredient:
                            return npc.services.Contains(CharacterContent.Service.BartersIngredients);
                        case ESM.Type.Light:
                            return npc.services.Contains(CharacterContent.Service.BartersLights);
                        case ESM.Type.MiscItem:
                            return npc.services.Contains(CharacterContent.Service.BartersMiscItems);
                        case ESM.Type.Weapon:
                            return npc.services.Contains(CharacterContent.Service.BartersWeapons);
                        case ESM.Type.Probe:
                            return npc.services.Contains(CharacterContent.Service.BartersProbes);
                        case ESM.Type.Lockpick:
                            return npc.services.Contains(CharacterContent.Service.BartersLockpicks);
                        case ESM.Type.RepairItem:
                            return npc.services.Contains(CharacterContent.Service.BartersRepairItems);
                        case ESM.Type.Alchemy:
                            return npc.services.Contains(CharacterContent.Service.BartersAlchemy);
                        case ESM.Type.Apparatus:
                            return npc.services.Contains(CharacterContent.Service.BartersApparatus);
                        default:
                            return false;
                    }
                }

                Cell cell = npc.cell;
                List<(string id, int quantity)> shopInv = new();

                void AddOrIncrement(List<(string id, int quantity)> list, (string id, int quantity) tuple)
                {
                    for(int i=0;i<list.Count();i++)
                    {
                        (string id, int quantity) entry = list[i];
                        if(entry.id.ToLower() == tuple.id.ToLower()) {
                            list.RemoveAt(i);
                            list.Add((entry.id, entry.quantity + tuple.quantity)); // can't increment value in a tuple because fuck
                            return;
                        }
                    }
                    list.Add(tuple);
                }

                foreach (ItemContent item in cell.items) // add loose items this npc owns
                {
                    if (item.ownerNpc == npc.id)
                    {
                        if (WillBarter(npc, item.type))
                        {
                            AddOrIncrement(shopInv, (item.id, 1));
                        }
                    }
                }
                foreach (ContainerContent container in cell.containers) // add containers this npc owns
                {
                    if (container.ownerNpc == npc.id)
                    {
                        foreach ((string id, int quantity) tuple in container.inventory)
                        {
                            Record record = esm.FindRecordById(tuple.id);
                            if (WillBarter(npc, record.type))
                            {
                                AddOrIncrement(shopInv, tuple);
                            }
                        }
                    }
                }

                foreach((string id, int quantity) tuple in npc.inventory) // add own inventory to potential barter
                {
                    Record record = esm.FindRecordById(tuple.id);
                    if (WillBarter(npc, record.type))
                    {
                        AddOrIncrement(shopInv, tuple);
                    }
                }
                if (shopInv.Count() > 0) { npc.barter = shopInv; }
            }

            foreach (Tile tile in tiles)
            {
                foreach (NpcContent npc in tile.npcs) { ResolveShop(npc); }
                foreach(CreatureContent creature in tile.creatures) { ResolveShop(creature); }
            }
            foreach (InteriorGroup group in interiors)
            {
                foreach (InteriorGroup.Chunk chunk in group.chunks)
                {
                    foreach (NpcContent npc in chunk.npcs) { ResolveShop(npc); }
                    foreach (CreatureContent creature in chunk.creatures) { ResolveShop(creature); }
                }
            }

            /* Handling npc/creature death and respawn flags */

            // Dead by type list.
            // So morrowind uses a weird system where it keeps a count of each "type" of npc/creature is killed
            // For most npcs this count will only ever be 0 or 1 since there is only one of that npc in the world
            // But for like rats and shit it keeps count so it knows you've killed 10 rats or whatever
            // So to emulate this system we will have a seperate counter flag for each record type of creature or npc
            Dictionary<string, Script.Flag> typeFlags = new();
            Script.Flag GetTypeCountFlag(string id)
            {
                if (typeFlags.ContainsKey(id))
                {
                    return typeFlags[id];
                }

                Script.Flag typeFlag = scriptManager.common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.DeadCount, id);
                typeFlags.Add(id, typeFlag);
                return typeFlag;
            }

            // Create entity ids and dead/disable flags for npcs
            void HandleNpcFlags(Script script, List<NpcContent> npcs)
            {
                foreach (NpcContent npc in npcs)
                {
                    Script.Flag countFlag = GetTypeCountFlag(npc.id);
                    npc.entity = script.CreateEntity(Script.EntityType.Enemy, $"NPC::{npc.id}");
                    if (npc.dead) { script.RegisterDeadNpc(npc); }
                    else { script.RegisterNpc(param, npc, countFlag); }
                }
            }

            // Create entity ids and dead/disable flags for creatures
            void HandleCreatureFlags(Script script, List<CreatureContent> creatures)
            {
                foreach (CreatureContent creature in creatures)
                {
                    Script.Flag countFlag = GetTypeCountFlag(creature.id);
                    creature.entity = script.CreateEntity(Script.EntityType.Enemy, $"Creature::{creature.id}");
                    script.RegisterCreature(creature, countFlag);
                }
            }

            // Generate scripts
            foreach (Tile tile in tiles)
            {
                if (tile.IsEmpty()) { continue; } // don't generate scripts for empty msbs
                Script script = scriptManager.GetScript(tile);
                HandleNpcFlags(script, tile.npcs);
                HandleCreatureFlags(script, tile.creatures);
            }

            foreach (InteriorGroup group in interiors)
            {
                if (group.IsEmpty()) { continue; } // don't generate scripts for empty msbs
                foreach (InteriorGroup.Chunk chunk in group.chunks)
                {
                    Script script = scriptManager.GetScript(group);
                    HandleNpcFlags(script, chunk.npcs);
                    HandleCreatureFlags(script, chunk.creatures);
                }
            }
        }

        public HugeTile GetHugeTile(Vector3 position)
        {
            foreach (HugeTile huge in huges)
            {
                if (huge.PositionInside(position))
                {
                    return huge;
                }
            }
            return null;
        }

        public HugeTile GetHugeTile(Int2 coordinate)
        {
            foreach (HugeTile huge in huges)
            {
                if (huge.coordinate == coordinate)
                {
                    return huge;
                }
            }
            return null;
        }

        public BigTile GetBigTile(Vector3 position)
        {
            foreach (BigTile big in bigs)
            {
                if (big.PositionInside(position))
                {
                    return big;
                }
            }
            return null;
        }

        public Tile GetTile(Vector3 position)
        {
            foreach(Tile tile in tiles)
            {
                if (tile.PositionInside(position))
                {
                    return tile;
                }
            }
            return null;
        }

        public Tile GetTile(Int2 coordinate)
        {
            foreach(Tile tile in tiles)
            {
                if(tile.coordinate == coordinate)
                {
                    return tile;
                }
            }
            return null;
        }

        /* Get an overworld tile that contains the cell of the given name */
        public Tile GetTile(string cellName)
        {
            foreach(Tile tile in tiles)
            {
                foreach(Cell cell in tile.cells)
                {
                    if(cell.name == cellName)
                    {
                        return tile;
                    }
                }
            }
            return null;
        }

        /* Generates a list of map ids that are being used */
        /* This is the first number in the msb name. m60 for example is overworld */
        public List<int> ListCommon()
        {
            List<int> list = new();
            list.Add(60);
            foreach(InteriorGroup group in interiors)
            {
                if (!list.Contains(group.map)) { list.Add(group.map); }
            }

            return list;
        }

        public class WarpDestination
        {
            public readonly Vector3 position, rotation;
            public readonly uint id;  // entity id

            public WarpDestination(Vector3 position, Vector3 rotation, uint id)
            {
                this.position = position - new Vector3(0f, Const.NPC_ROOT_OFFSET, 0f);  // fix load door offset
                this.rotation = rotation;
                this.id = id;
            }
        }
    }
}
