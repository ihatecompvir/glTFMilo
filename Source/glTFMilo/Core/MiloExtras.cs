using MiloLib.Assets;
using MiloLib.Assets.Rnd;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
namespace MiloGLTFUtils.Source.glTFMilo.Core
{
    public class MiloExtras
    {
        [JsonPropertyName("milo_filename")]
        public string Filename { get; set; } = string.Empty;

        [JsonPropertyName("milo_obj_type")]
        public string ObjectType { get; set; } = string.Empty;
        [JsonPropertyName("milo_note")]
        public string Note { get; set; } = string.Empty;
        [JsonPropertyName("milo_is_showing")]
        public int IsShowing { get; set; }

        [JsonPropertyName("milo_draw_order")]
        public float DrawOrder { get; set; }

        [JsonPropertyName("milo_sphere_radius")]
        public float SphereRadius { get; set; }

        [JsonPropertyName("milo_sphere_center")]
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

    public class CharHairExtras
    {
        // default wind name, used by all venues and vignettes afaict
        public const string DefaultWind = "world.wind";

        [JsonPropertyName("milo_hair_stiffness")]
        public float Stiffness { get; set; } = 0.04f;

        [JsonPropertyName("milo_hair_torsion")]
        public float Torsion { get; set; } = 0.1f;

        [JsonPropertyName("milo_hair_inertia")]
        public float Inertia { get; set; } = 0.7f;

        [JsonPropertyName("milo_hair_gravity")]
        public float Gravity { get; set; } = 1.0f;

        [JsonPropertyName("milo_hair_friction")]
        public float Friction { get; set; } = 0.3f;

        [JsonPropertyName("milo_hair_weight")]
        public float Weight { get; set; } = 0.5f;

        [JsonPropertyName("milo_hair_wind")]
        public string Wind { get; set; } = DefaultWind;
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
        public string MaterialType { get; set; } = string.Empty;

        [JsonPropertyName("milo_shader_variation")]
        public int ShaderVariation { get; set; }

        [JsonPropertyName("milo_material_prelit")]
        public int Prelit { get; set; }
        [JsonPropertyName("milo_normal_detail_map")]
        public string NormalDetailMap { get; set; } = string.Empty;
    }
}
