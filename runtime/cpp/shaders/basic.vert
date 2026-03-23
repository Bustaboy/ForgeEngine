#version 450

layout(push_constant) uniform PushConstants {
    mat4 model;
    vec4 color;
} pc;

layout(location = 0) out vec4 fragColor;

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
    gl_Position = pc.model * vec4(localPos, 0.0, 1.0);
    fragColor = pc.color;
}
