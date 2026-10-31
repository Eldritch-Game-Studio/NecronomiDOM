# Local AngleSharp patches

This directory's parent is AngleSharp 1.8.4 (upstream commit `32981ded5b0c78c4a5afcfbb677ffb329078fdf8`), trimmed to
the production library (`src/AngleSharp/**`, build properties, signing key, license, readme, changelog and logo).

`fragment-context.patch` is a cumulative unified diff against that upstream commit, already applied to the sources
here. It fixes HTML tree construction in eight parser files:

- Fragment parsing keeps the context element's namespace: `<g>` parsed in an SVG context is SVG, `<foo>` inside MathML
  `annotation-xml` is MathML unless the `encoding` attribute makes it an HTML integration point.
- Tree-construction algorithms updated to the current HTML Standard for context attributes, quirks mode and form
  owner inheritance.

Scripting is always parsed with the scripting flag off; NecronomiDOM never executes scripts.
