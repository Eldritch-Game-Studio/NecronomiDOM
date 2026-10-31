extends Node
## GDScript version of the demo: wires demo/grimoire.html through the DocumentHost script API.
## Run with `-- --smoke` to lay out, click a button through the DOM and exit 0 on success.

var _summoned := 0
@onready var _host: DocumentHost = $UI/Document


func _ready() -> void:
	_host.clicked.connect(_on_clicked)
	_host.cancel.connect(func() -> void: print("cancel requested"))
	if "--smoke" in OS.get_cmdline_user_args():
		_smoke.call_deferred()


func _smoke() -> void:
	for i in 3:
		await get_tree().process_frame
	_host.click("#summon")
	var count := _host.get_text("#count")
	print("smoke: buttons=%s count=\"%s\"" % [_host.query_ids("button"), count])
	get_tree().quit(0 if count.begins_with("One shape") else 1)


func _on_clicked(element_id: String) -> void:
	match element_id:
		"summon":
			_summoned += 1
		"banish":
			_summoned = 0
		_:
			return
	var text := "Nothing stirs."
	if _summoned == 1:
		text = "One shape stirs beneath the page."
	elif _summoned > 1:
		text = "%d shapes stir beneath the page." % _summoned
	_host.set_text("#count", text)
