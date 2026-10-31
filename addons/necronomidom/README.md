# NecronomiDOM

HTML and CSS user interfaces for Godot 4 (C#). Full documentation, screenshots and a demo:
https://github.com/Eldritch-Game-Studio/NecronomiDOM

## Setup

1. Requires the Godot 4.7+ .NET edition. If the project has no `.csproj`, use
   **Project → Tools → C# → Create C# solution**.
2. Add this line to your project's `.csproj`, before `</Project>`:

   ```xml
   <Import Project="addons/necronomidom/NecronomiDom.targets" />
   ```

3. Build (**Build** button in the editor, or `dotnet build`).
4. Add a `Control` under a `CanvasLayer`, attach `res://addons/necronomidom/runtime/DocumentHost.cs`, and set
   **Document Path** to your `.html` file.

Recommended: turn off **Project Settings → GUI → Common → Snap Controls to Pixels**.

MIT licensed (see `LICENSE`). Bundled third-party components: AngleSharp (MIT, `vendor/AngleSharp/LICENSE`),
Noto Sans (SIL OFL 1.1, `fonts/OFL.txt`), W3C webref data (MIT), Unicode bidi data (Unicode License v3,
`core/Css/Data/Unicode/LICENSE.txt`).
