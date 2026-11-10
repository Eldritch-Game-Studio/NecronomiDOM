# Local AngleSharp patches

This directory's parent is AngleSharp 1.8.4 (upstream commit `32981ded5b0c78c4a5afcfbb677ffb329078fdf8`), trimmed to
the production library (`src/AngleSharp/**`, build properties, signing key, license, readme, changelog and logo). The project builds only `net8.0` (upstream also builds
netstandard2.0, net10.0 and .NET Framework) and compiles as C# 12, so it needs nothing beyond the .NET 8 SDK.

`fragment-context.patch` is a cumulative unified diff against that upstream commit, already applied to the sources
here. It fixes HTML tree construction in eight parser files:

- Fragment parsing keeps the context element's namespace: `<g>` parsed in an SVG context is SVG, `<foo>` inside MathML
  `annotation-xml` is MathML unless the `encoding` attribute makes it an HTML integration point.
- Tree-construction algorithms updated to the current HTML Standard for context attributes, quirks mode and form
  owner inheritance.

Scripting is always parsed with the scripting flag off; NecronomiDOM never executes scripts.

Encoding integration is also patched directly in the vendored sources:
`EncodingMetaHandler` does not register a process-wide code-page provider;
`TextEncoding` uses the provider locally so collectible hosts do not acquire a
global provider root. The WHATWG replacement labels (including `iso-2022-cn`)
decode a nonempty stream to one U+FFFD through `ReplacementEncoding`, rather than
probing unsupported platform encodings or silently treating them as UTF-8.
`FormDataSet` normalizes replacement output encoding to UTF-8, as the standard
requires. Regression coverage lives in `TextEncodingTests`.
