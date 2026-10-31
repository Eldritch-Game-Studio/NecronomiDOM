instance uniform vec4 image_raster_rect : instance_index(0);
instance uniform vec4 image_source_rect : instance_index(1);
varying flat vec2 image_device_adjugate_x;
varying flat vec2 image_device_adjugate_y;
varying flat vec2 image_device_origin;
varying flat float image_device_determinant;

vec2 image_source_center(vec2 device_center) {
    vec2 relative = device_center - image_device_origin;
    // Defer inverse division until it can be combined with the actual raster/source normalization.
    vec2 numerator = vec2(dot(image_device_adjugate_x, relative), dot(image_device_adjugate_y, relative))
        - image_raster_rect.xy * image_device_determinant;
    vec2 denominator = image_raster_rect.zw * image_device_determinant;
    vec2 scale = image_source_rect.zw / denominator;
    // Correct hardware reciprocal rounding using its arithmetic residual, not a coordinate epsilon or tie snap.
    scale += fma(-scale, denominator, image_source_rect.zw) / denominator;
    return fma(numerator, scale, image_source_rect.xy - vec2(0.5));
}
