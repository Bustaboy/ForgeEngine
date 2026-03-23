#version 450

layout(location = 0) out vec4 out_color;

layout(set = 1, binding = 0) uniform sampler2D scene_sampler;
layout(set = 1, binding = 1) uniform sampler2D bloom_sampler;

layout(push_constant, std430) uniform PushConstants {
    layout(offset = 0) mat4 view_proj;
    layout(offset = 64) vec4 light_dir;
    layout(offset = 80) vec4 light_color;
    layout(offset = 96) mat4 model;
    layout(offset = 160) vec4 color;
    layout(offset = 176) vec4 bloom_threshold;
    layout(offset = 192) vec4 combine_params;
} push_constants;

vec3 ACESFilm(vec3 x) {
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main() {
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(scene_sampler, 0));
    vec3 scene_color = texture(scene_sampler, uv).rgb;
    vec3 bloom_color = texture(bloom_sampler, uv).rgb;
    float bloom_strength = push_constants.combine_params.x;
    float exposure = push_constants.combine_params.y;
    vec3 combined = scene_color + bloom_color * bloom_strength;
    vec3 tonemapped = ACESFilm(combined * max(exposure, 0.0001));
    out_color = vec4(tonemapped, 1.0);
}
