using JortPob.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace JortPob
{
    /* A Tile is what we call a single square on the Elden Ring cell grid. It's basically the Elden Ring version of a "cell" */
    [DebuggerDisplay("Tile m{map}_{coordinate.x}_{coordinate.y}_{block} :: [{cells.Count}] Cells")]
    public class Tile : BaseTile
    {
        public HugeTile huge;
        public BigTile big;

        public Tile(int m, int x, int y, int b) : base(m, x, y, b)
        {
            
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
            string redMountain = "Red Mountain Region".Trim().ToLower();
            if (regions.ContainsKey(redMountain))
            {
                if (regions[redMountain] >= 3) { most = redMountain; }
            }

            return most;
        }

        public override void AddCell(Cell cell)
        {
            cells.Add(cell);
        }

        public void AddTerrain(Vector3 position, TerrainInfo terrainInfo)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);
            Vector3 relative = (position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
            terrain.Add(new Tuple<Vector3, TerrainInfo>(relative, terrainInfo));
        }

        public new void AddContent(Cache cache, Cell cell, Content content)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);
            content.relative = (content.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);

            base.AddContent(cache, cell, content);
        }

        public void AddWarp(DoorContent.Warp warp)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);

            Layout.WarpDestination dest = new((warp.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y), warp.rotation, warp.entity);
            warps.Add(dest);
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

            warps = new();
        }

        public int[] IdList()
        {
            return new int[] { map, coordinate.x, coordinate.y, block };
        }

        public bool IsEmpty()
        {
            return cells.Count() <= 0 && terrain.Count() <= 0 && assets.Count() <= 0;
        }

        public abstract void AddCell(Cell cell);

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
                    Lort.Log(" ## WARNING ## Unhandled Content class fell through AddContent()", Lort.Type.Debug); break;
            }
        }
    }
}
