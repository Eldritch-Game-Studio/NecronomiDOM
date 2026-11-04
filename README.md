# NecronomiDOM

HTML and CSS user interfaces for Godot 4, from GDScript or C#. Author screens as ordinary `.html` and `.css` files; NecronomiDOM
parses them into a live DOM, runs the CSS cascade, lays the document out and paints it natively through Godot's
`RenderingServer` and `TextServerAdvanced`, with pointer, keyboard, gamepad and screen-reader support.

| Default workbench | Live CSS edit, retained focus | Markup reload, rebound controls |
|---|---|---|
| ![Nightward Guild workbench](docs/images/workbench.png) | ![Accent edit with retained input selection](docs/images/workbench-css.png) | ![Model retained after markup replacement](docs/images/workbench-rebound.png) |

The default C# demo (`demo/workbench.html` + `workbench.css`) rendered by stock Godot 4.7.2 .NET,
Forward+, 1440×1000. The smaller GDScript Grimoire example remains in `demo/gdscript/`.

## Live interfaces, not just menus

![NecronomiDOM rendering scrolling HTML elements, CSS arrow shapes, state feedback and live text updates in Godot](docs/images/rhythmchart.gif)

HTML/CSS can drive continuously changing game interfaces—not just static screens. This recording demonstrates:

- **Live DOM updates:** C# creates elements, updates text and changes styles while the interface is running.
- **Positioned layout:** independently moving elements and variable-height indicators inside a clipped playfield.
- **CSS geometry and styling:** arrow shapes built from polygons and rectangles, 2D transforms, borders and rounded corners.
- **State-driven feedback:** class changes restyle active elements and status text; numeric readouts and a progress bar update alongside them.
- **Native Godot rendering:** every visible UI element is painted through NecronomiDOM, without a browser or native UI widgets.

Captured in Godot 4.7.2 Forward+ using a standalone rhythm-interface adaptation from Kraken's Keg.
Movement and feedback are driven by C# updating the DOM, not CSS keyframe animations.

### Addon capabilities

- **DOM**: elements, attributes, events (`click`, pointer, keyboard, focus, input), `MutationObserver`, custom
  elements, shadow roots in the DOM API, CSSOM and CSS Typed OM from C#; a selector-based API and signals from
  GDScript.
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
- **Accessibility**: the DOM's accessibility tree is exposed through Godot's `AccessibilityServer`, subject to the
  [text and accessibility limits](#text-and-accessibility-limits) below.

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
| Accessibility | Per-control properties | Roles, names and supported states derived from HTML semantics and ARIA |

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

The engine qualification target is **official Godot 4.7.2**. Native desktop distribution targets are
Linux x64/ARM64, Windows x64 and macOS Apple silicon/Intel. Other engine versions and exported games
are not established by these checks.

| | Standard Godot (GDScript) | Godot .NET edition |
|---|---|---|
| Required native release gate / observed .NET scope | Fresh installed ZIP editor import/shutdown and native DOM action on all five desktop RIDs | Linux x64 stock 4.7.2 .NET workbench, bindings, inspector and reload |
| API | `DocumentHost` node, selector methods, signals | The same, plus the full C# DOM, CSSOM and layout API |
| Setup | Copy an addon ZIP containing the library for your desktop architecture | Copy the addon, add one line to the `.csproj` |

The standard-edition build is a GDExtension: the same C# code compiled ahead of time into a native library,
so consumers do not need the .NET runtime or SDK. CI requires all five desktop native libraries and runs the
installed-package standard-Godot acceptance on each native architecture. A configured job is not a passing
result: consult the exact candidate's workflow run before treating it as qualified. Local Linux x64 qualification
passed fresh installed-ZIP import/shutdown, editor quit, the native DOM action and four host lifecycle cycles.
That evidence does not establish the other four targets independently of their CI results.
Web, Android and iOS native exports are unsupported; no desktop or mobile export-template acceptance is claimed
for either API. Godot .NET's own export support is not evidence that this addon has passed those targets.

Native runtime success does not establish editor-import safety. The earlier `v1.1.1` partial artifact reproduced
a cold editor import/shutdown abort ([issue #5](https://github.com/Eldritch-Game-Studio/NecronomiDOM/issues/5)).
Asset Store submission is paused; the fresh-installed-package gate described below must pass before a replacement
is considered qualified.

## Add it to your project

### 1. Get the addon

Use an addon ZIP from a fully passing workflow run (or its gated GitHub release), and copy its
`addons/necronomidom/` folder into your project as `res://addons/necronomidom/`. Restart the editor to load the extension.
Asset Store submission is paused; an existing release download is not evidence of passing the new gate.

The addon ZIP contains the prebuilt native libraries. A plain clone or GitHub source ZIP does not; build them with
`native/build.sh` (see [Building the native libraries](#building-the-native-libraries)).

### 2. GDScript projects: done

Add a **DocumentHost** node (search for it in **Add Node**) under a `CanvasLayer` and set its **Document Path**.

### 3. C# projects: add one line to your `.csproj`

C# projects can use the GDScript API as is. For the C# DOM API, open your project's `.csproj` (next to
`project.godot`) and add the `Import` line before `</Project>`:

```xml
<Project Sdk="Godot.NET.Sdk/4.7.2">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <Import Project="addons/necronomidom/NecronomiDom.targets" />
</Project>
```

Godot gives addons no way to change a project's build, so this one step is manual. The import references the core
library as its own assembly and compiles the Godot glue (`addons/necronomidom/runtime/`) into your game; build with
the editor's **Build** button or `dotnet build`. In C#, attach `res://addons/necronomidom/runtime/DocumentHost.cs` to a
`Control` to get the C# class.

### 4. Recommended setting

Turn off **Project Settings → GUI → Common → Snap Controls to Pixels**
(`gui/common/snap_controls_to_pixels=false`); documents position boxes and glyphs at fractional pixels.

### Updating

Replace `addons/necronomidom/` with the new version and restart the editor (C#: rebuild).

## Use

Put a `DocumentHost` under a `CanvasLayer` and point it at a document:

```
Main (Node)
└── UI (CanvasLayer)
    └── Document (DocumentHost)
        document_path = "res://ui/menu.html"
```

```html
<!doctype html>
<html><head><link rel="stylesheet" href="menu.css"></head>
<body><main><p id="status">Ready.</p><button id="start" autofocus>Start</button></main></body></html>
```

### From GDScript

```gdscript
extends Node

@onready var ui: DocumentHost = $UI/Document

func _ready() -> void:
    ui.clicked.connect(_on_clicked)
    ui.cancel.connect(get_tree().quit)

func _on_clicked(element_id: String) -> void:
    if element_id == "start":
        ui.set_text("#status", "Started.")
```

Elements are addressed by CSS selector (the first match); events report the `id` of the clicked element or its
nearest ancestor with one.

| Member | Purpose |
|---|---|
| `document_path`, `load_document(path)` | Document to show (`res://…`, or `user://ui/…`). |
| `get_text` / `set_text(selector, text)` | Element text content. |
| `get_attribute` / `set_attribute` / `remove_attribute(selector, name, …)` | Attributes. |
| `has_class` / `add_class` / `remove_class` / `toggle_class(selector, class)` | Classes, which drive your CSS. |
| `get_value` / `set_value(selector, value)` | `<input>` / `<textarea>` values. |
| `focus_element(selector)`, `has_element(selector)`, `query_ids(selector)` | Focus, existence, ids of all matches. |
| `listen(type)` + signal `dom_event(type, element_id)` | Any other DOM event, e.g. `"input"`, `"focusin"`. |
| signals `clicked(element_id)`, `cancel()` | Clicks; Escape or the cancel action. |
| `set_input_enabled(bool)`, `set_application_navigation(bool)`, `apply_text_scale(float)` | Input and scaling (see the table below). |
| `font_path`, `font_family`, `font_paths`, `fallback_font_paths` | Fonts (see the table below). |
| `submit_action`, `cancel_action`, `navigate_*_action` | Input actions. |

### From C#

With the `.csproj` import (step 3), attach `DocumentHost.cs` instead of using the native node, and the C# class
exposes the whole DOM:

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

### `DocumentHost` settings (C# names)

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

### Typed C# state bindings

`host.CreateBindings()` creates a `NecronomiDom.Bindings.DocumentBindings` scope for the host's
currently live document. Sources implement ordinary `INotifyPropertyChanged`; getters are typed
delegates, not reflection, markup expressions or scripts. For example, the workbench presenter
binds its view model's text with:

```csharp
var bindings = host.CreateBindings();
bindings.Text("#character-preview", model, nameof(WorkbenchViewModel.CharacterPreview), state => state.CharacterPreview);
```

| Method | One-way target |
|---|---|
| `Text(selector, source, property, getter)` | Literal `textContent`, never HTML or an input's `value`. |
| `Class(selector, className, source, property, getter)` | Presence of one class token; other classes remain intact. |
| `Visible(selector, source, property, getter)` | Inverse `hidden` attribute. Author CSS must honor `[hidden]`, e.g. `[hidden] { display: none !important; }`. |
| `Enabled(selector, source, property, getter)` | Inverse `disabled` attribute on a button, input or textarea. |

Binding applies the initial value immediately. Later, only matching `PropertyChanged` notifications
read that getter (`null`/empty property means all); unchanged values do not rewrite the DOM.
Selectors resolve once to connected elements in this exact document. Missing/detached targets,
invalid tokens and getter failures throw `BindingException` with the source type/property,
target selector/property and original inner failure. Create, notify and dispose on the owning UI
thread; marshal background model notifications before publishing them.

The scope automatically unsubscribes and releases its document references on host withdrawal.
CSS-only reload keeps the scope. Markup reload needs a fresh scope and bindings on the replacement
document. Manual `Dispose()` also unsubscribes; `IsDisposed` and `Disposed` expose that lifetime.
For non-Godot DOM consumers, `new DocumentBindings(document)` is available with caller-owned disposal.

Bindings never write back to models. Keep clicks and edits as explicit application commands, and
detach those handlers as part of the presenter's document lifetime (the scope's `Disposed` event
can own that cleanup). See [`demo/Demo.cs`](demo/Demo.cs) and
[`demo/WorkbenchViewModel.cs`](demo/WorkbenchViewModel.cs): input events explicitly edit the model;
quest/loadout/save clicks remain ordinary handlers. Markup reload detaches/rebinds exactly once.

Run the real-engine binding scenario with .NET Godot:

```sh
godot --headless --path . res://demo/bindings/smoke.tscn
godot --path . res://demo/bindings/smoke.tscn -- --proof-out=/absolute/screenshot-directory
```

It exercises painted text/class/visibility transitions, actual disabled-button input, CSS scope
survival, markup disposal/rebinding and retired-document isolation. The typed API is C# only;
GDScript's existing selector/signal API is unchanged.

## Developer authoring tools

The inspector and file reload are explicit opt-ins, available only in a debug engine/export template.
Ordinary hosts do not allocate an inspector, retain reload source snapshots or poll files.
The default C# workbench exposes them in its authoring toolbar, without requiring `--dev-ui`:
**F6** toggles the inspector, **F7** checks changed files, and **F8** reparses/remounts the document
even if files are unchanged. Its source watcher starts **off**. The smaller GDScript example still
uses `-- --dev-ui` to enable its keyboard authoring controls and automatic source watching.

### Live inspector

![Nightward workbench with its bound quest tracker selected in the live inspector, showing inherited and resolved CSS variables](docs/images/authoring-inspector.png)

The actual workbench's bound `#tracker` element is selected; the **Variables** tab exposes inherited and resolved
custom properties such as `--accent`. Captured in stock Godot 4.7.2 .NET with Vulkan at 1920×1080.

Call `host.OpenInspector()` from C#, or `host.open_inspector()` from GDScript. Use **Pick element**,
the ancestry list, **Parent**, or a CSS selector to select a connected element. The tabs show:

- Logical/visual bounds and HTML start-token provenance where available.
- Matched stylesheet paths, selectors, live CSSOM rules and actual cascade winners.
- Computed values, custom-property inputs/resolution and consumer fallback results.
- Focus/pointer/text-selection state, layout/paint counters and opt-in CPU host timings.

The overlay suspends this host's document input; picking does not click the underlying element.
**Close** or **Escape** restores prior input/focus when still applicable. Applications that poll
global game input directly must separately pause their gameplay controls while a tool is open.
CSSOM does not retain rule/declaration line-column offsets; the inspector says so rather than
inventing locations. Highlights are visual AABBs, not clipped polygons. Timings exclude inspector
work and are neither GPU timings nor whole-frame measurements.

Additional C# APIs: `AttachInspector()`, `ToggleInspector()`, `BeginInspectorPick()`,
`SelectInspectorElement(selector)`, `GetInspectorReport()`, `InspectorOpen`, `InspectorSelection`.
GDScript equivalents use snake_case; state getters are `is_inspector_open()` and
`get_inspector_selection()`. `pick_inspector_element(x, y)` takes document-local CSS pixels.

### File reload and document ownership

Set `DeveloperReloadEnabled = true` (`developer_reload_enabled` in GDScript) to poll the current
HTML and its linked stylesheets every 0.5 seconds. Content comparison catches same-timestamp edits.
Enabling on an existing document establishes a baseline from current disk contents without changing
that document; edits made before enabling require `ForceReload()`. Disabling releases the baseline.

For manual-only reload, set `DeveloperReloadWatchEnabled = false`
(`developer_reload_watch_enabled` in GDScript) **before** enabling `DeveloperReloadEnabled`.
This retains the baseline and manual commands without polling. Toggling the watch property does
not clear the baseline or enable reload by itself; its default is `true` to preserve existing
opted-in hosts. The workbench's **Watch sources: OFF/ON** button is the explicit automatic-watch opt-in.

`ReloadChangedFiles()` preserves the exact DOM, input values/selections, focus, listeners and scroll
state for CSS-only edits; changed geometry can naturally clamp scroll offsets. It validates candidate
layout/native paint in an isolated snapshot using the current media environment before publishing
styles. Invalid CSS/resource edits report failure and leave the current UI intact during preparation.
Developer operations require a mounted host with a nonzero viewport, and use bounded local file reads.
`@import` remains outside the runtime profile; reload does not silently enable unsupported CSS.

Changed HTML or `ForceReload()` withdraws the old document and publishes a replacement. Discard old
element handles and bind the new document explicitly:

```csharp
host.ReloadCompleted += result =>
{
    if (result.DocumentReplaced) BindDocument();
};
host.DeveloperReloadWatchEnabled = false; // manual commands only; enable watching separately
host.DeveloperReloadEnabled = true;
```

GDScript methods `reload_changed_files()` / `force_reload()` return the kind, and signal
`developer_reloaded(kind, document_replaced, error)` supplies the details. Kinds:
Disabled=0, Unchanged=1, Styles=2, Document=3, Failed=4. Automatic idle checks are silent.
Preparation is isolated; application callback exceptions or failures during actual mounting are
not a transactional rollback of arbitrary application state.

### Generated capability reference

[Renderable capabilities](docs/capabilities.md) distinguish parser acceptance from actual
layout/paint/input admission and context-dependent restrictions. Runtime validation and the generator
share the same admission manifest; named native APIs/signals come from their binding annotations.

```sh
python3 scripts/generate-capabilities.py          # regenerate reference and editor data
python3 scripts/generate-capabilities.py --check  # CI: fail on stale/missing generated output
```

`docs/generated/necronomidom.css-data.json` and `necronomidom.html-data.json` are VS Code custom-data
files. Add them to `css.customData` / `html.customData` in workspace settings. They supplement
completion/documentation; they do not suppress every unsupported browser suggestion.

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

## Text and accessibility limits

Text uses stock Godot's standard shaping precision; higher-precision, unhinted metrics are not promised.
Screen-reader support is bounded by stock Godot: mixed checkbox states and `aria-invalid` states are not
reported to assistive technology. General screen-reader support does not imply those two states are conveyed.

## Demo and tests

The repository root opens the .NET **Nightward Guild UI workbench**, `demo/demo.tscn`.
The independent, minimal GDScript Grimoire remains at `demo/gdscript/demo.tscn` (any Godot edition,
needs the native library) and retains its original `grimoire.html` / `grimoire.css` sources.

```sh
native/build.sh                                      # native library for this machine
godot --path . res://demo/gdscript/demo.tscn        # GDScript demo (add `-- --smoke` for a check run)
dotnet build NecronomiDOM.Demo.csproj
godot --path .                                       # default C# workbench (.NET edition)
godot --headless --path . -- --smoke                 # default-demo acceptance: exit 0 on success
godot --headless --path . -- --workbench-smoke       # full workbench + reload/input regression
dotnet test tests/NecronomiDom.Tests                 # core library tests (no engine required)
```

### Guided workbench exercise

1. Edit the adventurer name/motto and equip the lantern. The preview is typed `Text` binding;
   the unsaved-card and equipped-loadout styles are `Class` bindings. The lantern hint is a
   `Visible` binding and Save profile is `Enabled` only for a nonblank, changed profile.
2. Accept a quest. Its tracker becomes visible, other accept buttons disable, and Record finding
   advances the local model. After three findings, collection enables; collect 12 supplies once.
   These are explicitly simulated, self-contained demo contracts, not game integration or persistence.
3. Change `--accent: #b9e38c` to `--accent: #eebd78` in `demo/workbench.css` and save. With the
   name field focused/selected, press **F7**. The toolbar reports **CSS applied**; value, selection,
   focus, DOM identity and bindings stay live. Watching is optional, never automatic by default.
4. Edit a guide heading in `demo/workbench.html`, retaining all control IDs, and press **F8**.
   The toolbar reports **Markup replaced**. The C# model (including unsaved form drafts, lantern,
   quest progress and earned supplies) survives; new inputs are restored and event handlers rebound.
   Markup replacement does **not** promise focus/selection preservation. Continue typing/recording.
5. Temporarily add `body { display: grid; }` to the CSS. **F7** rejects the unsupported layout,
   shows the runtime error and recovery instruction, and leaves the old working UI intact. Remove
   the rule and reload. Application binding callbacks are not transactional: deleting required
   control IDs is an application error, not a supported way to redesign the presenter.
6. **Inspect / F6** opens the real inspector; select `#tracker` to inspect bound content.
   **Reference + editor setup** exposes the generated capability-reference opener and displays
   VS Code `css.customData` / `html.customData` settings. File openers use the OS-associated editor;
   open the displayed repository paths manually if no association is configured.

The HTML uses admitted `div`/`section`/`p` elements with ARIA names/heading roles, not unsupported
browser elements. There is no JavaScript runtime, template engine, automatic two-way binding or
game-framework dependency. Inspector/reload stay guarded by a debug engine/template; their toolbar
actions are disabled in release templates. Model state is session-local, not saved to disk.

`--workbench-smoke` drives the **actual default document** through Godot mouse/keyboard input.
It copies HTML/CSS into an isolated temporary `user://ui/` directory for edits and removes it on
exit. It checks native paint, typed text/class/visibility/enabled state, CSS focus/selection retention,
forced markup rebinding, failed reload survival, once-only rewards, explicit watcher opt-in/off,
and inspector/reference access. The headless dummy window uses a 1440×1000 logical viewport;
this is behavioral/layout coverage, not rendered screenshot evidence.

For actual rendered evidence, run .NET Godot on an isolated display (or normally on your own desktop):

```sh
godot --path . --rendering-method forward_plus --resolution 1440x1000 -- \\
  --workbench-smoke --proof-out=/absolute/screenshot-directory
```

Seven viewport captures cover initial UI, bound state, CSS focus retention, rebound markup, rejected
reload, editor-reference access and the inspector. Verification for this workbench used stock Godot
4.7.2 .NET, Vulkan/Forward+ on AMD RX 7900 XTX under isolated headless gamescope, with all seven
screenshots inspected. No desktop window or software-rendering substitution was used.
The negative CSS check intentionally logs a `display: grid` reload error; it must still finish
`WORKBENCH_SMOKE_RESULT PASS`. OS external-editor launching and release-template export are not
exercised by this regression.

![Rejected CSS reload leaves the completed tracker and actions working](docs/images/workbench-rejected.png)


Authoring-tool scenarios use isolated temporary `user://ui/` files and never modify the demo sources:

```sh
godot --headless --path . res://demo/authoring/native-smoke.tscn  # standard Godot + built extension
godot --headless --path . res://demo/authoring/smoke.tscn         # .NET Godot
godot --path . res://demo/authoring/smoke.tscn -- --proof-out=/absolute/screenshot-directory
```

The .NET scenario intentionally attempts unsupported CSS and expects a failure diagnostic while the
previous UI remains usable. It then exercises actual mouse picking, stylesheet/markup reload and
rebinding. The native scenario additionally withdraws one of two hosts and updates the survivor.


## Building the native libraries

`native/build.sh [RID…]` builds the GDExtension library for this machine (or the given runtime identifiers:
`linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64`, `osx-x64`) into `addons/necronomidom/bin/` and writes
`addons/necronomidom/necronomidom.gdextension`. It needs the .NET 10 SDK and the C toolchain NativeAOT uses (clang on
Linux, Xcode on macOS, Visual Studio build tools on Windows), and fetches and builds the pinned
[Godot GDExtension .NET bindings](https://github.com/godotengine/godot-dotnet), which are not on NuGet yet. NativeAOT
only compiles for the OS it runs on. Full CI builds all five desktop RIDs on matching host architectures:
Linux x64/ARM64, Windows x64, macOS ARM64 and macOS x64 (the latter uses the `macos-15-intel` runner).

## CI qualification and publication status

**Asset Store submission and review are paused.** Existing tag-driven GitHub release automation remains enabled:
`v*` tag pushes run the same mandatory tests, source smoke and fresh-installed-package qualification before the
qualified ZIP can be attached to a GitHub release. The workflow does not submit an Asset Store review.

`.github/workflows/ci.yml` runs core tests and the generated capability-reference freshness/regression checks,
builds all five desktop native targets, and requires the existing source smoke suite to succeed. Both standard
and .NET editor imports are hard failures, including abnormal process shutdown. The .NET demo, full workbench,
inspector/state-preserving reload and typed-binding smokes remain required.

Only then does packaging assemble the full addon ZIP. Before retaining/uploading the candidate artifact, CI runs
`scripts/qualify-stock-package.py` with official standard Godot 4.7.2 on Linux x64. This extracts the ZIP into a new
temporary project, checks that every descriptor library exists, and copies only the existing GDScript Grimoire
and `native/tests/lifecycle.gd` / `lifecycle.html` source fixtures (not the source addon, C# build outputs or import
cache). A cold headless editor import must exit cleanly. A subsequent
`--editor res://demo/gdscript/demo.tscn --quit` must also open the actual native-host scene and shut down cleanly,
not merely open a blank editor.
The native Grimoire runtime must execute the real Summon DOM click and report
`count="One shape stirs beneath the page."`; a zero exit code alone is insufficient.
The lifecycle script must also exit cleanly and print exactly
`native-lifecycle: cycles=4 reloads=4 withdrawn=3 final-host-mounted=true`.
It exercises real DOM actions, inspector opening, four document reloads and three host withdrawals, leaving
the final host/inspector mounted so process shutdown must withdraw live resources.

The hosted qualification matrix downloads that **same final ZIP** and repeats the fresh install/import/editor,
DOM and lifecycle checks using standard Godot on Linux ARM64, Windows x64, macOS ARM64 and macOS Intel. Both macOS runners execute
the universal engine natively. A separate release job requires the complete matrix to succeed; candidate artifact
availability alone does not mean the workflow or all-platform qualification passed.

Run the same installed-package acceptance locally, without rebuilding:

```sh
python3 scripts/qualify-stock-package.py \
  --godot /path/to/Godot_v4.7.2-stable_linux.x86_64 \
  --package /path/to/NecronomiDOM-candidate.zip
```

This headless import/runtime gate does not establish rendered screenshot, screen-reader or export-template
acceptance. Those require their own evidence; the stock .NET workbench rendering evidence above has a separate scope.

Repository variable `LINUX_RUNNER=self-hosted` selects a Linux x64 runner labelled `necronomidom` for tests,
Linux x64 build, source smoke and packaging; otherwise those jobs use GitHub-hosted Ubuntu.

Full-distribution builds cannot skip hosted platforms through a repository variable. `HOSTED_PLATFORMS` no longer
controls the workflow. Only an explicit manual `workflow_dispatch` with `partial=true` skips the hosted builds
and their qualification matrix to produce a Linux-only diagnostic ZIP. It cannot reach the tag-only release job.

Diagnostic partial ZIPs list only native libraries actually included in their `.gdextension` descriptor and use
the `-partial.zip` suffix. They are not all-platform distributions. Missing platforms are not advertised; library
inclusion alone is not runtime evidence. Full packaging fails if any of the five required libraries is absent.
Fresh-install qualification rejects descriptors referencing missing files in either full or partial ZIPs.

On a self-hosted runner, intermediate Linux x64 binaries pass between jobs through the runner's local cache.
The candidate ZIP is uploaded to the workflow run's **Artifacts** section and retained in
`~/.cache/necronomidom-ci/dist/` after Linux x64 installed-package qualification; full qualification additionally
requires the hosted matrix described above.

To run the same workflow without GitHub (Docker and [act](https://nektosact.com) required):

```sh
scripts/ci-local.sh        # Linux x64 build, tests, source smoke and fresh-installed ZIP qualification
```

Local runs explicitly request the manual diagnostic partial mode: act has no Windows, macOS or ARM runners,
so that ZIP holds only the Linux x64 library. They do not satisfy the all-platform release gate.

Asset Store submissions remain manual and are currently paused. GitHub releases remain tag-driven and gated as above.

The runtime sources are shared: `native/` compiles `addons/necronomidom/runtime/` against the bindings with
`NECRONOMIDOM_GDEXTENSION` defined, and adds the GDScript API (`native/src/`).

## Layout of this repository

| Path | Contents |
|---|---|
| `addons/necronomidom/core/` | Engine-independent library: URL policy, DOM, HTML import, CSS, cascade, layout, display list. |
| `addons/necronomidom/runtime/` | Godot glue: `DocumentHost`, text shaping, paint, input, accessibility. |
| `addons/necronomidom/shaders/` | Canvas shaders used by the painter. |
| `addons/necronomidom/fonts/` | Default font (Noto Sans). |
| `addons/necronomidom/vendor/AngleSharp/` | Patched AngleSharp 1.8.4 HTML parser (see `patches/`). |
| `demo/` | Example document with C# and GDScript scenes. |
| `native/` | GDExtension build for standard Godot: project, GDScript API, build script. |
| `tests/` | NUnit tests for the core library. |

## License

MIT, see `LICENSE`. Third-party components and their licenses are listed in `THIRD_PARTY_NOTICES.md`.
