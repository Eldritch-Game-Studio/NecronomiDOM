# NecronomiDOM

Build Godot interfaces with HTML and CSS, from GDScript or C#. NecronomiDOM renders natively through Godot—no browser or JavaScript runtime.

NecronomiDOM is inspired by Valve's [Panorama](https://developer.valvesoftware.com/wiki/Panorama/Overview) UI framework. It is an independent project and is not affiliated with or endorsed by Valve.

[Download the latest release](https://github.com/Eldritch-Game-Studio/NecronomiDOM/releases/latest) · [Documentation and examples](https://github.com/Eldritch-Game-Studio/NecronomiDOM#readme)

## Installation

1. Download the attached **NecronomiDOM addon ZIP** from the latest GitHub release. Use the addon ZIP under **Assets**, not the automatic **Source code** archives.
2. Copy its `addons/necronomidom/` folder into your project.
3. Restart Godot to load the native extension.

The release ZIP includes native libraries for Windows x64, Linux x64/ARM64, and macOS Intel/Apple silicon. GDScript projects do not need .NET.

## GDScript

Add a **DocumentHost** node under a `CanvasLayer` and set **Document Path** to your `.html` file. Connect its `clicked(element_id)` signal and update the document with `set_text`, `add_class`, `set_attribute` and the other selector-based methods.

## C# DOM API

Use Godot .NET. Add this line to your project's `.csproj`, before `</Project>`, and build:

```xml
<Import Project="addons/necronomidom/NecronomiDom.targets" />
```

Attach `res://addons/necronomidom/runtime/DocumentHost.cs` to a `Control` to use the full C# DOM API, CSSOM and typed state bindings.

## Authoring

- Write ordinary HTML and linked CSS files for your interface.
- Use the live DOM inspector to inspect elements, styles and CSS custom properties.
- Reload CSS without replacing the document or losing input state; rebind controls after markup replacement.
- Enable source watching when you want automatic reload. Inspector and reload tools are debug-build features.

Recommended: turn off **Project Settings → GUI → Common → Snap Controls to Pixels** for fractional layout and text positioning.

## Compatibility

Requires **Godot 4.7.2**, standard or .NET edition. The C# DOM API requires the .NET edition. Android, iOS and Web native exports are not supported.

NecronomiDOM implements a defined HTML/CSS subset, not a browser. See the [supported capabilities](https://github.com/Eldritch-Game-Studio/NecronomiDOM/blob/main/docs/capabilities.md) for layout, input and accessibility details. Mixed checkbox and `aria-invalid` states are not exposed to screen readers.

## Examples

Clone the [source repository](https://github.com/Eldritch-Game-Studio/NecronomiDOM) for the Nightward Guild C# workbench and the smaller GDScript Grimoire demo. Install the release addon into that clone before running the examples. The installable ZIP contains the addon, not the full demo project.

## License

MIT licensed (see `LICENSE`). Bundled third-party components: AngleSharp (MIT, `vendor/AngleSharp/LICENSE`), Noto Sans (SIL OFL 1.1, `fonts/OFL.txt`), W3C webref data (MIT), Unicode bidi data (Unicode License v3, `core/Css/Data/Unicode/LICENSE.txt`). The native libraries in `bin/` are built with the Godot GDExtension .NET bindings (MIT) and the .NET runtime (MIT).
