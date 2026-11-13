@tool
extends EditorPlugin
## Keep the standard editor's native SceneTree and shutdown ownership intact.
## --editor --script with a custom SceneTree leaks stock editor resources even
## without a GDExtension. Both runners mount the same complete bridge oracle.

const AUTOFOCUS_SCENE := "res://demo/gdscript/demo.tscn"

func _enter_tree() -> void:
    var viewport := SubViewport.new()
    viewport.size = Vector2i(640, 480)
    viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
    # Real input targets the test surface, never the editor's own controls.
    add_child(viewport)
    var oracle = preload("res://native/tests/bridge.gd").new()
    # Editor startup reopens this autofocus scene at a host-dependent frame.
    # Reopen it deterministically while the tool document holds key focus.
    oracle.reopen_edited_scene = _reopen_autofocus_scene
    viewport.add_child(oracle)

func _reopen_autofocus_scene() -> void:
    EditorInterface.open_scene_from_path(AUTOFOCUS_SCENE)
    EditorInterface.reload_scene_from_path(AUTOFOCUS_SCENE)
