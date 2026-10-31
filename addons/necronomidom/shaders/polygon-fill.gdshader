shader_type canvas_item;
render_mode unshaded;

// A CSS polygon clips the box background; out-of-box vertices must not invent background overflow.
uniform vec4 reference_bounds = vec4(0.0);
varying vec2 document_position;

void vertex() {
    document_position = VERTEX;
}

void fragment() {
    vec2 pixel_size = max(abs(dFdx(document_position)) + abs(dFdy(document_position)), vec2(0.00001));
    vec2 first = clamp((document_position - reference_bounds.xy) / pixel_size + vec2(0.5), vec2(0.0), vec2(1.0));
    vec2 last = clamp((reference_bounds.xy + reference_bounds.zw - document_position) / pixel_size + vec2(0.5), vec2(0.0), vec2(1.0));
    vec2 coverage = min(first, last);
    COLOR.a *= coverage.x * coverage.y;
}
