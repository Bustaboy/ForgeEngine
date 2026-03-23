#version 450

layout(set = 0, binding = 0) uniform sampler2D sceneColor;
layout(set = 0, binding = 1) uniform sampler2D bloomInput;

layout(push_constant, std430) uniform PushConstants {
    layout(offset = 176) vec4 config;
} pc;

layout(location = 0) out vec4 outColor;

void main() {
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(sceneColor, 0));
    vec3 scene = texture(sceneColor, uv).rgb;
    float threshold = pc.config.x;
    vec3 extracted = max(scene - vec3(threshold), vec3(0.0));
    outColor = vec4(extracted, 1.0);
}
