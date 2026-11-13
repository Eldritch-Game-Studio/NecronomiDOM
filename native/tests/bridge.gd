@tool
extends Node
## Shared runtime and standard-editor oracle, mounted in the runner's viewport.
## Uses actual native DOM/input/layout; no managed runtime or synthetic DOM events.

var host: Variant
var failures: int = 0
var events: Array[Dictionary] = []
var captured: bool = false
var pointer_id: int = -1
var root: Variant
## Editor runner only: reopens an autofocus scene while this document holds focus.
var reopen_edited_scene: Callable

func _ready() -> void:
    root = get_viewport()
    _run.call_deferred()

func check(condition: bool, message: String) -> void:
    if not condition:
        failures += 1
        push_error("native-bridge: " + message)

func settle() -> void:
    for frame in 3:
        await get_tree().process_frame

func _run() -> void:
    root.size = Vector2i(640, 480)
    host = ClassDB.instantiate("DocumentHost")
    host.document_path = get_script().resource_path.get_base_dir().path_join("bridge.html")
    host.size = Vector2(640, 480)
    host.dom_event_detail.connect(_event)
    for type in ["pointerdown", "pointermove", "pointerup", "gotpointercapture", "lostpointercapture", "keydown", "input", "change", "wheel"]:
        host.listen(type)
    root.add_child(host)
    await settle()
    host.focus_element("#editor")
    host.set_value("#editor", "dirty retained value")
    host.set_checked("#check", true)
    host.set_selected_index("#choice", 1)
    check(host.get_checked("#check"), "checked state round trip")
    check(host.get_value("#choice") == "b", "selected index selects value")
    host.set_value("#choice", "a")
    check(host.get_selected_index("#choice") == 0, "select value selects index")
    host.scroll_to("#rows", 0, 60)
    await settle()
    var scroll: Vector2 = host.get_scroll("#rows")
    check(scroll.y > 0, "real scroll owner clamps and scrolls")
    host.set_attribute("#row_02", "data-retained", "identity-marker")
    host.append_html("#rows", '<div class="row" id="row_generated_0006">Six</div>')
    host.insert_html_before("#row_03", '<div class="row" id="row_inserted">Inserted</div>')
    host.set_inner_html("#row_01", '<div id="replacement">Replaced</div>')
    host.move_element("#row_generated_0006", "#rows", "#row_02")
    host.remove_element("#row_inserted")
    host.set_style("#row_02", "background-color", "#abcdef")
    if reopen_edited_scene.is_valid():
        # An edited-scene preview must not take the window's native key focus.
        reopen_edited_scene.call()
    await settle()
    check(host.get_active_element_id() == "editor", "unaffected focus survives all mutations")
    check(host.get_value("#editor") == "dirty retained value", "dirty control state survives (not reconstructed from markup)")
    check(host.get_scroll("#rows").is_equal_approx(scroll), "scroll survives incremental mutations")
    check(host.get_attribute("#row_02", "data-retained") == "identity-marker", "untouched sibling state survives")
    check(host.query_ids("#rows > .row") == PackedStringArray(["row_01", "row_generated_0006", "row_02", "row_03", "row_04", "row_05"]), "append/insert/remove/reorder order")
    check(host.get_text("#replacement") == "Replaced" and not host.has_element("#row_inserted"), "replace and remove affect only requested descendants")
    check(not host.get_style("#row_02", "background-color").is_empty(), "live inline style round trip")
    var key = InputEventKey.new()
    key.keycode = KEY_A
    key.physical_keycode = KEY_A
    key.unicode = 65
    key.shift_pressed = true
    key.pressed = true
    root.push_input(key)
    await settle()
    check(_has("keydown", "editor", "code", "KeyA"), "keyboard code payload")
    check(_has("keydown", "editor", "shift_key", true), "keyboard modifier payload")
    check(_has("input", "editor", "value", host.get_value("#editor")), "input payload contains edited value")
    var rect: Rect2 = host.get_element_rect("#drag")
    check(rect.size.x > 0 and rect.size.y > 0, "published border box")
    check(host.element_from_point(rect.get_center().x, rect.get_center().y) == "drag", "physical hit testing")
    _mouse(rect.get_center(), true)
    await settle()
    check(captured and pointer_id > 0, "pointerdown captures actual active pointer")
    var motion = InputEventMouseMotion.new()
    motion.position = Vector2(600, 450)
    motion.global_position = motion.position
    motion.button_mask = MOUSE_BUTTON_MASK_LEFT
    motion.shift_pressed = true
    root.push_input(motion)
    await settle()
    check(_has("pointermove", "drag", "position", motion.position), "capture routes outside motion with physical coordinates")
    check(_has("pointermove", "drag", "shift_key", true), "pointer modifiers use original native event")
    check(host.element_from_point(600, 450) != "drag", "physical hit target independent of capture target")
    # Capture belongs to the actual DOM node, not its ID. Retaining it across a
    # sibling mutation proves the host did not rebuild unchanged elements.
    host.append_html("#rows", '<div class="row" id="row_after_capture">Seven</div>')
    host.move_element("#row_after_capture", "#rows", "#row_01")
    host.remove_element("#row_after_capture")
    await settle()
    check(host.has_pointer_capture("#drag", pointer_id), "unaffected node identity retains capture across mutations")
    host.release_pointer_capture("#drag", pointer_id)
    check(not host.has_pointer_capture("#drag", pointer_id), "explicit capture release")
    _mouse(Vector2(600, 450), false)
    await settle()
    check(_has_type("gotpointercapture") and _has_type("lostpointercapture"), "capture lifecycle delivered")
    var wheel = InputEventMouseButton.new()
    wheel.position = rect.get_center()
    wheel.global_position = wheel.position
    wheel.button_index = MOUSE_BUTTON_WHEEL_DOWN
    wheel.factor = 1.0
    wheel.pressed = true
    root.push_input(wheel)
    await settle()
    check(_has("wheel", "drag", "delta_y", 40.0), "vertical wheel CSS pixel delta")
    check(_has("wheel", "drag", "delta_x", 0.0), "vertical wheel has zero horizontal delta")
    check(_has("wheel", "drag", "delta_mode", 0), "wheel delta mode is pixels")
    wheel.shift_pressed = true
    root.push_input(wheel)
    await settle()
    check(_has("wheel", "drag", "delta_x", 40.0), "shift wheel maps to horizontal delta")
    check(_has("wheel", "drag", "shift_key", true), "wheel carries original native modifiers")
    var resources: Array[WeakRef] = []
    for item in host.find_children("*", "MultiMeshInstance2D", true, false):
        resources.append(weakref(item.multimesh))
        resources.append(weakref(item.multimesh.mesh))
    check(not resources.is_empty(), "lifetime oracle observes real rendered box resources")
    host.queue_free()
    await settle()
    for resource in resources:
        check(resource.get_ref() == null, "paint resource released when native host is freed")
    if failures == 0:
        print("native-bridge: incremental DOM, state, geometry, keyboard, wheel, pointer capture and resource lifetime passed")
    get_tree().quit(1 if failures else 0)

func _mouse(point: Vector2, pressed: bool) -> void:
    var mouse = InputEventMouseButton.new()
    mouse.position = point
    mouse.global_position = point
    mouse.button_index = MOUSE_BUTTON_LEFT
    mouse.button_mask = MOUSE_BUTTON_MASK_LEFT if pressed else 0
    mouse.pressed = pressed
    root.push_input(mouse)

func _event(type: String, element_id: String, details: Dictionary) -> void:
    events.append({"type": type, "id": element_id, "details": details.duplicate()})
    if type == "pointerdown" and element_id == "drag":
        pointer_id = details.pointer_id
        check(details.button == 0 and details.buttons == 1, "DOM mouse button numbering")
        host.set_pointer_capture("#drag", pointer_id)
        captured = host.has_pointer_capture("#drag", pointer_id)

func _has(type: String, id: String, key: String, value: Variant) -> bool:
    for event in events:
        if event.type == type and event.id == id and event.details.get(key) == value:
            return true
    return false

func _has_type(type: String) -> bool:
    for event in events:
        if event.type == type:
            return true
    return false
