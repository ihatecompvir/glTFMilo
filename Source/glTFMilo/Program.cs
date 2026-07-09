using glTFMilo.Source;
using MiloGLTFUtils.Source.glTFMilo.Core;
using MiloGLTFUtils.Source.Shared;
using MiloLib;
using MiloLib.Assets;
using MiloLib.Assets.Char;
using MiloLib.Assets.P9;
using MiloLib.Assets.Rnd;
using MiloLib.Classes;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using System;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using TeximpNet;
using TeximpNet.Compression;

namespace MiloGLTFUtils.Source.glTFMilo
{
    public class Program
    {
        private const int MaxMeshInfluencingBones = 40;
        private const int MaxMeshVertices = ushort.MaxValue;

        private readonly struct SkinInfluence
        {
            public SkinInfluence(int jointIndex, float weight)
            {
                idx = jointIndex;
                this.weight = weight;
            }

            public int idx { get; }
            public float weight { get; }
        }

        private sealed class SkinWarningState
        {
            // used to signify we've already warned about this so we don't produce a bunch of log spam
            public bool loggedInvalidWeights { get; set; }
            public bool loggedInvalidJointIndices { get; set; }
            public bool loggedTrimmedInfluences { get; set; }
            public bool loggedExcludedJointInfluences { get; set; }
        }

        private readonly struct SourceTriangle
        {
            public SourceTriangle(uint index0, uint index1, uint index2)
            {
                idx0 = index0;
                idx1 = index1;
                idx2 = index2;
            }

            public uint idx0 { get; }
            public uint idx1 { get; }
            public uint idx2 { get; }
        }

        private sealed class MeshChunk
        {
            private readonly HashSet<int> _jointIndexSet = new();
            private readonly HashSet<uint> _vertexIndexSet = new();

            public List<SourceTriangle> tris { get; } = new();
            public List<int> jointIndices { get; } = new();
            public int uniqueVertexCount => _vertexIndexSet.Count;

            public int AdditionalJointCount(IReadOnlyCollection<int> triangleJointIndices)
            {
                int additionalJointCount = 0;
                foreach (int jointIndex in triangleJointIndices)
                {
                    if (!_jointIndexSet.Contains(jointIndex))
                    {
                        additionalJointCount++;
                    }
                }

                return additionalJointCount;
            }

            public int AdditionalVertexCount(SourceTriangle triangle)
            {
                int additionalVertexCount = 0;
                if (!_vertexIndexSet.Contains(triangle.idx0)) additionalVertexCount++;
                if (triangle.idx1 != triangle.idx0 && !_vertexIndexSet.Contains(triangle.idx1)) additionalVertexCount++;
                if (triangle.idx2 != triangle.idx0 && triangle.idx2 != triangle.idx1 && !_vertexIndexSet.Contains(triangle.idx2)) additionalVertexCount++;

                return additionalVertexCount;
            }

            public bool CanAddTriangle(SourceTriangle triangle, IReadOnlyCollection<int> triangleJointIndices, int maxJointCount, int maxVertexCount)
            {
                return jointIndices.Count + AdditionalJointCount(triangleJointIndices) <= maxJointCount &&
                    uniqueVertexCount + AdditionalVertexCount(triangle) <= maxVertexCount;
            }

            public void AddTriangle(SourceTriangle triangle, IReadOnlyCollection<int> triangleJointIndices)
            {
                tris.Add(triangle);
                _vertexIndexSet.Add(triangle.idx0);
                _vertexIndexSet.Add(triangle.idx1);
                _vertexIndexSet.Add(triangle.idx2);

                foreach (int jointIndex in triangleJointIndices)
                {
                    if (_jointIndexSet.Add(jointIndex))
                    {
                        jointIndices.Add(jointIndex);
                    }
                }
            }
        }

        // TODO: move these into their own file, putting them in Program.cs feels like unnecessary bloat
        private static bool IsHairBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return false;
            }
            else
            {
                if (boneName.StartsWith("bone_hair_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ValidateSkinAccessorSet(string meshName, string accessorSuffix, int expectedCount, ref IList<System.Numerics.Vector4>? joints, ref IList<System.Numerics.Vector4>? weights)
        {
            string jointsName = $"JOINTS_{accessorSuffix}";
            string weightsName = $"WEIGHTS_{accessorSuffix}";

            if (joints == null && weights == null)
            {
                return false;
            }

            if (joints == null || weights == null)
            {
                string presentAccessor = joints == null ? weightsName : jointsName;
                string missingAccessor = joints == null ? jointsName : weightsName;
                Logger.Warn($"Mesh {meshName} has {presentAccessor} without matching {missingAccessor}. {jointsName}/{weightsName} will be ignored.");
                joints = null;
                weights = null;
                return false;
            }

            if (joints.Count != weights.Count)
            {
                Logger.Warn($"Mesh {meshName} has mismatched {jointsName} ({joints.Count}) and {weightsName} ({weights.Count}) counts. {jointsName}/{weightsName} will be ignored.");
                joints = null;
                weights = null;
                return false;
            }

            if (joints.Count != expectedCount)
            {
                Logger.Warn($"Mesh {meshName} has {jointsName}/{weightsName} count {joints.Count}, but POSITION count is {expectedCount}. {jointsName}/{weightsName} will be ignored.");
                joints = null;
                weights = null;
                return false;
            }

            return true;
        }

        private static void AddValidatedSkinInfluence(List<SkinInfluence> influences, float jointValue, float weight, int skinJointCount, string meshName, string accessorName, HashSet<int> excludedJointIndices, SkinWarningState warningState)
        {
            // sanity check the weight and joint values first so we can let people know their shit is fucked

            if (!float.IsFinite(weight))
            {
                if (!warningState.loggedInvalidWeights)
                {
                    Logger.Warn($"Mesh {meshName} has invalid skin weights in {accessorName}. Affected influences will be ignored.");
                    warningState.loggedInvalidWeights = true;
                }

                return;
            }

            if (weight <= 0.0f) return;

            if (!float.IsFinite(jointValue))
            {
                if (!warningState.loggedInvalidJointIndices)
                {
                    Logger.Warn($"Mesh {meshName} has invalid joint indices in {accessorName}. Affected influences will be ignored.");
                    warningState.loggedInvalidJointIndices = true;
                }

                return;
            }

            int jointIndex = (int)MathF.Round(jointValue);
            if (MathF.Abs(jointValue - jointIndex) > 0.001f || jointIndex < 0 || jointIndex >= skinJointCount)
            {
                if (!warningState.loggedInvalidJointIndices)
                {
                    Logger.Warn($"Mesh {meshName} has invald joint indices in {accessorName}. Affected influences will be ignored.");
                    warningState.loggedInvalidJointIndices = true;
                }

                return;
            }

            // joints like neutral_bone never get a Trans or a bone transform, letting influences point at them would misalign every bone index after their slot
            if (excludedJointIndices.Contains(jointIndex))
            {
                if (!warningState.loggedExcludedJointInfluences)
                {
                    Logger.Warn($"Mesh {meshName} has influences on 'neutral_bone', which does not exist in the exported milo. Affected influences will be ignored.");
                    warningState.loggedExcludedJointInfluences = true;
                }

                return;
            }

            influences.Add(new SkinInfluence(jointIndex, weight));
        }

        private static List<SkinInfluence> GetVertexSkinInfluences(IList<System.Numerics.Vector4>? joints0, IList<System.Numerics.Vector4>? weights0, IList<System.Numerics.Vector4>? joints1, IList<System.Numerics.Vector4>? weights1, int vertexIndex, int skinJointCount, string meshName, HashSet<int> excludedJointIndices, SkinWarningState warningState)
        {
            var influences = new List<SkinInfluence>(8);

            if (joints0 != null && weights0 != null && vertexIndex < joints0.Count && vertexIndex < weights0.Count)
            {
                var joint = joints0[vertexIndex];
                var weight = weights0[vertexIndex];

                AddValidatedSkinInfluence(influences, joint.X, weight.X, skinJointCount, meshName, "JOINTS_0/WEIGHTS_0", excludedJointIndices, warningState);
                AddValidatedSkinInfluence(influences, joint.Y, weight.Y, skinJointCount, meshName, "JOINTS_0/WEIGHTS_0", excludedJointIndices, warningState);
                AddValidatedSkinInfluence(influences, joint.Z, weight.Z, skinJointCount, meshName, "JOINTS_0/WEIGHTS_0", excludedJointIndices, warningState);
                AddValidatedSkinInfluence(influences, joint.W, weight.W, skinJointCount, meshName, "JOINTS_0/WEIGHTS_0", excludedJointIndices, warningState);
            }

            if (joints1 != null && weights1 != null && vertexIndex < joints1.Count && vertexIndex < weights1.Count)
            {
                var joint = joints1[vertexIndex];
                var weight = weights1[vertexIndex];

                AddValidatedSkinInfluence(influences, joint.X, weight.X, skinJointCount, meshName, "JOINTS_1/WEIGHTS_1", excludedJointIndices, warningState);
                AddValidatedSkinInfluence(influences, joint.Y, weight.Y, skinJointCount, meshName, "JOINTS_1/WEIGHTS_1", excludedJointIndices, warningState);
                AddValidatedSkinInfluence(influences, joint.Z, weight.Z, skinJointCount, meshName, "JOINTS_1/WEIGHTS_1", excludedJointIndices, warningState);
                AddValidatedSkinInfluence(influences, joint.W, weight.W, skinJointCount, meshName, "JOINTS_1/WEIGHTS_1", excludedJointIndices, warningState);
            }

            var orderedInfluences = influences.OrderByDescending(influence => influence.weight).ToList();

            if (orderedInfluences.Count > 4 && !warningState.loggedTrimmedInfluences)
            {
                int droppedInfluenceCount = orderedInfluences.Count - 4;
                float droppedWeight = orderedInfluences.Skip(4).Sum(influence => influence.weight);
                Logger.Warn($"Mesh {meshName} has vertices with more than 4 valid skin influences. Extra influences will be dropped during export (first affected vertex dropped {droppedInfluenceCount} influence(s), total dropped weight {droppedWeight:0.###}).");
                warningState.loggedTrimmedInfluences = true;
            }

            var trimmedInfluences = orderedInfluences.Take(4).ToList();

            // milo wants the four weights normalized after we throw away extras
            float totalWeight = trimmedInfluences.Sum(influence => influence.weight);
            if (totalWeight > 0.0f)
            {
                for (int i = 0; i < trimmedInfluences.Count; i++)
                {
                    var influence = trimmedInfluences[i];
                    trimmedInfluences[i] = new SkinInfluence(influence.idx, influence.weight / totalWeight);
                }
            }

            return trimmedInfluences;
        }

        private static RndMesh CreateBaseMesh(MiloGame selectedGame, string platform, string parentName, Node node, MeshPrimitive primitive, bool convertCoordinates)
        {
            var mesh = RndMesh.New(GameRevisions.GetRevision(selectedGame).ModelRevision, 0, 0, 0);
            mesh.objFields.revision = 2;
            mesh.trans = RndTrans.New(9, 0);
            mesh.trans.parentObj = parentName;
            mesh.draw = RndDrawable.New(3, 0);
            mesh.draw.sphere = new Sphere();
            mesh.draw.sphere.radius = 0.0f;
            mesh.volume = RndMesh.Volume.kVolumeTriangles;
            mesh.keepMeshData = true;
            mesh.hasAOCalculation = false;

            MatrixHelpers.CopyMatrix(node.LocalMatrix, mesh.trans.localXfm, convertCoordinates);
            MatrixHelpers.CopyMatrix(node.WorldMatrix, mesh.trans.worldXfm, convertCoordinates);

            if (selectedGame == MiloGame.RockBand3 || selectedGame == MiloGame.DanceCentral1)
            {
                if (platform == "xbox")
                {
                    mesh.vertices.isNextGen = true;
                    mesh.vertices.compressionType = 1;
                    mesh.vertices.vertexSize = 36;
                }
                else if (platform == "ps3")
                {
                    mesh.vertices.isNextGen = true;
                    mesh.vertices.compressionType = 2;
                    mesh.vertices.vertexSize = 40;
                }
            }

            if (primitive.Material != null)
            {
                mesh.mat = primitive.Material.Name + ".mat";
                bool hasDiffuse = primitive.Material.FindChannel("BaseColor")?.Texture != null;
                bool hasNormal = primitive.Material.FindChannel("Normal")?.Texture != null;
                bool hasSpecular = primitive.Material.FindChannel("SpecularColor")?.Texture != null;

                if (!hasDiffuse || (!hasDiffuse && !hasNormal && !hasSpecular))
                {
                    Logger.Error($"Mesh {node.Name} is missing a diffuse map or has no maps at all! The model will likely appear black in-game!");
                }

                if (hasNormal)
                {
                    mesh.hasAOCalculation = true;
                }
            }

            return mesh;
        }

        private static List<SourceTriangle> BuildSourceTriangles(IntegerArray? indices, int positionCount, string meshName)
        {
            var triangles = new List<SourceTriangle>();
            uint maxPositionIndex = (uint)positionCount;

            if (indices == null || indices.Value.Count == 0)
            {
                if (positionCount % 3 != 0)
                {
                    Logger.Warn($"Mesh {meshName} has no index buffer and POSITION count {positionCount} is not divisible by 3. Trailing vertices will be ignored.");
                }

                for (int i = 0; i + 2 < positionCount; i += 3)
                {
                    triangles.Add(new SourceTriangle((uint)i, (uint)(i + 1), (uint)(i + 2)));
                }

                return triangles;
            }

            if (indices.Value.Count % 3 != 0)
            {
                Logger.Warn($"Mesh {meshName} has index count {indices.Value.Count} that is not divisible by 3. Trailing indices will be ignored.");
            }

            bool loggedInvalidIndex = false;
            for (int i = 0; i + 2 < indices.Value.Count; i += 3)
            {
                uint index0 = indices.Value[i];
                uint index1 = indices.Value[i + 1];
                uint index2 = indices.Value[i + 2];

                if (index0 >= maxPositionIndex || index1 >= maxPositionIndex || index2 >= maxPositionIndex)
                {
                    if (!loggedInvalidIndex)
                    {
                        Logger.Warn($"Mesh {meshName} has indices that point outside the POSITION accessor. Affected triangles will be ignored.");
                        loggedInvalidIndex = true;
                    }

                    continue;
                }

                triangles.Add(new SourceTriangle(index0, index1, index2));
            }

            return triangles;
        }

        private static List<MeshChunk> SplitTrianglesIntoMeshChunks(List<SourceTriangle> triangles, IReadOnlyList<List<SkinInfluence>> vertexSkinInfluences, string meshName)
        {
            // absolute dogshit code alert, this is an attempt to split the mesh into chunks that each fit within the bone and vert limits using a greedy mesh partitioner
            // it seems to work well in-game, although there still might be seaming issues in some cases

            var chunks = new List<MeshChunk>();

            // precompute the joint indices used by each triangle so we don't have to keep looking them up during the splitting process

            var triangleJointIndices = new List<List<int>>(triangles.Count);
            foreach (var triangle in triangles)
            {
                var currentTriangleJointIndices = new List<int>(12);
                var seenJointIndices = new HashSet<int>();

                foreach (uint vertexIndex in new[] { triangle.idx0, triangle.idx1, triangle.idx2 })
                {
                    foreach (var influence in vertexSkinInfluences[(int)vertexIndex])
                    {
                        if (seenJointIndices.Add(influence.idx))
                        {
                            currentTriangleJointIndices.Add(influence.idx);
                        }
                    }
                }

                // check that this triangle doesn't reference more joints than the maximum allowed, otherwise it can never fit in any chunk and we have to bail
                if (currentTriangleJointIndices.Count > MaxMeshInfluencingBones)
                {
                    throw new InvalidDataException(
                        $"{meshName} has a triangle that references more than {MaxMeshInfluencingBones} bones. Export cannot continue.");
                }

                triangleJointIndices.Add(currentTriangleJointIndices);
            }

            // if the entire mesh already fits, just don't chunk it
            var fullMeshChunk = new MeshChunk();
            for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                fullMeshChunk.AddTriangle(triangles[triangleIndex], triangleJointIndices[triangleIndex]);
            }

            if (fullMeshChunk.jointIndices.Count <= MaxMeshInfluencingBones &&
                fullMeshChunk.uniqueVertexCount <= MaxMeshVertices)
            {
                return [fullMeshChunk];
            }

            // build an egde -> triangle lookup with normalizaton so a, b and b, a refer to the same shared edge
            // used to find triangles that are neighbors across actual poly edges
            var edgeToTriangleIndices = new Dictionary<(uint, uint), List<int>>();
            for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                var triangle = triangles[triangleIndex];

                foreach (var edge in new[]
                {
            triangle.idx0 < triangle.idx1 ? (triangle.idx0, triangle.idx1) : (triangle.idx1, triangle.idx0),
            triangle.idx1 < triangle.idx2 ? (triangle.idx1, triangle.idx2) : (triangle.idx2, triangle.idx1),
            triangle.idx2 < triangle.idx0 ? (triangle.idx2, triangle.idx0) : (triangle.idx0, triangle.idx2)
        })
                {
                    if (!edgeToTriangleIndices.TryGetValue(edge, out var edgeTriangleIndices))
                    {
                        edgeTriangleIndices = new List<int>();
                        edgeToTriangleIndices.Add(edge, edgeTriangleIndices);
                    }

                    edgeTriangleIndices.Add(triangleIndex);
                }
            }

            // convert all the shared edges into triangle adjacency
            // two tris are considered neighbors only if they share an edge, not merely vertices
            var adjacencySets = new List<HashSet<int>>(triangles.Count);
            for (int i = 0; i < triangles.Count; i++)
            {
                adjacencySets.Add(new HashSet<int>());
            }

            foreach (var edgeTriangleIndices in edgeToTriangleIndices.Values)
            {
                if (edgeTriangleIndices.Count <= 1)
                {
                    continue;
                }

                for (int i = 0; i < edgeTriangleIndices.Count; i++)
                {
                    int triangleIndex = edgeTriangleIndices[i];

                    for (int j = 0; j < edgeTriangleIndices.Count; j++)
                    {
                        if (i == j)
                        {
                            continue;
                        }

                        adjacencySets[triangleIndex].Add(edgeTriangleIndices[j]);
                    }
                }
            }

            var triangleAdjacency = adjacencySets.Select(adjacencySet => adjacencySet.ToList()).ToList();
            var assignedTriangles = new bool[triangles.Count];
            int remainingTriangleCount = triangles.Count;

            while (remainingTriangleCount > 0)
            {
                // start each chunk with the unassigned triangle that already uses the most joints
                int seedTriangleIndex = -1;
                int bestSeedJointCount = -1;

                for (int triangleIndex = 0; triangleIndex < triangleJointIndices.Count; triangleIndex++)
                {
                    if (assignedTriangles[triangleIndex])
                    {
                        continue;
                    }

                    int jointCount = triangleJointIndices[triangleIndex].Count;
                    if (jointCount > bestSeedJointCount)
                    {
                        seedTriangleIndex = triangleIndex;
                        bestSeedJointCount = jointCount;
                    }
                }

                if (seedTriangleIndex < 0)
                {
                    break;
                }

                var chunk = new MeshChunk();
                chunk.AddTriangle(triangles[seedTriangleIndex], triangleJointIndices[seedTriangleIndex]);
                assignedTriangles[seedTriangleIndex] = true;
                remainingTriangleCount--;

                // grow the chunk through neighboring triangles first, to try to preserve locality
                var frontier = new HashSet<int>(triangleAdjacency[seedTriangleIndex].Where(index => !assignedTriangles[index]));

                while (frontier.Count > 0)
                {
                    int bestTriangleIndex = -1;
                    int bestAdditionalJointCount = int.MaxValue;
                    int bestAdditionalVertexCount = int.MaxValue;

                    foreach (int triangleIndex in frontier)
                    {
                        var triangle = triangles[triangleIndex];
                        var currentTriangleJointIndices = triangleJointIndices[triangleIndex];

                        if (!chunk.CanAddTriangle(
                            triangle,
                            currentTriangleJointIndices,
                            MaxMeshInfluencingBones,
                            MaxMeshVertices))
                        {
                            continue;
                        }

                        // prefer the adjacent candidate that adds the fewest new joints, if tie, prefer the one with the fewest new verts
                        int additionalJointCount = chunk.AdditionalJointCount(currentTriangleJointIndices);
                        int additionalVertexCount = chunk.AdditionalVertexCount(triangle);

                        if (additionalJointCount < bestAdditionalJointCount ||
                            (additionalJointCount == bestAdditionalJointCount &&
                             additionalVertexCount < bestAdditionalVertexCount))
                        {
                            bestTriangleIndex = triangleIndex;
                            bestAdditionalJointCount = additionalJointCount;
                            bestAdditionalVertexCount = additionalVertexCount;
                        }
                    }

                    // no adjacent tris can fit in this chunk, so break
                    if (bestTriangleIndex < 0)
                    {
                        break;
                    }

                    frontier.Remove(bestTriangleIndex);
                    chunk.AddTriangle(triangles[bestTriangleIndex], triangleJointIndices[bestTriangleIndex]);
                    assignedTriangles[bestTriangleIndex] = true;
                    remainingTriangleCount--;

                    // add newly exposed neighbors to the frontier so the chunk can continue growing
                    foreach (int adjacentTriangleIndex in triangleAdjacency[bestTriangleIndex])
                    {
                        if (!assignedTriangles[adjacentTriangleIndex])
                        {
                            frontier.Add(adjacentTriangleIndex);
                        }
                    }
                }

                // do a global fill pass, trying to use up any remaining chunk capacity with non-adjacent tris that still fit
                // basically we are trying to reduce the number of chunks emitted
                // this might still produce non-connected chunks/islands, but it should still be fine?
                int globalSearchStart = 0;
                while (remainingTriangleCount > 0 && chunk.uniqueVertexCount < MaxMeshVertices)
                {
                    int bestTriangleIndex = -1;

                    for (int offset = 0; offset < triangles.Count; offset++)
                    {
                        int triangleIndex = (globalSearchStart + offset) % triangles.Count;

                        if (assignedTriangles[triangleIndex])
                        {
                            continue;
                        }

                        var triangle = triangles[triangleIndex];
                        var currentTriangleJointIndices = triangleJointIndices[triangleIndex];

                        if (!chunk.CanAddTriangle(
                            triangle,
                            currentTriangleJointIndices,
                            MaxMeshInfluencingBones,
                            MaxMeshVertices))
                        {
                            continue;
                        }

                        bestTriangleIndex = triangleIndex;
                        globalSearchStart = (triangleIndex + 1) % triangles.Count;
                        break;
                    }

                    // there is nothing left, just break
                    if (bestTriangleIndex < 0)
                    {
                        break;
                    }

                    chunk.AddTriangle(triangles[bestTriangleIndex], triangleJointIndices[bestTriangleIndex]);
                    assignedTriangles[bestTriangleIndex] = true;
                    remainingTriangleCount--;
                }

                chunks.Add(chunk);
            }

            return chunks;

        }

        private static ushort GetRemappedBoneIndex(int jointIndex, Dictionary<int, ushort> jointIndexToLocalBoneIndex)
        {
            return jointIndexToLocalBoneIndex.TryGetValue(jointIndex, out ushort localBoneIndex) ? localBoneIndex: ushort.MaxValue;
        }

        // holy long function signature batman
        // TODO: refactor this
        private static void AddVertexToChunkMesh(RndMesh mesh, uint originalIndex, Dictionary<uint, ushort> originalIndexToNewIndex,IList<System.Numerics.Vector3> positions, IList<System.Numerics.Vector3>? normals, IList<System.Numerics.Vector2>? uvs, IList<System.Numerics.Vector4>? tangents, IList<System.Numerics.Vector4>? colors, IReadOnlyList<List<SkinInfluence>> vertexSkinInfluences, Dictionary<int, ushort> jointIndexToLocalBoneIndex, Skin? meshSkin, bool convertCoordinates)
        {
            if (originalIndexToNewIndex.ContainsKey(originalIndex))
            {
                return;
            }

            Vertex newVert = new Vertex();
            var pos = positions[(int)originalIndex];
            if (convertCoordinates)
            {
                pos = MatrixHelpers.ConvertGltfVectorToMilo(pos);
            }

            newVert.x = pos.X;
            newVert.y = pos.Y;
            newVert.z = pos.Z;

            if (uvs != null && originalIndex < uvs.Count)
            {
                var uv = uvs[(int)originalIndex];
                newVert.u = uv.X;
                newVert.v = uv.Y;
            }

            if (normals != null && originalIndex < normals.Count)
            {
                var normal = normals[(int)originalIndex];
                if (convertCoordinates)
                {
                    normal = MatrixHelpers.ConvertGltfVectorToMilo(normal);
                }

                newVert.nx = normal.X;
                newVert.ny = normal.Y;
                newVert.nz = normal.Z;
            }

            if (tangents != null && originalIndex < tangents.Count)
            {
                var tangent = tangents[(int)originalIndex];
                if (convertCoordinates)
                {
                    var tangentXyz = MatrixHelpers.ConvertGltfVectorToMilo(new System.Numerics.Vector3(tangent.X, tangent.Y, tangent.Z));
                    tangent = new System.Numerics.Vector4(tangentXyz, tangent.W);
                }

                newVert.tangent0 = tangent.X;
                newVert.tangent1 = tangent.Y;
                newVert.tangent2 = tangent.Z;
                newVert.tangent3 = tangent.W;
            }

            var vertexInfluences = vertexSkinInfluences[(int)originalIndex];
            if (vertexInfluences.Count > 0)
            {
                newVert.weight0 = vertexInfluences.Count > 0 ? vertexInfluences[0].weight : 0.0f;
                newVert.weight1 = vertexInfluences.Count > 1 ? vertexInfluences[1].weight : 0.0f;
                newVert.weight2 = vertexInfluences.Count > 2 ? vertexInfluences[2].weight : 0.0f;
                newVert.weight3 = vertexInfluences.Count > 3 ? vertexInfluences[3].weight : 0.0f;
            }
            else
            {
                newVert.weight0 = 0.0f;
                newVert.weight1 = 0.0f;
                newVert.weight2 = 0.0f;
                newVert.weight3 = 0.0f;
            }

            newVert.vertexColors = new HmxColor4();
            if (colors != null && originalIndex < colors.Count)
            {
                var vertexColors = colors[(int)originalIndex];
                newVert.vertexColors.r = vertexColors.X;
                newVert.vertexColors.g = vertexColors.Y;
                newVert.vertexColors.b = vertexColors.Z;
                newVert.vertexColors.a = vertexColors.W;
            }

            if (vertexInfluences.Count > 0 && meshSkin != null)
            {
                if (mesh.vertices.compressionType == 1)
                {
                    newVert.bone0 = vertexInfluences.Count > 3 ? GetRemappedBoneIndex(vertexInfluences[3].idx, jointIndexToLocalBoneIndex) : (ushort)0;
                    newVert.bone1 = vertexInfluences.Count > 2 ? GetRemappedBoneIndex(vertexInfluences[2].idx, jointIndexToLocalBoneIndex) : newVert.bone0;
                    newVert.bone2 = vertexInfluences.Count > 1 ? GetRemappedBoneIndex(vertexInfluences[1].idx, jointIndexToLocalBoneIndex) : newVert.bone1;
                    newVert.bone3 = vertexInfluences.Count > 0 ? GetRemappedBoneIndex(vertexInfluences[0].idx, jointIndexToLocalBoneIndex) : newVert.bone2;
                }
                else
                {
                    newVert.bone0 = vertexInfluences.Count > 0 ? GetRemappedBoneIndex(vertexInfluences[0].idx, jointIndexToLocalBoneIndex) : (ushort)0;
                    newVert.bone1 = vertexInfluences.Count > 1 ? GetRemappedBoneIndex(vertexInfluences[1].idx, jointIndexToLocalBoneIndex) : newVert.bone0;
                    newVert.bone2 = vertexInfluences.Count > 2 ? GetRemappedBoneIndex(vertexInfluences[2].idx, jointIndexToLocalBoneIndex) : newVert.bone1;
                    newVert.bone3 = vertexInfluences.Count > 3 ? GetRemappedBoneIndex(vertexInfluences[3].idx, jointIndexToLocalBoneIndex) : newVert.bone2;
                }

                ushort lastValidBone = 0;

                if (newVert.bone0 != ushort.MaxValue)
                {
                    lastValidBone = newVert.bone0;
                }
                else
                {
                    newVert.bone0 = lastValidBone;
                }

                if (newVert.bone1 != ushort.MaxValue)
                {
                    lastValidBone = newVert.bone1;
                }
                else
                {
                    newVert.bone1 = lastValidBone;
                }

                if (newVert.bone2 != ushort.MaxValue)
                {
                    lastValidBone = newVert.bone2;
                }
                else
                {
                    newVert.bone2 = lastValidBone;
                }

                if (newVert.bone3 != ushort.MaxValue)
                {
                    lastValidBone = newVert.bone3;
                }
                else
                {
                    newVert.bone3 = lastValidBone;
                }
            }

            if (mesh.hasAOCalculation)
            {
                newVert.vertexColors.r = 255.0f;
                newVert.vertexColors.g = 255.0f;
                newVert.vertexColors.b = 255.0f;
                newVert.vertexColors.a = 255.0f;
            }

            mesh.vertices.vertices.Add(newVert);
            if (mesh.vertices.vertices.Count > MaxMeshVertices)
            {
                throw new InvalidDataException($"Internal mesh chunk exceeded the {MaxMeshVertices} vertex limit.");
            }

            originalIndexToNewIndex[originalIndex] = (ushort)(mesh.vertices.vertices.Count - 1);
        }

        private static void PopulateMeshChunk(RndMesh mesh, MeshChunk meshChunk, IList<System.Numerics.Vector3> positions, IList<System.Numerics.Vector3>? normals, IList<System.Numerics.Vector2>? uvs, IList<System.Numerics.Vector4>? tangents, IList<System.Numerics.Vector4>? colors, IReadOnlyList<List<SkinInfluence>> vertexSkinInfluences, Skin? meshSkin, Node node, bool convertCoordinates)
        {
            var originalIndexToNewIndex = new Dictionary<uint, ushort>();
            var jointIndexToLocalBoneIndex = new Dictionary<int, ushort>(meshChunk.jointIndices.Count);
            for (int i = 0; i < meshChunk.jointIndices.Count; i++)
            {
                jointIndexToLocalBoneIndex[meshChunk.jointIndices[i]] = (ushort)i;
            }

            mesh.vertices.vertices.Clear();
            mesh.faces.Clear();

            foreach (var triangle in meshChunk.tris)
            {
                AddVertexToChunkMesh(mesh, triangle.idx0, originalIndexToNewIndex, positions, normals, uvs, tangents, colors, vertexSkinInfluences, jointIndexToLocalBoneIndex, meshSkin, convertCoordinates);
                AddVertexToChunkMesh(mesh, triangle.idx1, originalIndexToNewIndex, positions, normals, uvs, tangents, colors, vertexSkinInfluences, jointIndexToLocalBoneIndex, meshSkin, convertCoordinates);
                AddVertexToChunkMesh(mesh, triangle.idx2, originalIndexToNewIndex, positions, normals, uvs, tangents, colors, vertexSkinInfluences, jointIndexToLocalBoneIndex, meshSkin, convertCoordinates);

                mesh.faces.Add(new RndMesh.Face
                {
                    idx1 = originalIndexToNewIndex[triangle.idx0],
                    idx2 = originalIndexToNewIndex[triangle.idx1],
                    idx3 = originalIndexToNewIndex[triangle.idx2],
                });
            }

            if (meshSkin != null)
            {
                var boneTransList = new List<RndMesh.BoneTransform>();
                foreach (int jointIndex in meshChunk.jointIndices)
                {
                    // every chunk joint must produce exactly one entry here, since vertex bone indices point into this list by position
                    // joints that should not be exported (neutral_bone) are already filtered out of the influences before chunking
                    var jointNode = meshSkin.Joints[jointIndex];

                    var miloBoneTransform = new RndMesh.BoneTransform
                    {
                        name = jointNode.Name ?? $"joint_{jointIndex}"
                    };

                    if (!Matrix4x4.Invert(jointNode.WorldMatrix, out var boneWorldInverse))
                    {
                        Logger.Warn($"Bone {miloBoneTransform.name} has a non-invertible world matrix (likely zero scale on an axis). Its bone transform will be identity.");
                        boneWorldInverse = Matrix4x4.Identity;
                    }

                    var relativeTransform = boneWorldInverse * node.WorldMatrix;
                    MatrixHelpers.CopyMatrix(relativeTransform, miloBoneTransform.transform, convertCoordinates);
                    boneTransList.Add(miloBoneTransform);
                }

                mesh.boneTransforms = boneTransList;
            }
            else
            {
                mesh.boneTransforms = new List<RndMesh.BoneTransform>();
            }
        }

        public static void Run(Options opts)
        {
            string filePath = opts.Input;
            string outputPath = opts.Output;
            string platform = opts.Platform.ToLower();
            string gameArg = opts.Game.ToLower();
            string preLit = opts.Prelit.ToLower();
            string outfitConfig = opts.OutfitConfig.ToLower();
            string type = opts.Type.ToLower();
            string report = opts.Report.ToLower();
            bool ignoreLimits = opts.IgnoreTexSizeLimits;
            bool convertWorldCoordinates = type != "character" && type != "instrument" && type != "dancer";

            if (!File.Exists(filePath))
            {
                Logger.Error("File does not exist.");
                return;
            }

            if (!filePath.EndsWith(".gltf") && !filePath.EndsWith(".glb"))
            {
                Logger.Error("File is not a glTF file.");
                return;
            }

            // if they set an outfitconfig, check it exists
            if (!string.IsNullOrEmpty(outfitConfig) && !File.Exists(outfitConfig))
            {
                Logger.Error($"Specified OutfitConfig file {outfitConfig} does not exist.");
                return;
            }

            MiloGame selectedGame = MiloGame.RockBand3;
            if (gameArg == "tbrb")
            {
                selectedGame = MiloGame.TheBeatlesRockBand;
            }
            else if (gameArg == "rb3")
            {
                selectedGame = MiloGame.RockBand3;
            }
            else if (gameArg == "rb2")
            {
                selectedGame = MiloGame.RockBand2;
            }
            else
            {
                Logger.Warn("Invalid game specified. Defaulting to Rock Band 3.");
            }

            var model = ModelRoot.Load(filePath);

            DirectoryMeta meta = new DirectoryMeta();

            string filename = Path.GetFileNameWithoutExtension(filePath);
            meta.name = filename;

            // check if second arg is "ps3" or "xbox" to set platform
            if (platform == "xbox")
            {
                meta.platform = DirectoryMeta.Platform.Xbox;
            }
            else if (platform == "ps3")
            {
                meta.platform = DirectoryMeta.Platform.PS3;
            }
            else
            {
                Logger.Warn("Invalid platform specified. Defaulting to Xbox.");
                meta.platform = DirectoryMeta.Platform.Xbox;
            }

            meta.revision = GameRevisions.GetRevision(selectedGame).MiloRevision;

            if (opts.Type == "character" || opts.Type == "instrument" || opts.Type == "dancer")
                meta.type = "Character";
            else
                meta.type = "RndDir";

            List<(string, Matrix4x4)> bandConfigurationPositions = new();

            HashSet<string> hairStrandBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<(string MeshName, Matrix4x4 LocalMatrix, Matrix4x4 WorldMatrix)> hairCollisionMeshes = new();
            CharHairExtras detectedHairSettings = null;

            // loop through all gltf nodes and create a milo asset that matches the type of node it is
            // TODO: add lights, possibly other kinds
            foreach (var node in model.LogicalNodes)
            {
                if (NodeHelpers.IsPrimitive(node))
                {
                    if (node.Mesh != null)
                    {
                        int primitiveIndex = 0;
                        foreach (var primitive in node.Mesh.Primitives)
                        {
                            string baseFilename = primitiveIndex == 0
                                ? $"{node.Name}.mesh"
                                : $"{node.Name}_{primitiveIndex}.mesh";

                            if (node.Mesh != null)
                            {
                                IList<System.Numerics.Vector3> positions = null;
                                try { positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array(); }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error reading POSITION: {e.Message}");
                                    Logger.Error("POSITION data will be improper.");
                                }

                                IList<System.Numerics.Vector3> normals = null;
                                try { normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array(); }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error reading NORMAL: {e.Message}");
                                    Logger.Error("NORMAL data will be improper.");
                                }

                                // sanity check on vertex normals
                                // either they are null or are all 0, either case is bad
                                if (normals == null || normals.Count == 0 ||
                                    normals.All(n => n.X == 0 && n.Y == 0 && n.Z == 0))
                                {
                                    Logger.Error($"Mesh {node.Name} has none or all-zero vertex normals. The model will likely appear black in-game!");
                                }

                                IList<System.Numerics.Vector2> uvs = null;
                                try { uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array(); }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error reading TEXCOORD_0: {e.Message}");
                                    Logger.Error("UV data will be improper.");
                                }

                                // sanity check on uvs
                                // either they are null or are all 0, either case is bad
                                if (uvs == null || uvs.Count == 0 || uvs.All(uv => uv.X == 0 && uv.Y == 0))
                                {
                                    Logger.Error($"Mesh {node.Name} has no UVs or all-zero UVs. The model will likely have very distorted textures!");
                                }

                                IList<System.Numerics.Vector4> tangents = null;
                                try { tangents = primitive.GetVertexAccessor("TANGENT")?.AsVector4Array(); }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error reading TANGENT: {e.Message}");
                                    Logger.Error("TANGENT data will be improper.");
                                }

                                IList<System.Numerics.Vector4>? weights = null;
                                try { weights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array(); }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error reading WEIGHTS_0: {e.Message}");
                                    Logger.Error("WEIGHTS data will be improper.");
                                }

                                IList<System.Numerics.Vector4>? joints = null;
                                try { joints = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array(); }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error reading JOINTS_0: {e.Message}");
                                    Logger.Error("JOINTS data will be improper.");
                                }

                                IList<System.Numerics.Vector4>? weights1 = null;
                                try { weights1 = primitive.GetVertexAccessor("WEIGHTS_1")?.AsVector4Array(); }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error reading WEIGHTS_1: {e.Message}");
                                    Logger.Error("Secondary WEIGHTS data will be improper.");
                                }

                                IList<System.Numerics.Vector4>? joints1 = null;
                                try { joints1 = primitive.GetVertexAccessor("JOINTS_1")?.AsVector4Array(); }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error reading JOINTS_1: {e.Message}");
                                    Logger.Error("Secondary JOINTS data will be improper.");
                                }

                                IList<System.Numerics.Vector4> colors = null;
                                var colorsAccessor = primitive.GetVertexAccessor("COLOR_0");
                                if (colorsAccessor != null && colorsAccessor.Count > 0)
                                {
                                    try { colors = colorsAccessor.AsVector4Array(); }
                                    catch (Exception e)
                                    {
                                        Logger.Error($"Error reading COLOR_0: {e.Message}");
                                        Logger.Error("COLOR data will be improper.");
                                    }
                                }

                                IntegerArray? indices = null;
                                bool indicesReadFailed = false;
                                try { indices = primitive.IndexAccessor?.AsIndicesArray(); }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error reading indices: {e.Message}");
                                    Logger.Error("Cannot continue creating mesh.");
                                    indicesReadFailed = true;
                                }

                                // a failed index read means garbage geometry, unlike a missing accessor which just means a non-indexed mesh
                                if (indicesReadFailed)
                                {
                                    primitiveIndex++;
                                    continue;
                                }

                                // if there are no positions this isn't going to be valid geometry, so skip this primitive
                                if (positions == null)
                                {
                                    Logger.Warn($"Mesh {node.Name} has no POSITION data. It will be skipped.");
                                    primitiveIndex++;
                                    continue;
                                }
                                var meshSkin = node.Skin;

                                if ((joints != null || weights != null || joints1 != null || weights1 != null) && meshSkin == null)
                                {
                                    Logger.Warn($"Mesh {node.Name} has JOINTS/WEIGHTS accessors but no skin. Skinning data will be ignored.");
                                    joints = null;
                                    weights = null;
                                    joints1 = null;
                                    weights1 = null;
                                }

                                bool skinSet0Valid = ValidateSkinAccessorSet(node.Name, "0", positions.Count, ref joints, ref weights);
                                ValidateSkinAccessorSet(node.Name, "1", positions.Count, ref joints1, ref weights1);

                                // without a usable primary set, the secondary set is just residual weights that normalization would blow way out of proportion
                                if (!skinSet0Valid && joints1 != null)
                                {
                                    Logger.Warn($"Mesh {node.Name} has JOINTS_1/WEIGHTS_1 but no usable JOINTS_0/WEIGHTS_0. JOINTS_1/WEIGHTS_1 will be ignored.");
                                    joints1 = null;
                                    weights1 = null;
                                }

                                var vertexSkinInfluences = new List<List<SkinInfluence>>(positions.Count);
                                if (meshSkin != null && ((joints != null && weights != null) || (joints1 != null && weights1 != null)))
                                {
                                    var skinWarningState = new SkinWarningState();

                                    // neutral_bone never gets a Trans or a bone transform, so its influences must be dropped up front
                                    var excludedJointIndices = new HashSet<int>();
                                    for (int i = 0; i < meshSkin.Joints.Count; i++)
                                    {
                                        if (meshSkin.Joints[i] == null || meshSkin.Joints[i].Name == "neutral_bone")
                                        {
                                            excludedJointIndices.Add(i);
                                        }
                                    }

                                    for (int i = 0; i < positions.Count; i++)
                                    {
                                        var vertexInfluences = GetVertexSkinInfluences(joints, weights, joints1, weights1, i, meshSkin.Joints.Count, node.Name, excludedJointIndices, skinWarningState);
                                        vertexSkinInfluences.Add(vertexInfluences);
                                    }
                                }
                                else
                                {
                                    for (int i = 0; i < positions.Count; i++)
                                    {
                                        vertexSkinInfluences.Add(new List<SkinInfluence>());
                                    }
                                }

                                var sourceTriangles = BuildSourceTriangles(indices, positions.Count, node.Name);
                                if (sourceTriangles.Count == 0)
                                {
                                    Logger.Warn($"Mesh {node.Name} has no valid triangles after index validation. It will be skipped.");
                                    primitiveIndex++;
                                    continue;
                                }

                                var sourceVertexIndices = new HashSet<uint>();
                                foreach (var triangle in sourceTriangles)
                                {
                                    sourceVertexIndices.Add(triangle.idx0);
                                    sourceVertexIndices.Add(triangle.idx1);
                                    sourceVertexIndices.Add(triangle.idx2);
                                }

                                var meshChunks = SplitTrianglesIntoMeshChunks(sourceTriangles, vertexSkinInfluences, node.Name);
                                if (meshChunks.Count > 1)
                                {
                                    int totalInfluencingBoneCount = meshChunks
                                        .SelectMany(chunk => chunk.jointIndices)
                                        .Distinct()
                                        .Count();
                                    var splitReasons = new List<string>();

                                    if (totalInfluencingBoneCount > MaxMeshInfluencingBones)
                                    {
                                        splitReasons.Add($"more than {MaxMeshInfluencingBones} bones");
                                    }

                                    if (sourceVertexIndices.Count > MaxMeshVertices)
                                    {
                                        splitReasons.Add($"more than {MaxMeshVertices} vertices");
                                    }

                                    string splitReason = splitReasons.Count > 0
                                        ? string.Join(" and ", splitReasons)
                                        : "mesh export limits";

                                    Logger.Warn($"Mesh {node.Name} has {splitReason}. It will be exported as {meshChunks.Count} mesh chunks.");
                                }

                                for (int chunkIndex = 0; chunkIndex < meshChunks.Count; chunkIndex++)
                                {
                                    var meshChunk = meshChunks[chunkIndex];
                                    foreach (int jointIndex in meshChunk.jointIndices)
                                    {
                                        string jointName = meshSkin?.Joints[jointIndex].Name ?? string.Empty;
                                        if (IsHairBone(jointName))
                                        {
                                            hairStrandBones.Add(jointName);
                                        }
                                    }

                                    var mesh = CreateBaseMesh(selectedGame, platform, filename, node, primitive, convertWorldCoordinates);
                                    PopulateMeshChunk(mesh, meshChunk, positions, normals, uvs, tangents, colors, vertexSkinInfluences, meshSkin, node, convertWorldCoordinates);

                                    // TODO: this is clearly not right but i dont even know what these are still
                                    uint numFaces = (uint)mesh.faces.Count;
                                    mesh.groupSizes.Clear();
                                    while (numFaces > 0)
                                    {
                                        if (numFaces >= 255)
                                        {
                                            mesh.groupSizes.Add(255);
                                            numFaces -= 255;
                                        }
                                        else
                                        {
                                            mesh.groupSizes.Add((byte)numFaces);
                                            numFaces = 0;
                                        }
                                    }

                                    string overridenFilename = baseFilename;
                                    MiloExtras.AddToMesh(node, mesh, ref overridenFilename);
                                    if (meshChunks.Count > 1 && chunkIndex > 0)
                                    {
                                        string extension = Path.GetExtension(overridenFilename);
                                        overridenFilename = string.IsNullOrEmpty(extension)
                                            ? $"{overridenFilename}.{chunkIndex:00}"
                                            : $"{overridenFilename[..^extension.Length]}.{chunkIndex:00}{extension}";
                                    }

                                    DirectoryMeta.Entry entry = new DirectoryMeta.Entry("Mesh", overridenFilename, mesh);
                                    mesh.geomOwner = entry.name;
                                    meta.entries.Add(entry);

                                    string? objectType = null;
                                    if (node.Extras != null)
                                    {
                                        try
                                        {
                                            var extras = JsonSerializer.Deserialize<MiloExtras>(node.Extras.ToString());
                                            objectType = string.IsNullOrWhiteSpace(extras?.ObjectType) ? null : extras.ObjectType;
                                        }
                                        catch
                                        {
                                            objectType = null;
                                        }
                                    }

                                    string nodeName = node.Name ?? string.Empty;
                                    bool isHairCollisionMesh = string.Equals(objectType, "CharCollide", StringComparison.OrdinalIgnoreCase) ||
                                        entry.name.value.EndsWith(".coll", StringComparison.OrdinalIgnoreCase) ||
                                        entry.name.value.EndsWith(".collide", StringComparison.OrdinalIgnoreCase) ||
                                        nodeName.EndsWith(".coll", StringComparison.OrdinalIgnoreCase) ||
                                        nodeName.EndsWith(".collide", StringComparison.OrdinalIgnoreCase) ||
                                        nodeName.Contains("hair_collide", StringComparison.OrdinalIgnoreCase);

                                    if (isHairCollisionMesh)
                                    {
                                        hairCollisionMeshes.Add((entry.name.value, node.LocalMatrix, node.WorldMatrix));
                                    }
                                }
                            }


                            primitiveIndex++;
                        }
                    }
                    else
                    {
                        Logger.Error($"{node.Name} has no mesh but is a mesh node. Can not convert glTF.");
                        return;
                    }
                }
                else if (NodeHelpers.IsBone(node, model))
                {
                    NodeProcessor.ProcessBoneNode(node, model, meta, type, filename, convertWorldCoordinates);

                    if (IsHairBone(node.Name))
                    {
                        if (node.Extras != null)
                        {
                            try
                            {
                                string json = node.Extras.ToString();

                                if (json.Contains("milo_hair_"))
                                {
                                    var hairData = JsonSerializer.Deserialize<CharHairExtras>(json);
                                    if (hairData != null && detectedHairSettings == null)
                                    {
                                        detectedHairSettings = hairData;
                                    }
                                }
                            }
                            catch
                            {
                                // hair extras are optional, so bad extras should not detonate the whole export
                            }
                        }
                    }
                }
                else if (NodeHelpers.IsGroupNode(node, model))
                {
                    NodeProcessor.ProcessGroupNode(node, model, meta, selectedGame, convertWorldCoordinates);
                }
                else if (NodeHelpers.IsLightNode(node, model))
                {
                    NodeProcessor.ProcessLightNode(node, meta, selectedGame, convertWorldCoordinates);
                }
                else
                {

                }
            }

            if (hairStrandBones.Count > 0)
            {
                NodeProcessor.ProcessCharHair(meta, selectedGame, model, hairStrandBones, detectedHairSettings ?? new CharHairExtras(), convertWorldCoordinates, !opts.DisableSplitting);
                NodeProcessor.ProcessEmptyHairCollides(meta, selectedGame, hairCollisionMeshes, filename, convertWorldCoordinates);
            }

            foreach (var anim in model.LogicalAnimations)
            {
                bool isTransformOnly = anim.Channels.All(channel =>
                    channel.TargetNodePath.ToString() == "translation" ||
                    channel.TargetNodePath.ToString() == "rotation" ||
                    channel.TargetNodePath.ToString() == "scale"
                );

                if (isTransformOnly)
                {
                    // make sure all channels influence the same Node
                    string targetNode = anim.Channels[0].TargetNode.Name;
                    foreach (var channel in anim.Channels)
                    {
                        if (channel.TargetNode.Name != targetNode)
                        {
                            Logger.Error($"Animation {anim.Name} has channels that target different nodes.");
                            continue;
                        }
                    }
                    // we are just animating the transform, so we can use a TransAnim for this
                    RndTransAnim transAnim = new RndTransAnim();

                    // use reflection to set the revision
                    typeof(RndTransAnim).GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(transAnim, (UInt16)7);

                    transAnim.anim = RndAnimatable.New(GameRevisions.GetRevision(selectedGame).AnimatableRevision, 0);

                    // use 30 fps animations
                    transAnim.anim.rate = RndAnimatable.Rate.k30_fps;

                    transAnim.draw = RndDrawable.New(GameRevisions.GetRevision(selectedGame).DrawableRevision, 0);
                    transAnim.draw.sphere.radius = 0.0f;

                    transAnim.trans = targetNode + ".mesh";
                    transAnim.keysOwner = anim.Name + ".tnm";

                    transAnim.objFields.revision = 2;

                    // loop through all channels and add the keys to the transAnim
                    foreach (var channel in anim.Channels)
                    {
                        if (channel.TargetNodePath.ToString() == "translation")
                        {
                            foreach (var key in channel.GetTranslationSampler().GetLinearKeys().ToList())
                            {
                                // keys must live in the same coordinate system as the trans they animate
                                var translation = convertWorldCoordinates ? MatrixHelpers.ConvertGltfVectorToMilo(key.Value) : key.Value;
                                transAnim.transKeys.Add(new PropKey.Vec3Key
                                {
                                    pos = key.Key,
                                    vec = new MiloLib.Classes.Vector3(translation.X, translation.Y, translation.Z),
                                });
                            }
                        }
                        else if (channel.TargetNodePath.ToString() == "rotation")
                        {
                            foreach (var key in channel.GetRotationSampler().GetLinearKeys().ToList())
                            {
                                var rotation = convertWorldCoordinates ? MatrixHelpers.ConvertGltfQuaternionToMilo(key.Value) : key.Value;
                                var propKey = new PropKey.QuatKey
                                {
                                    pos = key.Key,
                                    quat = new MiloLib.Classes.Vector4(rotation.X, rotation.Y, rotation.Z),
                                };
                                propKey.quat.w = rotation.W;
                                transAnim.rotKeys.Add(propKey);
                            }
                        }
                        else if (channel.TargetNodePath.ToString() == "scale")
                        {
                            foreach (var key in channel.GetScaleSampler().GetLinearKeys().ToList())
                            {
                                var scale = convertWorldCoordinates ? MatrixHelpers.ConvertGltfScaleToMilo(key.Value) : key.Value;
                                transAnim.scaleKeys.Add(new PropKey.Vec3Key
                                {
                                    pos = key.Key,
                                    vec = new MiloLib.Classes.Vector3(scale.X, scale.Y, scale.Z),
                                });
                            }
                        }
                    }


                    DirectoryMeta.Entry entry = new DirectoryMeta.Entry("TransAnim", anim.Name + ".tnm", transAnim);
                    meta.entries.Add(entry);

                }
            }
            /*
            // TODO: finish this
            if (bandConfigurationPositions.Count != 0)
            {
                // create a band configuration object
                BandConfiguration bandConfig = new BandConfiguration();
                bandConfig.objFields.revision = 2;
                foreach (var pos in bandConfigurationPositions)
                {
                    var trans = new BandConfiguration.TargTransform();
                    trans.target = pos.Item1;
                    trans.xfm.m11 = pos.Item2.M11;
                    trans.xfm.m12 = pos.Item2.M12;
                    trans.xfm.m13 = pos.Item2.M13;
                    trans.xfm.m21 = pos.Item2.M21;
                    trans.xfm.m22 = pos.Item2.M22;
                    trans.xfm.m23 = pos.Item2.M23;
                    trans.xfm.m31 = pos.Item2.M31;
                    trans.xfm.m32 = pos.Item2.M32;
                    trans.xfm.m33 = pos.Item2.M33;
                    trans.xfm.m41 = pos.Item2.M41;
                    trans.xfm.m42 = pos.Item2.M42;
                    trans.xfm.m43 = pos.Item2.M43;
                    bandConfig.transforms.Add(trans);
                }
                // add the band configuration to the meta
                DirectoryMeta.Entry entry = new DirectoryMeta.Entry("BandConfiguration", filename, bandConfig);
                meta.entries.Add(entry);
            }
            */

            int curmat = 0;

            // loop through all materials
            foreach (var material in model.LogicalMaterials)
            {
                RndMat mat = RndMat.New(GameRevisions.GetRevision(selectedGame).MatRevision, 0);

                DirectoryMeta.Entry matEntry = new DirectoryMeta.Entry("Mat", material.Name + ".mat", mat);

                meta.entries.Add(matEntry);

                RndTex tex = RndTex.New(GameRevisions.GetRevision(selectedGame).TextureRevision, 0);
                tex.objFields.revision = 2;

                var baseColorTexture = material.FindChannel("BaseColor")?.Texture;
                var sampler = baseColorTexture?.Sampler;

                if (baseColorTexture != null)
                {
                    curmat++;
                    mat.diffuseTex = material.Name + ".tex";
                    mat.stencilMode = RndMat.StencilMode.kStencilIgnore;

                    mat.perPixelLit = true;
                    if (opts.Prelit != "false")
                    {
                        mat.preLit = true;
                    }
                    mat.pointLights = true;
                    mat.projLights = true;
                    mat.fog = false;
                    mat.cull = !material.DoubleSided;
                    // check if the shader name contains _skin or _hair to turn on the skin or hair shader variant
                    // TODO: I need to make double check this is what we should actually be doing and what the skin and hair shaders really do
                    if (material.Name.Contains("_skin"))
                    {
                        mat.shaderVariation = RndMat.ShaderVariation.kShaderVariationSkin;
                    }
                    else if (material.Name.Contains("_hair"))
                    {
                        mat.shaderVariation = RndMat.ShaderVariation.kShaderVariationHair;
                    }
                    else
                    {
                        mat.shaderVariation = RndMat.ShaderVariation.kShaderVariationNone;
                    }
                    mat.blend = RndMat.Blend.kBlendSrc;
                    mat.emissiveMultiplier = 1.0f;
                    mat.normalDetailTiling = 1.0f;
                    mat.rimPower = 0.0f;
                    mat.specular2Power = 0.0f;
                    mat.rimRGB = new HmxColor3(0.0f, 0.0f, 0.0f, 0.0f);
                    mat.rimPower = 4.0f;
                    mat.specularPower = 0.0f;
                    mat.specular2Power = 0.0f;



                    // try to get texture wrap settings
                    if (sampler != null)
                    {
                        var wrapS = sampler.WrapS;
                        var wrapT = sampler.WrapT;

                        if (wrapS == TextureWrapMode.CLAMP_TO_EDGE || wrapT == TextureWrapMode.CLAMP_TO_EDGE)
                        {
                            mat.texWrap = RndMat.TexWrap.kTexWrapClamp;
                        }
                        else if (wrapS == TextureWrapMode.MIRRORED_REPEAT || wrapT == TextureWrapMode.MIRRORED_REPEAT)
                        {
                            mat.texWrap = RndMat.TexWrap.kTexWrapMirror;
                        }
                        else
                        {
                            mat.texWrap = RndMat.TexWrap.kTexWrapRepeat;
                        }
                    }
                    else
                    {
                        // No sampler, just use tiling
                        mat.texWrap = RndMat.TexWrap.kTexWrapRepeat;
                    }

                    using (var str = baseColorTexture.PrimaryImage.Content.Open())
                    {
                        Surface tempImage = Surface.LoadFromStream(str);
                        bool hasAlpha = tempImage.IsTransparent;
                        str.Position = 0;
                        CompressionFormat format = hasAlpha ? CompressionFormat.BC3 : CompressionFormat.BC1;
                        TextureUtils.ConvertToDDS(str, $"output_{curmat}.dds", format, ignoreLimits);
                        mat.zMode = RndMat.ZMode.kZModeNormal;

                        // i think this is generally correct?
                        if (material.Alpha == SharpGLTF.Schema2.AlphaMode.MASK)
                        {
                            mat.alphaCut = true;
                            mat.alphaThreshold = (int)(material.AlphaCutoff * 255.0f);
                            mat.blend = RndMat.Blend.kBlendSrc;
                        }
                        else if (hasAlpha)
                        {
                            mat.alphaCut = false;
                            mat.alphaWrite = true;
                            mat.blend = RndMat.Blend.kBlendSrcAlpha;
                        }
                        else
                        {
                            mat.alphaCut = false;
                            mat.blend = RndMat.Blend.kBlendSrc;
                        }

                        var (width, height, bpp, mipMapCount, pixels) = TextureUtils.ParseDDS($"output_{curmat}.dds");
                        tex.width = (uint)width;
                        tex.height = (uint)height;
                        tex.bpp = (uint)bpp;
                        tex.externalPath = material.Name + ".png";
                        tex.mipMapK = -8.0f;
                        tex.type = RndTex.Type.kRegular;
                        tex.optimizeForPS3 = true;

                        tex.bitmap = RndBitmap.New(1, 0);
                        tex.bitmap.height = (ushort)tex.height;
                        tex.bitmap.width = (ushort)tex.width;
                        tex.bitmap.bpp = (byte)tex.bpp;
                        tex.bitmap.encoding = hasAlpha ? RndBitmap.TextureEncoding.DXT5_BC3 : RndBitmap.TextureEncoding.DXT1_BC1;
                        tex.bitmap.mipMaps = 0;
                        tex.bitmap.bpl = (ushort)(width * bpp / 8);

                        if (meta.platform == DirectoryMeta.Platform.Xbox)
                        {
                            List<byte> swapped = new List<byte>();
                            for (int i = 0; i < pixels.Length; i += 4)
                            {
                                swapped.Add(pixels[i + 1]);
                                swapped.Add(pixels[i]);
                                swapped.Add(pixels[i + 3]);
                                swapped.Add(pixels[i + 2]);
                            }
                            tex.bitmap.textures.Add(swapped);
                        }
                        else
                        {
                            tex.bitmap.textures.Add(pixels.ToList());
                        }
                    }

                    // delete the dds file that was created so we don't have a bunch of dds files lying around
                    File.Delete($"output_{curmat}.dds");

                    DirectoryMeta.Entry texEntry = new DirectoryMeta.Entry("Tex", material.Name + ".tex", tex);
                    meta.entries.Add(texEntry);

                    mat.objFields.revision = 2;
                }

                var normalChannel = material.FindChannel("Normal");
                var normalMapTexture = normalChannel?.Texture;

                RndTex normalTex = RndTex.New(GameRevisions.GetRevision(selectedGame).TextureRevision, 0);
                normalTex.objFields.revision = 2;

                if (normalMapTexture != null)
                {
                    using (var str = normalMapTexture.PrimaryImage.Content.Open())
                    {
                        if (opts.Platform == "xbox")
                            TextureUtils.ConvertToDDS(str, $"output_{curmat}_norm.dds", CompressionFormat.BC5, ignoreLimits);
                        else
                            TextureUtils.ConvertToDDS(str, $"output_{curmat}_norm.dds", CompressionFormat.BC1, ignoreLimits);
                        var (width, height, bpp, mipMapCount, pixels) = TextureUtils.ParseDDS($"output_{curmat}_norm.dds");
                        normalTex.width = (uint)width;
                        normalTex.height = (uint)height;
                        normalTex.bpp = (uint)bpp;
                        normalTex.externalPath = material.Name + "_norm.png";
                        normalTex.mipMapK = -8.0f;
                        normalTex.type = RndTex.Type.kRegular;
                        normalTex.optimizeForPS3 = true;

                        normalTex.bitmap = RndBitmap.New(1, 0);
                        normalTex.bitmap.height = (ushort)normalTex.height;
                        normalTex.bitmap.width = (ushort)normalTex.width;
                        normalTex.bitmap.bpp = (byte)normalTex.bpp;
                        if (opts.Platform == "xbox")
                            normalTex.bitmap.encoding = RndBitmap.TextureEncoding.ATI2_BC5;
                        else
                            normalTex.bitmap.encoding = RndBitmap.TextureEncoding.DXT1_BC1;
                        normalTex.bitmap.mipMaps = 0;
                        normalTex.bitmap.bpl = (ushort)(width * bpp / 8);


                        if (meta.platform == DirectoryMeta.Platform.Xbox)
                        {
                            List<byte> swapped = new List<byte>();
                            for (int i = 0; i < pixels.Length; i += 4)
                            {
                                swapped.Add(pixels[i + 1]);
                                swapped.Add(pixels[i]);
                                swapped.Add(pixels[i + 3]);
                                swapped.Add(pixels[i + 2]);
                            }
                            normalTex.bitmap.textures.Add(swapped);
                        }
                        else
                        {
                            normalTex.bitmap.textures.Add(pixels.ToList());
                        }
                    }

                    mat.normalMap = material.Name + "_norm.tex";

                    File.Delete($"output_{curmat}_norm.dds");

                    DirectoryMeta.Entry texEntry = new DirectoryMeta.Entry("Tex", material.Name + "_norm.tex", normalTex);
                    meta.entries.Add(texEntry);
                }


                // emissive map
                var emissiveChannel = material.FindChannel("Emissive");
                var emissiveMapTexture = emissiveChannel?.Texture;


                RndTex emissiveTex = RndTex.New(GameRevisions.GetRevision(selectedGame).TextureRevision, 0);
                emissiveTex.objFields.revision = 2;


                if (emissiveMapTexture != null)
                {
                    using (var str = emissiveMapTexture.PrimaryImage.Content.Open())
                    {
                        TextureUtils.ConvertToDDS(str, $"output_{curmat}_emissive.dds", CompressionFormat.BC1, ignoreLimits);
                        var (width, height, bpp, mipMapCount, pixels) = TextureUtils.ParseDDS($"output_{curmat}_emissive.dds");
                        emissiveTex.width = (uint)width;
                        emissiveTex.height = (uint)height;
                        emissiveTex.bpp = (uint)bpp;
                        emissiveTex.externalPath = material.Name + "_emissive.png";
                        emissiveTex.mipMapK = -8.0f;
                        emissiveTex.type = RndTex.Type.kRegular;

                        emissiveTex.bitmap = RndBitmap.New(1, 0);
                        emissiveTex.bitmap.height = (ushort)emissiveTex.height;
                        emissiveTex.bitmap.width = (ushort)emissiveTex.width;
                        emissiveTex.bitmap.bpp = (byte)emissiveTex.bpp;
                        emissiveTex.bitmap.encoding = RndBitmap.TextureEncoding.DXT1_BC1;
                        emissiveTex.bitmap.mipMaps = 0;
                        emissiveTex.bitmap.bpl = (ushort)((width * bpp) / 8);

                        if (meta.platform == DirectoryMeta.Platform.Xbox)
                        {
                            List<byte> swapped = new List<byte>();
                            for (int i = 0; i < pixels.Length; i += 4)
                            {
                                swapped.Add(pixels[i + 1]);
                                swapped.Add(pixels[i]);
                                swapped.Add(pixels[i + 3]);
                                swapped.Add(pixels[i + 2]);
                            }
                            emissiveTex.bitmap.textures.Add(swapped);
                        }
                        else
                        {
                            emissiveTex.bitmap.textures.Add(pixels.ToList());

                            emissiveTex.optimizeForPS3 = true;
                        }
                    }

                    mat.emissiveMap = material.Name + "_emissive.tex";
                    mat.emissiveMultiplier = 1.0f;

                    File.Delete($"output_{curmat}_emissive.dds");

                    DirectoryMeta.Entry emissiveTexEntry = new DirectoryMeta.Entry("Tex", material.Name + "_emissive.tex", emissiveTex);
                    meta.entries.Add(emissiveTexEntry);
                }


                // specular color map
                var specularColorMapTexture = material.FindChannel("SpecularColor")?.Texture;
                var specularColor = material.FindChannel("SpecularColor");


                RndTex specularColorMapTex = RndTex.New(GameRevisions.GetRevision(selectedGame).TextureRevision, 0);
                specularColorMapTex.objFields.revision = 2;

                if (specularColorMapTexture != null)
                {
                    using (var str = specularColorMapTexture.PrimaryImage.Content.Open())
                    {
                        TextureUtils.ConvertToDDS(str, $"output_{curmat}_spec.dds", CompressionFormat.BC3, ignoreLimits);
                        var (width, height, bpp, mipMapCount, pixels) = TextureUtils.ParseDDS($"output_{curmat}_spec.dds");
                        specularColorMapTex.width = (uint)width;
                        specularColorMapTex.height = (uint)height;
                        specularColorMapTex.bpp = (uint)bpp;
                        specularColorMapTex.externalPath = material.Name + "_spec.png";
                        specularColorMapTex.mipMapK = -8.0f;
                        specularColorMapTex.type = RndTex.Type.kRegular;

                        specularColorMapTex.bitmap = RndBitmap.New(1, 0);
                        specularColorMapTex.bitmap.height = (ushort)specularColorMapTex.height;
                        specularColorMapTex.bitmap.width = (ushort)specularColorMapTex.width;
                        specularColorMapTex.bitmap.bpp = (byte)specularColorMapTex.bpp;
                        specularColorMapTex.bitmap.encoding = RndBitmap.TextureEncoding.DXT5_BC3;
                        specularColorMapTex.bitmap.mipMaps = 0;
                        specularColorMapTex.bitmap.bpl = (ushort)((width * bpp) / 8);

                        if (meta.platform == DirectoryMeta.Platform.Xbox)
                        {
                            List<byte> swapped = new List<byte>();
                            for (int i = 0; i < pixels.Length; i += 4)
                            {
                                swapped.Add(pixels[i + 1]);
                                swapped.Add(pixels[i]);
                                swapped.Add(pixels[i + 3]);
                                swapped.Add(pixels[i + 2]);
                            }
                            specularColorMapTex.bitmap.textures.Add(swapped);
                        }
                        else
                        {
                            specularColorMapTex.bitmap.textures.Add(pixels.ToList());

                            specularColorMapTex.optimizeForPS3 = true;
                        }
                    }

                    mat.specularMap = material.Name + "_spec.tex";

                    File.Delete($"output_{curmat}_spec.dds");

                    DirectoryMeta.Entry specularTexEntry = new DirectoryMeta.Entry("Tex", material.Name + "_spec.tex", specularColorMapTex);
                    meta.entries.Add(specularTexEntry);
                }


                if (specularColor != null)
                {
                    mat.specularRGB = new HmxColor3(specularColor.Value.Color.X, specularColor.Value.Color.Y, specularColor.Value.Color.Z, specularColor.Value.Color.W);
                }

                var specularFactor = material.FindChannel("SpecularFactor");
                if (specularFactor != null)
                {
                    mat.specularPower = (float)specularFactor.Value.Parameters[0].Value;
                }

                // handle material extras
                if (material.Extras != null)
                {
                    string extrasJson = material.Extras.ToString();
                    var matExtras = JsonSerializer.Deserialize<MaterialExtras>(extrasJson);

                    // if they don't explicitly pass the prelit option, we should try to get it from extras
                    if (preLit == string.Empty)
                        mat.preLit = matExtras?.Prelit == 1;
                    mat.alphaCut = matExtras?.AlphaCut == 1;
                    mat.alphaThreshold = (int)(matExtras?.AlphaThreshold ?? 0.0f * 255.0f);
                    mat.alphaWrite = matExtras?.AlphaWrite == 1;
                    mat.zMode = (RndMat.ZMode)matExtras?.ZMode;
                    mat.blend = (RndMat.Blend)matExtras?.BlendMode;
                    mat.useEnviron = matExtras?.UseEnvironment == 1;
                    mat.emissiveMultiplier = matExtras?.EmissiveMultiplier ?? 1.0f;
                    mat.cull = matExtras?.Cull == 1;
                    mat.pointLights = matExtras?.PointLights == 1;
                    mat.normalDetailMap = matExtras?.NormalDetailMap ?? string.Empty;
                    mat.shaderVariation = (RndMat.ShaderVariation)(matExtras?.ShaderVariation ?? (int)RndMat.ShaderVariation.kShaderVariationNone);
                }
            }


            // only create the all geom group if its a venue we are trying to produce
            if (opts.Type == "venue")
            {
                // create a new Group with all the geometry inside of it
                RndGroup allGeomGrp = RndGroup.New(GameRevisions.GetRevision(selectedGame).GroupRevision, 0);

                allGeomGrp.trans = RndTrans.New(GameRevisions.GetRevision(selectedGame).TransRevision, 0);
                allGeomGrp.draw = RndDrawable.New(GameRevisions.GetRevision(selectedGame).DrawableRevision, 0);
                allGeomGrp.draw.sphere = new Sphere();
                allGeomGrp.draw.sphere.radius = 0.0f;
                allGeomGrp.anim = RndAnimatable.New(GameRevisions.GetRevision(selectedGame).AnimatableRevision, 0);

                allGeomGrp.objFields.revision = 2;

                foreach (var entry in meta.entries)
                {
                    if (entry.type == "Mesh")
                    {
                        allGeomGrp.objects.Add(entry.name);
                    }
                }

                DirectoryMeta.Entry grpEntry = new DirectoryMeta.Entry("Group", filename + "_geom.grp", allGeomGrp);
                meta.entries.Add(grpEntry);
            }

            // if they specified an OutfitConfig, create it
            OutfitConfigBuilder.BuildOutfitConfig(opts, selectedGame, meta);

            if (opts.Type == "character" || opts.Type == "instrument" || opts.Type == "dancer")
            {
                DirBuilder.BuildCharacterDirectory(opts, selectedGame, meta);
            }
            else
            {
                DirBuilder.BuildRndDirectory(opts, selectedGame, meta);
            }

            MiloFile miloFile = new MiloFile(meta);

            miloFile.Save(opts.Output, MiloFile.Type.Uncompressed, 0x810, MiloLib.Utils.Endian.LittleEndian, MiloLib.Utils.Endian.BigEndian);

            if (report == "true")
            {
                ReportGenerator.Generate(meta, selectedGame, type);
            }


            Logger.Success("Milo scene created at " + opts.Output);
        }
    }
}
