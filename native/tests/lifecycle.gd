extends SceneTree
## Run in a scratch project containing the built addon and this directory.
## Exercises real native shaping/paint, authoring reload and host withdrawal, not source text.

const DOCUMENT_RELOADED := 3
const CYCLES := 4


func _initialize() -> void:
    _run.call_deferred()


func _check(condition: bool, detail: String) -> bool:
    if condition:
        return true
    push_error("native-lifecycle: " + detail)
    quit(1)
    return false


func _run() -> void:
    if not _check(ClassDB.class_exists("DocumentHost"), "DocumentHost is not registered"):
        return
    root.size = Vector2i(640, 480)
    var document_path: String = get_script().resource_path.get_base_dir().path_join("lifecycle.html")
    for cycle in CYCLES:
        var host: Variant = ClassDB.instantiate("DocumentHost")
        host.document_path = document_path
        host.size = Vector2(640, 480)
        host.developer_reload_enabled = true
        host.developer_reload_watch_enabled = false
        host.clicked.connect(_on_clicked.bind(host))
        root.add_child(host)
        for frame in 3:
            await process_frame
        if not _check(host.has_element("#action"), "document did not mount in cycle %d" % cycle):
            return
        host.click("#action")
        if not _check(host.get_text("#result") == "Activated", "DOM signal did not update text in cycle %d" % cycle):
            return
        host.open_inspector()
        await process_frame
        if not _check(host.is_inspector_open(), "inspector did not open in cycle %d" % cycle):
            return
        if not _check(host.force_reload() == DOCUMENT_RELOADED, "authoring reload failed in cycle %d" % cycle):
            return
        for frame in 2:
            await process_frame
        if not _check(host.get_text("#result") == "Ready", "reload did not replace the live document in cycle %d" % cycle):
            return
        host.click("#action")
        if not _check(host.get_text("#result") == "Activated", "DOM signal was not rebound after reload in cycle %d" % cycle):
            return
        if cycle + 1 < CYCLES:
            host.queue_free()
            for frame in 2:
                await process_frame
            if not _check(not is_instance_valid(host), "host was not withdrawn in cycle %d" % cycle):
                return
    # Leave the final host and inspector mounted: normal SceneTree shutdown must withdraw them.
    print("native-lifecycle: cycles=4 reloads=4 withdrawn=3 final-host-mounted=true")
    quit(0)


func _on_clicked(element_id: String, host: Variant) -> void:
    if element_id == "action":
        host.set_text("#result", "Activated")
