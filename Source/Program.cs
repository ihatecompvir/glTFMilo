using CommandLine;

namespace glTFMilo.Source
{
    public class Program
    {
        static void Main(string[] args)
        {
            var res = Parser.Default.ParseArguments<Options>(args);

            // if res.Value.Input ends in a milo extension, we run Program
            // if it ends in a glb or gltf extension, we run Program
            if (res == null)
            {
                Console.WriteLine("Error parsing arguments");
                return;
            }

            if (res.Value.Input.EndsWith(".milo_xbox", StringComparison.OrdinalIgnoreCase))
            {
                MiloGLTFUtils.Source.MiloglTF.Program.Run(res.Value);
            }
            else if (res.Value.Input.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || res.Value.Input.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
            {
                MiloGLTFUtils.Source.glTFMilo.Program.Run(res.Value);
            }
            else
            {
                Console.WriteLine("Unsupported file type. Please provide a .milo, .glb, or .gltf file.");
            }
        }
    }
}
