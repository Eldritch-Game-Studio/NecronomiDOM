shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;
#include "res://addons/necronomidom/shaders/image_coordinates.gdshaderinc"
// Uploaded images are premultiplied before filtering: transparent pixels must not darken opaque neighbors.
uniform bool nearest_sampling = false;
instance uniform bool image_edge_fringe : instance_index(2);
varying vec4 image_modulate;

void vertex() {
    image_modulate = vec4(COLOR.rgb * COLOR.a, COLOR.a);
    mat4 device = CANVAS_MATRIX * MODEL_MATRIX;
    image_device_adjugate_x = vec2(device[1][1], -device[1][0]);
    image_device_adjugate_y = vec2(-device[0][1], device[0][0]);
    image_device_origin = device[3].xy;
    image_device_determinant = device[0][0] * device[1][1] - device[0][1] * device[1][0];
    if (image_edge_fringe) {
        // The owning clip supplies coverage, including samples outside the hard triangle.
        vec2 inverse_extent = vec2(length(image_device_adjugate_x), length(image_device_adjugate_y))
            / abs(image_device_determinant);
        VERTEX += sign(VERTEX - (image_raster_rect.xy + image_raster_rect.zw * 0.5)) * inverse_extent;
    }
}

void fragment() {
    vec2 centered = image_source_center(FRAGCOORD.xy);
    if (image_edge_fringe) {
        centered = clamp(centered, image_source_rect.xy - vec2(0.5),
            image_source_rect.xy + image_source_rect.zw - vec2(0.5));
    }
    vec2 uv;
    vec2 size = vec2(textureSize(TEXTURE, 0));
    if (nearest_sampling) {
        // Nearest sampling assigns an exact texel-boundary tie to the preceding texel.
        uv = (ceil(centered + vec2(0.5)) - vec2(0.5)) / size;
    } else {
        // Chrome's software image painter quantizes bilinear phases to four bits.
        // Keep this deterministic source-space filter; alpha remains premultiplied.
        uv = (floor(centered * 16.0) / 16.0 + vec2(0.5)) / size;
    }
    COLOR = image_modulate * texture(TEXTURE, uv);
}
