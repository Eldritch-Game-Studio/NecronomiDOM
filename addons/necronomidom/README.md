# NecronomiDOM

HTML and CSS user interfaces for Godot 4, from GDScript or C#. Full documentation, screenshots and demos:
https://github.com/Eldritch-Game-Studio/NecronomiDOM

## Setup

- **GDScript (official Godot 4.7.2, standard or .NET)**: restart the editor after installing, then add a **DocumentHost** node
  under a `CanvasLayer` and set **Document Path** to your `.html` file. Connect its `clicked(element_id)` signal and
  change the page with `set_text`, `add_class`, `set_attribute` and friends.
- **C# DOM API (.NET edition)**: also add this line to your project's `.csproj`, before `</Project>`, and build:

  ```xml
  <Import Project="addons/necronomidom/NecronomiDom.targets" />
  ```

Recommended: turn off **Project Settings → GUI → Common → Snap Controls to Pixels**.

## Compatibility and qualification

Native desktop distribution targets are Linux x64/ARM64, Windows x64 and macOS Apple silicon/Intel.
The release gate requires all five libraries and fresh-installed-ZIP stock Godot 4.7.2 import/editor shutdown,
native DOM output and mount/reload/withdrawal/final-shutdown checks on matching architectures.
A configured matrix is not evidence of a pass: check the exact candidate's full workflow result.
Local Linux x64 qualification passed fresh installed-ZIP import/shutdown, editor quit, native DOM output,
and four host lifecycle cycles; that does not establish the other four targets.
Stock Godot 4.7.2 .NET workbench, bindings, inspector and reload evidence is Linux x64 only.
Other engine versions and exported games are not qualified; Web, Android and iOS native exports are unsupported.

CI generates the portable AnyCPU managed SDK package feed once on Linux, validates the same pinned source/patch
identity on every build host, and runs NativeAOT separately on each target OS/architecture. Sharing that managed
feed is not native cross-compilation and does not replace any target's installed-package runtime checks.

Native runtime success alone does not establish cold editor-import/shutdown safety. The earlier `v1.1.1`
partial artifact reproduced an import/shutdown abort (repository issue #5). Asset Store submission and review are paused.
CI requires clean fresh-installed-ZIP import and editor shutdown, a real native DOM action/output check, and the
four-cycle host lifecycle regression on Linux x64 before uploading a candidate ZIP; all four other desktop
architecture checks must also pass before a tag-driven GitHub release.
Only explicit manual/local diagnostic runs can produce partial ZIPs; those are not all-platform distributions
and advertise only included native libraries. Inclusion is not runtime verification.

Text uses stock Godot's standard shaping precision, not guaranteed higher-precision unhinted metrics.
Mixed checkbox and `aria-invalid` states are not reported to screen readers on stock Godot.

MIT licensed (see `LICENSE`). Bundled third-party components: AngleSharp (MIT, `vendor/AngleSharp/LICENSE`),
Noto Sans (SIL OFL 1.1, `fonts/OFL.txt`), W3C webref data (MIT), Unicode bidi data (Unicode License v3,
`core/Css/Data/Unicode/LICENSE.txt`). The native libraries in `bin/` are built with the Godot GDExtension .NET
bindings (MIT) and the .NET runtime (MIT).
