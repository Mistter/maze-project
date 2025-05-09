using OpenTK.Graphics.OpenGL4;
using ExtTextureFilterAnisotropic = OpenTK.Graphics.OpenGL.ExtTextureFilterAnisotropic;

namespace MazeEngine.Graphics
{
    internal class Texture
    {
        private readonly int _id;

        public readonly int Width;
        public readonly int Height;

        public Texture(string filename)
        {
            // Carrega o bitmap a partir de um arquivo
            var data = new TextureData(filename);

            Width = data.Width;
            Height = data.Height;

            // Gera o ID da textura
            _id = GL.GenTexture();

            // Faz bind dela na TextureUnit desejada (Texture0 por padrão)
            Bind(TextureUnit.Texture0);

            // Cria a textura 2D no OpenGL
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba,           // Armazenamento interno
                Width,
                Height,
                0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, // Formato do bitmap em RAM
                PixelType.UnsignedByte,
                data.DataPtr
            );

            // Define filtros
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, 16);

            // Se quiser gerar mipmaps, use GL.GenerateMipmap:
            // GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

            // Desbloqueia os pixels e descarta o bitmap
            data.Dispose();
        }

        // Faz bind desta textura na TextureUnit passada
        public void Bind(TextureUnit unit)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, _id);
        }
    }
}