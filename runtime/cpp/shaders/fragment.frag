#version 450

layout(push_constant, std430) uniform PushConstants {
    layout(offset = 0) mat4 viewProj;
    layout(offset = 64) vec4 lightDir;
    layout(offset = 80) vec4 lightColor;
    layout(offset = 96) mat4 model;
    layout(offset = 160) vec4 color;
} pc;

layout(location = 0) in vec4 inColor;
layout(location = 0) out vec4 outColor;

void main() {
    vec3 normal = vec3(0.0, 1.0, 0.0);
    vec3 lightDir = normalize(pc.lightDir.xyz);
    float diffuse = max(0.0, dot(normal, lightDir));
    float ambient = 0.2;
    vec3 lit = inColor.rgb * (ambient + diffuse * pc.lightColor.rgb);
    outColor = vec4(clamp(lit, 0.0, 1.0), inColor.a);
}
