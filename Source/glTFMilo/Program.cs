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
using System.Numerics;
using System.Reflection;
using TeximpNet;
using TeximpNet.Compression;

namespace MiloGLTFUtils.Source.glTFMilo
{
    public class Program
    {
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

            if (opts.Type == "character" || opts.Type == "instrument")
                meta.type = "Character";
            else
                meta.type = "RndDir";

            List<(string, Matrix4x4)> bandConfigurationPositions = new();

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

                            RndMesh mesh = RndMesh.New(33, 0, 0, 0);
                            mesh.objFields.revision = 2;
                            mesh.trans = RndTrans.New(9, 0);
                            mesh.trans.parentObj = filename;
                            mesh.draw = RndDrawable.New(3, 0);
                            mesh.draw.sphere = new Sphere();
                            mesh.draw.sphere.radius = 10000.0f;

                            mesh.volume = RndMesh.Volume.kVolumeTriangles;

                            mesh.keepMeshData = true;
                            mesh.hasAOCalculation = false;

                            MatrixHelpers.CopyMatrix(node.LocalMatrix, mesh.trans.localXfm);
                            MatrixHelpers.CopyMatrix(node.WorldMatrix, mesh.trans.worldXfm);

                            List<string> influencingJoints = new List<string>();

                            if (node.Mesh != null)
                            {
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
                                }
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

                                IList<System.Numerics.Vector4> weights = null;
                                try { weights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array(); }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error reading WEIGHTS_0: {e.Message}");
                                    Logger.Error("WEIGHTS data will be improper.");
                                }

                                IList<System.Numerics.Vector4> joints = null;
                                try { joints = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array(); }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error reading JOINTS_0: {e.Message}");
                                    Logger.Error("JOINTS data will be improper.");
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
                                try { indices = primitive.IndexAccessor?.AsIndicesArray(); }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error reading indices: {e.Message}");
                                    Logger.Error("Cannot continue creating mesh.");
                                }


                                // if there are no positions this isn't going to be valid geometry, so just bail out
                                if (positions == null) return;
                                var originalIndexToNewIndex = new Dictionary<uint, ushort>();
                                mesh.vertices.vertices.Clear();

                                if (joints != null && weights != null)
                                {
                                    for (int i = 0; i < joints.Count; i++)
                                    {
                                        var joint = joints[i];
                                        var weight = weights[i];

                                        // these are all the joints that influence this particular mesh
                                        if (weight.X > 0 && !influencingJoints.Contains(node.Skin.Joints[(int)joint.X].Name)) influencingJoints.Add(node.Skin.Joints[(int)joint.X].Name);
                                        if (weight.Y > 0 && !influencingJoints.Contains(node.Skin.Joints[(int)joint.Y].Name)) influencingJoints.Add(node.Skin.Joints[(int)joint.Y].Name);
                                        if (weight.Z > 0 && !influencingJoints.Contains(node.Skin.Joints[(int)joint.Z].Name)) influencingJoints.Add(node.Skin.Joints[(int)joint.Z].Name);
                                        if (weight.W > 0 && !influencingJoints.Contains(node.Skin.Joints[(int)joint.W].Name)) influencingJoints.Add(node.Skin.Joints[(int)joint.W].Name);
                                    }
                                }

                                // check if there are more than 40 influencing joints
                                if (influencingJoints.Count > 40)
                                {
                                    throw new InvalidDataException($"{node.Name} has more than 40 influencing bones, this will crash the game, so we cannot proceed. Please split the mesh into multiple parts so no single part influences more than 40 bones.");
                                }

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
                                        var joint = joints[(int)originalIndex];

                                        // use the index of the influencing bone name to set the bone indices
                                        newVert.bone0 = (ushort)influencingJoints.IndexOf(node.Skin.Joints[(int)joint.X].Name);
                                        newVert.bone1 = (ushort)influencingJoints.IndexOf(node.Skin.Joints[(int)joint.Y].Name);
                                        newVert.bone2 = (ushort)influencingJoints.IndexOf(node.Skin.Joints[(int)joint.Z].Name);
                                        newVert.bone3 = (ushort)influencingJoints.IndexOf(node.Skin.Joints[(int)joint.W].Name);
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
                                for (int i = 0; i < indices.Value.Count; i += 3)
                                {
                                    RndMesh.Face face = new RndMesh.Face();

                                    face.idx1 = originalIndexToNewIndex[indices.Value[i]];
                                    face.idx2 = originalIndexToNewIndex[indices.Value[i + 1]];
                                    face.idx3 = originalIndexToNewIndex[indices.Value[i + 2]];

                                    mesh.faces.Add(face);
                                }


                            }

                            // write bone transforms
                            var skin = node.Skin;
                            if (skin != null)
                            {
                                var joints = skin.Joints;
                                var boneTransList = new List<RndMesh.BoneTransform>();

                                for (int i = 0; i < influencingJoints.Count; i++)
                                {
                                    // linq to the rescue
                                    var jointNode = joints.FirstOrDefault(j => j.Name == influencingJoints[i]);
                                    if (jointNode.Name == "neutral_bone") continue;

                                    var miloBoneTransform = new RndMesh.BoneTransform
                                    {
                                        name = jointNode.Name ?? $"joint_{i}"
                                    };
                                    Matrix4x4.Invert(jointNode.WorldMatrix, out var boneWorldInverse);
                                    var relativeTransform = boneWorldInverse * node.WorldMatrix;
                                    MatrixHelpers.CopyMatrix(relativeTransform, miloBoneTransform.transform);
                                    boneTransList.Add(miloBoneTransform);
                                }
                                mesh.boneTransforms = boneTransList;
                            }



                            if (primitiveIndex == 0)
                            {
                                DirectoryMeta.Entry entry = new DirectoryMeta.Entry("Mesh", node.Name + ".mesh", mesh);
                                mesh.geomOwner = entry.name;
                                meta.entries.Add(entry);
                            }
                            else
                            {
                                DirectoryMeta.Entry entry = new DirectoryMeta.Entry("Mesh", node.Name + "_" + primitiveIndex + ".mesh", mesh);
                                mesh.geomOwner = entry.name;
                                meta.entries.Add(entry);
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
                    NodeProcessor.ProcessBoneNode(node, model, meta, type, filename);
                }
                else if (NodeHelpers.IsGroupNode(node, model))
                {
                    NodeProcessor.ProcessGroupNode(node, model, meta, selectedGame);
                }
                else if (NodeHelpers.IsLightNode(node, model))
                {
                    NodeProcessor.ProcessLightNode(node, meta, selectedGame);
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
            }


            // only create the all geom group if its a venue we are trying to produce
            if (opts.Type == "venue")
            {
                // create a new Group with all the geometry inside of it
                RndGroup allGeomGrp = RndGroup.New(GameRevisions.GetRevision(selectedGame).GroupRevision, 0);

                allGeomGrp.trans = RndTrans.New(GameRevisions.GetRevision(selectedGame).TransRevision, 0);
                allGeomGrp.draw = RndDrawable.New(GameRevisions.GetRevision(selectedGame).DrawableRevision, 0);
                allGeomGrp.draw.sphere = new Sphere();
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
            }

            // if they specified an OutfitConfig, create it
            OutfitConfigBuilder.BuildOutfitConfig(opts, selectedGame, meta);

            if (opts.Type == "character" || opts.Type == "instrument")
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
