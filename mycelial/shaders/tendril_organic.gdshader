shader_type canvas_item;

// === Texture size (set from C# every frame — do NOT use textureSize()) ===
uniform vec2 tex_size = vec2(500.0, 280.0);

// === Writhing / UV Distortion ===
uniform float writhe_speed : hint_range(0.0, 5.0) = 1.2;
uniform float writhe_strength : hint_range(0.0, 20.0) = 5.0;
uniform float writhe_scale : hint_range(0.5, 40.0) = 10.0;

// === Color Mottling ===
uniform float mottle_strength : hint_range(0.0, 1.0) = 0.35;
uniform float mottle_scale : hint_range(0.5, 30.0) = 5.0;

// === Pulse Wave ===
uniform float pulse_speed : hint_range(0.0, 10.0) = 2.0;
uniform float pulse_strength : hint_range(0.0, 1.0) = 0.4;
uniform float pulse_wavelength : hint_range(5.0, 200.0) = 40.0;

// === Edge Wobble ===
uniform float edge_wobble_speed : hint_range(0.0, 5.0) = 1.5;
uniform float edge_wobble_strength : hint_range(0.0, 10.0) = 3.0;
uniform float edge_wobble_scale : hint_range(1.0, 30.0) = 7.0;

// === Vein Pattern ===
uniform float vein_strength : hint_range(0.0, 0.6) = 0.2;
uniform float vein_scale : hint_range(1.0, 30.0) = 4.0;
uniform float vein_speed : hint_range(0.0, 3.0) = 0.3;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
	float v = 0.0;
	v += vnoise(p) * 0.55;
	v += vnoise(p * 2.13 + 17.3) * 0.30;
	v += vnoise(p * 4.57 + 31.7) * 0.15;
	return v;
}

float ridged(vec2 p) {
	float n = vnoise(p);
	n = abs(n - 0.5) * 2.0;
	return 1.0 - n * n;
}

void fragment() {
	vec2 pixel_uv = UV * tex_size;

	// =====================================================================
	//  EDGE WOBBLE
	// =====================================================================
	float t_edge = TIME * edge_wobble_speed;
	vec2 edge_coord = pixel_uv / edge_wobble_scale;
	float edge_dx = (fbm(edge_coord + vec2(t_edge * 0.6, t_edge * 0.4)) - 0.5);
	float edge_dy = (fbm(edge_coord + vec2(t_edge * 0.4 + 80.0, t_edge * 0.6 + 40.0)) - 0.5);
	vec2 edge_offset = vec2(edge_dx, edge_dy) * edge_wobble_strength / tex_size;

	vec4 tex = texture(TEXTURE, UV + edge_offset);

	if (tex.a < 0.01) {
		COLOR = vec4(0.0);
		return;
	}

	// =====================================================================
	//  WRITHING
	// =====================================================================
	float t_writhe = TIME * writhe_speed;
	vec2 writhe_coord = pixel_uv / writhe_scale;

	float dx = fbm(writhe_coord + vec2(t_writhe * 0.7, t_writhe * 0.3)) - 0.5;
	float dy = fbm(writhe_coord + vec2(t_writhe * 0.3 + 100.0, t_writhe * 0.7 + 50.0)) - 0.5;
	vec2 writhe_offset = vec2(dx, dy) * writhe_strength / tex_size;

	vec4 distorted = texture(TEXTURE, UV + edge_offset + writhe_offset);

	if (distorted.a < 0.01) {
		distorted = tex;
	}

	vec3 color = distorted.rgb;

	// =====================================================================
	//  MOTTLING
	// =====================================================================
	float mottle1 = fbm(pixel_uv / mottle_scale + vec2(37.0, 91.0));
	float mottle2 = vnoise(pixel_uv / (mottle_scale * 0.5) + vec2(200.0, 150.0));
	float mottle = (mottle1 * 0.7 + mottle2 * 0.3) - 0.45;
	color += vec3(mottle * mottle_strength);

	// =====================================================================
	//  VEINS
	// =====================================================================
	if (vein_strength > 0.0) {
		float t_vein = TIME * vein_speed;
		float vein = ridged(pixel_uv / vein_scale + vec2(t_vein * 0.2, t_vein * 0.15));
		color *= 1.0 - (1.0 - vein) * vein_strength;
	}

	// =====================================================================
	//  PULSE WAVE
	// =====================================================================
	float wave_pos = (pixel_uv.x * 0.7 + pixel_uv.y) / pulse_wavelength - TIME * pulse_speed;
	float pulse = sin(wave_pos * 6.283) * 0.5 + 0.5;
	float pulse_mult = 1.0 + pulse * pulse_strength;
	color *= pulse_mult;

	// =====================================================================
	//  FINAL
	// =====================================================================
	COLOR = vec4(clamp(color, 0.0, 1.0), distorted.a);
}
