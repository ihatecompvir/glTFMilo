using CommandLine;
using MiloGLTFUtils.Source;

namespace glTFMilo.Source
{
    public class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args).WithParsed((options) =>
            {
                // Sanity check
                if (options == null)
                {
                    Logger.Error("Unknown error from parsing arguments");
                    return;
                }

                // if res.Value.Input ends in a milo extension, we run Program
                // if it ends in a glb or gltf extension, we run Program
                if (options.Input.EndsWith(".milo_xbox", StringComparison.OrdinalIgnoreCase))
                {
                    MiloGLTFUtils.Source.MiloglTF.Program.Run(options);
                }
                else if (options.Input.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || options.Input.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
                {
                    MiloGLTFUtils.Source.glTFMilo.Program.Run(options);
                }
                else
                {
                    Logger.Error("Unsupported file type. Please provide a .milo, .glb, or .gltf file.");
                }
            });
        }
    }
}
