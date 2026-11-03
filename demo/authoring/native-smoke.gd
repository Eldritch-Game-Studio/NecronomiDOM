extends Node
## Real standard-Godot smoke for the GDExtension developer API.

var host: DocumentHost
var clicks := 0
var replacements := 0
var failed := false

func check(condition: bool, label: String) -> void:
    failed = failed or not condition
    print("NATIVE_AUTHORING_SMOKE %s %s" % ["PASS" if condition else "FAIL", label])

func _ready() -> void:
    var layer := CanvasLayer.new()
    add_child(layer)
    host = DocumentHost.new()
    host.document_path = "res://demo/grimoire.html"
    layer.add_child(host)
    host.clicked.connect(func(id: String) -> void:
        if id == "summon":
            clicks += 1
    )
    host.developer_reloaded.connect(func(_kind: int, replaced: bool, _error: String) -> void:
        if replaced:
            replacements += 1
    )
    for i in 4:
        await get_tree().process_frame
    check(host.reload_changed_files() == 0, "reload disabled by default")
    host.developer_reload_enabled = true
    host.set_value("#name", "Keep native text")
    check(host.reload_changed_files() == 1, "unchanged source detected")
    check(host.get_value("#name") == "Keep native text", "unchanged reload preserves edited value")
    host.open_inspector()
    await get_tree().process_frame
    check(host.is_inspector_open() and not host.is_input_enabled(), "native inspector suspends host input")
    check(host.select_inspector_element("#summon"), "native selector inspection")
    var report := host.get_inspector_report()
    check(report.contains("summon") and report.contains("grimoire.css"), "native report contains actual element and source")
    host.close_inspector()
    check(not host.is_inspector_open() and host.is_input_enabled(), "native close restores input")
    host.click("#summon")
    check(clicks == 1, "native document callbacks work")
    check(host.force_reload() == 3 and replacements == 1, "native markup reload signals replacement")
    host.click("#summon")
    check(clicks == 2, "native callbacks rebound to replacement document")
    var peer := DocumentHost.new()
    peer.document_path = "res://demo/grimoire.html"
    layer.add_child(peer)
    for i in 4:
        await get_tree().process_frame
    host.queue_free()
    for i in 4:
        await get_tree().process_frame
    peer.set_text("#title", "A second document still renders")
    for i in 4:
        await get_tree().process_frame
    check(peer.get_text("#title") == "A second document still renders", "withdrawing one host preserves the other host's text server")
    peer.queue_free()
    for i in 4:
        await get_tree().process_frame
    print("NATIVE_AUTHORING_SMOKE_RESULT %s" % ["FAIL" if failed else "PASS"])
    get_tree().quit(1 if failed else 0)
