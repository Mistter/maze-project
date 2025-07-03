#version 430

in vec3 passTexCoords;
out vec4 outColor;

uniform sampler2DArray uTexture;

void main()
{
    outColor = texture(uTexture, passTexCoords);
}
