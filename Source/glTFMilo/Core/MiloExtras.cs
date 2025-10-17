using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MiloLib.Assets;
using MiloLib.Assets.Rnd;
using SharpGLTF.Schema2;
namespace MiloGLTFUtils.Source.glTFMilo.Core
{
    public class MiloExtras
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        [JsonPropertyName("obj_type")]
        public string ObjectType { get; set; }

        [JsonPropertyName("note")]
        public string Note { get; set; }
        [JsonPropertyName("is_showing")]
        public int IsShowing { get; set; }

        [JsonPropertyName("draw_order")]
        public float DrawOrder { get; set; }

        [JsonPropertyName("custom_sphere_radius")]
        public float SphereRadius { get; set; }

        [JsonPropertyName("custom_sphere_center")]
        public float[] SphereCenter { get; set; } = new float[3];

        public static void AddToObject(Node node, MiloLib.Assets.Object obj, ref string fileName)
        {
            if (node.Extras != null)
            {
                string extrasJson = node.Extras.ToString();
                var miloExtras = JsonSerializer.Deserialize<MiloExtras>(extrasJson);
                if (miloExtras == null)
                {
                    Logger.Warn($"Node {node.Name} has extras but they could not be deserialized.");
                }
                else
                {
                    // Object
                    if (miloExtras.Filename != null && miloExtras.Filename != string.Empty)
                    {
                        fileName = miloExtras.Filename;
                    }
                    if (miloExtras.ObjectType != null && miloExtras.ObjectType != string.Empty)
                        obj.objFields.type = miloExtras.ObjectType;
                    if (miloExtras.Note != null && miloExtras.Note != string.Empty)
                        obj.objFields.note = miloExtras.Note;
                }
            }
        }

        public static void AddToMesh(Node node, RndMesh mesh, ref string fileName)
        {
            if (node.Extras != null)
            {
                string extrasJson = node.Extras.ToString();
                var miloExtras = JsonSerializer.Deserialize<MiloExtras>(extrasJson);

                if (miloExtras == null)
                {
                    Logger.Warn($"Node {node.Name} has extras but they could not be deserialized.");
                }
                else
                {

                    // Object
                    if (miloExtras.Filename != null && miloExtras.Filename != string.Empty)
                    {
                        fileName = miloExtras.Filename;
                    }

                    if (miloExtras.ObjectType != null && miloExtras.ObjectType != string.Empty)
                        mesh.objFields.type = miloExtras.ObjectType;

                    if (miloExtras.Note != null && miloExtras.Note != string.Empty)
                        mesh.objFields.note = miloExtras.Note;

                    // drawable
                    mesh.draw.showing = miloExtras?.IsShowing == 1;
                    mesh.draw.drawOrder = miloExtras?.DrawOrder ?? 0;
                    mesh.draw.sphere.radius = miloExtras?.SphereRadius ?? 10000.0f;
                    mesh.draw.sphere.x = miloExtras?.SphereCenter[0] ?? 0.0f;
                    mesh.draw.sphere.y = miloExtras?.SphereCenter[1] ?? 0.0f;
                    mesh.draw.sphere.z = miloExtras?.SphereCenter[2] ?? 0.0f;
                }
            }
        }

        public static void AddToGroup(Node node, RndGroup group, ref string fileName)
        {
            if (node.Extras != null)
            {
                string extrasJson = node.Extras.ToString();
                var miloExtras = JsonSerializer.Deserialize<MiloExtras>(extrasJson);
                if (miloExtras == null)
                {
                    Logger.Warn($"Node {node.Name} has extras but they could not be deserialized.");
                }
                else
                {
                    // Object
                    if (miloExtras.Filename != null && miloExtras.Filename != string.Empty)
                    {
                        fileName = miloExtras.Filename;
                    }
                    if (miloExtras.ObjectType != null && miloExtras.ObjectType != string.Empty)
                        group.objFields.type = miloExtras.ObjectType;
                    if (miloExtras.Note != null && miloExtras.Note != string.Empty)
                        group.objFields.note = miloExtras.Note;
                    // drawable
                    group.draw.showing = miloExtras?.IsShowing == 1;
                    group.draw.drawOrder = miloExtras?.DrawOrder ?? 0;
                    group.draw.sphere.radius = miloExtras?.SphereRadius ?? 10000.0f;
                    group.draw.sphere.x = miloExtras?.SphereCenter[0] ?? 0.0f;
                    group.draw.sphere.y = miloExtras?.SphereCenter[1] ?? 0.0f;
                    group.draw.sphere.z = miloExtras?.SphereCenter[2] ?? 0.0f;
                }
            }
        }
    }

    public class MaterialExtras
    {
        [JsonPropertyName("milo_z_mode")]
        public int ZMode { get; set; }
        [JsonPropertyName("milo_blend_mode")]
        public int BlendMode { get; set; }

        [JsonPropertyName("milo_alpha_cut")]
        public int AlphaCut { get; set; }

        [JsonPropertyName("milo_alpha_threshold")]
        public float AlphaThreshold { get; set; }
        [JsonPropertyName("milo_alpha_write")]
        public int AlphaWrite { get; set; }
        [JsonPropertyName("milo_use_environ")]
        public int UseEnvironment { get; set; }
        [JsonPropertyName("milo_emissive_multiplier")]
        public float EmissiveMultiplier { get; set; }
        [JsonPropertyName("milo_cull")]
        public int Cull { get; set; }
        [JsonPropertyName("milo_point_lights")]
        public int PointLights { get; set; }
        [JsonPropertyName("milo_proj_lights")]
        public int ProjectedLights { get; set; }

        [JsonPropertyName("milo_material_type")]
        public string MaterialType { get; set; }

        [JsonPropertyName("milo_shader_variation")]
        public int ShaderVariation { get; set; }

        [JsonPropertyName("milo_material_prelit")]
        public int Prelit { get; set; }
        [JsonPropertyName("milo_normal_detail_map")]
        public string NormalDetailMap { get; set; }
    }
}