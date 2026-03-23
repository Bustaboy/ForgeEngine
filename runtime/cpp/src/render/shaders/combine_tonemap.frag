#version 450

layout(set = 0, binding = 0) uniform sampler2D sceneColor;
layout(set = 0, binding = 1) uniform sampler2D bloomColor;

layout(push_constant, std430) uniform PushConstants {
    layout(offset = 176) vec4 config;
} pc;

layout(location = 0) out vec4 outColor;

vec3 ACESFilm(vec3 x) {
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main() {
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(sceneColor, 0));
    float sceneStrength = pc.config.x;
    float bloomStrength = pc.config.y;
    vec3 scene = texture(sceneColor, uv).rgb * sceneStrength;
    vec3 bloom = texture(bloomColor, uv).rgb * bloomStrength;
    vec3 combined = scene + bloom;
    outColor = vec4(ACESFilm(combined), 1.0);
}
