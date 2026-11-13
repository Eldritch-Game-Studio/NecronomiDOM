extends SceneTree
## Runtime entry point: --script res://native/tests/bridge_runtime.gd.

func _initialize() -> void:
    root.add_child.call_deferred(preload("res://native/tests/bridge.gd").new())
