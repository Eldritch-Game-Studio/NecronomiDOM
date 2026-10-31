# NecronomiDOM

HTML and CSS user interfaces for Godot 4, from GDScript or C#. Full documentation, screenshots and demos:
https://github.com/Eldritch-Game-Studio/NecronomiDOM

## Setup

- **GDScript (standard or .NET Godot 4.7+)**: restart the editor after installing, then add a **DocumentHost** node
  under a `CanvasLayer` and set **Document Path** to your `.html` file. Connect its `clicked(element_id)` signal and
  change the page with `set_text`, `add_class`, `set_attribute` and friends.
- **C# DOM API (.NET edition)**: also add this line to your project's `.csproj`, before `</Project>`, and build:

  ```xml
  <Import Project="addons/necronomidom/NecronomiDom.targets" />
  ```

Recommended: turn off **Project Settings → GUI → Common → Snap Controls to Pixels**.

MIT licensed (see `LICENSE`). Bundled third-party components: AngleSharp (MIT, `vendor/AngleSharp/LICENSE`),
Noto Sans (SIL OFL 1.1, `fonts/OFL.txt`), W3C webref data (MIT), Unicode bidi data (Unicode License v3,
`core/Css/Data/Unicode/LICENSE.txt`). The native libraries in `bin/` are built with the Godot GDExtension .NET
bindings (MIT) and the .NET runtime (MIT).
