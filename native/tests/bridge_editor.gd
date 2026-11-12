@tool
extends EditorPlugin
## Keep the standard editor's native SceneTree and shutdown ownership intact.
## --editor --script with a custom SceneTree leaks stock editor resources even
## without a GDExtension. Both runners mount the same complete bridge oracle.

func _enter_tree() -> void:
    var viewport := SubViewport.new()
    viewport.size = Vector2i(640, 480)
    viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
    # Real input targets the test surface, never the editor's own controls.
    add_child(viewport)
    viewport.add_child(preload("res://native/tests/bridge.gd").new())
