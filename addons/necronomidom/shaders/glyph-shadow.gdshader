shader_type canvas_item;
render_mode unshaded;

uniform float shadow_sigma = 1.0;
uniform vec2 mask_texel_size;
uniform vec2 blur_axis = vec2(0.0, 1.0);
uniform vec4 shadow_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
varying vec4 vertex_tint;

void vertex() {
    vertex_tint = COLOR;
}

// A finite three-sigma Gaussian, two one-dimensional passes, on actual glyph coverage.
// Outside the retained padded mask is transparent, never clamped glyph-edge coverage.
void fragment() {
    float sigma = max(shadow_sigma, 0.000001);
    int radius = int(ceil(3.0 * sigma));
    float alpha = 0.0;
    float total = 0.0;
    for (int i = -radius; i <= radius; i++) {
        float distance = float(i);
        float weight = exp(-0.5 * distance * distance / (sigma * sigma));
        vec2 sample_uv = UV + blur_axis * mask_texel_size * distance;
        if (all(greaterThanEqual(sample_uv, vec2(0.0))) && all(lessThanEqual(sample_uv, vec2(1.0)))) {
            alpha += texture(TEXTURE, sample_uv).a * weight;
        }
        total += weight;
    }
    COLOR = vec4(shadow_color.rgb, shadow_color.a * alpha / total) * vertex_tint;
}
