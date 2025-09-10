using HKLib.hk2018.hkHashMapDetail;
using JortPob.Common;
using SoulsFormats;
using SoulsFormats.Formats.Morpheme.MorphemeBundle.Network;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TES3;
using static JortPob.Model.MaterialContext;

namespace JortPob.Model
{
    public partial class ModelConverter
    {
        public static ModelInfo NIFToFLVER(MaterialContext materialContext,
            ModelInfo modelInfo,
            bool forceCollision,
            string modelPath,
            string outputPath)
        {
            var loadResult = TES3.Interop.LoadScene(Utf8String.From(modelPath));

            if (loadResult.IsErr)
            {
                Console.WriteLine("Error loading scene");
                return null;
            }

            var nif = loadResult.AsOk();

            FLVER2 flver = new();
            flver.Header.Version = 131098; // Elden Ring FLVER Version Number
            flver.Header.Unk5D = 0;        // Unk
            flver.Header.Unk68 = 4;        // Unk

            /* Add bones and nodes for FLVER */
            FLVER.Node rootNode = new();
            FLVER2.SkeletonSet skeletonSet = new();
            FLVER2.SkeletonSet.Bone rootBone = new(0);

            rootNode.Name = Path.GetFileNameWithoutExtension(modelPath);
            skeletonSet.AllSkeletons.Add(rootBone);
            skeletonSet.BaseSkeleton.Add(rootBone);
            flver.Nodes.Add(rootNode);
            flver.Skeletons = skeletonSet;

            List<Tuple<string, Vector3>> nodes = new(); // 
            List<Utf8String> textures = new();

            Matrix4x4 desiredRotation = Matrix4x4.CreateRotationY((float)Math.PI) * Matrix4x4.CreateRotationX((float)Math.PI / 2);

            for (int texI = 0; texI < nif.VisualMeshes.Count; texI++)
            {
                textures.Add(nif.VisualMeshes[texI].Texture);
            }

            /* Generate material data */
            List<MaterialContext.MaterialInfo> materialInfos = materialContext.GenerateMaterials(textures);
            foreach (MaterialContext.MaterialInfo mat in materialInfos)
            {
                flver.Materials.Add(mat.material);
                flver.GXLists.Add(mat.gx);
                flver.BufferLayouts.Add(mat.layout);
                foreach (TextureInfo info in mat.info)
                {
                    modelInfo.textures.Add(info);
                }
            }

            for (int idx = 0; idx < nif.VisualMeshes.Count; idx++)
            {
                var mesh = nif.VisualMeshes[idx];

                nodes.Add(new ($"Mesh {idx}", new Vector3(mesh.Transform.Translation)));

                FLVER2.Mesh flverMesh = new();
                FLVER2.FaceSet flverFaces = new();
                flverMesh.FaceSets.Add(flverFaces);
                flverFaces.CullBackfaces = true;
                flverFaces.Unk06 = 1;
                flverMesh.NodeIndex = 0;

                /* Setup Vertex Buffer */
                FLVER2.VertexBuffer flverBuffer = new(0);
                flverBuffer.LayoutIndex = idx;
                flverMesh.VertexBuffers.Add(flverBuffer);

                Matrix4x4 mt = Matrix4x4.CreateTranslation(mesh.Transform.Translation.ToVector3());
                Matrix4x4 mr = Matrix4x4.CreateFromQuaternion(mesh.Transform.Rotation.ToQuaternion());
                Matrix4x4 ms = Matrix4x4.CreateScale(mesh.Transform.Scale);

                for (int f = 0; f < mesh.Triangles.Count; f++)
                {
                    for (int t = 0; t < 3; t++)
                    {
                        FLVER.Vertex flverVertex = new();

                        var vertIdx = mesh.Triangles[f].T(t);

                        /* Grab vertice position + normals/tangents */
                        Vector3 pos = mesh.Vertices[vertIdx].ToNumeric();
                        Vector3 norm = mesh.Normals[vertIdx].ToNumeric();
                        Vector3 tang = new Vector3(1, 0, 0);
                        Vector3 bitang  = new Vector3(0, 0, 1);

                        // collapse the mesh transform onto the vert data
                        pos = Vector3.Transform(pos, ms * mr * mt);
                        norm = Vector3.TransformNormal(norm, mr);
                        tang = Vector3.TransformNormal(tang, mr);
                        bitang = Vector3.TransformNormal(bitang, mr);

                        // Fromsoftware lives in the mirror dimension. I do not know why.
                        pos = pos * Const.GLOBAL_SCALE;
                        pos.X *= -1f;
                        norm.X *= -1f;
                        tang.X *= -1f;
                        bitang.X *= -1f;

                        /* Rotate Y 180 degrees because... */
                        pos = Vector3.Transform(pos, desiredRotation);

                        /* Rotate normals/tangents to match */
                        norm = Vector3.Normalize(Vector3.TransformNormal(norm, desiredRotation));
                        tang = Vector3.Normalize(Vector3.TransformNormal(tang, desiredRotation));
                        bitang = Vector3.Normalize(Vector3.TransformNormal(bitang, desiredRotation));

                        // Set ...
                        flverVertex.Position = pos;
                        flverVertex.Normal = norm;
                        if (mesh.UvSet0 != null && vertIdx < mesh.UvSet0.Count && mesh.UvSet0.Count != 0)
                        {
                            var uv = mesh.UvSet0[vertIdx];
                            flverVertex.UVs.Add(new Vector3(uv.x, uv.y, 0));
                        }
                        else
                        {
                            if (flverVertex.UVs == null) flverVertex.UVs = new();
                            flverVertex.UVs.Add(new Vector3(0, 0, 0));
                        }

                        flverVertex.Bitangent = new Vector4(bitang.X, bitang.Y, bitang.Z, 0);
                        flverVertex.Tangents.Add(new Vector4(tang.X, tang.Y, tang.Z, 0));

                        flverVertex.Colors.Add(new FLVER.VertexColor(255, 255, 255, 255));

                        if (materialInfos[idx].template == MaterialContext.MaterialTemplate.Foliage)
                        {
                            flverVertex.UVs.Add(new Vector3(0f, .2f, 0f));
                            flverVertex.UVs.Add(new Vector3(1f, 1f, 0f));
                            flverVertex.UVs.Add(new Vector3(1f, 1f, 0f));
                        }

                        flverMesh.Vertices.Add(flverVertex);
                        flverFaces.Indices.Add(flverMesh.Vertices.Count - 1);
                    }

                }
                //Lort.Log($"Mesh {idx} in {modelPath} has {uvSanity.Count} uvs", Lort.Type.Debug);

                flver.Meshes.Add(flverMesh);
            }

            /* Add Dummy Polys */
            short nextRef = 500; // idk why we start at 500, i'm copying old code from DS3 portjob here
            nodes.Insert(0, new("root", Vector3.Zero));    // always add a dummy at root for potential use by fxr later
            foreach (Tuple<string, Vector3> tuple in nodes)
            {
                string name = tuple.Item1;
                Vector3 position = tuple.Item2;

                if (name.Contains(".0")) { name = name.Substring(0, name.Length - 4); }   // Duplicate nodes get a '.001' and what not appended to their names. Remove that.
                short refid = modelInfo.dummies.ContainsKey(name) ? modelInfo.dummies[name] : nextRef++;

                // correct position using same math as we use for vertices above
                position = position * Const.GLOBAL_SCALE;
                position.X *= -1f;
                Matrix4x4 rotateY180Matrix = Matrix4x4.CreateRotationY((float)Math.PI);
                position = Vector3.Transform(position, rotateY180Matrix);

                FLVER.Dummy dmy = new();
                dmy.Position = position;
                dmy.Forward = new(0, 0, 1);
                dmy.Upward = new(0, 1, 0);
                dmy.Color = System.Drawing.Color.White;
                dmy.ReferenceID = refid;
                dmy.ParentBoneIndex = 0;
                dmy.AttachBoneIndex = -1;
                dmy.UseUpwardVector = true;
                flver.Dummies.Add(dmy);
                if (!modelInfo.dummies.ContainsKey(name)) { modelInfo.dummies.Add(name, refid); }
            }

            /* Calculate bounding boxes */
            BoundingBoxSolver.FLVER(flver);

            /* Optimize flver */
            flver = FLVERUtil.Optimize(flver);

            /* Calculate model size */
            float size = Vector3.Distance(rootNode.BoundingBoxMin, rootNode.BoundingBoxMax);
            modelInfo.size = size;

            /* Write flver */ 
            flver.Write(outputPath);

            /* Load overrides list for collision */
            JsonNode json = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\static_collision.json")));
            bool CheckOverride(string name)
            {
                foreach (JsonNode node in json.AsArray())
                {
                    if (node.ToString().ToLower() == name.ToLower()) { return true; }
                }
                return false;
            }

            /* Generate collision obj if the model contains a collision mesh */
            if ((nif.CollisionMeshes.Count > 0 || forceCollision) && !CheckOverride(modelInfo.name))
            {
                /* Best guess for collision material */
                Obj.CollisionMaterial matguess = Obj.CollisionMaterial.None;
                void Guess(string[] keys, Obj.CollisionMaterial type)
                {
                    if (matguess != Obj.CollisionMaterial.None) { return; }
                    foreach (var tex in textures)
                    {
                        foreach (string key in keys)
                        {
                            if (Utility.PathToFileName(modelInfo.name).ToLower().Contains(key)) { matguess = type; return; }
                            if (tex.String.ToLower().Contains(key)) { matguess = type; return; }
                            if (tex.String != null && Utility.PathToFileName(tex.String).ToLower().Contains(key)) { matguess = type; return; }
                        }
                    }
                    return;
                }

                /* This is a hierarchy, first found keyword determines collision type, more obvious keywords at the top, niche ones at the bottom */
                Guess(new string[] { "wood", "log", "bark" }, Obj.CollisionMaterial.Wood);
                Guess(new string[] { "sand" }, Obj.CollisionMaterial.Sand);
                Guess(new string[] { "rock", "stone", "boulder" }, Obj.CollisionMaterial.Rock);
                Guess(new string[] { "dirt", "soil", "grass" }, Obj.CollisionMaterial.Dirt);
                Guess(new string[] { "iron", "metal", "steel" }, Obj.CollisionMaterial.IronGrate);
                Guess(new string[] { "mushroom", }, Obj.CollisionMaterial.ScarletMushroom);
                Guess(new string[] { "statue", "adobe" }, Obj.CollisionMaterial.Rock);
                Guess(new string[] { "dwrv", "daed" }, Obj.CollisionMaterial.Rock);

                // Give up!
                if (matguess == Obj.CollisionMaterial.None) { matguess = Obj.CollisionMaterial.Stock; }

                /* If the model doesnt have an explicit collision mesh but forceCollision is on because it's a static, we use the visual mesh as a collision mesh */
                Obj obj = COLLISIONtoOBJ(nif.CollisionMeshes.Count > 0 ? nif.CollisionMeshes : nif.VisualMeshes, matguess);
                if (nif.CollisionMeshes.Count <= 0) { Lort.Log($"{modelInfo.name} had forced collision gen...", Lort.Type.Debug); }

                /* Make obj file for collision. These will be converted to HKX later */
                string objPath = outputPath.Replace(".flver", ".obj");
                CollisionInfo collisionInfo = new(modelInfo.name, $"meshes\\{Utility.PathToFileName(objPath)}.obj");
                modelInfo.collision = collisionInfo;

                obj = obj.optimize();
                obj.write(objPath);
            }

            return modelInfo;
        }
    }

    public static class Tes3Extensions
    {
        public static Vec3 Multiply(this Vec3 vec, float value)
        {
            var result = vec;
            result.x = result.x * value;
            result.y = result.y * value;
            result.z = result.z * value;
            return result;
        }

        public static Vector3 ToNumeric(this Vec3 vec)
        {
            return new System.Numerics.Vector3(vec.x, vec.y, vec.z);
        }

        public static Vector3 ToVector3(this float[] floats)
        {
            if (floats.Length < 3) return Vector3.Zero;
            return new Vector3(floats[0], floats[1], floats[2]);
        }

        public static Vector4 ToVector4(this float[] floats)
        {
            if (floats.Length < 4) return Vector4.Zero;
            return new Vector4(floats[0], floats[1], floats[2], floats[3]);
        }

        public static Quaternion ToQuaternion(this float[] floats)
        {
            if (floats.Length < 4) return Quaternion.Identity;
            return new Quaternion(floats[0], floats[1], floats[2], floats[3]);
        }

        public static Vector3 ToNumeric(this Vec2 vec)
        {
            return new System.Numerics.Vector3(vec.x, vec.y, 0);
        }

        public static int T(this Triangle tri, int i)
        {
            return (new int[] { tri.v0, tri.v1, tri.v2 })[i];
        }
    }
}
