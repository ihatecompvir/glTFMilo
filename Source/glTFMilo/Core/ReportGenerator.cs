using MiloGLTFUtils.Source.Shared;
using MiloLib;
using MiloLib.Assets;
using MiloLib.Assets.Char;
using MiloLib.Assets.Rnd;

namespace MiloGLTFUtils.Source.glTFMilo.Core
{
    public static class ReportGenerator
    {
        public static void Generate(DirectoryMeta meta, MiloGame selectedGame, string type)
        {
            int meshCount = meta.entries.Count(e => e.type == "Mesh");
            int texCount = meta.entries.Count(e => e.type == "Tex");
            int matCount = meta.entries.Count(e => e.type == "Mat");
            int groupCount = meta.entries.Count(e => e.type == "Group");

            Logger.Info("===== glTFMilo Report =====");
            Logger.Info($"Game: {selectedGame}");
            Logger.Info($"Type: {type}");
            Logger.Info("===== Counts =====");
            Logger.Info($"Meshes created: {meshCount}");
            Logger.Info($"Textures created: {texCount}");
            Logger.Info($"Materials created: {matCount}");
            Logger.Info($"Groups created: {groupCount}");
            Logger.Info($"OutfitConfig created: {meta.entries.Count(e => e.type == "OutfitConfig")}");
            Logger.Info($"Lights created: {meta.entries.Count(e => e.type == "Light")}");

            Logger.Info("===== Object Info =====");

            foreach (var matEntry in meta.entries.Where(e => e.type == "Mat"))
            {
                var mat = (RndMat)matEntry.obj;
                Logger.Info($"Material: {matEntry.name}");
                Logger.Info($"  Has Diffuse Map: {!string.IsNullOrEmpty(mat.diffuseTex)}" +
                            (string.IsNullOrEmpty(mat.diffuseTex) ? "" : $" ({mat.diffuseTex})"));
                Logger.Info($"  Has Normal Map: {!string.IsNullOrEmpty(mat.normalMap)}" +
                            (string.IsNullOrEmpty(mat.normalMap) ? "" : $" ({mat.normalMap})"));
                Logger.Info($"  Has Specular Map: {!string.IsNullOrEmpty(mat.specularMap)}" +
                            (string.IsNullOrEmpty(mat.specularMap) ? "" : $" ({mat.specularMap})"));
                Logger.Info($"  Has Emissive Map: {!string.IsNullOrEmpty(mat.emissiveMap)}" +
                            (string.IsNullOrEmpty(mat.emissiveMap) ? "" : $" ({mat.emissiveMap})"));
            }

            foreach (var texEntry in meta.entries.Where(e => e.type == "Tex"))
            {
                var tex = (RndTex)texEntry.obj;
                Logger.Info($"Texture: {texEntry.name}");
                Logger.Info($"  Format: {tex.bitmap.encoding}");
                Logger.Info($"  Width: {tex.width}, Height: {tex.height}");
            }

            foreach (var meshEntry in meta.entries.Where(e => e.type == "Mesh"))
            {
                var mesh = (RndMesh)meshEntry.obj;
                Logger.Info($"Mesh: {meshEntry.name}");
                Logger.Info($"  Is Rigged: {mesh.boneTransforms.Count > 0}");

                if (mesh.boneTransforms is { Count: > 0 })
                {
                    Logger.Info("  Influencing Bones:");
                    foreach (var bone in mesh.boneTransforms)
                        Logger.Info($"    {bone.name}");
                }
            }

            foreach (var cfg in meta.entries.Where(e => e.type == "OutfitConfig"))
            {
                Logger.Info($"OutfitConfig: {cfg.name}");
                var config = (OutfitConfig)cfg.obj;
                if (config.matSwaps != null)
                    Logger.Info($"  Material Swaps: {config.matSwaps.Count}");
                if (config.overlays != null)
                    Logger.Info($"  Overlays: {config.overlays.Count}");
            }

            Logger.Info("===================================");
        }
    }
}
