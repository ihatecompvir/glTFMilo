using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace glTFMilo.Source
{
    public class Options
    {
        [Option("input_path", Required = true, HelpText = "Input glTF/glb file path.")]
        public string Input { get; set; }

        [Option("output_path", Required = true, HelpText = "Output Milo file path.")]
        public string Output { get; set; }

        [Option("platform", Required = true, HelpText = "Target platform (xbox/ps3). Default is Xbox.")]
        public string Platform { get; set; }

        [Option('g', "game", Required = false, HelpText = "Game (rb3, rb2, tbrb). Default is rb3.")]
        public string Game { get; set; } = "rb3";

        [Option("prelit", Required = false, HelpText = "Whether or not Materials should be pre-lit.")]
        public string Prelit { get; set; } = string.Empty;

        [Option("ignore_tex_size_limits", Required = false, HelpText = "Allows arbitrary texture sizes rather than resizing to the max of 512 x 512. Larger textures can sometimes work, but your mileage may greatly vary by going beyond the safe maximum.")]
        public bool IgnoreTexSizeLimits { get; set; } = false;

        [Option("type", Required = false, HelpText = "What the intention of the output Milo is. Supported values are character, venue, or other. Default is other.")]
        public string Type { get; set; } = "other";
    }
}
