using JortPob.Common;
using SoulsFormats;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace JortPob.Model
{
    public partial class ModelConverter
    {
        public static TerrainInfo LANDSCAPEtoFLVER(MaterialContext materialContext, TerrainInfo terrainInfo, Landscape landscape, string outputFilename)
        {
            /* Create a blank FLVER configured for Elden Ring */
            FLVER2 flver = new();
            flver.Header.Version = 131098; // Elden Ring FLVER Version Number
            flver.Header.Unk5D = 0;        // Unk
            flver.Header.Unk68 = 4;        // Unk

            /* Add bones and nodes for FLVER */
            FLVER.Node rootNode = new();
            FLVER2.SkeletonSet skeletonSet = new();
            FLVER2.SkeletonSet.Bone rootBone = new(0);

            rootNode.Name = Path.GetFileNameWithoutExtension($"terrain-{terrainInfo.path}");
            skeletonSet.AllSkeletons.Add(rootBone);
            skeletonSet.BaseSkeleton.Add(rootBone);
            flver.Nodes.Add(rootNode);
            flver.Skeletons = skeletonSet;

            /* Generate material data */
            List<MaterialContext.MaterialInfo> materialInfo = materialContext.GenerateMaterials(landscape);
            foreach (MaterialContext.MaterialInfo mat in materialInfo)
            {
                flver.Materials.Add(mat.material);
                flver.GXLists.Add(mat.gx);
                flver.BufferLayouts.Add(mat.layout);
                foreach (TextureInfo info in mat.info)
                {
                    terrainInfo.textures.Add(info);
                }
            }

            /* Generate blank flver mesh and faceset */
            int i = 0;
            foreach (Landscape.Mesh landMesh in landscape.meshes)
            {
                FLVER2.Mesh flverMesh = new();
                flverMesh.NodeIndex = 0; // attach to rootnode
                flverMesh.MaterialIndex = i;

                /* Setup Vertex Buffer */
                FLVER2.VertexBuffer flverBuffer = new(0);
                flverBuffer.LayoutIndex = i++;
                flverMesh.VertexBuffers.Add(flverBuffer);

                /* Convert vertex data */
                foreach (Landscape.Vertex vertex in landMesh.vertices)
                {
                    FLVER.Vertex flverVertex = new();

                    /* Grab vertice position + normal */
                    Vector3 pos = new(vertex.position.X, vertex.position.Y, vertex.position.Z);
                    Vector3 norm = new(vertex.normal.X, vertex.normal.Y, vertex.normal.Z);

                    // Fromsoftware lives in the mirror dimension. I do not know why.
                    pos.X *= -1f;
                    norm.X *= -1f;

                    // Set ...
                    flverVertex.Position = pos;
                    flverVertex.Normal = norm;

                    Vector3 uvw = new(vertex.coordinate.X, -vertex.coordinate.Y, 0);
                    Vector3 blend = new(0f);
                    if (landMesh.textures.Count() >= 2 && vertex.texture == landMesh.textures[1].index) { blend.X = 1f; }
                    else if (landMesh.textures.Count() >= 3 && vertex.texture == landMesh.textures[2].index) { blend.Y = 1f; }
                    Vector3 blank = new(0, 0, 0);
                    flverVertex.UVs.Add(uvw);
                    flverVertex.UVs.Add(blend);  // Second UV channel is just used as a blender for the multimaterial.
                    flverVertex.UVs.Add(blank);      // I don't know why we need a third channel but SoulsFormat complains if it's not there so here ya go!

                    flverVertex.Bitangent = new Vector4(0, 0, 1, 1);  // @TODO: WRONG!
                    flverVertex.Tangents.Add(new Vector4(1, 0, 0, 1));  // @TODO: WRONG!

                    byte average = (byte)((float)(vertex.color.x + vertex.color.y + vertex.color.z) / 3f);
                    FLVER.VertexColor color = new(255, average, average, average); // Generically set value, elden ring vertex color support is shit garbage. we use a texture to handle this
                    flverVertex.Colors.Add(color);

                    flverMesh.Vertices.Add(flverVertex);
                }

                /* Convert indice data */
                FLVER2.FaceSet.FSFlags[] flags = new FLVER2.FaceSet.FSFlags[] { FLVER2.FaceSet.FSFlags.None, FLVER2.FaceSet.FSFlags.LodLevel1, FLVER2.FaceSet.FSFlags.LodLevel2 };
                for(int j=0;j<3;j++)
                {
                    List<int> indiceSet = landMesh.indices[j];
                    FLVER2.FaceSet flverFaces = new();
                    flverFaces.Flags = flags[j];
                    flverFaces.CullBackfaces = true;
                    flverFaces.Unk06 = 1;

                    foreach(int index in indiceSet)
                    {
                        flverFaces.Indices.Add(index);
                    }

                    flverMesh.FaceSets.Add(flverFaces);
                }

                flver.Meshes.Add(flverMesh);
            }

            /* Calculate bounding boxes */
            BoundingBoxSolver.FLVER(flver);

            /* Optimize flver */
            flver = FLVERUtil.Optimize(flver);

            /* Write flver */
            flver.Write(outputFilename);

            /* Generate collision obj */
            Obj obj = LANDSCAPEtoOBJ(landscape);
            Obj navObj = obj.collapse(Obj.CollisionMaterial.Stock).optimize();
            navObj.write(Path.ChangeExtension(outputFilename, ".obj"));
            List<Obj> objs = obj.split();            // due to an issue with OBJtoHKX we can only have one material per hkx so until that's fixed im splitting objs off their materials

            for (int j = 0; j < objs.Count; j++)
            {
                string objPath = outputFilename.Replace(".flver", $"_split{j}.obj");
                CollisionInfo collisionInfo = new($"ext{landscape.coordinate.x},{landscape.coordinate.y}_split{j}", $"terrain\\ext{landscape.coordinate.x},{landscape.coordinate.y}_split{j}.obj");
                terrainInfo.collision.Add(collisionInfo);
                objs[j] = objs[j].optimize();
                objs[j].write(objPath);
            }

            return terrainInfo;
        }
    }
}
