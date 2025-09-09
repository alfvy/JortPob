﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace JortPob.Common
{
    public class Obj
    {
        public List<Vector3> vs, vts, vns;
        public List<ObjG> gs;
        public Obj()
        {
            vs = new();
            vts = new();
            vns = new();
            gs = new();
        }

        public Obj(string path)
        {
            vs = new();
            vts = new();
            vns = new();
            gs = new();

            ObjG last = null;
            string[] data = File.ReadAllLines(path);
            foreach (string line in data)
            {
                string[] values = line.Replace("  ", " ").Split(" ");
                switch (values[0])
                {
                    case "v":
                        {
                            float x = float.Parse(values[1]);
                            float y = float.Parse(values[2]);
                            float z = float.Parse(values[3]);
                            vs.Add(new Vector3(x, y, z));
                            break;
                        }
                    case "vt":
                        {
                            float x = float.Parse(values[1]);
                            float y = float.Parse(values[2]);
                            float z = float.Parse(values[3]);
                            vts.Add(new Vector3(x, y, z));
                            break;
                        }
                    case "vn":
                        {
                            float x = float.Parse(values[1]);
                            float y = float.Parse(values[2]);
                            float z = float.Parse(values[3]);
                            vns.Add(new Vector3(x, y, z));
                            break;
                        }
                    case "g":
                        {
                            ObjG g = new();
                            g.name = values[1];
                            last = g;
                            gs.Add(g);
                            break;
                        }
                    case "usemtl":
                        {
                            last.mtl = values[1];
                            break;
                        }
                    case "f":
                        {
                            string[] a = values[1].Split("/");
                            string[] b = values[2].Split("/");
                            string[] c = values[3].Split("/");

                            ObjV A = new(int.Parse(a[0]) - 1, int.Parse(a[1]) - 1, int.Parse(a[2]) - 1);
                            ObjV B = new(int.Parse(b[0]) - 1, int.Parse(b[1]) - 1, int.Parse(b[2]) - 1);
                            ObjV C = new(int.Parse(c[0]) - 1, int.Parse(c[1]) - 1, int.Parse(c[2]) - 1);

                            last.fs.Add(new(A, B, C));
                            break;
                        }
                    default: break;
                }
            }
        }

        // optimize by re-indexing duplicate vertice data
        public Obj optimize() {
            Obj nu = new();

            Dictionary<Vector3, int> vsLookup = new();
            Dictionary<Vector3, int> vnsLookup = new();
            Dictionary<Vector3, int> vtsLookup = new();

            int[] GetIndex(Vector3 v, Vector3 vn, Vector3 vt)
            {
                int[] indset = new int[3];

                // V
                if (!vsLookup.TryGetValue(v, out indset[0]))
                {
                    indset[0] = nu.vs.Count;
                    nu.vs.Add(v);
                    vsLookup[v] = indset[0];
                }

                // VN  
                if (!vnsLookup.TryGetValue(vn, out indset[1]))
                {
                    indset[1] = nu.vns.Count;
                    nu.vns.Add(vn);
                    vnsLookup[vn] = indset[1];
                }

                // VT
                if (!vtsLookup.TryGetValue(vt, out indset[2]))
                {
                    indset[2] = nu.vts.Count;
                    nu.vts.Add(vt);
                    vtsLookup[vt] = indset[2];
                }

                return indset;
            }

            foreach (ObjG g in gs)
            {
                ObjG nug = new();
                nug.name = g.name;
                nug.mtl = g.mtl;
                foreach(ObjF f in g.fs)
                {
                    int[] a = GetIndex(vs[f.a.v], vns[f.a.vn], vts[f.a.vt]);
                    int[] b = GetIndex(vs[f.b.v], vns[f.b.vn], vts[f.b.vt]);
                    int[] c = GetIndex(vs[f.c.v], vns[f.c.vn], vts[f.c.vt]);

                    ObjV A = new ObjV(a[0], a[2], a[1]);  //lol i swapped the order of vt vn on accident. fixed
                    ObjV B = new ObjV(b[0], b[2], b[1]);
                    ObjV C = new ObjV(c[0], c[2], c[1]);

                    ObjF nuf = new ObjF(A, B, C);
                    nug.fs.Add(nuf);
                }
                nu.gs.Add(nug);
            }
            return nu;
        }

        /* Scale this obj */
        public void scale(float scale)
        {
            for(int i = 0;i<vs.Count;i++)
            {
                vs[i] *= scale;
            }
        }

        /* Split an obj into multiple objs based of its 'g's. */
        /* This is due to an hkx issue where having multiple materials in an obj just results in broken stuff. So we are splitting each material into its own obj and then it's own hkx */
        public List<Obj> split()
        {
            List<Obj> objs = new();
            foreach(ObjG g in gs)
            {
                Obj nuobj = new();
                ObjG nug = new();
                nug.name = g.name;
                nug.mtl = g.mtl;

                int c = 0;
                foreach(ObjF f in g.fs)
                {
                    List<ObjV> V = new();
                    for (int i = 0; i < 3; i++)
                    {
                        nuobj.vs.Add(vs[f.AsArray()[i].v]);
                        nuobj.vns.Add(vs[f.AsArray()[i].vn]);
                        nuobj.vts.Add(vs[f.AsArray()[i].vt]);
                        V.Add(new ObjV(c, c, c));
                        c++;
                    }
                    ObjF nuf = new(V[0], V[1], V[2]);
                    nug.fs.Add(nuf);
                }
                nuobj.gs.Add(nug);
                objs.Add(nuobj);
            }

            return objs;
        }

        /* Takes data in this class and writes an obj file of it to the path specified */
        public void write(string outPath)
        {
            StringBuilder sb = new();

            /* Header */
            sb.Append($"mtllib {Utility.PathToFileName(outPath)}.mtl\r\n");

            /* write vertices */
            sb.Append("## Vertices: "); sb.Append(vs.Count); sb.Append(" ##\r\n");
            foreach (Vector3 v in vs)
            {
                sb.Append("v  "); sb.Append(v.X); sb.Append(' '); sb.Append(v.Y); sb.Append(' '); sb.Append(v.Z); sb.Append("\r\n");
            }

            /* write texture coordinates */
            sb.Append("\r\n## Texture Coordinates: "); sb.Append(vts.Count); sb.Append(" ##\r\n");
            foreach (Vector3 vt in vts)
            {
                sb.Append("vt "); sb.Append(vt.X); sb.Append(' '); sb.Append(vt.Y); sb.Append(' '); sb.Append(vt.Z); sb.Append("\r\n");
            }

            /* write vertex normals */
            sb.Append("\r\n## Vertex Normals: "); sb.Append(vns.Count); sb.Append(" ##\r\n");
            foreach (Vector3 vn in vns)
            {
                sb.Append("vn "); sb.Append(vn.X); sb.Append(' '); sb.Append(vn.Y); sb.Append(' '); sb.Append(vn.Z); sb.Append("\r\n");
            }

            foreach (ObjG g in gs)
            {
                g.write(sb);
            }

            /* write to file */
            if (File.Exists(outPath)) { File.Delete(outPath); }
            if(!Directory.Exists(Path.GetDirectoryName(outPath))) { Directory.CreateDirectory(Path.GetDirectoryName(outPath)); }
            File.WriteAllText(outPath, sb.ToString());
        }

        /* Opens an obj and scrape the material names */
        public static List<CollisionMaterial> GetMaterials(string path)
        {
            List<CollisionMaterial> mats = new();
            string[] file = File.ReadAllLines(path);
            foreach (string l in file)
            {
                string line = l.Trim();
                if (line.ToLower().StartsWith("usemtl "))
                {
                    string mtl = line.Substring(7).Trim(); // get value of usemtl line
                    mtl = mtl.Substring(4); // remove HKM_
                    mtl = mtl.Substring(0, mtl.Length - 6); //remove _SAFE#
                    mats.Add(Enum.Parse<CollisionMaterial>(mtl));
                }
            }
            return mats;
        }

        public enum CollisionMaterial
        {
            None = 0,
            Stock = 1,
            Rock = 2,
            Sand = 3,
            Wood = 4,
            Dirt = 5,
            Ore = 6,
            Lava = 7,
            ScarletTree = 8,
            IronGrate = 11,
            ScarletMushroom = 12,
            Bone = 14,
            Water = 21,
            ShallowPoisonSwamp = 23,
            PoisonSwamp = 24,
            RainDirt = 44,
            ScarletSwamp = 46,
            LakeofRot = 49
        }
    }

    public class ObjG
    {
        public string name, mtl;
        public List<ObjF> fs;
        public ObjG()
        {
            fs = new();
        }

        public void write(StringBuilder sb)
        {
            /* write object name */
            sb.Append("\r\n");
            sb.Append("g "); sb.Append(name);
            sb.Append("\r\n");

            /* write material */
            sb.Append("usemtl "); sb.Append(mtl);

            /* write triangles */
            sb.Append("\r\n## Triangles: "); sb.Append(fs.Count); sb.Append(" ##\r\n");
            foreach (ObjF f in fs)
            {
                f.write(sb);
            }
            sb.Append("\r\n");
        }
    }

    public class ObjF
    {
        public ObjV a, b, c;
        public ObjF(ObjV a, ObjV b, ObjV c)
        {
            this.a = a; this.b = b; this.c = c;
        }

        public ObjV[] AsArray()
        {
            return new ObjV[] { a, b, c };
        }

        public void write(StringBuilder sb)
        {
            sb.Append("f ");
            a.write(sb);
            b.write(sb);
            c.write(sb);
            sb.Append("\r\n");
        }
    }

    public class ObjV
    {
        public int v, vt, vn;
        public ObjV(int v, int vt, int vn)
        {
            this.v = v; this.vt = vt; this.vn = vn;
        }

        public void write(StringBuilder sb)
        {
            sb.Append(v + 1); sb.Append('/'); sb.Append(vt + 1); sb.Append('/'); sb.Append(vn + 1); sb.Append(' ');
        }
    }
}
