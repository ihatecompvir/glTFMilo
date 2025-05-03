using glTFMilo.Source;
using MiloLib;
using MiloLib.Assets;
using MiloLib.Assets.Rnd;
using SharpGLTF;
using SharpGLTF.Schema2;
using System.Numerics;

namespace MiloGLTFUtils.Source.MiloglTF
{
    public class Program
    {
        public static void Run(Options opts)
        {
            string inputPath = opts.Input;
            string outputPath = opts.Output;

            // check if the file exists
            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"File {inputPath} does not exist.");
                return;
            }

            // check if the input has a valid extension (.milo_wii, .milo_xbox, .milo_ps3, and .milo are supported)
            if (!inputPath.EndsWith(".milo_wii", StringComparison.OrdinalIgnoreCase) &&
                !inputPath.EndsWith(".milo_xbox", StringComparison.OrdinalIgnoreCase) &&
                !inputPath.EndsWith(".milo_ps3", StringComparison.OrdinalIgnoreCase) &&
                !inputPath.EndsWith(".milo", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"File {inputPath} does not have a valid extension. Supported extensions are .milo_wii, .milo_xbox, .milo_ps3, and .milo.");
                return;
            }

            // check that the output extension is either glb or gltf
            if (!outputPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) && !outputPath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Output file {outputPath} does not have a valid extension. Supported extensions are .glb and .gltf.");
                return;
            }

            // open the milo scene
            MiloFile milo = new MiloFile(inputPath);

            // create a new model root in which to put everything
            var gltf = ModelRoot.CreateModel();

            // recursively process directory
            ProcessDirectory(milo.dirMeta, gltf);

            // save the glTF file
            if (outputPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            {
                gltf.SaveGLB(outputPath);
            }
            else
            {
                gltf.SaveGLTF(outputPath);
            }
        }

        private static void ProcessDirectory(DirectoryMeta dirMeta, ModelRoot gltf)
        {
            foreach (var asset in dirMeta.entries)
            {
                if (asset.type == "Light" && asset.obj is RndLight light)
                {
                    PunctualLight pLight = light.type switch
                    {
                        RndLight.Type.kDirectional => gltf.CreatePunctualLight(asset.name, PunctualLightType.Directional),
                        RndLight.Type.kPoint => gltf.CreatePunctualLight(asset.name, PunctualLightType.Point),
                        RndLight.Type.kSpot => gltf.CreatePunctualLight(asset.name, PunctualLightType.Spot),
                        _ => null
                    };

                    if (pLight != null)
                    {
                        pLight.Color = new System.Numerics.Vector3(light.color.r, light.color.g, light.color.b);
                        pLight.Intensity = light.range;
                    }


                }

                if (asset.dir is DirectoryMeta subDir)
                {
                    ProcessDirectory(subDir, gltf);
                }
            }

            if (dirMeta.directory is ObjectDir objDir)
            {
                foreach (var inline in objDir.inlineSubDirs)
                {
                    ProcessDirectory(inline, gltf);
                }
            }
        }
    }
}
