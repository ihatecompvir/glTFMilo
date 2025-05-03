using glTFMilo.Source;
using MiloLib;
using MiloLib.Assets;
using MiloLib.Assets.Char;
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
            string type = opts.Type.ToLower();
            bool ignoreLimits = opts.IgnoreTexSizeLimits;

            if (!File.Exists(filePath))
            {
                Console.WriteLine("File does not exist.");
                return;
            }

            if (!filePath.EndsWith(".gltf") && !filePath.EndsWith(".glb"))
            {
                Console.WriteLine("File is not a glTF file.");
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
                            HashSet<int> influencingJoints = new HashSet<int>();

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

                            if (node.Mesh != null)
                            {
                                if (primitive.Material != null)
                                {
                                    mesh.mat = primitive.Material.Name + ".mat";
                                }
                                IList<System.Numerics.Vector3> positions = null;
                                try { positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array(); }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Error reading POSITION: {e.Message}");
                                    Console.WriteLine("POSITION data will be improper.");
                                }

                                IList<System.Numerics.Vector3> normals = null;
                                try { normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array(); }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Error reading NORMAL: {e.Message}");
                                    Console.WriteLine("NORMAL data will be improper.");
                                }

                                IList<System.Numerics.Vector2> uvs = null;
                                try { uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array(); }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Error reading TEXCOORD_0: {e.Message}");
                                    Console.WriteLine("UV data will be improper.");
                                }

                                IList<System.Numerics.Vector4> tangents = null;
                                try { tangents = primitive.GetVertexAccessor("TANGENT")?.AsVector4Array(); }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Error reading TANGENT: {e.Message}");
                                    Console.WriteLine("TANGENT data will be improper.");
                                }

                                IList<System.Numerics.Vector4> weights = null;
                                try { weights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array(); }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Error reading WEIGHTS_0: {e.Message}");
                                    Console.WriteLine("WEIGHTS data will be improper.");
                                }

                                IList<System.Numerics.Vector4> joints = null;
                                try { joints = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array(); }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Error reading JOINTS_0: {e.Message}");
                                    Console.WriteLine("JOINTS data will be improper.");
                                }

                                IList<System.Numerics.Vector4> colors = null;
                                var colorsAccessor = primitive.GetVertexAccessor("COLOR_0");
                                if (colorsAccessor != null && colorsAccessor.Count > 0)
                                {
                                    try { colors = colorsAccessor.AsVector4Array(); }
                                    catch (Exception e)
                                    {
                                        Console.WriteLine($"Error reading COLOR_0: {e.Message}");
                                        Console.WriteLine("COLOR data will be improper.");
                                    }
                                }

                                IntegerArray? indices = null;
                                try { indices = primitive.IndexAccessor?.AsIndicesArray(); }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Error reading indices: {e.Message}");
                                    Console.WriteLine("Index data will be improper.");
                                }


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

                                    if (joints != null && originalIndex < joints.Count)
                                    {
                                        var joint = joints[(int)originalIndex];
                                        var weight = weights[(int)originalIndex];

                                        if (weight.X > 0) influencingJoints.Add((int)joint.X);
                                        if (weight.Y > 0) influencingJoints.Add((int)joint.Y);
                                        if (weight.Z > 0) influencingJoints.Add((int)joint.Z);
                                        if (weight.W > 0) influencingJoints.Add((int)joint.W);
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
                            if (opts.Type == "instrument")
                            {
                                var skin = node.Skin; // Skin associated with this mesh node
                                if (skin != null)
                                {
                                    var joints = skin.Joints;

                                    var boneTransList = new List<RndMesh.BoneTransform>();


                                    foreach (var jointIndex in influencingJoints)
                                    {
                                        var jointNode = skin.Joints[jointIndex];
                                        if (jointNode.Name == "neutral_bone") continue;

                                        var miloBoneTransform = new RndMesh.BoneTransform
                                        {
                                            name = jointNode.Name ?? $"joint_{jointIndex}"
                                        };
                                        Matrix4x4.Invert(jointNode.WorldMatrix, out var boneWorldInverse);
                                        var relativeTransform = boneWorldInverse * node.WorldMatrix;
                                        MatrixHelpers.CopyMatrix(relativeTransform, miloBoneTransform.transform);
                                        boneTransList.Add(miloBoneTransform);
                                    }
                                    mesh.boneTransforms = boneTransList;

                                }
                            }
                            else
                            {
                                var skin = node.Skin; // Skin associated with this mesh node
                                if (skin != null)
                                {
                                    var joints = skin.Joints;

                                    var boneTransList = new List<RndMesh.BoneTransform>();


                                    //foreach (var jointIndex in influencingJoints)
                                    for (int i = 0; i < joints.Count; i++)
                                    {
                                        var jointNode = joints[i];
                                        //var jointNode = skin.Joints[jointIndex];
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
                        Console.WriteLine($"{node.Name} has no mesh but is a mesh node. Can not convert glTF.");
                    }
                }
                else if (NodeHelpers.IsBone(node, model))
                {
                    if (node.Name == "neutral_bone") continue; // skip the neutral bone
                    if (type == "character")
                    {
                        // check if the bone name is in the list of rb3 skeleton bones, and ignore it if so
                        if (BoneNames.rb3SkeletonBones.Contains(node.Name))
                        {
                            continue;
                        }
                    }
                    RndTrans trans = RndTrans.New(9, 0);
                    trans.objFields.revision = 2;
                    string parentNodeName = NodeHelpers.GetParentBoneName(node, model);

                    MatrixHelpers.CopyMatrix(node.LocalMatrix, trans.localXfm);
                    MatrixHelpers.CopyMatrix(node.WorldMatrix, trans.worldXfm);

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
                else if (NodeHelpers.IsGroupNode(node, model))
                {
                    RndGroup grp = RndGroup.New(GameRevisions.GetRevision(selectedGame).GroupRevision, 0);
                    grp.trans = RndTrans.New(GameRevisions.GetRevision(selectedGame).TransRevision, 0);
                    grp.draw = RndDrawable.New(GameRevisions.GetRevision(selectedGame).DrawableRevision, 0);
                    grp.objFields.revision = 2;
                    grp.anim = RndAnimatable.New(GameRevisions.GetRevision(selectedGame).AnimatableRevision, 0);
                    List<string> children = NodeHelpers.GetAllDescendantNames(node);
                    if (children.Count > 0)
                    {
                        foreach (var child in children)
                        {
                            // add all children to the grp if its not null
                            if (child != null)
                                grp.objects.Add(child);
                        }
                    }
                    DirectoryMeta.Entry entry = new DirectoryMeta.Entry("Group", node.Name + ".grp", grp);
                    meta.entries.Add(entry);
                }
                else if (NodeHelpers.IsLightNode(node, model))
                {
                    Console.WriteLine("Node is Light Node, create RndLight");
                    RndLight light = new RndLight();

                    // todo: remove this reflection stuff eventually
                    typeof(RndLight)
                        .GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                        .SetValue(light, GameRevisions.GetRevision(selectedGame).LightRevision);
                    light.objFields.revision = 2;
                    light.range = node.PunctualLight.Range;
                    light.colorOwner = node.Name + ".lit";
                    light.color = new HmxColor4(node.PunctualLight.Color.X, node.PunctualLight.Color.Y, node.PunctualLight.Color.Z, 1.0f);
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

                    MatrixHelpers.CopyMatrix(node.LocalMatrix, light.trans.localXfm);
                    MatrixHelpers.CopyMatrix(node.WorldMatrix, light.trans.worldXfm);

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
                /*
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
                */


                // specular color map
                var specularColorMapTexture = material.FindChannel("SpecularColor")?.Texture;
                var specularColor = material.FindChannel("SpecularColor");

                /*
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
                */

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


            if (opts.Type == "character" || opts.Type == "instrument")
            {
                Character character = Character.New(GameRevisions.GetRevision(selectedGame).CharacterRevision, 0);
                character.viewports = new();

                if (opts.Type == "character")
                {
                    if (selectedGame == MiloGame.RockBand3)
                    {
                        // include an absolute reference to char_shared which will link the skeleton bones to this milo
                        character.subDirs.Add("../../shared/char_shared.milo");
                    }
                }

                if (opts.Type == "instrument")
                {
                    character.objFields.type = "outfit_variation";
                    character.inlineProxy = true;
                }

                // default empty viewports, still not sure what viewports even are
                character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
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
                    .SetValue(character, GameRevisions.GetRevision(selectedGame).RndDirRevision);

                typeof(ObjectDir)
                    .GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(character, GameRevisions.GetRevision(selectedGame).ObjectDirRevision);

                // todo: restore this but use the git commit hash
                //character.objFields.note = "Milo created with Program";



                meta.directory = character;


                MiloFile miloFile = new MiloFile(meta);

                miloFile.Save(opts.Output, MiloFile.Type.Uncompressed, 0x810, MiloLib.Utils.Endian.LittleEndian, MiloLib.Utils.Endian.BigEndian);
            }
            else
            {
                RndDir rndDir = new RndDir(0, 0);
                rndDir.viewports = new();

                // default empty viewports, still not sure what viewports even are
                rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
                rndDir.currentViewportIdx = 6;
                rndDir.objFields.revision = 2;
                rndDir.anim = RndAnimatable.New(GameRevisions.GetRevision(selectedGame).AnimatableRevision, 0);
                rndDir.draw = RndDrawable.New(GameRevisions.GetRevision(selectedGame).DrawableRevision, 0);
                rndDir.draw.sphere.radius = 10000.0f;
                rndDir.trans = RndTrans.New(GameRevisions.GetRevision(selectedGame).TransRevision, 0);


                typeof(ObjectDir)
                    .GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(rndDir, GameRevisions.GetRevision(selectedGame).ObjectDirRevision);

                typeof(RndDir)
                    .GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(rndDir, GameRevisions.GetRevision(selectedGame).RndDirRevision);

                meta.directory = rndDir;


                MiloFile miloFile = new MiloFile(meta);

                miloFile.Save(opts.Output, MiloFile.Type.Uncompressed, 0x810, MiloLib.Utils.Endian.LittleEndian, MiloLib.Utils.Endian.BigEndian);
            }

            Console.WriteLine("Milo scene created at " + opts.Output);
        }
    }
}
