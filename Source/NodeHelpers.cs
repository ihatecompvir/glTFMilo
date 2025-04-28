using SharpGLTF.Schema2;

namespace glTFMilo.Source
{
    public class NodeHelpers
    {
        public static bool IsBone(Node node, ModelRoot model)
        {
            // If this node is referenced by any Skin as a joint, it's a bone (should be anyway lol)
            return model.LogicalSkins.Any(skin => skin.Joints.Contains(node));
        }

        public static bool IsPrimitive(Node node)
        {
            // If this node has a mesh, it is a mesh/primitive node
            return node.Mesh != null;
        }

        public static Node GetParentNode(Node targetNode, ModelRoot model)
        {
            foreach (var node in model.LogicalNodes)
            {
                if (node.VisualChildren.Contains(targetNode))
                {
                    return node;
                }
            }
            return null; // probably root, or not found
        }

        public static bool IsGroupNode(Node node, ModelRoot model)
        {
            bool hasMesh = node.Mesh != null;
            bool isBone = model.LogicalSkins.Any(skin => skin.Joints.Contains(node));
            bool hasChildren = node.VisualChildren.Count() > 0;

            return !hasMesh && !isBone && hasChildren;
        }

        public static bool IsLightNode(Node node, ModelRoot model)
        {
            if (node == null) return false;
            return node.PunctualLight != null;
        }

        public static string GetParentBoneName(Node node, ModelRoot model)
        {
            var parent = GetParentNode(node, model);
            if (parent != null && model.LogicalSkins.Any(skin => skin.Joints.Contains(parent)))
            {
                return parent.Name;
            }
            return null;
        }

        public static List<string> GetAllDescendantNames(Node node)
        {
            List<string> names = new List<string>();
            foreach (var child in node.VisualChildren)
            {
                names.Add(child.Name);
                names.AddRange(GetAllDescendantNames(child)); // recurse
            }
            return names;
        }
    }
}
