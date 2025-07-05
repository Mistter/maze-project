// MazeEngine/Graphics/Shader.cs
using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace MazeEngine.Graphics
{
    public class Shader : IDisposable
    {
        public readonly int Handle;

        public Shader(string baseName)
        {
            string vsPath = Path.Combine("Shaders", baseName + ".vs");
            string fsPath = Path.Combine("Shaders", baseName + ".fs");

            if (!File.Exists(vsPath)) throw new Exception($"VS não encontrado: {vsPath}");
            if (!File.Exists(fsPath)) throw new Exception($"FS não encontrado: {fsPath}");

            string vsSource = File.ReadAllText(vsPath);
            string fsSource = File.ReadAllText(fsPath);

            Console.WriteLine($"[DEBUG] {baseName}.vs length = {vsSource.Length}");
            Console.WriteLine($"[DEBUG] {baseName}.fs length = {fsSource.Length}");

            if (string.IsNullOrWhiteSpace(vsSource)) throw new Exception($"VS vazio: {vsPath}");
            if (string.IsNullOrWhiteSpace(fsSource)) throw new Exception($"FS vazio: {fsPath}");

            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vsSource);
            GL.CompileShader(vs);
            GL.GetShader(vs, ShaderParameter.CompileStatus, out var statusVs);
            if (statusVs != (int)All.True)
                throw new Exception($"Erro VS compile ({baseName}):\n{GL.GetShaderInfoLog(vs)}");

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fsSource);
            GL.CompileShader(fs);
            GL.GetShader(fs, ShaderParameter.CompileStatus, out var statusFs);
            if (statusFs != (int)All.True)
                throw new Exception($"Erro FS compile ({baseName}):\n{GL.GetShaderInfoLog(fs)}");

            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vs);
            GL.AttachShader(Handle, fs);
            GL.LinkProgram(Handle);
            GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out var statusLink);
            if (statusLink != (int)All.True)
                throw new Exception($"Erro Link ({baseName}):\n{GL.GetProgramInfoLog(Handle)}");

            GL.DetachShader(Handle, vs);
            GL.DeleteShader(vs);
            GL.DetachShader(Handle, fs);
            GL.DeleteShader(fs);
        }

        public void Bind() => GL.UseProgram(Handle);
        public void Unbind() => GL.UseProgram(0);

        public void SetInt(string name, int v) => GL.Uniform1(GL.GetUniformLocation(Handle, name), v);
        public void SetFloat(string name, float v) => GL.Uniform1(GL.GetUniformLocation(Handle, name), v);
        public void SetMVP(Matrix4 mvp)
            => GL.UniformMatrix4(GL.GetUniformLocation(Handle, "uMVP"), false, ref mvp);

        public void SetMatrix4(string name, Matrix4 m)
            => GL.UniformMatrix4(GL.GetUniformLocation(Handle, name), false, ref m);

        public void Dispose() => GL.DeleteProgram(Handle);
    }
}
