shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;
uniform vec2 box_size;
uniform vec4 widths; // top, right, bottom, left; transparent sides keep their width
uniform vec4 radius_x; // TL, TR, BR, BL, already overlap-normalized
uniform vec4 radius_y;
uniform vec4 background_color : source_color;
uniform vec4 top_color : source_color;
uniform vec4 right_color : source_color;
uniform vec4 bottom_color : source_color;
uniform vec4 left_color : source_color;
varying vec4 paint_modulate;
varying vec2 box_position;
// QuadMesh UVs inherit 3D orientation; native canvas vertices already use CSS positive-down coordinates.
void vertex() { paint_modulate = COLOR; box_position = VERTEX + box_size * 0.5; }

float ellipse_edge(vec2 offset, vec2 radius) {
    if (min(radius.x, radius.y) <= 0.0) return -1e6;
    vec2 q = offset / radius;
    float implicit_distance = dot(q, q) - 1.0;
    return implicit_distance / max(2.0 * length(q / radius), 1e-6);
}
float rounded_edge(vec2 p, vec2 size, vec4 rx, vec4 ry) {
    float d = max(max(-p.x, p.x-size.x), max(-p.y, p.y-size.y));
    if (p.x < rx.x && p.y < ry.x) d = max(d, ellipse_edge(p-vec2(rx.x,ry.x), vec2(rx.x,ry.x)));
    if (p.x > size.x-rx.y && p.y < ry.y) d = max(d, ellipse_edge(p-vec2(size.x-rx.y,ry.y), vec2(rx.y,ry.y)));
    if (p.x > size.x-rx.z && p.y > size.y-ry.z) d = max(d, ellipse_edge(p-vec2(size.x-rx.z,size.y-ry.z), vec2(rx.z,ry.z)));
    if (p.x < rx.w && p.y > size.y-ry.w) d = max(d, ellipse_edge(p-vec2(rx.w,size.y-ry.w), vec2(rx.w,ry.w)));
    return d;
}
float coverage(float distance) {
    float aa = max(fwidth(distance), 1e-5);
    return clamp(0.5-distance/aa, 0.0, 1.0);
}
vec4 premultiply(vec4 c) { return vec4(c.rgb*c.a, c.a); }
void fragment() {
    vec2 p = box_position;
    float outer = coverage(rounded_edge(p, box_size, radius_x, radius_y));
    vec2 inner_size = box_size-vec2(widths.w+widths.y, widths.x+widths.z);
    vec4 inner_rx = max(radius_x-vec4(widths.w,widths.y,widths.y,widths.w), vec4(0.0));
    vec4 inner_ry = max(radius_y-vec4(widths.x,widths.x,widths.z,widths.z), vec4(0.0));
    float inner = 0.0;
    if (min(inner_size.x, inner_size.y) > 0.0)
        inner = coverage(rounded_edge(p-vec2(widths.w,widths.x), inner_size, inner_rx, inner_ry));
    // CSS corner joins follow rays from outer corners to corresponding inner corners.
    // At zero content size this is exactly the four CSS triangle construction wedges.
    vec4 distances = vec4(p.y,box_size.x-p.x,box_size.y-p.y,p.x)/max(widths,vec4(1e-6));
    if (widths.x == 0.0) distances.x = 1e6;
    if (widths.y == 0.0) distances.y = 1e6;
    if (widths.z == 0.0) distances.z = 1e6;
    if (widths.w == 0.0) distances.w = 1e6;
    // Each side is its own painted wedge, not a normalized color partition.
    // Signed differences describe the actual width-dependent junction rays.
    // Independent antialiased wedges retain their coverage before source-over,
    // including when an adjacent authored side is transparent.
    vec4 neighbors = vec4(
        min(min(distances.y, distances.z), distances.w),
        min(min(distances.x, distances.z), distances.w),
        min(min(distances.x, distances.y), distances.w),
        min(min(distances.x, distances.y), distances.z));
    vec4 junction = distances - neighbors;
    vec4 weight = clamp(vec4(0.5)-junction/max(fwidth(junction),vec4(1e-6)),vec4(0.0),vec4(1.0));
    vec4 top = premultiply(top_color)*weight.x;
    vec4 right = premultiply(right_color)*weight.y;
    vec4 bottom = premultiply(bottom_color)*weight.z;
    vec4 left = premultiply(left_color)*weight.w;
    vec4 border = right+top*(1.0-right.a);
    border = bottom+border*(1.0-bottom.a);
    border = left+border*(1.0-left.a);
    float ring = clamp((outer-inner)/max(outer,1e-6),0.0,1.0);
    if (dot(widths,vec4(1.0)) == 0.0) ring = 0.0;
    border *= ring;
    // Composite within the box before outer coverage; applying coverage twice creates alpha halos.
    vec4 bg = premultiply(background_color);
    COLOR = (border+bg*(1.0-border.a))*outer*vec4(paint_modulate.rgb*paint_modulate.a,paint_modulate.a);
}
