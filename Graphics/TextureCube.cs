// MazeEngine/Graphics/TextureCube.cs
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OpenTK.Graphics.OpenGL4;

namespace MazeEngine.Graphics
{
    public class TextureCube : IDisposable
    {
        public readonly int Handle;

        /// <summary>
        /// paths[0]=+X(right), [1]=-X(left),
        /// paths[2]=+Y(top),   [3]=-Y(bottom),
        /// paths[4]=+Z(front), [5]=-Z(back)
        /// </summary>
        public TextureCube(string[] paths)
        {
            if (paths.Length != 6)
                throw new ArgumentException("Cube map precisa de 6 faces.");

            Handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.TextureCubeMap, Handle);

            for (int i = 0; i < 6; i++)
            {
                Bitmap bmp;
                if (File.Exists(paths[i]))
                {
                    bmp = new Bitmap(paths[i]);
                }
                else
                {
                    Console.WriteLine($"Aviso: cubemap face não encontrada em {paths[i]}");
                    // Fallback: bitmap 1×1 magenta
                    bmp = new Bitmap(1, 1);
                    bmp.SetPixel(0, 0, Color.Magenta);
                }

                var data = bmp.LockBits(
                    new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb
                );

                GL.TexImage2D(
                    TextureTarget.TextureCubeMapPositiveX + i,
                    0,
                    PixelInternalFormat.Rgba,
                    bmp.Width,
                    bmp.Height,
                    0,
                    OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
                    PixelType.UnsignedByte,
                    data.Scan0
                );

                bmp.UnlockBits(data);
                bmp.Dispose();
            }

            // Parâmetros de filtragem e wrap
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

            GL.BindTexture(TextureTarget.TextureCubeMap, 0);
        }

        public void Bind(TextureUnit unit)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.TextureCubeMap, Handle);
        }

        public void Dispose()
        {
            GL.DeleteTexture(Handle);
        }
    }
}
