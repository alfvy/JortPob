using JortPob.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Numerics;
using static JortPob.Layout;
using static SoulsFormats.MSBAC4.Event;

namespace JortPob
{
    /* A Tile is what we call a single square on the Elden Ring cell grid. It's basically the Elden Ring version of a "cell" */
    [DebuggerDisplay("Tile m{map}_{coordinate.x}_{coordinate.y}_{block} :: [{cells.Count}] Cells")]
    public class Tile : BaseTile
    {
        public HugeTile huge;
        public BigTile big;

        public Obj nav;  // navmesh repersentation of this msb tile
        public readonly List<PathGridPoint> paths; // mw uses these for nav. we are only using them for wander positions
        public readonly List<TravelPoint> travels; // positions directly referenced in AiPackages

        public Tile(int m, int x, int y, int b) : base(m, x, y, b)
        {
            nav = new();
            paths = new();
            travels = new();
        }

        /* Checks ABSOLUTE POSITION! This is the position of an object from the ESM accounting for the layout offset! */
        public bool PositionInside(Vector3 position)
        {
            Vector3 pos = position + Const.LAYOUT_COORDINATE_OFFSET;

            float x1 = (coordinate.x * Const.TILE_SIZE) - (Const.TILE_SIZE * 0.5f);
            float y1 = (coordinate.y * Const.TILE_SIZE) - (Const.TILE_SIZE * 0.5f);
            float x2 = x1 + Const.TILE_SIZE;
            float y2 = y1 + Const.TILE_SIZE;

            if(pos.X >= x1 && pos.X < x2 && pos.Z >= y1 && pos.Z < y2)
            {
                return true;
            }

            return false;
        }

        /* Returns averaged region of this tile. Each cell has a region set so the best we can do is see what region is most common among cells in this tile and return that */
        public string GetRegion()
        {
            Dictionary<string, int> regions = new();
            foreach(Cell cell in cells)
            {
                if (cell.region == null) { continue; }
                string r = cell.region.Trim().ToLower();
                if (regions.ContainsKey(r)) { regions[r]++; }
                else { regions.Add(r, 1); }
            }

            if (regions.Count() <= 0) { return "Default Region"; } // no regions set so guh

            string most = regions.Keys.First();
            foreach(KeyValuePair<string, int> kvp in regions)
            {
                if (regions[most] < kvp.Value)
                {
                    most = kvp.Key;
                }
            }

            /* Red Mountain has priority for skybox */
            string redMountain = "red mountain region"; // Case sensitive
            if (regions.ContainsKey(redMountain))
            {
                if (regions[redMountain] >= 3) { most = redMountain; }
            }

            return most;
        }

        public IEnumerable<Content> GetAllContent()
        {
            IEnumerable<IEnumerable<Content>> all = [
                assets,
                doors,
                emitters,
                lights,
                creatures,
                npcs,
                containers,
                pickables,
                items,
            ];

            foreach (IEnumerable<Content> enumerable in all)
            {
                foreach (Content content in enumerable)
                {
                    yield return content;
                }
            }
        }

        public override void AddCell(ScriptManager scriptManager, Cell cell)
        {
            cells.Add(cell);

            /* Add cells pathgrid to the tile */
            Script script = scriptManager.GetScript(this);
            for (int i = 0; i < cell.paths.Count(); i++)
            {
                Vector3 path = cell.paths[i];
                string name = $"PathGrid_{map:D2}{coordinate.x:D2}{coordinate.y:D2}_{cell.coordinate.x:D2}{cell.coordinate.y:D2}_{i:D4}";
                float x = (coordinate.x * Const.TILE_SIZE);
                float y = (coordinate.y * Const.TILE_SIZE);
                Vector3 relative = (path + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
                Layout.PathGridPoint point = new(name,relative, script.CreateEntity(Script.EntityType.Region, $"PathGridPoint"));
                paths.Add(point);
            }
        }

        public void AddTerrain(Vector3 position, TerrainInfo terrainInfo)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);
            Vector3 relative = (position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
            terrain.Add(new Tuple<Vector3, TerrainInfo>(relative, terrainInfo));
            nav.add(new Obj(Path.Combine(Const.CACHE_PATH, terrainInfo.obj)), relative, Vector3.Zero, 1f);
        }

        public new void AddContent(Cache cache, Cell cell, Content content)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);
            content.relative = (content.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);

            AddNav(cache, cell, content);

            base.AddContent(cache, cell, content);
        }

        public void AddNav(Cache cache, Cell cell, Content content)
        {
            if (Const.DEBUG_SKIP_NAVMESH) { return; }

            /* Recalcualte relative for this tile because this content may be coming from a BigTile or HugeTile and the content.relative will not be valid in those cases */
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);
            Vector3 relative = (content.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);

            switch (content)
            {
                case AssetContent a:
                    ModelInfo modelInfo = cache.GetModel(content.mesh, content.scale);
                    if (!modelInfo.HasCollision()) { break; } // no collision means no nav info
                    nav.add(new Obj(Path.Combine(Const.CACHE_PATH, modelInfo.collision.obj)), relative, content.rotation, modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);
                    break;
                default: break;
            }
        }

        public void AddWarp(DoorContent.Warp warp)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);

            Layout.WarpDestination dest = new((warp.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y), warp.rotation, warp.entity);
            warps.Add(dest);
        }

        public void AddMapPoint(Layout.MapPoint point)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);

            point.relative = (point.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
            points.Add(point);
        }

        public void AddScriptedPosition(Script script, Vector3 position, float rot)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);

            Vector3 relative = (position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
            Vector3 rotation = new Vector3(0, rot, 0); // @TODO: THIS IS WRONG!
            uint region = script.CreateEntity(Script.EntityType.Region, $"ScriptedPosition:Region:{position.ToString()}");
            uint player = script.CreateEntity(Script.EntityType.Region, $"ScriptedPosition:Player:{position.ToString()}");
            positions.Add(new(position, relative, rotation, region, player, map, coordinate.x, coordinate.y, block));
        }
        
        /* Add travelpoint */
        public void AddTravelPoint(Script script, Vector3 point, float radius = -1f)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);
            Vector3 relative = (point + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
            uint region = script.CreateEntity(Script.EntityType.Region, $"Travel:Region:{point.ToString()}");
            TravelPoint travel = new($"Travel_{map:D2}{coordinate.x:D2}{coordinate.y:D2}_{travels.Count():D4}", point, relative, radius == -1f ? Const.PATH_REGION_SIZE : radius, region);
            travels.Add(travel);
        }

        /* Converts all "Travel" positions to travelpoints */
        public void ProcessTravelPoints(ScriptManager scriptManager)
        {
            Script script = scriptManager.GetScript(this);
            void HandleCharacterContent(CharacterContent content)
            {
                foreach (CharacterContent.AiPackage package in content.packages)
                {
                    if (package.type == CharacterContent.AiPackage.Type.Travel)
                    {
                        float x = (coordinate.x * Const.TILE_SIZE);
                        float y = (coordinate.y * Const.TILE_SIZE);
                        Vector3 relative = (package.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
                        uint region = script.CreateEntity(Script.EntityType.Region, $"Travel:Region:{package.position.ToString()}");
                        TravelPoint travel = new($"Travel_{map:D2}{coordinate.x:D2}{coordinate.y:D2}_{travels.Count():D4}", package.position, relative, Const.PATH_REGION_SIZE, region);
                        travels.Add(travel);
                    }
                }
            }

            foreach (NpcContent c in npcs) { HandleCharacterContent(c); }
            foreach (CreatureContent c in creatures) { HandleCharacterContent(c); }
        }
    }



    public abstract class BaseTile
    {
        public readonly int map;
        public readonly Int2 coordinate;
        public readonly int block;

        public readonly List<Cell> cells;

        public readonly List<Tuple<Vector3, TerrainInfo>> terrain;
        public readonly List<AssetContent> assets;
        public readonly List<DoorContent> doors;
        public readonly List<LightContent> lights;
        public readonly List<EmitterContent> emitters;
        public readonly List<CreatureContent> creatures;
        public readonly List<NpcContent> npcs;
        public readonly List<ContainerContent> containers;
        public readonly List<PickableContent> pickables;
        public readonly List<ItemContent> items;

        public readonly List<Layout.WarpDestination> warps; // end points for load doors in other cells. also used by travel npcs
        public readonly List<Layout.MapPoint> points;
        public readonly List<Layout.ScriptedPosition> positions; // used by scripts to target locations EX: 'PositionCell'

        public BaseTile(int m, int x, int y, int b)
        {
            /* Tile Data */
            map = m;
            coordinate = new(x, y);
            block = b;

            /* Tile Content Data */
            cells = new();
            terrain = new();
            assets = new();
            doors = new();
            emitters = new();
            lights = new();
            creatures = new();
            npcs = new();
            containers = new();
            pickables = new();
            items = new();

            positions = new();
            points = new();
            warps = new();
        }

        public int[] IdList()
        {
            return [map, coordinate.x, coordinate.y, block];
        }

        public bool IsEmpty()
        {
            return cells.Count() <= 0 && terrain.Count() <= 0 && assets.Count() <= 0;
        }

        public abstract void AddCell(ScriptManager scriptManager, Cell cell);

        /* Incoming content is in aboslute worldspace from the ESM, when adding content to a tile we convert it's coordiantes to relative space */
        public void AddContent(Cache cache, Cell cell, Content content)
        {
            switch(content)
            {
                case AssetContent a:
                    assets.Add(a); break;
                case DoorContent d:
                    doors.Add(d); break;
                case EmitterContent e:
                    emitters.Add(e); break;
                case LightContent l:
                    lights.Add(l); break;
                case ContainerContent o:
                    containers.Add(o); break;
                case PickableContent p:
                    pickables.Add(p); break;
                case ItemContent i:
                    items.Add(i); break;
                case NpcContent n:
                    npcs.Add(n); break;
                case CreatureContent c:
                    creatures.Add(c); break;
                default:
                    Lort.Log($" ## WARNING ## Unhandled Content class '{content.type}::{content.id}' fell through AddContent()", Lort.Type.Debug); break;
            }
        }
    }
}
