shader_type canvas_item;
render_mode unshaded;

// Exact single-paint polygon contexts retain the fan raster and both coverage stages.
// Reproduce the removed HDR target's storage and low-alpha unpremultiplication.
uniform vec4 reference_bounds = vec4(0.0);
uniform mat4 polygon_edges = mat4(0.0);
uniform int edge_count = 0;
uniform vec4 fill_color = vec4(1.0);
varying vec2 document_position;
varying vec4 item_modulate;

void vertex() {
    document_position = VERTEX;
    item_modulate = COLOR;
}

void fragment() {
    vec2 pixel_size = max(abs(dFdx(document_position)) + abs(dFdy(document_position)), vec2(0.00001));
    vec2 first = clamp((document_position - reference_bounds.xy) / pixel_size + vec2(0.5), vec2(0.0), vec2(1.0));
    vec2 last = clamp((reference_bounds.xy + reference_bounds.zw - document_position) / pixel_size + vec2(0.5), vec2(0.0), vec2(1.0));
    vec2 box_coverage = min(first, last);
    float alpha = fill_color.a * box_coverage.x * box_coverage.y;
    vec4 captured = vec4(fill_color.rgb * alpha, alpha);
    captured = vec4(unpackHalf2x16(packHalf2x16(captured.rg)), unpackHalf2x16(packHalf2x16(captured.ba)));
    float coverage = 1.0;
    for (int i = 0; i < edge_count; i++) {
        vec3 edge = polygon_edges[i].xyz;
        float distance_inside = dot(edge.xy, document_position) + edge.z;
        float pixel_width = max(fwidth(distance_inside), 0.00001);
        coverage = min(coverage, clamp(distance_inside / pixel_width + 0.5, 0.0, 1.0));
    }
    if (captured.a > 0.0001) {
        captured.rgb /= captured.a;
    }
    COLOR = item_modulate * vec4(captured.rgb, captured.a * coverage);
}
