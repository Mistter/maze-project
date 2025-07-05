#version 430
in vec2 vUV;
out vec4 fragColor;
uniform sampler2D uSkyTexture;

void main() {
    fragColor = texture(uSkyTexture, vUV);
}
