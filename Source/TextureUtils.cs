using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeximpNet.Compression;
using TeximpNet;

namespace glTFMilo.Source
{
    public class TextureUtils
    {
        // TODO: put these into their own class or something instead of randomyly at the top of this
        public static bool ConvertToDDS(Stream inputStream, string outputPath, CompressionFormat format)
        {
            if (inputStream.CanSeek)
                inputStream.Position = 0;

            Surface image = Surface.LoadFromStream(inputStream);
            if (image == null)
                throw new InvalidOperationException("Failed to load input image from stream.");

            if (image.Width % 4 != 0 || image.Height % 4 != 0)
                throw new InvalidOperationException($"BC1 compression requires image dimensions to be multiples of 4. Current dimensions: {image.Width}x{image.Height}");

            // check if either dimension is larger than 2048 x 2048 (which seems to be the limit to textures in Milo)
            if (image.Width > 2048 || image.Height > 2048)
            {
                float scale = Math.Min(2048f / image.Width, 2048f / image.Height);
                int newWidth = (int)(image.Width * scale);
                int newHeight = (int)(image.Height * scale);
                bool succeeded = image.Resize(newWidth, newHeight, ImageFilter.Lanczos3);
                if (!succeeded)
                {
                    throw new InvalidOperationException($"Failed to resize image to {newWidth}x{newHeight}.");
                }
            }

            image.FlipVertically();

            using (Compressor compressor = new Compressor())
            {
                compressor.Input.GenerateMipmaps = false;
                compressor.Input.SetData(image);
                compressor.Compression.Format = format;


                using (var memoryStream = new MemoryStream())
                {
                    bool success = compressor.Process(memoryStream);
                    memoryStream.Position = 0;
                    File.WriteAllBytes(outputPath, memoryStream.ToArray());
                    if (!success)
                    {
                        throw new InvalidOperationException("DDS compression failed.");
                    }
                }

                return true;
            }
        }

        // crappy way to parse a DDS file
        // TODO: create a proper class
        public static (int width, int height, int bpp, int mipMapCount, byte[] pixelData) ParseDDS(string ddsFilePath)
        {
            byte[] fileBytes = File.ReadAllBytes(ddsFilePath);
            if (fileBytes.Length <= 128)
            {
                Console.WriteLine("Invalid DDS file.");
                return (0, 0, 0, 0, new byte[0]);
            }

            using (var ms = new MemoryStream(fileBytes))
            using (var br = new BinaryReader(ms))
            {
                // Check magic number "DDS " to see if we are really dealing with a dds
                if (br.ReadUInt32() != 0x20534444)
                    throw new InvalidOperationException("Not a valid DDS file.");

                br.BaseStream.Seek(8, SeekOrigin.Current);
                int height = br.ReadInt32();
                int width = br.ReadInt32();
                br.BaseStream.Seek(8, SeekOrigin.Current);
                int mipMapCount = br.ReadInt32();
                br.BaseStream.Seek(44, SeekOrigin.Current);
                br.BaseStream.Seek(4, SeekOrigin.Current);
                uint pfFlags = br.ReadUInt32();
                uint fourCC = br.ReadUInt32();

                int bpp = fourCC switch
                {
                    0x31545844 => 4, // 'DXT1' = BC1 = 4 bpp
                    0x33545844 => 8, // 'DXT3' = BC2 = 8 bpp
                    0x35545844 => 8, // 'DXT5' = BC3 = 8 bpp
                    0x32495441 => 8, // 'ATI2' = BC5 = 8 bpp
                    _ => throw new NotSupportedException($"Unsupported format FourCC: 0x{fourCC:X}")
                };

                byte[] pixelData = new byte[fileBytes.Length - 128];
                Array.Copy(fileBytes, 128, pixelData, 0, pixelData.Length);

                return (width, height, bpp, mipMapCount, pixelData);
            }
        }
    }
}
