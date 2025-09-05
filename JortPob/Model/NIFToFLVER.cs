using JortPob.Common;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TES3;

namespace JortPob.Model
{
    public partial class ModelConverter
    {
        public static ModelInfo NIFToFLVER(MaterialContext materialContext, ModelInfo modelInfo, bool forceCollision, string modelPath, string outputPath)
        {
            var loadResult = TES3.Interop.LoadScene(Utf8String.From(modelPath));

            if (loadResult.IsErr)
            {
                Console.WriteLine("Error loading scene");
                return null;
            }

            var nif = loadResult.AsOk();
            var meshes = nif.VisualMeshes;

            var flver = new FLVER2();

            // Set header defaults (these values work fine for modern DS3/Elden Ring style FLVERs)
            flver.Header.Version = 131098;
            flver.Header.Unk5D = 0;
            flver.Header.Unk68 = 4;

            // Root node
            var rootNode = new FLVER.Node { Name = "Root" };
            flver.Nodes.Add(rootNode);

            // must have a skeleton
            FLVER2.SkeletonSet.Bone rootBone = new(0);
            flver.Skeletons = new();
            flver.Skeletons.AllSkeletons.Add(rootBone);
            flver.Skeletons.BaseSkeleton.Add(rootBone);

            var layout = new FLVER2.BufferLayout();
            layout.Add(new FLVER.LayoutMember(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position));
            layout.Add(new FLVER.LayoutMember(FLVER.LayoutType.Byte4B, FLVER.LayoutSemantic.Normal));
            layout.Add(new FLVER.LayoutMember(FLVER.LayoutType.Float4, FLVER.LayoutSemantic.VertexColor));
            layout.Add(new FLVER.LayoutMember(FLVER.LayoutType.Float2, FLVER.LayoutSemantic.UV));
            flver.BufferLayouts.Add(layout);

            List<Utf8String> textures = new();
            
            // Convert each NifMesh
            for (int i = 0; i < meshes.Count; i++)
            {
                var mesh = meshes[i];

                textures.Add(mesh.Texture);

                var newMesh = new FLVER2.Mesh
                {
                    NodeIndex = 0,
                    MaterialIndex = 0
                };

                // Vertex buffer (layout index 0 since we added one above)
                var vbuf = new FLVER2.VertexBuffer(0) { LayoutIndex = 0 };
                newMesh.VertexBuffers.Add(vbuf);

                var verts = new List<FLVER.Vertex>();

                for (int j = 0; j < mesh.Vertices.Count; j++)
                {
                    var v = new FLVER.Vertex();

                    // Position
                    v.Position = RotateZUp(mesh.Vertices[j].ToNumeric() * Const.GLOBAL_SCALE); // optional scale

                    // Normal
                    if (mesh.Normals != null && j < mesh.Normals.Count)
                        v.Normal = RotateZUp(mesh.Normals[j].ToNumeric());
                    else
                        v.Normal = Vector3.UnitY;

                    v.Tangents.Add(new Vector4(1, 0, 0, 0));
                    v.Bitangent = new Vector4(0, 0, 0, 1);

                    // UV0
                    if (mesh.UvSet0 != null && j < mesh.UvSet0.Count)
                    {
                        var uv = mesh.UvSet0[j];
                        v.UVs.Add(new Vector3(uv.x, 1 - uv.y, 0)); // flip V like FBX pipeline
                    }
                    else
                        v.UVs.Add(new Vector3(0, 0, 0));

                    // Vertex color
                    if (mesh.Colors != null && j < mesh.Colors.Count)
                    {
                        var c = mesh.Colors[j];
                        v.Colors.Add(new FLVER.VertexColor(
                            (byte)(c.x * 255),
                            (byte)(c.y * 255),
                            (byte)(c.z * 255),
                            (byte)(c.w * 255)));
                    }
                    else
                    {
                        v.Colors.Add(new FLVER.VertexColor(255, 255, 255, 255));
                    }

                    verts.Add(v);
                }

                newMesh.Vertices = verts;
                var vertCount = mesh.Vertices.Count;
                // Faceset: groups the triangle indices
                var faceSet = new FLVER2.FaceSet
                {
                    TriangleStrip = false,
                    CullBackfaces = true,
                    Unk06 = 1,
                };
                // Add indices (triangles)
                for (int idx = 0; idx < mesh.Triangles.Count; idx++)
                {
                    var tri = mesh.Triangles[idx];
                    if (tri.v0 < vertCount && tri.v1 < vertCount && tri.v2 < vertCount)
                    {
                        faceSet.Indices.Add(tri.v0);
                        faceSet.Indices.Add(tri.v1);
                        faceSet.Indices.Add(tri.v2);
                    }
                }
                newMesh.FaceSets.Add(faceSet);
                Console.WriteLine($"Mesh built: {mesh.Vertices.Count} verts, {faceSet.Indices.Count / 3} faces");

                flver.Meshes.Add(newMesh);
            }

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
                    foreach (var mat in textures)
                    {
                        foreach (string key in keys)
                        {
                            if (Utility.PathToFileName(modelInfo.name).ToLower().Contains(key)) { matguess = type; return; }
                            if (mat.String.ToLower().Contains(key)) { matguess = type; return; }
                            if (mat.String != null && Utility.PathToFileName(mat.String).ToLower().Contains(key)) { matguess = type; return; }
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

            GetSceneBoundingBox(nif, out var min, out var max);

            var size = Vector3.Distance(min, max);
            modelInfo.size = size;

            flver.Write(outputPath, DCX.Type.DCX_KRAK);

            return modelInfo;

            Vector3 RotateZUp(Vector3 v)
            {
                return new Vector3(
                    v.X,
                    v.Z,
                    v.Y
                );
            }

            void GetSceneBoundingBox(Scene scene, out Vector3 min, out Vector3 max)
            {
                min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                for (int i = 0; i > scene.VisualMeshes.Count; i++)
                {
                    var mesh = scene.VisualMeshes[i];
                    for (int j = 0; j < mesh.Vertices.Count; j++)
                    {
                        var vertex = mesh.Vertices[j];
                        if (vertex.x < min.X) min.X = vertex.x;
                        if (vertex.y < min.Y) min.Y = vertex.y;
                        if (vertex.z < min.Z) min.Z = vertex.z;

                        if (vertex.x > max.X) max.X = vertex.x;
                        if (vertex.y > max.Y) max.Y = vertex.y;
                        if (vertex.z > max.Z) max.Z = vertex.z;
                    }
                }
            }
        }
    }

    public static class Vec3Extensions
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
    }
}
