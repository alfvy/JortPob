using MeshDecimator;
using MeshDecimator.Algorithms;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace JortPob.Model
{
    public static class MeshSimplifier
    {
        /// <summary>
        /// Decimates a FLVER face set by a required amount and returns a new face set.
        /// </summary>
        /// <param name="mesh">The parent mesh containing the vertices.</param>
        /// <param name="sourceFaceSet">The face set to decimate.</param>
        /// <param name="targetReduction">The reduction factor (e.g., 0.5 for 50% reduction of triangles).</param>
        /// <returns>A new decimated face set.</returns>
        public static FLVER2.FaceSet Decimate(FLVER2.Mesh mesh, FLVER2.FaceSet sourceFaceSet, float targetReduction)
        {
            if (targetReduction <= 0) return sourceFaceSet;
            if (targetReduction >= 1.0f) targetReduction = 0.99f; // Don't allow 100% reduction

            // Extract vertices used by this face set
            var uniqueIndices = sourceFaceSet.Indices.Distinct().ToList();
            var indexMapping = new Dictionary<int, int>();
            var positions = new MeshDecimator.Math.Vector3d[uniqueIndices.Count];

            for (int i = 0; i < uniqueIndices.Count; i++)
            {
                int originalIndex = uniqueIndices[i];
                indexMapping[originalIndex] = i;
                var pos = mesh.Vertices[originalIndex].Position;
                positions[i] = new MeshDecimator.Math.Vector3d(pos.X, pos.Y, pos.Z);
            }

            // Extract triangles
            int triangleCount = sourceFaceSet.Indices.Count / 3;
            var indices = new int[sourceFaceSet.Indices.Count];
            for (int i = 0; i < sourceFaceSet.Indices.Count; i++)
            {
                indices[i] = indexMapping[sourceFaceSet.Indices[i]];
            }

            // Create MeshDecimator mesh
            var decimationMesh = new MeshDecimator.Mesh(positions, new int[][] { indices });

            // Use MeshDecimator
            var algorithm = new FastQuadricMeshSimplification();
            algorithm.Initialize(decimationMesh);

            // Quality refinements to prevent holes and preserve geometry
            algorithm.PreserveBorders = true;
            algorithm.PreserveFoldovers = true;
            algorithm.EnableSmartLink = true;

            int targetTriangleCount = (int)(triangleCount * (1.0f - targetReduction));
            if (targetTriangleCount < 1) targetTriangleCount = 1;

            algorithm.DecimateMesh(targetTriangleCount);

            var resultMesh = algorithm.ToMesh();
            var resultIndices = resultMesh.GetIndices(0);
            var resultPositions = resultMesh.Vertices;

            // Map decimated mesh back to FLVER
            FLVER2.FaceSet decimatedFaceSet = new FLVER2.FaceSet
            {
                CullBackfaces = sourceFaceSet.CullBackfaces,
                Unk06 = sourceFaceSet.Unk06,
                Flags = sourceFaceSet.Flags,
                TriangleStrip = false // We output triangles
            };

            Dictionary<int, int> newVertexMap = new Dictionary<int, int>();

            for (int i = 0; i < resultIndices.Length; i++)
            {
                int vertexIndex = resultIndices[i];
                if (!newVertexMap.TryGetValue(vertexIndex, out int meshVertexIndex))
                {
                    var pos = resultPositions[vertexIndex];
                    var newVertex = new FLVER.Vertex
                    {
                        Position = new Vector3((float)pos.x, (float)pos.y, (float)pos.z)
                    };

                    // Inherit attributes from the closest original vertex
                    // For now, we use a simple approach: take attributes from the first original vertex.
                    var originalVertex = FindClosestVertex(mesh, uniqueIndices, newVertex.Position);;
                    newVertex.Normal = originalVertex.Normal;
                    newVertex.UVs = new List<Vector3>(originalVertex.UVs);
                    newVertex.Tangents = new List<Vector4>(originalVertex.Tangents);
                    newVertex.Colors = new List<FLVER.VertexColor>(originalVertex.Colors);

                    meshVertexIndex = mesh.Vertices.Count;
                    mesh.Vertices.Add(newVertex);
                    newVertexMap[vertexIndex] = meshVertexIndex;
                }
                decimatedFaceSet.Indices.Add(meshVertexIndex);
            }

            return decimatedFaceSet;
        }

        private static FLVER.Vertex FindClosestVertex(FLVER2.Mesh mesh, List<int> candidateIndices, Vector3 position)
        {
            FLVER.Vertex best = mesh.Vertices[candidateIndices[0]];
            float bestDistSq = Vector3.DistanceSquared(best.Position, position);

            for (int i = 1; i < candidateIndices.Count; i++)
            {
                FLVER.Vertex v = mesh.Vertices[candidateIndices[i]];
                float distSq = Vector3.DistanceSquared(v.Position, position);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = v;
                }
            }

            return best;
        }
    }
}
