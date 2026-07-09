using glTFMilo.Source;
using MiloGLTFUtils.Source.Shared;
using MiloLib.Assets;
using MiloLib.Assets.Rnd;
using MiloLib.Classes;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MiloGLTFUtils.Source.glTFMilo.Core
{
    /// <summary>
    /// Processes nodes in the glTF and generates corresponding Rnd objects.
    /// </summary>
    public static class NodeProcessor
    {
        private static bool IsHairBoneNode(Node node, ModelRoot model)
        {
            return node != null &&
                NodeHelpers.IsBone(node, model) &&
                !string.IsNullOrEmpty(node.Name) &&
                node.Name.StartsWith("bone_hair_", StringComparison.OrdinalIgnoreCase);
        }

        // detect non-hair bones under hair bones since this is a bone setup user error, we can still generate the hair but it will likely look or perform wrong so maybe this should be a hard stop
        private static void WarnAboutNonHairChildBones(Node node, ModelRoot model)
        {
            foreach (var child in node.VisualChildren)
            {
                if (NodeHelpers.IsBone(child, model) && !IsHairBoneNode(child, model))
                {
                    Logger.Warn($"Non-hair bone '{child.Name}' found under hair bone '{node.Name}'. It will not be included in CharHair strand generation.");
                }
            }
        }

        // builds the list of hair chains as full root to leaf paths, duplicating shared bones across strands when the tree branches
        // this is just so i can a/b test a new branch splitter, which can be turned off with --disable-splitting and fallback to this behavior
        private static void CollectHairChains(Node node, ModelRoot model, HashSet<string> weightedHairBoneNames, List<Node> currentChain, List<List<Node>> chains)
        {
            currentChain.Add(node);

            WarnAboutNonHairChildBones(node, model);

            var hairChildren = node.VisualChildren
                .Where(child => IsHairBoneNode(child, model))
                .ToList();

            if (hairChildren.Count > 1)
            {
                Logger.Warn($"Hair bone '{node.Name}' branches into multiple hair chains and strand splitting is disabled. Bones above the branch will be simulated by multiple strands, which will likely behave incorrectly in-game.");
            }

            if (hairChildren.Count == 0)
            {
                if (currentChain.Any(chainNode => weightedHairBoneNames.Contains(chainNode.Name)))
                {
                    chains.Add(new List<Node>(currentChain));
                }
            }
            else
            {
                foreach (var hairChild in hairChildren)
                {
                    CollectHairChains(hairChild, model, weightedHairBoneNames, currentChain, chains);
                }
            }

            currentChain.RemoveAt(currentChain.Count - 1);
        }

        // builds the list of hair chains by walking the hair bone tree, ending a chain at every branch point so each bone becomes a point in exactly one strand
        // this implements how the decomp actually expects hair to be structured, so it seems to be the correct way to do this
        private static bool CollectHairChainsSplitAtBranches(Node node, ModelRoot model, HashSet<string> weightedHairBoneNames, bool ancestorWeighted, List<List<Node>> chains)
        {
            var segment = new List<Node>();
            var current = node;

            while (true)
            {
                segment.Add(current);

                WarnAboutNonHairChildBones(current, model);

                var hairChildren = current.VisualChildren
                    .Where(child => IsHairBoneNode(child, model))
                    .ToList();

                if (hairChildren.Count == 1)
                {
                    current = hairChildren[0];
                    continue;
                }

                // leaf or branch point, this segment ends here and every child starts its own segment
                bool segmentWeighted = segment.Any(segmentNode => weightedHairBoneNames.Contains(segmentNode.Name));
                var childChains = new List<List<Node>>();
                bool subtreeWeighted = false;

                foreach (var hairChild in hairChildren)
                {
                    subtreeWeighted |= CollectHairChainsSplitAtBranches(hairChild, model, weightedHairBoneNames, ancestorWeighted || segmentWeighted, childChains);
                }

                // emit the segment if a weighted bone exists anywhere on a root to leaf path through it
                if (segmentWeighted || ancestorWeighted || subtreeWeighted)
                {
                    chains.Add(segment);
                }

                chains.AddRange(childChains);
                return segmentWeighted || subtreeWeighted;
            }
        }

        // converts c# vector3 to milo
        private static MiloLib.Classes.Vector3 ToMiloVector3(System.Numerics.Vector3 value)
        {
            return new MiloLib.Classes.Vector3(value.X, value.Y, value.Z);
        }

        // gets a nodes world pos from its world matrix
        private static System.Numerics.Vector3 GetNodeWorldPos(Node node)
        {
            return new System.Numerics.Vector3(node.WorldMatrix.M41, node.WorldMatrix.M42, node.WorldMatrix.M43);
        }

        // gets the length of the hair point by looking at the next point in the chain, if it exists or just fallback
        private static float GetHairPointLength(IReadOnlyList<Node> chain, int pointIndex)
        {
            if (pointIndex < chain.Count - 1)
            {
                // world space distance so lengths stay consistent with the world space point positions, no matter how the bones are oriented or scaled
                return System.Numerics.Vector3.Distance(GetNodeWorldPos(chain[pointIndex]), GetNodeWorldPos(chain[pointIndex + 1]));
            }

            if (pointIndex > 0)
            {
                return GetHairPointLength(chain, pointIndex - 1);
            }

            // single point chain, so use the distance from the parent bone if there is one
            var parent = chain[pointIndex].VisualParent;
            if (parent != null)
            {
                float parentDistance = System.Numerics.Vector3.Distance(GetNodeWorldPos(parent), GetNodeWorldPos(chain[pointIndex]));
                if (parentDistance > 0.0f)
                {
                    return parentDistance;
                }
            }

            // if we cannot determine the length, use 5 :shrug:
            return 5.0f;
        }

        public static void ProcessBoneNode(Node node, ModelRoot model, DirectoryMeta meta, string type, string fallbackParent, bool convertCoordinates = false)
        {
            if (node.Name == "neutral_bone") return;

            if (type == "character" && BoneNames.rb3SkeletonBones.Contains(node.Name))
                return;

            var trans = RndTrans.New(9, 0);
            trans.objFields.revision = 2;

            string parentName = NodeHelpers.GetParentBoneName(node, model) ?? fallbackParent;

            MatrixHelpers.CopyMatrix(node.LocalMatrix, trans.localXfm, convertCoordinates);
            MatrixHelpers.CopyMatrix(node.WorldMatrix, trans.worldXfm, convertCoordinates);
            trans.parentObj = parentName;

            meta.entries.Add(new DirectoryMeta.Entry("Trans", node.Name, trans));
        }

        public static void ProcessGroupNode(Node node, ModelRoot model, DirectoryMeta meta, MiloGame game, bool convertCoordinates = false)
        {
            if (node.Name == "Armature") return;

            string overriddenFilename = node.Name + ".grp";

            var rev = GameRevisions.GetRevision(game);

            var group = RndGroup.New(rev.GroupRevision, 0);
            group.objFields.revision = 2;
            group.trans = RndTrans.New(rev.TransRevision, 0);
            MatrixHelpers.CopyMatrix(node.LocalMatrix, group.trans.localXfm, convertCoordinates);
            MatrixHelpers.CopyMatrix(node.WorldMatrix, group.trans.worldXfm, convertCoordinates);
            group.draw = RndDrawable.New(rev.DrawableRevision, 0);
            group.anim = RndAnimatable.New(rev.AnimatableRevision, 0);

            var children = NodeHelpers.GetAllDescendantNames(node);
            foreach (var child in children)
            {
                if (child != null) group.objects.Add(child);
            }

            // handle extras
            MiloExtras.AddToGroup(node, group, ref overriddenFilename);

            meta.entries.Add(new DirectoryMeta.Entry("Group", overriddenFilename, group));
        }

        public static void ProcessLightNode(Node node, DirectoryMeta meta, MiloGame game, bool convertCoordinates = false)
        {
            var rev = GameRevisions.GetRevision(game);

            string overriddenFilename = node.Name + ".lit";

            var light = new RndLight();
            typeof(RndLight).GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?.SetValue(light, rev.LightRevision);

            light.objFields.revision = 2;
            light.range = node.PunctualLight.Range;
            light.colorOwner = node.Name + ".lit";
            light.color = new HmxColor4(node.PunctualLight.Color.X, node.PunctualLight.Color.Y, node.PunctualLight.Color.Z, 1.0f);

            light.type = node.PunctualLight.LightType switch
            {
                PunctualLightType.Point => RndLight.Type.kPoint,
                PunctualLightType.Spot => RndLight.Type.kSpot,
                PunctualLightType.Directional => RndLight.Type.kDirectional,
                _ => RndLight.Type.kPoint
            };

            light.trans = RndTrans.New(rev.TransRevision, 0);
            MatrixHelpers.CopyMatrix(node.LocalMatrix, light.trans.localXfm, convertCoordinates);
            MatrixHelpers.CopyMatrix(node.WorldMatrix, light.trans.worldXfm, convertCoordinates);

            MiloExtras.AddToObject(node, light, ref overriddenFilename);

            meta.entries.Add(new DirectoryMeta.Entry("Light", overriddenFilename, light));
        }

        public static void ProcessCharHair(DirectoryMeta meta, MiloGame game, ModelRoot model, IEnumerable<string> weightedHairBoneNames, CharHairExtras physicsSettings, bool convertCoordinates = false, bool splitStrandsAtBranches = true)
        {
            var weightedHairBoneSet = new HashSet<string>(weightedHairBoneNames, StringComparer.OrdinalIgnoreCase);
            if (weightedHairBoneSet.Count == 0) return;

            var hair = new MiloLib.Assets.Char.CharHair();

            typeof(MiloLib.Assets.Char.CharHair)
                .GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(hair, (System.UInt16)11);

            hair.objFields.revision = 2;

            hair.simulate = true;
            hair.stiffness = physicsSettings.Stiffness;
            hair.torsion = physicsSettings.Torsion;
            hair.inertia = physicsSettings.Inertia;
            hair.gravity = physicsSettings.Gravity;
            hair.weight = physicsSettings.Weight;
            hair.friction = physicsSettings.Friction;
            hair.wind = string.IsNullOrEmpty(physicsSettings.Wind) ? CharHairExtras.DefaultWind : physicsSettings.Wind;

            var rootNodes = new List<Node>();
            var seenRootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var weightedHairNode in model.LogicalNodes.Where(node => !string.IsNullOrEmpty(node.Name) && weightedHairBoneSet.Contains(node.Name)))
            {
                var rootNode = weightedHairNode;
                while (true)
                {
                    var parent = NodeHelpers.GetParentNode(rootNode, model);
                    if (parent == null || !IsHairBoneNode(parent, model))
                    {
                        break;
                    }

                    rootNode = parent;
                }

                if (!seenRootNames.Add(rootNode.Name))
                {
                    continue;
                }

                rootNodes.Add(rootNode);
            }

            var chains = new List<List<Node>>();
            foreach (var rootNode in rootNodes)
            {
                if (splitStrandsAtBranches)
                {
                    CollectHairChainsSplitAtBranches(rootNode, model, weightedHairBoneSet, false, chains);
                }
                else
                {
                    CollectHairChains(rootNode, model, weightedHairBoneSet, new List<Node>(), chains);
                }
            }

            foreach (var chain in chains)
            {
                if (chain.Count == 0) continue;

                var strand = new MiloLib.Assets.Char.CharHair.Strand();
                strand.root = chain[0].Name;
                MatrixHelpers.CopyMatrix3(chain[0].LocalMatrix, strand.baseMat, convertCoordinates);
                MatrixHelpers.CopyMatrix3(chain[0].LocalMatrix, strand.rootMat, convertCoordinates);
                var strandRoot = chain[0];

                for (int pointIndex = 0; pointIndex < chain.Count; pointIndex++)
                {
                    var chainNode = chain[pointIndex];
                    var pointLength = GetHairPointLength(chain, pointIndex);
                    var currentPosition = GetNodeWorldPos(chainNode);
                    System.Numerics.Vector3 pointPosition;

                    if (pointIndex < chain.Count - 1)
                    {
                        pointPosition = GetNodeWorldPos(chain[pointIndex + 1]);
                    }
                    else
                    {
                        // no next bone at the tip, so point it along the node's local Y axis
                        var direction = new System.Numerics.Vector3(chainNode.WorldMatrix.M21, chainNode.WorldMatrix.M22, chainNode.WorldMatrix.M23);
                        if (direction.LengthSquared() <= float.Epsilon)
                        {
                            direction = System.Numerics.Vector3.UnitY;
                        }
                        else
                        {
                            direction = System.Numerics.Vector3.Normalize(direction);
                            if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z))
                            {
                                direction = System.Numerics.Vector3.UnitY;
                            }
                        }

                        pointPosition = currentPosition + (direction * pointLength);
                    }

                    // reset position is the world point transformed into the strand root parent's space
                    // i think this is right, works in testing at least :clueless:
                    var parent = NodeHelpers.GetParentNode(strandRoot, model);
                    var parentWorld = parent?.WorldMatrix ?? Matrix4x4.Identity;
                    var resetPosition = Matrix4x4.Invert(parentWorld, out var parentWorldInverse)? System.Numerics.Vector3.Transform(pointPosition, parentWorldInverse) : pointPosition;

                    float radius = 0.0f;
                    float outerRadius = 0.0f;
                    if (pointIndex < chain.Count - 1 && chain.Count > 1)
                    {
                        // taper the hair points as the chain goes down, to zero at the tip
                        // might need to revisit this tbqh, but this does actually work, it is just likely inaccurate for hair that *grows* from the root
                        float t = (float)pointIndex / (chain.Count - 1);
                        radius = MathF.Max(0.0f, 0.75f * (1.0f - (t * 0.5f)));
                        outerRadius = MathF.Max(0.0f, 2.0f * (1.0f - t));
                    }

                    // all the math above happens in gltf space, so only convert at the moment we write the values out
                    if (convertCoordinates)
                    {
                        pointPosition = MatrixHelpers.ConvertGltfVectorToMilo(pointPosition);
                        resetPosition = MatrixHelpers.ConvertGltfVectorToMilo(resetPosition);
                    }

                    var point = new MiloLib.Assets.Char.CharHair.Point();
                    point.bone = chainNode.Name;
                    point.pos = ToMiloVector3(pointPosition);
                    point.unk5c = ToMiloVector3(resetPosition);
                    point.sideLength = -1.0f;
                    point.radius = radius;
                    point.outerRadius = outerRadius;
                    point.length = pointLength;

                    strand.points.Add(point);
                }

                hair.strands.Add(strand);
            }

            if (hair.strands.Count == 0) return;

            string hairName = "hair.hair";
            meta.entries.Add(new DirectoryMeta.Entry("CharHair", hairName, hair));
        }

        public static void ProcessEmptyHairCollides(
            DirectoryMeta meta,
            MiloGame game,
            IEnumerable<(string MeshName, Matrix4x4 LocalMatrix, Matrix4x4 WorldMatrix)> hairMeshes,
            string parentName,
            bool convertCoordinates = false)
        {
            var rev = GameRevisions.GetRevision(game);
            var seenMeshes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var hairMesh in hairMeshes)
            {
                if (!seenMeshes.Add(hairMesh.MeshName))
                {
                    continue;
                }

                string collideName = hairMesh.MeshName.EndsWith(".mesh", StringComparison.OrdinalIgnoreCase)
                    ? hairMesh.MeshName[..^5] + ".coll"
                    : hairMesh.MeshName + ".coll";

                bool collideAlreadyExists = meta.entries.Any(entry =>
                    entry.type.value == "CharCollide" &&
                    string.Equals(entry.name.value, collideName, StringComparison.OrdinalIgnoreCase));

                if (collideAlreadyExists)
                {
                    continue;
                }

                // create a CharCollide for the hair even though it is empty, from looking at the decomp it seemed that there must be one or hair won't be sim, could be wrong
                // TODO: add actual collision support so eople can make basic collision meshes for their models to avoid clipping which is the next logical step for thisd
                var collide = new MiloLib.Assets.Char.CharCollide();

                // i really really need to implement some proper way to do stuff like this in milolib, using reflection to override private fields feels so bad even though it is technically fine
                typeof(MiloLib.Assets.Char.CharCollide).GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(collide, (ushort)7);

                collide.objFields.revision = 2;
                collide.trans = RndTrans.New(rev.TransRevision, 0);
                MatrixHelpers.CopyMatrix(hairMesh.LocalMatrix, collide.trans.localXfm, convertCoordinates);
                MatrixHelpers.CopyMatrix(hairMesh.WorldMatrix, collide.trans.worldXfm, convertCoordinates);
                collide.trans.parentObj = parentName;
                collide.shape = MiloLib.Assets.Char.CharCollide.Shape.kSphere;
                collide.flags = 0;
                collide.mesh = hairMesh.MeshName;
                collide.meshYBias = false;

                collide.unknownTransform.m11 = 1.0f;
                collide.unknownTransform.m22 = 1.0f;
                collide.unknownTransform.m33 = 1.0f;

                collide.structs.Clear();
                for (int i = 0; i < 8; i++)
                {
                    collide.structs.Add(new MiloLib.Assets.Char.CharCollide.CharCollideStruct());
                }

                meta.entries.Add(new DirectoryMeta.Entry("CharCollide", collideName, collide));
            }
        }
    }
}
