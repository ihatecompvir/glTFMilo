using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace MiloGLTFUtils.Source.Shared
{
    public class MatrixHelpers
    {
        // stufd for dealing with conversion between milo and how gltf stores matrices

        private static readonly Matrix4x4 GltfToMiloBasis = new(
            1.0f, 0.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 1.0f, 0.0f,
            0.0f, -1.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);

        private static readonly Matrix4x4 MiloToGltfBasis = Matrix4x4.Invert(GltfToMiloBasis, out var inverseBasis)
            ? inverseBasis
            : Matrix4x4.Identity;

        public static Vector3 ConvertGltfVectorToMilo(Vector3 value)
        {
            return new Vector3(value.X, -value.Z, value.Y);
        }

        public static Quaternion ConvertGltfQuaternionToMilo(Quaternion value)
        {
            // the basis change is a pure rotation, so the quaternion's axis transforms like a vector and the angle is unchanged
            return new Quaternion(value.X, -value.Z, value.Y, value.W);
        }

        public static Vector3 ConvertGltfScaleToMilo(Vector3 value)
        {
            // per-axis scale just swaps the y and z axes, sign doesn't matter for scale
            return new Vector3(value.X, value.Z, value.Y);
        }

        /// <summary>
        /// Normalizes the given vector. If the vector is zero-length or contains non-finite values, returns the provided fallback vector instead.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="fallback"></param>
        /// <returns></returns>
        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            if (value.LengthSquared() <= float.Epsilon)
            {
                return fallback;
            }

            var normalized = Vector3.Normalize(value);
            if (!float.IsFinite(normalized.X) || !float.IsFinite(normalized.Y) || !float.IsFinite(normalized.Z))
            {
                // log a warning that we had to fallback on a row since this probably is an indication of some kind of issue
                Logger.Warn($"Encountered non-finite values when normalizing a matrix row. Falling back to default unit vector {fallback}.");
                return fallback;
            }

            return normalized;
        }

        /// <summary>
        /// Baasic matrix copier, copies one matrix to another. Does not perform any sort of checks on the data, it is just a raw copier.
        /// </summary>
        /// <param name="source">The source matrix to copy.</param>
        /// <param name="dest">The destination matrix.</param>
        /// <param name="convertGltfToMilo">Whether to convert from glTF's coordinate system to Milo's coordinate system. If true, the source matrix will be transformed accordingly before copying.</param>
        public static void CopyMatrix(Matrix4x4 source, MiloLib.Classes.Matrix dest, bool convertGltfToMilo = false)
        {
            if (convertGltfToMilo)
            {
                // change from glTF's coordinate system to milo's coordinate system
                source = MiloToGltfBasis * source * GltfToMiloBasis;
            }

            dest.m11 = source.M11;
            dest.m12 = source.M12;
            dest.m13 = source.M13;
            dest.m21 = source.M21;
            dest.m22 = source.M22;
            dest.m23 = source.M23;
            dest.m31 = source.M31;
            dest.m32 = source.M32;
            dest.m33 = source.M33;
            dest.m41 = source.M41;
            dest.m42 = source.M42;
            dest.m43 = source.M43;
        }

        /// <summary>
        /// Copy the upper-left 3x3 portion of the source matrix to the destination, normalizing each row. If a row is zero-length or contains non-finite values, it will be replaced with a default unit vector
        /// </summary>
        /// <param name="source">The source matrix to copy.</param>
        /// <param name="dest">The destination matrix.</param>
        /// <param name="convertGltfToMilo">Whether to convert from glTF's coordinate system to Milo's coordinate system. If true, the source matrix will be transformed accordingly before copying.</param>
        public static void CopyMatrix3(Matrix4x4 source, MiloLib.Classes.Matrix3 dest, bool convertGltfToMilo = false)
        {
            if (convertGltfToMilo)
            {
                // change from glTF's coordinate system to milo's coordinate system
                source = MiloToGltfBasis * source * GltfToMiloBasis;
            }

            Vector3 row1 = NormalizeOrFallback(new Vector3(source.M11, source.M12, source.M13), Vector3.UnitX);
            Vector3 row2 = NormalizeOrFallback(new Vector3(source.M21, source.M22, source.M23), Vector3.UnitY);
            Vector3 row3 = NormalizeOrFallback(new Vector3(source.M31, source.M32, source.M33), Vector3.UnitZ);

            dest.m11 = row1.X;
            dest.m12 = row1.Y;
            dest.m13 = row1.Z;
            dest.m21 = row2.X;
            dest.m22 = row2.Y;
            dest.m23 = row2.Z;
            dest.m31 = row3.X;
            dest.m32 = row3.Y;
            dest.m33 = row3.Z;
        }

    }
}
