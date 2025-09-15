using JortPob.Common;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;

namespace JortPob
{
    public class Cell
    {
        public readonly string name;
        public readonly string region;
        public readonly Int2 coordinate;  // Position on the cell grid
        public readonly Vector3 center;
        public readonly Vector3 boundsMin;
        public readonly Vector3 boundsMax;

        public readonly List<Content> contents;            // All of this
        public readonly List<CreatureContent> creatures;
        public readonly List<NpcContent> npcs;
        public readonly List<AssetContent> assets;
        public readonly List<DoorContent> doors;
        public readonly List<LightContent> lights;
        public readonly List<EmitterContent> emitters;

        public Cell(ESM esm, JsonNode json)
        {
            /* Cell Data */
            name = json["name"].ToString() == "" ? null : json["name"].ToString();
            region = json["region"] != null ? json["region"].ToString() : "null";

            int x = int.Parse(json["data"]["grid"][0].ToString());
            int y = int.Parse(json["data"]["grid"][1].ToString());
            coordinate = new Int2(x, y);

            float half = Const.CELL_SIZE / 2f;
            center = new Vector3(coordinate.x, 0.0f, coordinate.y) * Const.CELL_SIZE + new Vector3(half, 0f, half);

            /* Cell Content Data */
            contents = new();
            creatures = new();
            npcs = new();
            assets = new();
            doors = new();
            emitters = new();
            lights = new();

            foreach (JsonNode reference in json["references"].AsArray())
            {
                string id = reference["id"].ToString();
                if (id == "ex_shore_all_lev+0")
                {
                    Console.WriteLine("HI");
                }
                Record record = esm.FindRecordById(id);

                if(record == null) { continue; }

                string mesh = record.json["mesh"] != null ? record.json["mesh"].ToString() : null;
                if (mesh != null && mesh.Trim() == "") { mesh = null; }                             // For some reason a null mesh can just be "" sometimes?

                switch(record.type)
                {
                    case ESM.Type.Static:
                    case ESM.Type.Container:
                    case ESM.Type.Activator:
                        if (mesh != null) { assets.Add(new AssetContent(this, reference, record)); }
                        break;
                    case ESM.Type.Door:
                        if (mesh != null) { doors.Add(new DoorContent(this, reference, record)); }
                        break;
                    case ESM.Type.Light:
                        if (mesh == null) { lights.Add(new LightContent(this, reference, record)); }
                        else { emitters.Add(new EmitterContent(this, reference, record)); }
                        break;
                    case ESM.Type.Npc:
                        npcs.Add(new NpcContent(this, reference, record));
                        break;
                    case ESM.Type.Creature:
                    case ESM.Type.LeveledCreature:
                        creatures.Add(new CreatureContent(this, reference, record));
                        break;
                }
            }

            contents.AddRange(creatures);
            contents.AddRange(npcs);
            contents.AddRange(assets);
            contents.AddRange(doors);
            contents.AddRange(emitters);
            contents.AddRange(lights);


            /* Calculate bounding box */
            float x1 = float.MaxValue, y1 = float.MaxValue, z1 = float.MaxValue, x2 = float.MinValue, y2 = float.MinValue, z2 = float.MinValue;
            foreach (Content content in contents)
            {
                x1 = Math.Min(x1, content.position.X);
                y1 = Math.Min(y1, content.position.Y);
                z1 = Math.Min(z1, content.position.Z);
                x2 = Math.Max(x2, content.position.X);
                y2 = Math.Max(y2, content.position.Y);
                z2 = Math.Max(z2, content.position.Z);
            }
            const float PAD = 10f; // originally was multiplying but that resulted in the box being moved when all 4 points existed in the same quadrant (XY). padding is easier and safe
            boundsMin = new Vector3(x1, y1, z1) - new Vector3(PAD); // this is calc'd before we load models so we can't get a perfectly accurate bounding box. so we just pad it a bit and call it a day
            boundsMax = new Vector3(x2, y2, z2) + new Vector3(PAD);
        }

        public bool IsPointInside(Vector3 point)
        {
            float startX = center.X - Const.CELL_SIZE;
            float endX = center.X;
            float startY = center.Z - Const.CELL_SIZE;
            float endY = center.Z;

            Vector3 min = new(startX, 0f, startY);
            Vector3 max = new(endX, 0f, endY);
            if (point.X < min.X || point.X > max.X) return false;
            if(point.Z < min.Z || point.Z > max.Z) return false;

            return true;
        }

        public bool IsPointInside(List<Vector3> points)
        {
            foreach(Vector3 point in points)
            {
                if (IsPointInside(point))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
