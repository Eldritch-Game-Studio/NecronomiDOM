# NecronomiDOM

HTML and CSS user interfaces for Godot 4 (C#). Author screens as ordinary `.html` and `.css` files; NecronomiDOM
parses them into a live DOM, runs the CSS cascade, lays the document out and paints it natively through Godot's
`RenderingServer` and `TextServerAdvanced`, with pointer, keyboard, gamepad and screen-reader support.

| Rendered | After clicks and typing | Gamepad navigation |
|---|---|---|
| ![Demo document](docs/images/demo.png) | ![Input editing](docs/images/demo-input.png) | ![Focus navigation](docs/images/demo-navigation.png) |

The demo (`demo/grimoire.html` + `grimoire.css`) rendered by stock Godot 4.7.2, Forward+, 1280×720.

- **DOM**: elements, attributes, events (`click`, pointer, keyboard, focus, input), `MutationObserver`, custom
  elements, shadow roots in the DOM API, CSSOM and CSS Typed OM, all from C#.
- **HTML**: standards-based parsing (a patched copy of [AngleSharp](https://github.com/AngleSharp/AngleSharp)); no
  scripting, no network.
- **CSS**: selectors (including `:is`, `:where`, `:not`, `:has`, `:focus-visible`, `:hover`), custom properties,
  `@media`, `@font-face`, nesting, computed values from the W3C property grammars.
- **Layout**: block/flow-root, relative and absolute positioning, flexbox (row/column, wrapping rows), images,
  inline text with wrapping and ellipsis, overflow scrolling with scrollbars, 2D transforms.
- **Paint**: backgrounds, gradients, background images, borders, rounded corners, `clip-path`, opacity, text strokes
  and shadows, transitions of opacity, colors, width and scale.
- **Input**: mouse and pointer capture, keyboard focus and Tab order, gamepad/arrow navigation between focusable
  elements, `<input>`/`<textarea>` editing with caret and selection.
- **Accessibility**: the DOM's accessibility tree is exposed through Godot's `AccessibilityServer`.

## Why not Godot's built-in UI?

Godot's `Control` nodes and `Theme` resources work well for editor-style tools. Game interfaces built with them tend to
end up as deep scene trees, `StyleBoxFlat` resources and theme overrides spread across the inspector, and layout code
in `_Ready`. NecronomiDOM moves that work into the format most UI people already use.

| | Godot `Control` + `Theme` | NecronomiDOM |
|---|---|---|
| Authoring | Nodes edited in the inspector, saved as `.tscn`/`.tres` | Plain `.html` + `.css` in any editor |
| Styling a state (hover, focus, disabled) | Separate `StyleBox` per state per control type, or overrides on each node | One selector: `button:focus { border-color: … }` |
| Reuse and theming | Theme inheritance and type variations | Cascade, classes, custom properties (`--accent`), `@media` |
| Layout | Containers nested per axis, plus anchors and size flags | Flexbox, padding, positioning, percentages |
| Who can work on it | Godot users | Anyone who knows HTML and CSS, including web designers |
| Reviewing changes | Diffs of serialized resources with generated IDs | Readable markup and stylesheet diffs |
| Dynamic content | Instance and free nodes, re-apply overrides | Change the DOM (`TextContent`, `SetAttribute`, `classList`) and the layout updates |
| Gamepad and keyboard | Configure each control's focus neighbors | Tab order and directional navigation come from the document |
| Accessibility | Per-control properties | Roles, names and states derived from HTML semantics and ARIA |

Compare a styled, focus-aware button. With built-in nodes you create a `Button`, then a `StyleBoxFlat` for each of
normal, hover, pressed and focus, set colors, borders and corner radii on each, and assign them as theme overrides.
In NecronomiDOM:

```css
button { padding: 8px 12px; border: 2px solid #4a7a60; border-radius: 8px; background-color: #22342c; }
button:hover { background-color: #304a3e; }
button:focus { border-color: #96dcaa; }
```

That rule then applies to every button in every document.

**When to stay with built-in `Control`s:** editor plugins, UI that needs widgets outside the supported profile
(trees, item lists, rich text, color pickers), or projects not using C#. NecronomiDOM supports a defined subset of
HTML and CSS (see [Supported profile](#supported-profile)) and fails loudly on anything outside it, so you find gaps
while authoring instead of in a shipped build.

## Requirements

- Godot 4.7 or newer, **.NET edition** (NecronomiDOM is C#).
- Nothing else to install: it targets `net8.0`, the default of Godot C# projects, and needs no engine
  modules.

## Add it to your project

### From the Godot Asset Store

Search for **NecronomiDOM** in the editor's **AssetLib** tab (or on [store.godotengine.org](https://store.godotengine.org/))
and install it; it lands in `res://addons/necronomidom/`. Then do steps 1 and 3–6 below.

### Step by step

### 1. Make sure the project uses C#

If your project has no `.csproj` yet, open it in Godot and choose **Project → Tools → C# → Create C# solution**.

### 2. Copy the addon

Copy the `addons/necronomidom/` folder from this repository into your project, so you have
`res://addons/necronomidom/`. Either:

- download the repository ZIP from GitHub and copy the folder out of it, or
- clone it and copy the folder:

  ```sh
  git clone --depth 1 https://github.com/Eldritch-Game-Studio/NecronomiDOM.git
  cp -r NecronomiDOM/addons/necronomidom your-project/addons/
  ```

You only need `addons/necronomidom/`; the demo, tests and docs stay behind.

### 3. Add one line to your `.csproj`

Open your project's `.csproj` (next to `project.godot`) and add the `Import` line before `</Project>`:

```xml
<Project Sdk="Godot.NET.Sdk/4.7.2">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <Import Project="addons/necronomidom/NecronomiDom.targets" />
</Project>
```

This is the only manual step; Godot gives addons no way to change a project's build. The import references the core
library as its own assembly and compiles the Godot glue (`addons/necronomidom/runtime/`) into your game.

### 4. Build

Press **Build** in the Godot editor (top right), or run `dotnet build` in the project folder.

### 5. Add a document

Create `res://ui/menu.html` and `res://ui/menu.css` (see [Use](#use) below for an example). Then, in a scene:

1. Add a **CanvasLayer**.
2. Under it, add a **Control** and attach the script `res://addons/necronomidom/runtime/DocumentHost.cs`.
3. In the inspector, set **Document Path** to `res://ui/menu.html`.

Run the scene; the document fills the window.

### 6. Recommended setting

Turn off **Project Settings → GUI → Common → Snap Controls to Pixels**
(`gui/common/snap_controls_to_pixels=false`); documents position boxes and glyphs at fractional pixels.

### Updating

Replace `addons/necronomidom/` with the new version and rebuild.

## Use

Add a `DocumentHost` (a `Control`) under a `CanvasLayer` and point it at a document:

```
Main (Node)
└── UI (CanvasLayer)
    └── Document (Control, script: res://addons/necronomidom/runtime/DocumentHost.cs)
        DocumentPath = "res://ui/menu.html"
```

```html
<!doctype html>
<html><head><link rel="stylesheet" href="menu.css"></head>
<body><main><p id="status">Ready.</p><button id="start" autofocus>Start</button></main></body></html>
```

Wire behavior in C#:

```csharp
using NecronomiDom.Dom;
using NecronomiDom.Runtime;

public partial class Main : Godot.Node
{
    public override void _Ready()
    {
        var host = GetNode<DocumentHost>("UI/Document");
        host.Document.GetElementById("start")!.AddEventListener("click",
            new DelegateEventListener(_ => host.Document.GetElementById("status")!.TextContent = "Started."));
        host.CancelRequested += () => GetTree().Quit();
    }
}
```

A host under a `CanvasLayer` fills the viewport; a host under another `Control` fills that parent. Call
`LoadDocument(path)` to switch documents. DOM changes made from C# re-layout on the next frame (or immediately via
`FlushPendingLayout()`).

### `DocumentHost` settings

| Member | Default | Purpose |
|---|---|---|
| `DocumentPath` | `""` | Document loaded on ready (`res://…`, or `user://ui/…`). |
| `FontFamily` / `FontPath` | `"Noto Sans"` / bundled `fonts/NotoSans-Regular.otf` | Family available to `font-family`. |
| `FontPaths` | empty | More family → font file mappings (`@font-face` in stylesheets also works). |
| `FallbackFontPaths` | empty | Ordered fonts searched for characters a family lacks (CJK, symbols). |
| `SubmitAction`, `CancelAction` | `ui_accept`, `ui_cancel` | Gamepad confirm / cancel actions. |
| `NavigateLeftAction` … `NavigateDownAction` | `ui_left` … `ui_down` | Directional focus navigation. |
| `ApplicationNavigation` | `false` | Your code owns directional navigation; the document does not move focus. |
| `InputEnabled` | `true` | Suspend input (e.g. while a modal covers the host); also releases focus and pointer capture. |
| `CancelRequested` | event | Escape or `CancelAction` pressed while the host accepts input. |
| `ApplyTextScale(float)` | `1` | Scale the whole document for a text-size setting; `TextScaleGroup` reaches every host. |
| `DocumentLive` / `DocumentWithdrawing` | static events | Observe every live document (e.g. global click sounds). |

Stylesheet extensions: `--necronomidom-selection-color` (text selection highlight), `--necronomidom-scrollbar-size`,
`-inset`, `-radius`, `-track`, `-thumb`, `-thumb-hover` (scrollbar styling), and the application media features
`-necronomidom-input` (default `keyboard`) and `-necronomidom-platform` (default `desktop`). Change them by assigning
a new environment, which re-cascades the document:

```csharp
var media = new NecronomiDom.Css.Media.MediaEnvironment();
media.Features["-necronomidom-input"] = "gamepad";   // matches @media (-necronomidom-input: gamepad)
host.Styles.Media = media;
```

## Supported profile

NecronomiDOM implements a deliberate subset of the web platform and refuses the rest loudly: content outside the
profile throws `NotSupportedException` naming the element, property or rule, instead of rendering something
approximate.

- Elements: `html`, `body`, `div`, `main`, `section`, `p`, `span`, `button`, `a`, `img` (PNG/JPEG/WebP),
  `input` (text types), `textarea`.
- Stylesheets: `<style>`, `<link rel="stylesheet">`, style rules, `@media`, `@font-face`, nesting.
- Text: left-to-right, integer `px` font sizes, regular faces with synthesized bold/italic.
- Not supported: scripts, network URLs, `float`, grid, tables, `position: fixed/sticky`, `z-index`, column-wrapped
  flex, 3D transforms, vertical margins in normal flow (use padding or flex), pseudo-elements.

Because block margins do not collapse, the browser default margin on `<p>` is rejected too. Start every stylesheet
with a reset:

```css
html, body, main, section, div, p, button, input, textarea { margin: 0; box-sizing: border-box; }
```

Resource URLs resolve inside `res://` (and `user://ui/`); anything else is rejected.

## Engine extensions

Two capabilities use engine APIs that stock Godot 4.7 does not expose. NecronomiDOM detects them at runtime:

- `FontFile.high_precision_shaping` (unhinted high-precision metrics). Without it, text metrics use Godot's standard
  shaping precision.
- `AccessibilityServer.update_set_checked_mixed` / `update_set_invalid_state`. Without them, mixed checkbox and
  `aria-invalid` states are not reported to screen readers.

## Demo and tests

The repository root is a Godot project with a demo (`demo/demo.tscn`):

```sh
dotnet build NecronomiDOM.Demo.csproj
godot --path .                          # run the demo
godot --headless --path . -- --smoke    # lay out, click a button, print a summary, exit 0 on success
dotnet test tests/NecronomiDom.Tests    # core library tests (no engine required)
```

## Layout of this repository

| Path | Contents |
|---|---|
| `addons/necronomidom/core/` | Engine-independent library: URL policy, DOM, HTML import, CSS, cascade, layout, display list. |
| `addons/necronomidom/runtime/` | Godot glue: `DocumentHost`, text shaping, paint, input, accessibility. |
| `addons/necronomidom/shaders/` | Canvas shaders used by the painter. |
| `addons/necronomidom/fonts/` | Default font (Noto Sans). |
| `addons/necronomidom/vendor/AngleSharp/` | Patched AngleSharp 1.8.4 HTML parser (see `patches/`). |
| `demo/` | Example scene, document and C# script. |
| `tests/` | NUnit tests for the core library. |

## License

MIT, see `LICENSE`. Third-party components and their licenses are listed in `THIRD_PARTY_NOTICES.md`.
