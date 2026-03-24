#version 450

layout(push_constant, std430) uniform PushConstants {
    layout(offset = 0) mat4 viewProj;
    layout(offset = 64) vec4 lightDir;
    layout(offset = 80) vec4 lightColor;
    layout(offset = 96) mat4 model;
    layout(offset = 160) vec4 color;
} pc;

layout(location = 0) out vec4 outColor;
layout(location = 1) out vec3 outNormal;

vec2 kQuadVertices[6] = vec2[](
    vec2(-0.5, -0.5),
    vec2(0.5, -0.5),
    vec2(0.5, 0.5),
    vec2(-0.5, -0.5),
    vec2(0.5, 0.5),
    vec2(-0.5, 0.5)
);

void main() {
    vec2 localPos = kQuadVertices[gl_VertexIndex];
    vec4 worldPos = pc.model * vec4(localPos, 0.0, 1.0);
    gl_Position = pc.viewProj * worldPos;
    outColor = pc.color;
    mat3 normalMatrix = mat3(transpose(inverse(pc.model)));
    outNormal = normalize(normalMatrix * vec3(0.0, 1.0, 0.0));
}
