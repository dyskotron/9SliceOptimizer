# 9-Slice Optimizer

The reverse of what a 9-slice renderer does. A 9-sliced sprite stretches its middle column (between the left and right borders) and its middle row (between the bottom and top borders), so whatever those stretch zones contain only matters as an average: the tool shrinks them to a few pixels and keeps the four corners pixel for pixel. The border values don't change, so the optimized sprite renders exactly like the source wherever the zones are uniform along the stretch axis (a gradient *across* an edge survives; detail *along* it is averaged away).

A 497 × 71 button body whose middle is one flat colour becomes, say, 120 × 71: the same look on screen, a fraction of the texture memory and atlas space.

## Installation

Add to your Unity project via the Package Manager using the git URL:

```
https://github.com/dyskotron/9SliceOptimizer.git?path=Packages/com.dyskotron.nineslice-optimizer
```

Or in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.dyskotron.nineslice-optimizer": "https://github.com/dyskotron/9SliceOptimizer.git?path=Packages/com.dyskotron.nineslice-optimizer"
  }
}
```

Append `#v1.0.0` to the URL to pin a release. Requires Unity 2021.3 or newer and the 2D Sprite package (installed as a dependency).

## Use

1. Set the sprite's border in the Sprite Editor (Single or Multiple sprite mode, Sprite (2D and UI) texture type).
2. Right-click the texture or sprite in the Project window: **9-Slice › Optimize 9-Slice…** (or *Tools › UI › 9-Slice Optimizer* and pick the asset in the dialog).
3. The dialog shows the sprite with its 9-slice lines, the parameters and a live preview of the result:
   - **Center Width / Center Height**: how many pixels the stretched column and row keep (so the top/bottom edges are *Center Width* wide and the left/right edges *Center Height* high). Never grown: a zone smaller than the request keeps its size. An axis with no border on either side isn't sliced and is kept whole: a left/right-only (3-slice) button keeps its full height, a top/bottom-only one its full width.
   - **Border**: read from the sprite; edit it here to override (the source isn't changed by that, only the output's border).
4. **Save As New…** writes a PNG next to the source (`{name}_9s.png` suggested) as a Single sprite with the same border, pivot and import settings (mesh type FullRect), or **Replace Source** overwrites the source PNG and sets its border; in Multiple mode with one sprite the sprite's rect is updated and its name and ID kept, so references to it keep working. Replace needs a PNG source with a single sprite; a sheet with several sprites is cropped to the chosen sprite and can only be saved as new.

Pixels come from the file itself (PNG/JPG), so the import's compression and Max Size don't matter; other formats fall back to the imported texture. Each output pixel is the box-filtered average of the source span it covers, colour alpha-weighted (transparent pixels don't darken edges).

## Code

- `NineSliceShrink` — the pure maths (plan per axis, spans, averaging), tested in `Tests/Editor/NineSliceShrinkTests.cs`.
- `NineSliceOptimizer` — the asset side: loads a texture/sprite (rect and border through `ISpriteEditorDataProvider` for sprite sheets), writes the PNG and configures its importer.
- `NineSliceOptimizerWindow` — the dialog and the menu items.

Assembly `Dyskotron.NineSlice.Editor` (Editor only); tests in `Dyskotron.NineSlice.Editor.Tests` (EditMode; list the package under `testables` in your manifest to see them in the Test Runner).

## License

MIT
