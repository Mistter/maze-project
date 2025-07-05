#version 430
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec2 inUV;

out vec2 vUV;
uniform mat4 uProjection;
uniform mat4 uView;

void main() {
    vUV = inUV;
    vec4 pos = uProjection * uView * vec4(inPosition, 1.0);
    gl_Position = pos.xyww;
}
