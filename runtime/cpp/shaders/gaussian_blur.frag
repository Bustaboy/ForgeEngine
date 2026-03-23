#version 450

layout(set = 0, binding = 0) uniform sampler2D source_image;

layout(push_constant, std430) uniform PushConstants {
    layout(offset = 0) mat4 view_proj;
    layout(offset = 64) vec4 light_dir;
    layout(offset = 80) vec4 light_color;
    layout(offset = 96) mat4 model;
    layout(offset = 160) vec4 color;
    layout(offset = 176) vec4 blur_direction;
} push_constants;

layout(location = 0) out vec4 out_color;

void main() {
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(source_image, 0));
    vec2 direction = push_constants.blur_direction.xy;
    vec3 result = texture(source_image, uv).rgb * 0.227027;
    result += texture(source_image, uv + direction * 1.384615).rgb * 0.316216;
    result += texture(source_image, uv - direction * 1.384615).rgb * 0.316216;
    result += texture(source_image, uv + direction * 3.230769).rgb * 0.070270;
    result += texture(source_image, uv - direction * 3.230769).rgb * 0.070270;
    out_color = vec4(result, 1.0);
}
