extends Node
## GDScript version of the demo: wires demo/grimoire.html through the DocumentHost script API.

var _summoned := 0
@onready var _host: DocumentHost = $UI/Document


func _ready() -> void:
	_host.clicked.connect(_on_clicked)
	_host.cancel.connect(func() -> void: print("cancel requested"))


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
