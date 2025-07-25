using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MiloGLTFUtils.Source.Shared
{
    public class OutfitConfigJSON
    {
        public bool ComputeAO { get; set; }
        public List<MatSwap> MatSwaps { get; set; } = new();
        public string TexBlender { get; set; }
        public string WrinkleBlender { get; set; }
        public string BandLogo { get; set; }
        public List<Overlay> Overlays { get; set; } = new();
        public class RGBAColor
        {
            public float R { get; set; }
            public float G { get; set; }
            public float B { get; set; }
            public float A { get; set; }
        }

        public class MatSwap
        {
            public string Mat { get; set; }
            public string ResourceMat { get; set; }
            public string TwoColorDiffuseTex { get; set; }
            public string TwoColorInterpTex { get; set; }
            public string TwoColorMaskTex { get; set; }
            public List<RGBAColor> Color1Palette { get; set; } = new();
            public List<RGBAColor> Color2Palette { get; set; } = new();
            public List<string> Textures { get; set; } = new();
        }

        public class Overlay
        {
            public int Category { get; set; }
            public string Texture { get; set; }
        }
    }
}
