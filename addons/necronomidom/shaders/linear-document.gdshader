shader_type canvas_item;
render_mode unshaded, blend_disabled;
uniform sampler2D scene_color : hint_screen_texture, filter_nearest, repeat_disable;
uniform bool destination_linear = false;

vec3 decode_srgb(vec3 c) {
    return mix(pow((c + vec3(0.055)) / 1.055, vec3(2.4)), c / 12.92, lessThanEqual(c, vec3(0.04045)));
}
vec3 encode_srgb(vec3 c) {
    return mix(1.055 * pow(max(c, vec3(0.0)), vec3(1.0 / 2.4)) - vec3(0.055), c * 12.92, lessThanEqual(c, vec3(0.0031308)));
}
void fragment() {
    // The HDR isolation texture is already linear and premultiplied. Never decode it again.
    vec4 source = textureLod(TEXTURE, UV, 0.0);
    vec4 destination = textureLod(scene_color, SCREEN_UV, 0.0);
    vec3 background = destination_linear ? destination.rgb : decode_srgb(destination.rgb);
    vec3 result = source.rgb + background * (1.0 - source.a);
    COLOR = vec4(destination_linear ? result : encode_srgb(result), source.a + destination.a * (1.0 - source.a));
}
