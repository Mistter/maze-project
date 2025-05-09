using OpenTK.Graphics.OpenGL4;
using ExtTextureFilterAnisotropic = OpenTK.Graphics.OpenGL.ExtTextureFilterAnisotropic;

namespace MazeEngine.Graphics
{
    internal class TextureArray
    {
        public readonly int Width;
        public readonly int Height;

        private readonly int _id;

        public TextureArray(int width, int height, int count)
        {
            Width = width;
            Height = height;

            // Gera o ID para a textura
            _id = GL.GenTexture();
            Bind(TextureUnit.Texture0);

            // Aloca o armazenamento para a textura array
            GL.TextureStorage3D(_id, 1, SizedInternalFormat.Rgba8, width, height, count);

            // Configura os parâmetros de filtro
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            // Configura anisotropia se suportado
            float maxAniso = 0;
            GL.GetFloat((GetPName)ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt, out maxAniso);
            GL.TexParameter(TextureTarget.Texture2DArray, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, maxAniso);
        }

        public void SetTexture(int index, TextureData data)
        {
            // Garante que a textura está vinculada antes de modificar
            Bind(TextureUnit.Texture0);

            // Define a subimagem na camada especificada
            GL.TextureSubImage3D(
                _id,
                0, // Nível do mipmap
                0, 0, index, // Offset (x, y) e índice do array (z)
                data.Width, data.Height, 1, // Dimensões
                PixelFormat.Bgra, // Formato dos dados
                PixelType.UnsignedByte, // Tipo de pixel
                data.DataPtr // Ponteiro para os dados
            );

            // Libera os dados após o upload
            data.Dispose();
        }

        public void GenerateMipmaps()
        {
            // Gera mipmaps para o array de texturas
            Bind(TextureUnit.Texture0);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2DArray);
        }

        public void Bind(TextureUnit unit)
        {
            // Vincula o array de texturas à unidade de textura ativa
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2DArray, _id);
        }

        ~TextureArray()
        {
            // Libera os recursos do OpenGL ao destruir a classe
            if (GL.IsTexture(_id))
            {
                GL.DeleteTexture(_id);
            }
        }
    }
}