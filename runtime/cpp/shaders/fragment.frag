#version 450

layout(push_constant, std430) uniform PushConstants {
    layout(offset = 0) mat4 viewProj;
    layout(offset = 64) vec4 lightDir;
    layout(offset = 80) vec4 lightColor;
    layout(offset = 96) mat4 model;
    layout(offset = 160) vec4 color;
    layout(offset = 176) uvec4 textureInfo;
} pc;

layout(set = 2, binding = 0) uniform sampler2D bindlessTextures[512];

layout(location = 0) in vec4 inColor;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUv;
layout(location = 3) flat in uint inTextureIndex;
layout(location = 0) out vec4 outColor;

void main() {
    vec3 lightDir = normalize(pc.lightDir.xyz);
    vec3 normal = normalize(inNormal);
    float diffuse = max(0.0, dot(normal, lightDir));
    if (pc.textureInfo.y == 1u) {
        diffuse = floor(diffuse * 3.0) / 3.0;
    }
    float ambient = 0.2;
    vec3 lit = inColor.rgb * (ambient + diffuse * pc.lightColor.rgb);
    vec4 textureColor = texture(bindlessTextures[inTextureIndex], inUv);
    outColor = vec4(clamp(lit, 0.0, 1.0), inColor.a) * textureColor;
}
