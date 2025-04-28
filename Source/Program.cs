using System;
using System.Collections.Generic;
using System.Reflection;
using MiloLib;
using MiloLib.Assets;
using MiloLib.Assets.Char;
using MiloLib.Assets.Rnd;
using SharpGLTF.Schema2;
using TeximpNet;
using TeximpNet.Compression;
using TeximpNet.DDS;
using System.Numerics;
using MiloLib.Assets.Band;
using CommandLine;

namespace glTFMilo.Source
{


    internal class Program
    {

        // TODO: put these into their own class or something instead of randomyly at the top of this
        public static bool ConvertToDDS(Stream inputStream, string outputPath, CompressionFormat format)
        {
            // Delete the existing file at outputPath if one exists
            File.Delete(outputPath);
            if (inputStream.CanSeek)
                inputStream.Position = 0;

            Surface image = Surface.LoadFromStream(inputStream);
            if (image == null)
                throw new InvalidOperationException("Failed to load input image from stream.");

            if (image.Width % 4 != 0 || image.Height % 4 != 0)
                throw new InvalidOperationException($"BC1 compression requires image dimensions to be multiples of 4. Current dimensions: {image.Width}x{image.Height}");

            // check if either dimension is larger than 2048 x 2048 (which seems to be the limit to textures in Milo)
            if (image.Width > 2048 || image.Height > 2048)
            {
                float scale = Math.Min(2048f / image.Width, 2048f / image.Height);
                int newWidth = (int)(image.Width * scale);
                int newHeight = (int)(image.Height * scale);
                bool succeeded = image.Resize(newWidth, newHeight, ImageFilter.Lanczos3);
                if (!succeeded)
                {
                    throw new InvalidOperationException($"Failed to resize image to {newWidth}x{newHeight}.");
                }
            }

            image.FlipVertically();

            using (Compressor compressor = new Compressor())
            {
                compressor.Input.GenerateMipmaps = false;
                compressor.Input.SetData(image);
                compressor.Compression.Format = format;


                var stream = System.IO.File.Open(outputPath, FileMode.Create, FileAccess.Write);
                try
                {

                    bool success = compressor.Process(stream);
                    if (!success)
                    {
                        throw new InvalidOperationException("DDS compression failed.");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error writing DDS file: {e.Message}");
                    return false;
                }

                // close stream
                stream.Close();
                return true;
            }
        }

        // crappy way to parse a DDS file
        // TODO: create a proper class
        public static (int width, int height, int bpp, int mipMapCount, byte[] pixelData) ParseDDS(string ddsFilePath)
        {
            byte[] fileBytes = File.ReadAllBytes(ddsFilePath);
            if (fileBytes.Length <= 128)
            {
                Console.WriteLine("Invalid DDS file.");
                return (0, 0, 0, 0, new byte[0]);
            }

            using (var ms = new MemoryStream(fileBytes))
            using (var br = new BinaryReader(ms))
            {
                // Check magic number "DDS " to see if we are really dealing with a dds
                if (br.ReadUInt32() != 0x20534444)
                    throw new InvalidOperationException("Not a valid DDS file.");

                br.BaseStream.Seek(8, SeekOrigin.Current);
                int height = br.ReadInt32();
                int width = br.ReadInt32();
                br.BaseStream.Seek(8, SeekOrigin.Current);
                int mipMapCount = br.ReadInt32();
                br.BaseStream.Seek(44, SeekOrigin.Current);
                br.BaseStream.Seek(4, SeekOrigin.Current);
                uint pfFlags = br.ReadUInt32();
                uint fourCC = br.ReadUInt32();

                int bpp = fourCC switch
                {
                    0x31545844 => 4, // 'DXT1' = BC1 = 4 bpp
                    0x33545844 => 8, // 'DXT3' = BC2 = 8 bpp
                    0x35545844 => 8, // 'DXT5' = BC3 = 8 bpp
                    0x32495441 => 8, // 'ATI2' = BC5 = 8 bpp
                    _ => throw new NotSupportedException($"Unsupported format FourCC: 0x{fourCC:X}")
                };

                byte[] pixelData = new byte[fileBytes.Length - 128];
                Array.Copy(fileBytes, 128, pixelData, 0, pixelData.Length);

                return (width, height, bpp, mipMapCount, pixelData);
            }
        }

        static bool IsBone(Node node, ModelRoot model)
        {
            // If this node is referenced by any Skin as a joint, it's a bone (should be anyway lol)
            return model.LogicalSkins.Any(skin => skin.Joints.Contains(node));
        }

        static bool IsPrimitive(Node node)
        {
            // If this node has a mesh, it is a mesh/primitive node
            return node.Mesh != null;
        }

        static Node GetParentNode(Node targetNode, ModelRoot model)
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

        static bool IsGroupNode(Node node, ModelRoot model)
        {
            bool hasMesh = node.Mesh != null;
            bool isBone = model.LogicalSkins.Any(skin => skin.Joints.Contains(node));
            bool hasChildren = node.VisualChildren.Count() > 0;

            return !hasMesh && !isBone && hasChildren;
        }

        static bool IsLightNode(Node node, ModelRoot model)
        {
            if (node == null) return false;
            return node.PunctualLight != null;
        }

        static string GetParentBoneName(Node node, ModelRoot model)
        {
            var parent = GetParentNode(node, model);
            if (parent != null && model.LogicalSkins.Any(skin => skin.Joints.Contains(parent)))
            {
                return parent.Name;
            }
            return null;
        }

        static List<string> GetAllDescendantNames(Node node)
        {
            List<string> names = new List<string>();
            foreach (var child in node.VisualChildren)
            {
                names.Add(child.Name);
                names.AddRange(GetAllDescendantNames(child)); // recurse
            }
            return names;
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(Run);
        }


        static void Run(Options opts)
        {
            string filePath = opts.Input;
            string outputPath = opts.Output;
            string platform = opts.Platform.ToLower();
            string gameArg = opts.Game.ToLower();
            string preLit = opts.Prelit.ToLower();

            if (!System.IO.File.Exists(filePath))
            {
                Console.WriteLine("File does not exist.");
                return;
            }

            if (!filePath.EndsWith(".gltf") && !filePath.EndsWith(".glb"))
            {
                Console.WriteLine("File is not a glTF file.");
                return;
            }

            Game selectedGame = Game.RockBand3;
            if (gameArg == "tbrb")
            {
                selectedGame = Game.TheBeatlesRockBand;
            }
            else if (gameArg == "rb3")
            {
                selectedGame = Game.RockBand3;
            }
            else if (gameArg == "rb2")
            {
                selectedGame = Game.RockBand2;
            }
            else
            {
                Console.WriteLine("Invalid game specified. Defaulting to Rock Band 3.");
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
                Console.WriteLine("Invalid platform specified. Defaulting to Xbox.");
                meta.platform = DirectoryMeta.Platform.Xbox;
            }

            meta.revision = GameRevisions.GetRevision(selectedGame).MiloRevision;
            meta.type = "Character"; // root is a character dir, in the future we should support more kinds of dirs

            List<(string, Matrix4x4)> bandConfigurationPositions = new();

            // loop through all gltf nodes and create a milo asset that matches the type of node it is
            // TODO: add lights, possibly other kinds
            foreach (var node in model.LogicalNodes)
            {
                if (IsPrimitive(node))
                {
                    if (node.Mesh != null)
                    {
                        int primitiveIndex = 0;
                        foreach (var primitive in node.Mesh.Primitives)
                        {
                            RndMesh mesh = RndMesh.New(33, 0, 0, 0);
                            mesh.objFields.revision = 2;
                            mesh.trans = RndTrans.New(9, 0);
                            mesh.trans.parentObj = filename;
                            mesh.draw = RndDrawable.New(3, 0);
                            mesh.draw.sphere = new MiloLib.Classes.Sphere();
                            mesh.draw.sphere.radius = 10000.0f;

                            mesh.volume = RndMesh.Volume.kVolumeTriangles;

                            mesh.keepMeshData = true;
                            mesh.hasAOCalculation = false;

                            var localMatrix = node.LocalMatrix;
                            mesh.trans.localXfm.m11 = localMatrix.M11;
                            mesh.trans.localXfm.m12 = localMatrix.M12;
                            mesh.trans.localXfm.m13 = localMatrix.M13;
                            mesh.trans.localXfm.m21 = localMatrix.M21;
                            mesh.trans.localXfm.m22 = localMatrix.M22;
                            mesh.trans.localXfm.m23 = localMatrix.M23;
                            mesh.trans.localXfm.m31 = localMatrix.M31;
                            mesh.trans.localXfm.m32 = localMatrix.M32;
                            mesh.trans.localXfm.m33 = localMatrix.M33;
                            mesh.trans.localXfm.m41 = localMatrix.M41;
                            mesh.trans.localXfm.m42 = localMatrix.M42;
                            mesh.trans.localXfm.m43 = localMatrix.M43;

                            if (node.Mesh != null)
                            {
                                if (primitive.Material != null)
                                {
                                    mesh.mat = primitive.Material.Name + ".mat";
                                }
                                var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
                                var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
                                var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
                                var tangents = primitive.GetVertexAccessor("TANGENT")?.AsVector4Array();
                                var weights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
                                var joints = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
                                var colors = primitive.GetVertexAccessor("COLOR_0")?.AsVector4Array();
                                var indices = primitive.IndexAccessor.AsIndicesArray();

                                // if there are no positions this isn't going to be valid geometry, so just bail out
                                if (positions == null) return;
                                var originalIndexToNewIndex = new Dictionary<uint, ushort>();
                                mesh.vertices.vertices.Clear();

                                for (uint originalIndex = 0; originalIndex < positions.Count; ++originalIndex)
                                {

                                    Vertex newVert = new Vertex();
                                    var pos = positions[(int)originalIndex];
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
                                        // VERTEX.NORMALS.X/Y/Z ARE WRONG!!! USE NORMALS.NX/NY/NZ
                                        var normal = normals[(int)originalIndex];
                                        newVert.nx = normal.X;
                                        newVert.ny = normal.Y;
                                        newVert.nz = normal.Z;
                                    }

                                    if (tangents != null && originalIndex < tangents.Count)
                                    {
                                        var tangent = tangents[(int)originalIndex];
                                        newVert.tangent0 = tangent.X;
                                        newVert.tangent1 = tangent.Y;
                                        newVert.tangent2 = tangent.Z;
                                        newVert.tangent3 = tangent.W;
                                    }

                                    if (weights != null && originalIndex < weights.Count)
                                    {
                                        // write weights, if the mesh is not skinned
                                        var weight = weights[(int)originalIndex];
                                        newVert.weight0 = weight.X;
                                        newVert.weight1 = weight.Y;
                                        newVert.weight2 = weight.Z;
                                        newVert.weight3 = weight.W;
                                    }
                                    else if (colors != null && originalIndex < colors.Count)
                                    {
                                        var vertexColors = colors[(int)originalIndex];
                                        newVert.weight0 = vertexColors.X;
                                        newVert.weight1 = vertexColors.Y;
                                        newVert.weight2 = vertexColors.Z;
                                        newVert.weight3 = vertexColors.W;
                                    }
                                    else
                                    {
                                        newVert.weight0 = 0.0f;
                                        newVert.weight1 = 0.0f;
                                        newVert.weight2 = 0.0f;
                                        newVert.weight3 = 0.0f;
                                    }

                                    if (joints != null && originalIndex < joints.Count)
                                    {
                                        // i presume this to be correct, who knows if it is *shrug*
                                        // it seems to be, anyway
                                        var joint = joints[(int)originalIndex];
                                        newVert.bone0 = (ushort)joint.X;
                                        newVert.bone1 = (ushort)joint.Y;
                                        newVert.bone2 = (ushort)joint.Z;
                                        newVert.bone3 = (ushort)joint.W;
                                    }

                                    mesh.vertices.vertices.Add(newVert);
                                    ushort newIndex = (ushort)(mesh.vertices.vertices.Count - 1);

                                    if (mesh.vertices.vertices.Count > ushort.MaxValue)
                                    {
                                        Console.Error.WriteLine("Warning: Vertex count exceeds ushort.MaxValue!");
                                    }

                                    originalIndexToNewIndex[originalIndex] = newIndex;
                                }

                                mesh.faces.Clear();
                                for (int i = 0; i < indices.Count; i += 3)
                                {
                                    RndMesh.Face face = new RndMesh.Face();

                                    face.idx1 = originalIndexToNewIndex[indices[i]];
                                    face.idx2 = originalIndexToNewIndex[indices[i + 1]];
                                    face.idx3 = originalIndexToNewIndex[indices[i + 2]];

                                    mesh.faces.Add(face);
                                }


                            }


                            /*
                            // write bone transforms
                            var skin = node.Skin; // Skin associated with this mesh node
                            if (skin != null)
                            {
                                var inverseBindMatricesAccessor = skin.GetInverseBindMatricesAccessor();
                                if (inverseBindMatricesAccessor != null)
                                {
                                    var gltfInverseBindMatrices = inverseBindMatricesAccessor.AsMatrix4x4Array();
                                    var joints = skin.Joints;

                                    var boneTransList = new List<RndMesh.BoneTransform>();


                                    for (int i = 0; i < 1; i++)
                                    {
                                        var jointNode = joints[i];
                                        var miloBoneTransform = new RndMesh.BoneTransform();

                                        miloBoneTransform.name = jointNode.Name ?? $"joint_{i}";

                                        miloBoneTransform.transform.m11 = jointNode.LocalMatrix.M11;
                                        miloBoneTransform.transform.m12 = jointNode.LocalMatrix.M12;
                                        miloBoneTransform.transform.m13 = jointNode.LocalMatrix.M13;
                                        miloBoneTransform.transform.m21 = jointNode.LocalMatrix.M21;
                                        miloBoneTransform.transform.m22 = jointNode.LocalMatrix.M22;
                                        miloBoneTransform.transform.m23 = jointNode.LocalMatrix.M23;
                                        miloBoneTransform.transform.m31 = jointNode.LocalMatrix.M31;
                                        miloBoneTransform.transform.m32 = jointNode.LocalMatrix.M32;
                                        miloBoneTransform.transform.m33 = jointNode.LocalMatrix.M33;
                                        miloBoneTransform.transform.m41 = jointNode.LocalMatrix.M41;
                                        miloBoneTransform.transform.m42 = jointNode.LocalMatrix.M42;
                                        miloBoneTransform.transform.m43 = jointNode.LocalMatrix.M43;

                                        boneTransList.Add(miloBoneTransform);
                                    }
                                    mesh.boneTransforms = boneTransList;


                                }
                                else
                                {
                                    Console.WriteLine($"Warning: Skinned mesh node '{node.Name}' is linked to skin '{skin.Name ?? "Unnamed"}' which is missing the Inverse Bind Matrices accessor.");
                                }
                            }
                            */


                            if (primitiveIndex == 0)
                            {
                                DirectoryMeta.Entry entry = new DirectoryMeta.Entry("Mesh", node.Name + ".mesh", mesh);
                                meta.entries.Add(entry);
                            }
                            else
                            {
                                DirectoryMeta.Entry entry = new DirectoryMeta.Entry("Mesh", node.Name + "_" + primitiveIndex + ".mesh", mesh);
                                meta.entries.Add(entry);
                            }

                            primitiveIndex++;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{node.Name} has no mesh but is a mesh node. Can not convert glTF.");
                    }
                }
                else if (IsBone(node, model))
                {
                    RndTrans trans = RndTrans.New(9, 0);
                    trans.objFields.revision = 2;
                    string parentNodeName = GetParentBoneName(node, model);

                    if (parentNodeName != null)
                    {
                        var localMatrix = node.LocalMatrix;

                        trans.localXfm.m11 = localMatrix.M11;
                        trans.localXfm.m12 = localMatrix.M12;
                        trans.localXfm.m13 = localMatrix.M13;

                        trans.localXfm.m21 = localMatrix.M21;
                        trans.localXfm.m22 = localMatrix.M22;
                        trans.localXfm.m23 = localMatrix.M23;

                        trans.localXfm.m31 = localMatrix.M31;
                        trans.localXfm.m32 = localMatrix.M32;
                        trans.localXfm.m33 = localMatrix.M33;

                        trans.localXfm.m41 = localMatrix.M41;
                        trans.localXfm.m42 = localMatrix.M42;
                        trans.localXfm.m43 = localMatrix.M43;

                        trans.parentObj = parentNodeName;
                    }
                    else
                    {
                        var worldMatrix = node.WorldMatrix;

                        trans.worldXfm.m11 = worldMatrix.M11;
                        trans.worldXfm.m12 = worldMatrix.M12;
                        trans.worldXfm.m13 = worldMatrix.M13;

                        trans.worldXfm.m21 = worldMatrix.M21;
                        trans.worldXfm.m22 = worldMatrix.M22;
                        trans.worldXfm.m23 = worldMatrix.M23;

                        trans.worldXfm.m31 = worldMatrix.M31;
                        trans.worldXfm.m32 = worldMatrix.M32;
                        trans.worldXfm.m33 = worldMatrix.M33;

                        trans.worldXfm.m41 = worldMatrix.M41;
                        trans.worldXfm.m42 = worldMatrix.M42;
                        trans.worldXfm.m43 = worldMatrix.M43;
                    }

                    if (parentNodeName != null)
                    {
                        trans.parentObj = parentNodeName;
                    }
                    else
                    {
                        trans.parentObj = meta.name;
                    }

                    DirectoryMeta.Entry entry = new DirectoryMeta.Entry("Trans", node.Name, trans);
                    meta.entries.Add(entry);
                }
                else if (IsGroupNode(node, model))
                {
                    RndGroup grp = RndGroup.New(GameRevisions.GetRevision(selectedGame).GroupRevision, 0);
                    grp.trans = RndTrans.New(GameRevisions.GetRevision(selectedGame).TransRevision, 0);
                    grp.draw = RndDrawable.New(GameRevisions.GetRevision(selectedGame).DrawableRevision, 0);
                    grp.objFields.revision = 2;
                    grp.anim = RndAnimatable.New(GameRevisions.GetRevision(selectedGame).AnimatableRevision, 0);
                    List<string> children = GetAllDescendantNames(node);
                    if (children.Count > 0)
                    {
                        foreach (var child in children)
                        {
                            // add all children to the grp

                            if (child != null)
                                grp.objects.Add(child);
                        }
                    }
                    DirectoryMeta.Entry entry = new DirectoryMeta.Entry("Group", node.Name + ".grp", grp);
                    meta.entries.Add(entry);
                }
                else if (IsLightNode(node, model))
                {
                    Console.WriteLine("Node is Light Node, create RndLight");
                    RndLight light = new RndLight();

                    // todo: remove this reflection stuff eventually
                    typeof(RndLight)
                        .GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                        .SetValue(light, (ushort)GameRevisions.GetRevision(selectedGame).LightRevision);
                    light.objFields.revision = 2;
                    light.range = node.PunctualLight.Range;
                    light.colorOwner = node.Name + ".lit";
                    light.color = new MiloLib.Classes.HmxColor4(node.PunctualLight.Color.X, node.PunctualLight.Color.Y, node.PunctualLight.Color.Z, 1.0f);
                    switch (node.PunctualLight.LightType)
                    {
                        case PunctualLightType.Point:
                            light.type = RndLight.Type.kPoint;
                            break;
                        case PunctualLightType.Spot:
                            light.type = RndLight.Type.kSpot;
                            break;
                        case PunctualLightType.Directional:
                            light.type = RndLight.Type.kDirectional;
                            break;
                        default:
                            light.type = RndLight.Type.kPoint;
                            break;
                    }

                    // set light transform
                    light.trans = RndTrans.New(GameRevisions.GetRevision(selectedGame).TransRevision, 0);

                    light.trans.localXfm.m11 = node.WorldMatrix.M11;
                    light.trans.localXfm.m12 = node.WorldMatrix.M12;
                    light.trans.localXfm.m13 = node.WorldMatrix.M13;
                    light.trans.localXfm.m21 = node.WorldMatrix.M21;
                    light.trans.localXfm.m22 = node.WorldMatrix.M22;
                    light.trans.localXfm.m23 = node.WorldMatrix.M23;
                    light.trans.localXfm.m31 = node.WorldMatrix.M31;
                    light.trans.localXfm.m32 = node.WorldMatrix.M32;
                    light.trans.localXfm.m33 = node.WorldMatrix.M33;
                    light.trans.localXfm.m41 = node.WorldMatrix.M41;
                    light.trans.localXfm.m42 = node.WorldMatrix.M42;
                    light.trans.localXfm.m43 = node.WorldMatrix.M43;

                    DirectoryMeta.Entry entry = new DirectoryMeta.Entry("Light", node.Name + ".lit", light);
                    meta.entries.Add(entry);
                }
                else
                {
                    /*
                    // check if it is one of the BandConfiguration nodes (which is named player_bass0, player_guitar0, player_drum0, player_vocals0, player_keyboard0)
                    if (node.Name == "player_bass0" || node.Name == "player_guitar0" || node.Name == "player_drum0" || node.Name == "player_vocals0" || node.Name == "player_keyboard0")
                    {
                        // add the position to the bandConfigurationPositions list
                        bandConfigurationPositions.Add((node.Name, node.LocalMatrix));
                    }
                    */

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
                    mat.zMode = RndMat.ZMode.kZModeNormal;
                    mat.perPixelLit = true;
                    if (opts.Prelit != "false")
                    {
                        mat.preLit = true;
                    }
                    mat.pointLights = true;
                    mat.projLights = true;
                    mat.fog = false;
                    mat.cull = true;
                    mat.shaderVariation = RndMat.ShaderVariation.kShaderVariationNone;
                    mat.blend = RndMat.Blend.kBlendSrc;
                    mat.emissiveMultiplier = 1.0f;
                    mat.specularPower = 0.0f;
                    mat.normalDetailTiling = 1.0f;
                    mat.rimPower = 0.0f;
                    mat.specular2Power = 0.0f;
                    mat.rimRGB = new MiloLib.Classes.HmxColor3(0.0f, 0.0f, 0.0f, 0.0f);
                    mat.rimPower = 4.0f;
                    mat.specularPower = 10.0f;
                    mat.specular2Power = 10.0f;



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
                        ConvertToDDS(str, $"output_{curmat}.dds", format);

                        var (width, height, bpp, mipMapCount, pixels) = ParseDDS($"output_{curmat}.dds");
                        tex.width = (uint)width;
                        tex.height = (uint)height;
                        tex.bpp = (uint)bpp;
                        tex.externalPath = material.Name + ".png";
                        tex.mipMapK = -8.0f;
                        tex.type = RndTex.Type.kRegular;

                        tex.bitmap = RndBitmap.New(1, 0);
                        tex.bitmap.height = (ushort)tex.height;
                        tex.bitmap.width = (ushort)tex.width;
                        tex.bitmap.bpp = (byte)tex.bpp;
                        tex.bitmap.encoding = hasAlpha ? RndBitmap.TextureEncoding.DXT5_BC3 : RndBitmap.TextureEncoding.DXT1_BC1;
                        tex.bitmap.mipMaps = 0;
                        tex.bitmap.bpl = (ushort)((width * bpp) / 8);

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

                            tex.optimizeForPS3 = true;
                        }
                    }

                    // delete the dds file that was created so we don't have a bunch of dds files lying around
                    File.Delete($"output_{curmat}.dds");

                    DirectoryMeta.Entry texEntry = new DirectoryMeta.Entry("Tex", material.Name + ".tex", tex);
                    meta.entries.Add(texEntry);

                    mat.objFields.revision = 2;
                }

                var normalMapTexture = material.FindChannel("Normal")?.Texture;

                RndTex normalTex = RndTex.New(GameRevisions.GetRevision(selectedGame).TextureRevision, 0);
                normalTex.objFields.revision = 2;

                if (normalMapTexture != null)
                {
                    using (var str = normalMapTexture.PrimaryImage.Content.Open())
                    {
                        ConvertToDDS(str, $"output_{curmat}_norm.dds", CompressionFormat.BC5);
                        var (width, height, bpp, mipMapCount, pixels) = ParseDDS($"output_{curmat}_norm.dds");
                        normalTex.width = (uint)width;
                        normalTex.height = (uint)height;
                        normalTex.bpp = (uint)bpp;
                        normalTex.externalPath = material.Name + "_norm.png";
                        normalTex.mipMapK = -8.0f;
                        normalTex.type = RndTex.Type.kRegular;

                        normalTex.bitmap = RndBitmap.New(1, 0);
                        normalTex.bitmap.height = (ushort)normalTex.height;
                        normalTex.bitmap.width = (ushort)normalTex.width;
                        normalTex.bitmap.bpp = (byte)normalTex.bpp;
                        normalTex.bitmap.encoding = RndBitmap.TextureEncoding.ATI2_BC5;
                        normalTex.bitmap.mipMaps = 0;
                        normalTex.bitmap.bpl = (ushort)((width * bpp) / 8);

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

                            normalTex.optimizeForPS3 = true;
                        }
                    }

                    mat.normalMap = material.Name + "_norm.tex";

                    File.Delete($"output_{curmat}_norm.dds");

                    DirectoryMeta.Entry texEntry = new DirectoryMeta.Entry("Tex", material.Name + "_norm.tex", normalTex);
                    meta.entries.Add(texEntry);
                }

            }

            // create a new Group with all the geometry inside of it
            RndGroup allGeomGrp = RndGroup.New(GameRevisions.GetRevision(selectedGame).GroupRevision, 0);

            allGeomGrp.trans = RndTrans.New(GameRevisions.GetRevision(selectedGame).TransRevision, 0);
            allGeomGrp.draw = RndDrawable.New(GameRevisions.GetRevision(selectedGame).DrawableRevision, 0);
            allGeomGrp.draw.sphere = new MiloLib.Classes.Sphere();
            allGeomGrp.draw.sphere.radius = 10000.0f;
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



            Character character = Character.New(GameRevisions.GetRevision(selectedGame).CharacterRevision, 0);
            character.viewports = new();

            // default empty viewports, still not sure what viewports even are
            character.viewports.Add(new MiloLib.Classes.Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.viewports.Add(new MiloLib.Classes.Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.viewports.Add(new MiloLib.Classes.Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.viewports.Add(new MiloLib.Classes.Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.viewports.Add(new MiloLib.Classes.Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.viewports.Add(new MiloLib.Classes.Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.viewports.Add(new MiloLib.Classes.Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.currentViewportIdx = 6;

            character.objFields.revision = 2;

            // give it a huge radius so it will always appear
            character.anim = RndAnimatable.New(GameRevisions.GetRevision(selectedGame).AnimatableRevision, 0);
            character.draw = RndDrawable.New(GameRevisions.GetRevision(selectedGame).DrawableRevision, 0);
            character.draw.sphere.radius = 10000.0f;
            character.trans = RndTrans.New(GameRevisions.GetRevision(selectedGame).TransRevision, 0);
            character.sphereBase = meta.name;

            character.charTest = Character.CharacterTesting.New(GameRevisions.GetRevision(selectedGame).CharacterTestingRevision, 0);
            character.charTest.distMap = "none";


            // reflection hack to set revisions until I implement something proper in MiloLib
            // TODO: GET RID OF THIS SHIT
            typeof(RndDir)
                .GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(character, (ushort)GameRevisions.GetRevision(selectedGame).RndDirRevision);

            typeof(ObjectDir)
                .GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(character, (ushort)GameRevisions.GetRevision(selectedGame).ObjectDirRevision);

            // todo: restore this but use the git commit hash
            //character.objFields.note = "Milo created with glTFMilo";



            meta.directory = character;


            MiloFile miloFile = new MiloFile(meta);

            miloFile.Save(opts.Output, MiloFile.Type.Uncompressed, 0x810, MiloLib.Utils.Endian.LittleEndian, MiloLib.Utils.Endian.BigEndian);

            Console.WriteLine("Milo scene created at " + opts.Output);
        }
    }
}
