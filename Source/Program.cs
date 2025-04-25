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

namespace glTFMilo.Source
{
    internal class Program
    {
        // TODO: put these into their own class or something instead of randomyly at the top of this
        public static bool ConvertToDDS(Stream inputStream, string outputPath)
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

            image.FlipVertically();

            using (Compressor compressor = new Compressor())
            {
                compressor.Input.GenerateMipmaps = false;
                compressor.Input.SetData(image);
                compressor.Compression.Format = CompressionFormat.BC1;

                bool success = compressor.Process(out DDSContainer ddsContainer);
                if (!success || ddsContainer == null)
                {
                    throw new InvalidOperationException("DDS compression failed even with valid dimensions.");
                }


                using (var stream = System.IO.File.Open(outputPath, FileMode.Create, FileAccess.Write))
                {
                    try
                    {
                        ddsContainer.Write(stream);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error writing DDS file: {e.Message}");
                        return false;
                    }
                }

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
            if (args.Length == 0)
            {
                Console.WriteLine("glTFMilo - glTF to Milo converter");
                Console.WriteLine("Usage: glTFMilo <input.gltf/glb> <output.milo> <platform (xbox/ps3)>");
                return;
            }
            string filePath = args[0];

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

            var model = ModelRoot.Load(filePath);

            DirectoryMeta meta = new DirectoryMeta();

            string filename = Path.GetFileNameWithoutExtension(filePath);
            meta.name = filename;

            // check if second arg is "Ps3" or "xbox" to set platform
            string platform = args[2];
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

            meta.revision = 28; // rb3's revision
            meta.type = "Character"; // root is a character dir, in the future we should support more kinds of dirs

            // loop through all gltf nodes and create a milo asset that matches the type of node it is
            // TODO: add lights, possibly other kinds
            foreach (var node in model.LogicalNodes)
            {
                if (IsPrimitive(node))
                {
                    RndMesh mesh = RndMesh.New(33, 0, 0, 0);
                    mesh.objFields.revision = 2;
                    mesh.trans = RndTrans.New(9, 0);
                    mesh.trans.parentObj = filename;
                    mesh.draw = RndDrawable.New(3, 0);
                    mesh.draw.sphere = new MiloLib.Classes.Sphere();
                    mesh.draw.sphere.radius = 10000.0f;

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
                        if (node.Mesh.Primitives.Count == 1)
                        {
                            foreach (var primitive in node.Mesh.Primitives)
                            {
                                if (primitive.Material != null)
                                {
                                    mesh.mat = primitive.Material.Name;
                                }
                                var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
                                var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
                                var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
                                var tangents = primitive.GetVertexAccessor("TANGENT")?.AsVector4Array();
                                var weights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
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
                                        var normal = normals[(int)originalIndex];
                                        newVert.normals.x = normal.X;
                                        newVert.normals.y = normal.Y;
                                        newVert.normals.z = normal.Z;
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
                                        var weight = weights[(int)originalIndex];
                                        newVert.weight0 = weight.X;
                                        newVert.weight1 = weight.Y;
                                        newVert.weight2 = weight.Z;
                                        newVert.weight3 = weight.W;
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
                        }
                        else
                        {
                            throw new Exception("Too many primitives in Node! Make sure each node has only a single primitive.");
                        }
                    }

                    DirectoryMeta.Entry entry = new DirectoryMeta.Entry("Mesh", node.Name, mesh);
                    meta.entries.Add(entry);
                }
                else if (IsBone(node, model))
                {
                    RndTrans trans = RndTrans.New(9, 0);

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

                    // set up the parent bone if there is one
                    string parentNodeName = GetParentBoneName(node, model);
                    if (parentNodeName != null)
                    {
                        trans.parentObj = parentNodeName;
                    }

                    DirectoryMeta.Entry entry = new DirectoryMeta.Entry("Trans", node.Name, trans);
                    meta.entries.Add(entry);
                }
                else if (IsGroupNode(node, model))
                {
                    RndGroup grp = RndGroup.New(0xE, 0);
                    grp.trans = RndTrans.New(9, 0);
                    grp.draw = RndDrawable.New(3, 0);
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
                    DirectoryMeta.Entry entry = new DirectoryMeta.Entry("Group", node.Name, grp);
                    meta.entries.Add(entry);
                }
            }

            int curmat = 0;

            // loop through all materials
            foreach (var material in model.LogicalMaterials)
            {
                RndMat mat = RndMat.New(0x44, 0);

                DirectoryMeta.Entry matEntry = new DirectoryMeta.Entry("Mat", material.Name, mat);

                meta.entries.Add(matEntry);

                RndTex tex = RndTex.New(0xB, 0);
                tex.objFields.revision = 2;

                var baseColorTexture = material.FindChannel("BaseColor")?.Texture;
                if (baseColorTexture != null)
                {
                    curmat++;
                    mat.diffuseTex = material.Name + ".tex";
                    mat.stencilMode = RndMat.StencilMode.kStencilIgnore;
                    mat.zMode = RndMat.ZMode.kZModeNormal;
                    mat.perPixelLit = true;
                    mat.preLit = false;
                    mat.blend = RndMat.Blend.kBlendSrc;
                    mat.texWrap = RndMat.TexWrap.kTexWrapClamp;
                    mat.emissiveMultiplier = 1.0f;
                    mat.specularPower = 0.0f;
                    mat.normalDetailTiling = 1.0f;
                    mat.rimPower = 1.0f;
                    mat.specular2Power = 0.0f;
                    mat.cull = true;
                    using (var str = baseColorTexture.PrimaryImage.Content.Open())
                    {
                        // shit
                        ConvertToDDS(str, $"output_{curmat}.dds");
                        var (width, height, bpp, mipMapCount, pixels) = ParseDDS($"output_{curmat}.dds");
                        tex.width = (uint)width;
                        tex.height = (uint)height;
                        tex.bpp = (uint)bpp;

                        tex.bitmap = RndBitmap.New(1, 0);
                        tex.bitmap.height = (ushort)tex.height;
                        tex.bitmap.width = (ushort)tex.width;
                        tex.bitmap.bpp = (byte)tex.bpp;
                        tex.bitmap.encoding = RndBitmap.TextureEncoding.DXT1_BC1;
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
                        }
                    }

                    // delete the dds file that was created so we don't have a bunch of dds files lying around
                    File.Delete($"output_{curmat}.dds");

                    DirectoryMeta.Entry texEntry = new DirectoryMeta.Entry("Tex", material.Name + ".tex", tex);
                    meta.entries.Add(texEntry);
                }
                /*
                // todo: restore this so normals can be imported
                var normalMapTexture = material.FindChannel("Normal")?.Texture;
                if (normalMapTexture != null)
                {
                    mat.normalMap = material.Name + "_normal.tex";
                    using (var str = normalMapTexture.PrimaryImage.Content.Open())
                    {
                        ConvertToDDS(str, "output.dds");
                        var (width, height, bpp, mipMapCount, pixels) = ParseDDS("output.dds");
                        tex.width = (uint)width;
                        tex.height = (uint)height;
                        tex.bpp = (uint)bpp;
                        tex.externalPath = material.Name + ".png";

                        tex.bitmap = RndBitmap.New(1, 0);
                        tex.bitmap.height = (ushort)tex.height;
                        tex.bitmap.width = (ushort)tex.width;
                        tex.bitmap.bpp = (byte)tex.bpp;
                        tex.bitmap.encoding = RndBitmap.TextureEncoding.DXT1_BC1;
                        tex.bitmap.mipMaps = 0;

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

                    DirectoryMeta.Entry texEntry = new DirectoryMeta.Entry("Tex", material.Name + "_normal.tex", tex);
                    meta.entries.Add(texEntry);
            }
                */

            }

            Character character = Character.New(17, 0);
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
            character.anim = RndAnimatable.New(4, 0);
            character.draw = RndDrawable.New(3, 0);
            character.draw.sphere.radius = 10000.0f;
            character.trans = RndTrans.New(9, 0);
            character.sphereBase = meta.name;

            character.charTest = Character.CharacterTesting.New(15, 0);
            character.charTest.distMap = "none";


            // reflection hack to set revisions until I implement something proper in MiloLib
            // TODO: GET RID OF THIS SHIT
            typeof(RndDir)
                .GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(character, (ushort)10);

            typeof(ObjectDir)
                .GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(character, (ushort)27);

            // todo: restore this but use the git commit hash
            //character.objFields.note = "Milo created with glTFMilo";



            meta.directory = character;


            MiloFile miloFile = new MiloFile(meta);

            miloFile.Save(args[1], MiloFile.Type.Uncompressed, 0x810, MiloLib.Utils.Endian.LittleEndian, MiloLib.Utils.Endian.BigEndian);

            Console.WriteLine("Milo scene created at " + args[1]);
        }
    }
}
