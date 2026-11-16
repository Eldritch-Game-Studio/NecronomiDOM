extends SceneTree
## GDScript-visible API of NecronomiDOM's GDExtension classes (DocumentHost and its peers), read from ClassDB in
## standard Godot with the native extension installed: methods, signals, properties, constants and enums.
##   godot --headless --path . --script res://api/gdscript/check_api.gd               # check against api.json
##   godot --headless --path . --script res://api/gdscript/check_api.gd -- --update   # rewrite api.json
## A removed or changed entry is a breaking change; any difference fails until api.json changes with the code.

const BASELINE := "res://api/gdscript/api.json"
const SKIPPED_PROPERTY_USAGE := PROPERTY_USAGE_CATEGORY | PROPERTY_USAGE_GROUP | PROPERTY_USAGE_SUBGROUP


func _initialize() -> void:
	var current: Dictionary = JSON.parse_string(JSON.stringify(snapshot(), "", true))
	if not current.has("DocumentHost"):
		print("GDSCRIPT_API FAIL DocumentHost is not registered; install the native extension and import first")
		quit(1)
		return
	if OS.get_cmdline_user_args().has("--update"):
		var file := FileAccess.open(BASELINE, FileAccess.WRITE)
		file.store_string(JSON.stringify(current, "  ", true) + "\n")
		file.close()
		print("GDSCRIPT_API UPDATED %s (%d classes)" % [BASELINE, current.size()])
		quit(0)
		return
	var text := FileAccess.get_file_as_string(BASELINE)
	var baseline = JSON.parse_string(text)
	if typeof(baseline) != TYPE_DICTIONARY:
		print("GDSCRIPT_API FAIL %s is missing or not a JSON object" % BASELINE)
		quit(1)
		return
	var differences := compare(baseline, current, "")
	for line in differences:
		print("GDSCRIPT_API ", line)
	if differences.is_empty():
		print("GDSCRIPT_API PASS %d classes match %s" % [current.size(), BASELINE])
		quit(0)
	else:
		print("GDSCRIPT_API FAIL declare intentional changes in the same change: rerun with -- --update")
		quit(1)


func snapshot() -> Dictionary:
	var classes := {}
	for name in ClassDB.get_class_list():
		var api := ClassDB.class_get_api_type(name)
		if api != ClassDB.API_EXTENSION and api != ClassDB.API_EDITOR_EXTENSION:
			continue
		var methods := {}
		for method in ClassDB.class_get_method_list(name, true):
			methods[method.name] = {
				"args": method.args.map(argument),
				"default_args": method.default_args.map(func(value): return var_to_str(value)),
				"return": argument(method["return"]),
				"flags": method.flags,
			}
		var signals := {}
		for signal_info in ClassDB.class_get_signal_list(name, true):
			signals[signal_info.name] = signal_info.args.map(argument)
		var properties := {}
		for property in ClassDB.class_get_property_list(name, true):
			if property.usage & SKIPPED_PROPERTY_USAGE:
				continue
			properties[property.name] = argument(property)
		var constants := {}
		for constant in ClassDB.class_get_integer_constant_list(name, true):
			constants[constant] = ClassDB.class_get_integer_constant(name, constant)
		var enums := {}
		for enum_name in ClassDB.class_get_enum_list(name, true):
			enums[enum_name] = ClassDB.class_get_enum_constants(name, enum_name, true)
		classes[name] = {
			"inherits": ClassDB.get_parent_class(name),
			"methods": methods,
			"signals": signals,
			"properties": properties,
			"constants": constants,
			"enums": enums,
		}
	return classes

## GDScript passes arguments by position, so parameter names are not part of the compatibility contract.
func argument(info: Dictionary) -> Dictionary:
	return {
		"type": type_string(info.type),
		"class_name": str(info.class_name),
		"hint": info.hint,
		"hint_string": info.hint_string,
	}


func compare(expected, actual, path: String) -> Array[String]:
	var lines: Array[String] = []
	if typeof(expected) == TYPE_DICTIONARY and typeof(actual) == TYPE_DICTIONARY:
		var keys: Array = expected.keys()
		for key in actual.keys():
			if not expected.has(key):
				keys.append(key)
		keys.sort()
		for key in keys:
			var child := "%s/%s" % [path, key]
			if not actual.has(key):
				lines.append("BREAKING removed %s" % child)
			elif not expected.has(key):
				lines.append("UNDECLARED added %s" % child)
			else:
				lines.append_array(compare(expected[key], actual[key], child))
	elif expected != actual:
		lines.append("BREAKING changed %s: %s -> %s" % [path, JSON.stringify(expected), JSON.stringify(actual)])
	return lines
