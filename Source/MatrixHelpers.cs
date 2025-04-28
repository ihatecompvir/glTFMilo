using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace glTFMilo.Source
{
    public class MatrixHelpers
    {
        public static void CopyMatrix(Matrix4x4 source, MiloLib.Classes.Matrix dest)
        {
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
    }
}
