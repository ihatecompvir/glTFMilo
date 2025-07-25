using glTFMilo.Source;
using MiloGLTFUtils.Source.Shared;
using MiloLib.Assets;
using MiloLib.Assets.Char;
using MiloLib.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MiloGLTFUtils.Source.glTFMilo.Core
{
    public static class OutfitConfigBuilder
    {
        public static void BuildOutfitConfig(Options opts, MiloGame selectedGame, DirectoryMeta meta)
        {
            if (opts.Type != "character" || string.IsNullOrEmpty(opts.OutfitConfig))
                return;

            if (!File.Exists(opts.OutfitConfig))
            {
                Logger.Error($"Specified OutfitConfig file {opts.OutfitConfig} does not exist.");
                return;
            }

            var json = File.ReadAllText(opts.OutfitConfig);
            var configJson = System.Text.Json.JsonSerializer.Deserialize<OutfitConfigJSON>(json);
            if (configJson == null)
                return;

            OutfitConfig config = new OutfitConfig();
            typeof(OutfitConfig)
                .GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(config, GameRevisions.GetRevision(selectedGame).OutfitConfigRevision);

            config.objFields.revision = 2;
            config.computeAO = configJson.ComputeAO;

            AddMatSwaps(configJson, config, meta);
            AddOverlays(configJson, config);
            config.texBlender = configJson.TexBlender;
            config.wrinkleBlender = configJson.WrinkleBlender;
            config.bandLogo = configJson.BandLogo;

            meta.entries.Add(new DirectoryMeta.Entry("OutfitConfig", "outfig_config.cfg", config));
        }

        private static void AddMatSwaps(OutfitConfigJSON configJson, OutfitConfig config, DirectoryMeta meta)
        {
            if (configJson.MatSwaps == null || configJson.MatSwaps.Count == 0)
                return;

            config.matSwaps = new List<OutfitConfig.MatSwap>();

            foreach (var swap in configJson.MatSwaps)
            {
                var matSwap = new OutfitConfig.MatSwap
                {
                    resourceMat = swap.ResourceMat,
                    mat = swap.Mat,
                    twoColorDiffuse = swap.TwoColorDiffuseTex,
                    twoColorMask = swap.TwoColorMaskTex,
                    twoColorInterp = swap.TwoColorInterpTex,
                };

                // handle textures
                foreach (var tex in swap.Textures)
                {
                    if (!string.IsNullOrEmpty(tex))
                    {
                        matSwap.textures.Add(tex);
                    }
                }

                matSwap.color1Palette = BuildPalette(swap.Color1Palette, "outfit_config_pal1.pal", meta);
                matSwap.color2Palette = BuildPalette(swap.Color2Palette, "outfit_config_pal2.pal", meta);

                config.matSwaps.Add(matSwap);
            }
        }

        private static string? BuildPalette(List<OutfitConfigJSON.RGBAColor>? colors, string name, DirectoryMeta meta)
        {
            if (colors == null || colors.Count == 0)
                return null;

            ColorPalette pal = new ColorPalette();
            typeof(ColorPalette)
                .GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(pal, (ushort)1);
            pal.objFields.revision = 2;
            pal.colors = colors.Select(c => new HmxColor4(c.R, c.G, c.B, c.A)).ToList();

            meta.entries.Add(new DirectoryMeta.Entry("ColorPalette", name, pal));
            return name;
        }

        private static void AddOverlays(OutfitConfigJSON configJson, OutfitConfig config)
        {
            if (configJson.Overlays == null || configJson.Overlays.Count == 0)
                return;

            config.overlays = configJson.Overlays
                .Select(o => new OutfitConfig.Overlay
                {
                    category = o.Category,
                    texture = o.Texture
                }).ToList();
        }
    }
}
