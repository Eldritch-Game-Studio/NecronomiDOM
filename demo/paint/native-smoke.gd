extends Node
## Standard-Godot smoke for the GDExtension paint classes behind retained updates.

const MARKUP := """<!doctype html><html><head><style>
html,body,div,p{display:flex;flex-direction:column;flex-shrink:0;margin:0;padding:0;border:0 solid black;box-sizing:border-box;font-size:18px}
html,body{width:100%;height:100%;background-color:#202c39;color:white}
#frame{position:absolute;left:40px;top:40px;width:240px;height:80px;border:3px solid #c0a55c;border-left-color:#5ab0ff}
#frame.shift{left:41px}
#caption{position:absolute;left:40px;top:160px;width:360px;text-shadow:1px 1px 2px black}
</style></head><body><div id="frame"></div><p id="caption">Retained glyph targets</p></body></html>"""

var failed := false

func check(condition: bool, label: String) -> void:
    failed = failed or not condition
    print("NATIVE_PAINT_SMOKE %s %s" % ["PASS" if condition else "FAIL", label])

func collect(node: Node, type: String, result: Dictionary) -> void:
    if node.get_class() == type:
        result[node.get_instance_id()] = true
    for child in node.get_children(true):
        collect(child, type, result)

func paint_nodes(host: Node, type: String) -> Dictionary:
    var result := {}
    collect(host, type, result)
    return result

func _ready() -> void:
    DirAccess.make_dir_recursive_absolute("user://ui")
    var file := FileAccess.open("user://ui/native-paint-smoke.html", FileAccess.WRITE)
    file.store_string(MARKUP)
    file.close()
    var layer := CanvasLayer.new()
    add_child(layer)
    var host := DocumentHost.new()
    host.document_path = "user://ui/native-paint-smoke.html"
    layer.add_child(host)
    for i in 6:
        await get_tree().process_frame
    var borders := paint_nodes(host, "NativeBorderPaint")
    var targets := paint_nodes(host, "SubViewport")
    check(borders.size() == 1, "asymmetric border uses the registered immutable border mesh node")
    check(targets.size() > 0, "blurred text shadow owns glyph targets")
    for frame in 60:
        host.toggle_class("#frame", "shift")
        host.set_text("#caption", "Retained glyph targets" + ("!" if frame % 2 == 0 else ""))
        await get_tree().process_frame
    check(paint_nodes(host, "NativeBorderPaint") == borders, "moving border keeps its native node")
    check(paint_nodes(host, "SubViewport") == targets, "changed shadowed text reuses its glyph targets")
    host.queue_free()
    for i in 4:
        await get_tree().process_frame
    DirAccess.remove_absolute("user://ui/native-paint-smoke.html")
    print("NATIVE_PAINT_SMOKE_RESULT %s" % ["FAIL" if failed else "PASS"])
    get_tree().quit(1 if failed else 0)
