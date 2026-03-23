#version 450

layout(push_constant) uniform PushConstants {
    layout(offset = 0) mat4 viewProj;
    layout(offset = 64) vec4 lightDir;
    layout(offset = 80) vec4 lightColor;
    layout(offset = 96) mat4 model;
    layout(offset = 160) vec4 color;
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
    vec4 worldPos = pc.model * vec4(localPos, 0.0, 1.0);
    gl_Position = pc.viewProj * worldPos;

    vec3 normal = vec3(0.0, 1.0, 0.0);
    vec3 lightDirection = normalize(pc.lightDir.xyz);
    float ndotl = max(dot(normal, lightDirection), 0.0);
    vec3 ambient = pc.color.rgb * 0.25;
    vec3 diffuse = pc.color.rgb * pc.lightColor.rgb * ndotl;
    fragColor = vec4(ambient + diffuse, pc.color.a);
}
