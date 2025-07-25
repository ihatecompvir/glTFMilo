using TeximpNet.Compression;
using TeximpNet;
using MiloGLTFUtils.Source;

namespace MiloGLTFUtils.Source.glTFMilo.Core
{
    public class TextureUtils
    {
        public static bool ConvertToDDS(Stream inputStream, string outputPath, CompressionFormat format, bool ignoreLimits = false)
        {
            if (inputStream.CanSeek)
                inputStream.Position = 0;

            Surface image = Surface.LoadFromStream(inputStream);
            if (image == null)
                throw new InvalidOperationException("Failed to load input image from stream. Ensure the stream contains a valid image format supported by TeximpNet.");

            if (image.Width % 4 != 0 || image.Height % 4 != 0)
                throw new InvalidOperationException($"Invalid image dimensions for BC1 compression. Width and height must be multiples of 4. Current: {image.Width}x{image.Height}. Please resize your image accordingly.");

            // check if either dimension is larger than 2048 x 2048 (which seems to be the limit to textures in Milo)
            if (image.Width > 512 || image.Height > 512)
            {
                // if ignoreLimits is true, we can ignore this and just allow the texture to be larger
                if (!ignoreLimits)
                {
                    float scale = Math.Min(512f / image.Width, 512f / image.Height);
                    int newWidth = (int)(image.Width * scale);
                    int newHeight = (int)(image.Height * scale);
                    bool succeeded = image.Resize(newWidth, newHeight, ImageFilter.Lanczos3);
                    if (!succeeded)
                    {
                        throw new InvalidOperationException($"Image exceeded size limits (512x512) and failed to auto-resize to {newWidth}x{newHeight}. Try manually resizing the image or check for invalid image data.");
                    }
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
                Logger.Error("Invalid DDS file.");
                return (0, 0, 0, 0, new byte[0]);
            }

            using (var ms = new MemoryStream(fileBytes))
            using (var br = new BinaryReader(ms))
            {
                // Check magic number "DDS " to see if we are really dealing with a dds
                if (br.ReadUInt32() != 0x20534444)
                    throw new InvalidOperationException("File does not start with DDS magic number. Make sure the file is actually a valid DDS file.");

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
                    _ => throw new NotSupportedException($"Unsupported DDS compression format (FourCC: 0x{fourCC:X}). Supported formats: DXT1, DXT3, DXT5, ATI2.")
                };

                byte[] pixelData = new byte[fileBytes.Length - 128];
                Array.Copy(fileBytes, 128, pixelData, 0, pixelData.Length);

                return (width, height, bpp, mipMapCount, pixelData);
            }
        }
    }
}
