#version 450

layout(set = 0, binding = 0) uniform sampler2D sceneColor;

layout(push_constant, std430) uniform PushConstants {
    layout(offset = 0) mat4 viewProj;
    layout(offset = 64) vec4 lightDir;
    layout(offset = 80) vec4 lightColor;
    layout(offset = 96) mat4 model;
    layout(offset = 160) vec4 color;
    layout(offset = 176) vec4 threshold;
} pc;

layout(location = 0) out vec4 outColor;

void main() {
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(sceneColor, 0));
    vec3 hdrColor = texture(sceneColor, uv).rgb;
    float brightness = max(max(hdrColor.r, hdrColor.g), hdrColor.b);
    float mask = brightness > pc.threshold.x ? 1.0 : 0.0;
    outColor = vec4(hdrColor * mask, 1.0);
}
