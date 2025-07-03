#version 430

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inTexCoords;

out vec3 passTexCoords;

uniform mat4 uMVP;

void main()
{
    gl_Position = uMVP * vec4(inPosition, 1.0);
    passTexCoords = inTexCoords;
}
