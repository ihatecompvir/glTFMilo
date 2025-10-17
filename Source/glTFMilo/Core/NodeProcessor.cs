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
        public static void ProcessBoneNode(Node node, ModelRoot model, DirectoryMeta meta, string type, string fallbackParent)
        {
            if (node.Name == "neutral_bone") return;

            if (type == "character" && BoneNames.rb3SkeletonBones.Contains(node.Name))
                return;

            var trans = RndTrans.New(9, 0);
            trans.objFields.revision = 2;

            string parentName = NodeHelpers.GetParentBoneName(node, model) ?? fallbackParent;

            MatrixHelpers.CopyMatrix(node.LocalMatrix, trans.localXfm);
            MatrixHelpers.CopyMatrix(node.WorldMatrix, trans.worldXfm);
            trans.parentObj = parentName;

            meta.entries.Add(new DirectoryMeta.Entry("Trans", node.Name, trans));
        }

        public static void ProcessGroupNode(Node node, ModelRoot model, DirectoryMeta meta, MiloGame game)
        {
            if (node.Name == "Armature") return;

            string overriddenFilename = node.Name + ".grp";

            var rev = GameRevisions.GetRevision(game);

            var group = RndGroup.New(rev.GroupRevision, 0);
            group.objFields.revision = 2;
            group.trans = RndTrans.New(rev.TransRevision, 0);
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

        public static void ProcessLightNode(Node node, DirectoryMeta meta, MiloGame game)
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
            MatrixHelpers.CopyMatrix(node.LocalMatrix, light.trans.localXfm);
            MatrixHelpers.CopyMatrix(node.WorldMatrix, light.trans.worldXfm);

            MiloExtras.AddToObject(node, light, ref overriddenFilename);

            meta.entries.Add(new DirectoryMeta.Entry("Light", overriddenFilename, light));
        }
    }
}
