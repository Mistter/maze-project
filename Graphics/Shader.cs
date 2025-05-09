using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using MazeEngine.Utils;

namespace MazeEngine.Graphics
{
    internal class Shader
    {
        public const string FragmentShaderExt = ".fs";
        public const string VertexShaderExt = ".vs";

        private readonly int _programId;
        private readonly int _uMVPLocation;

        public Shader(string path)
            : this(path, File.ReadAllText(path + VertexShaderExt), File.ReadAllText(path + FragmentShaderExt))
        {
        }

        public Shader(string name, string vsSource, string fsSource)
        {
            _programId = GL.CreateProgram();

            AttachShader(ShaderType.VertexShader, vsSource);
            AttachShader(ShaderType.FragmentShader, fsSource);

            GL.LinkProgram(_programId);
            var infoLog = GL.GetProgramInfoLog(_programId);
            if (!string.IsNullOrEmpty(infoLog))
                Logger.Error($"Erro ao linkar shader \"{name}\": {infoLog}");

            // Use o programa para obter localizações
            GL.UseProgram(_programId);

            // Definir uTexture para TextureUnit 0
            int uTextureLocation = GL.GetUniformLocation(_programId, "uTexture");
            if (uTextureLocation != -1)
                GL.Uniform1(uTextureLocation, 0); // TextureUnit 0

            // Obter localização de uMVP
            _uMVPLocation = GL.GetUniformLocation(_programId, "uMVP");
            if (_uMVPLocation == -1)
                Logger.Error("Uniform 'uMVP' não encontrado.");
        }

        public void Bind() => GL.UseProgram(_programId);

        public void SetMVP(Matrix4 mvp)
        {
            if (_uMVPLocation != -1)
                GL.UniformMatrix4(_uMVPLocation, false, ref mvp);
        }

        private void AttachShader(ShaderType type, string source)
        {
            var id = GL.CreateShader(type);
            GL.ShaderSource(id, source);
            GL.CompileShader(id);

            // Verificar status de compilação
            GL.GetShader(id, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                string info = GL.GetShaderInfoLog(id);
                Logger.Error($"Erro compilando {type}: {info}");
            }

            GL.AttachShader(_programId, id);
        }
    }
}