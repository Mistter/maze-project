// ui_shader.fs
#version 430

in vec2 passUV;
out vec4 outColor;

uniform sampler2D uTexture;
uniform float uSkyOffset;

void main()
{
    // desloca horizontalmente e repete
    float u = fract(passUV.x + uSkyOffset);
    vec2 uv = vec2(u, passUV.y);
    outColor = texture(uTexture, uv);
}
