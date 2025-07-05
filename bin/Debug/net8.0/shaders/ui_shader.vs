// ui_shader.vs
#version 430

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec2 inTexCoords;

out vec2 passUV;

uniform mat4 uMVP;
uniform float uSkyOffset;

void main()
{
    gl_Position = uMVP * vec4(inPosition, 1.0);
    passUV = inTexCoords;
}
