# NecronomiDOM

Build Godot interfaces with HTML and CSS, from GDScript or C#. NecronomiDOM renders natively through Godot—no browser or JavaScript runtime.

[Download the latest release](https://github.com/Eldritch-Game-Studio/NecronomiDOM/releases/latest) · [Documentation and examples](https://github.com/Eldritch-Game-Studio/NecronomiDOM#readme)

## Installation

1. Download an attached addon ZIP under **Assets** from the latest GitHub release, not an automatic **Source code** archive. Choose **`NecronomiDOM-v…-runtime.zip`** for GDScript or Dirge1.1's standard-Godot dependency installer, or the **full `NecronomiDOM-v….zip`** for Godot .NET/C# integration. The Godot Asset Store package remains the full ZIP.
2. Copy its `addons/necronomidom/` folder into your project.
3. Restart Godot to load the native extension.

Both release ZIPs include native libraries for Windows x64, Linux x64/ARM64, and macOS Intel/Apple silicon. GDScript projects do not need .NET; the runtime-only ZIP omits managed source and build inputs.

The runtime-only ZIP is derived before qualification from the exact full ZIP, retaining identical native library, GDExtension descriptor, GDScript/asset, license/notice and `ci-package-scope.json` bytes. Its `runtime-package.json` (schema `necronomidom.runtime-package/1`) records the source ZIP filename/SHA256 and retained native binary hashes. Source target scope is preserved: partial packages are never promoted to full releases.

Both exact ZIPs must pass the same strict installed-package checks on Linux x64/ARM64, native macOS ARM64/x64 and Windows under Proton before either is released. Windows-Proton is compatibility qualification, not native-Windows runtime proof. Manual partial diagnostics are not releases.

## GDScript

Add a **DocumentHost** node under a `CanvasLayer` and set **Document Path** to your `.html` file. Connect its `clicked(element_id)` signal and update the document with `set_text`, `add_class`, `set_attribute` and the other selector-based methods.

## C# DOM API

Install the **full ZIP** and use Godot .NET. Add this line to your project's `.csproj`, before `</Project>`, and build:

```xml
<Import Project="addons/necronomidom/NecronomiDom.targets" />
```

Attach `res://addons/necronomidom/runtime/DocumentHost.cs` to a `Control` to use the full C# DOM API, CSSOM and typed state bindings.

Managed editor assembly reload withdraws the host's native callbacks and paint
resources before unloading, then reconstructs mounted documents from their
restored `DocumentPath`. This is a document reload, not preservation of in-memory
DOM edits or focus state. Detached hosts reconstruct when they next enter the tree.

Reuse Typed OM transform values for animation. Mutations of a reused transform
slot are revision-tracked, so supported independent 2D transforms update retained
paint and hit geometry without relaying out the document. Relative/font-dependent
transforms still take the full layout path.

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
