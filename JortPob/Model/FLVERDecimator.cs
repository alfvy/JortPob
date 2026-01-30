using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SoulsFormats;
using g3;
using SharpAssimp;

namespace JortPob.Model
{
    public class FLVERDecimator
    {
        // Decimation percentages - how much of the BASE MESH to keep
        private const float LOD1_PERCENTAGE = 0.1f;  // Keep 50% of base mesh faces
        private const float LOD2_PERCENTAGE = 0.05f;  // Keep 25% of base mesh faces

        /// <summary>
        /// Adds LOD1 and LOD2 facesets to an existing FLVER mesh using geometry3Sharp.
        /// Both LODs are decimated from the base mesh independently.
        /// </summary>
        public static void AddLODsToMesh(FLVER2.Mesh flverMesh)
        {
            if (flverMesh.Vertices.Count == 0 || flverMesh.FaceSets.Count == 0)
                return;

            // Convert FLVER mesh to DMesh3
            DMesh3 baseMesh = ConvertToDMesh3(flverMesh);

            // Generate LOD1 (50% of base) - use FAST mode for aggressive decimation
            DMesh3 lod1Mesh = DecimateMesh(baseMesh, LOD1_PERCENTAGE);
            if (lod1Mesh.TriangleCount > 0)
            {
                var lod1FaceSet = CreateFaceSetFromDMesh3(flverMesh, lod1Mesh, FLVER2.FaceSet.FSFlags.LodLevel1);
                flverMesh.FaceSets.Add(lod1FaceSet);
            }

            // Generate LOD2 (25% of base) - use FAST mode for even more aggressive decimation
            DMesh3 lod2Mesh = DecimateMesh(baseMesh, LOD2_PERCENTAGE);
            if (lod2Mesh.TriangleCount > 0)
            {
                var lod2FaceSet = CreateFaceSetFromDMesh3(flverMesh, lod2Mesh, FLVER2.FaceSet.FSFlags.LodLevel2);
                flverMesh.FaceSets.Add(lod2FaceSet);
            }
        }

        /// <summary>
        /// Converts a FLVER2 mesh to geometry3Sharp DMesh3 format
        /// </summary>
        private static DMesh3 ConvertToDMesh3(FLVER2.Mesh flverMesh)
        {
            DMesh3 mesh = new DMesh3(MeshComponents.VertexNormals | MeshComponents.VertexUVs);

            var baseFaceSet = flverMesh.FaceSets[0];
            var indices = baseFaceSet.Indices;

            // Add vertices
            var vertexMap = new Dictionary<int, int>();
            for (int i = 0; i < indices.Count; i++)
            {
                int originalIndex = indices[i];

                if (!vertexMap.ContainsKey(originalIndex))
                {
                    var v = flverMesh.Vertices[originalIndex];

                    // Convert position
                    Vector3d pos = new Vector3d(v.Position.X, v.Position.Y, v.Position.Z);
                    int newVertexId = mesh.AppendVertex(pos);

                    // Set normal
                    if (mesh.HasVertexNormals)
                    {
                        mesh.SetVertexNormal(newVertexId, new Vector3f(v.Normal.X, v.Normal.Y, v.Normal.Z));
                    }

                    // Set UV (use first UV channel)
                    if (mesh.HasVertexUVs && v.UVs.Count > 0)
                    {
                        mesh.SetVertexUV(newVertexId, new Vector2f(v.UVs[0].X, v.UVs[0].Y));
                    }

                    vertexMap[originalIndex] = newVertexId;
                }
            }

            // Add triangles
            for (int i = 0; i < indices.Count; i += 3)
            {
                int v0 = vertexMap[indices[i]];
                int v1 = vertexMap[indices[i + 1]];
                int v2 = vertexMap[indices[i + 2]];

                mesh.AppendTriangle(v0, v1, v2);
            }

            return mesh;
        }

        /// <summary>
        /// Decimates a DMesh3 to the target percentage using geometry3Sharp's Reducer.
        /// Can use FAST mode for more aggressive decimation or QUALITY mode for better results.
        /// </summary>
        private static DMesh3 DecimateMesh(DMesh3 sourceMesh, float percentageToKeep)
        {
            // Clone the mesh so we don't modify the original
            DMesh3 mesh = new DMesh3(sourceMesh);

            int targetVertexCount = (int)(mesh.TriangleCount * percentageToKeep);

            // Set up the reducer
            Reducer reducer = new Reducer(mesh);

            reducer.PreserveBoundaryShape = false;     // Don't preserve boundaries (faster)
            // reducer.ReduceToEdgeLength(0.0);               // No minimum edge length limit
            reducer.ProjectionMode = Reducer.TargetProjectionMode.Inline;          // Faster but less accurate

            // Perform reduction
            reducer.ReduceToVertexCount(targetVertexCount);

            return reducer.Mesh;
        }

        /// <summary>
        /// Creates a FLVER2 FaceSet from a decimated DMesh3
        /// </summary>
        private static FLVER2.FaceSet CreateFaceSetFromDMesh3(FLVER2.Mesh flverMesh,
            DMesh3 decimatedMesh, FLVER2.FaceSet.FSFlags lodFlag)
        {
            var faceSet = new FLVER2.FaceSet();
            var baseFaceSet = flverMesh.FaceSets[0];

            // Copy properties from base faceset
            faceSet.CullBackfaces = baseFaceSet.CullBackfaces;
            faceSet.Unk06 = baseFaceSet.Unk06;
            faceSet.TriangleStrip = false;
            faceSet.Flags = lodFlag;

            var indices = new List<int>();
            var vertexToIndex = new Dictionary<string, int>();

            // Create vertex lookup for existing vertices
            for (int i = 0; i < flverMesh.Vertices.Count; i++)
            {
                var v = flverMesh.Vertices[i];
                string key = MakeVertexKey(v.Position);
                if (!vertexToIndex.ContainsKey(key))
                    vertexToIndex[key] = i;
            }

            // Process each triangle in the decimated mesh
            foreach (int triangleId in decimatedMesh.TriangleIndices())
            {
                Index3i tri = decimatedMesh.GetTriangle(triangleId);

                // Get vertex data from DMesh3
                Vector3d p0 = decimatedMesh.GetVertex(tri.a);
                Vector3d p1 = decimatedMesh.GetVertex(tri.b);
                Vector3d p2 = decimatedMesh.GetVertex(tri.c);

                Vector3f n0 = decimatedMesh.HasVertexNormals ? decimatedMesh.GetVertexNormal(tri.a) : Vector3f.AxisY;
                Vector3f n1 = decimatedMesh.HasVertexNormals ? decimatedMesh.GetVertexNormal(tri.b) : Vector3f.AxisY;
                Vector3f n2 = decimatedMesh.HasVertexNormals ? decimatedMesh.GetVertexNormal(tri.c) : Vector3f.AxisY;

                Vector2f uv0 = decimatedMesh.HasVertexUVs ? decimatedMesh.GetVertexUV(tri.a) : Vector2f.Zero;
                Vector2f uv1 = decimatedMesh.HasVertexUVs ? decimatedMesh.GetVertexUV(tri.b) : Vector2f.Zero;
                Vector2f uv2 = decimatedMesh.HasVertexUVs ? decimatedMesh.GetVertexUV(tri.c) : Vector2f.Zero;

                // Find or add vertices
                int idx0 = FindOrAddVertex(flverMesh, vertexToIndex, p0, n0, uv0);
                int idx1 = FindOrAddVertex(flverMesh, vertexToIndex, p1, n1, uv1);
                int idx2 = FindOrAddVertex(flverMesh, vertexToIndex, p2, n2, uv2);

                indices.Add(idx0);
                indices.Add(idx1);
                indices.Add(idx2);
            }

            faceSet.Indices = indices;
            return faceSet;
        }

        /// <summary>
        /// Creates a position-based key for vertex lookup
        /// </summary>
        private static string MakeVertexKey(Vector3 position)
        {
            return $"{position.X:F6},{position.Y:F6},{position.Z:F6}";
        }

        /// <summary>
        /// Finds an existing vertex or adds a new one to the FLVER mesh
        /// </summary>
        private static int FindOrAddVertex(FLVER2.Mesh mesh,
            Dictionary<string, int> vertexToIndex,
            Vector3d position, Vector3f normal, Vector2f uv)
        {
            Vector3 pos = new Vector3((float)position.x, (float)position.y, (float)position.z);
            string key = MakeVertexKey(pos);

            // Try to find existing vertex with same position
            if (vertexToIndex.TryGetValue(key, out int existingIdx))
            {
                return existingIdx;
            }

            // Create new vertex
            var vertex = new FLVER.Vertex();
            vertex.Position = pos;
            vertex.Normal = new Vector3(normal.x, normal.y, normal.z);

            // Calculate tangent and bitangent (simple approach)
            Vector3 tangent = Math.Abs(normal.y) < 0.999f
                ? Vector3.Normalize(Vector3.Cross(new Vector3(0, 1, 0), vertex.Normal))
                : new Vector3(1, 0, 0);
            Vector3 bitangent = Vector3.Normalize(Vector3.Cross(vertex.Normal, tangent));

            vertex.Tangents = new List<Vector4> { new Vector4(tangent, 0) };
            vertex.Bitangent = new Vector4(bitangent, 0);
            vertex.UVs = new List<Vector3> { new Vector3(uv.x, uv.y, 0) };
            vertex.Colors = new List<FLVER.VertexColor> { new FLVER.VertexColor(255, 255, 255, 255) };

            int newIdx = mesh.Vertices.Count;
            mesh.Vertices.Add(vertex);
            vertexToIndex[key] = newIdx;

            return newIdx;
        }
    }

    public static class G3Extensions
    {
        public static Vector3d d(this Vector3 vec) => new Vector3d(vec.X, vec.Y, vec.Z);
        public static Vector3f f(this Vector3 vec) => new Vector3f(vec.X, vec.Y, vec.Z);
        public static Vector3f f(this FLVER.VertexColor vec) => new Vector3f(vec.R, vec.G, vec.B);
        public static Vector2f f2(this Vector3 vec) => new Vector2f(vec.X, vec.Y);

        public static Vector3 n(this Vector3d vec) => new Vector3(((float)vec.x), ((float)vec.y), ((float)vec.z));
        public static Vector3 n(this Vector3f vec) => new Vector3(vec.x, vec.y, vec.z);
        public static Vector3 n2(this Vector2f vec) => new Vector3(vec.x, vec.y, 0);
        public static FLVER.VertexColor nc(this Vector3f vec) => new FLVER.VertexColor(1, vec.x, vec.y, vec.z);
    }
}
