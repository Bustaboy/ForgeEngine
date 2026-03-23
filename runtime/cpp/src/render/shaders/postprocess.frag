#version 450

layout(set = 0, binding = 0) uniform sampler2D sceneColor;
layout(set = 0, binding = 1) uniform sampler2D bloomColor;

layout(push_constant) uniform PostProcessParams {
    vec4 bloomA;
    vec4 bloomB;
} params;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

vec3 SampleBloom(vec2 coord) {
    vec2 texel = 1.0 / vec2(textureSize(bloomColor, 0));
    vec3 c = texture(bloomColor, coord).rgb * 0.227027;
    c += texture(bloomColor, coord + vec2(texel.x, 0.0)).rgb * 0.1945946;
    c += texture(bloomColor, coord - vec2(texel.x, 0.0)).rgb * 0.1945946;
    c += texture(bloomColor, coord + vec2(0.0, texel.y)).rgb * 0.1945946;
    c += texture(bloomColor, coord - vec2(0.0, texel.y)).rgb * 0.1945946;
    c += texture(bloomColor, coord + texel).rgb * 0.0945946;
    c += texture(bloomColor, coord - texel).rgb * 0.0945946;
    return c;
}

vec3 ACESFilm(vec3 x) {
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main() {
    vec3 scene = texture(sceneColor, uv).rgb;
    vec3 bloom = SampleBloom(uv) * params.bloomA.y;
    vec3 color = scene + bloom;
    color *= params.bloomA.w;
    color = ACESFilm(color);
    color = pow(color, vec3(1.0 / 2.2));
    outColor = vec4(color, 1.0);
}
