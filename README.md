# 9-Slice Optimizer

A Unity editor tool that shrinks a 9-sliced sprite's stretch zones to a few pixels, keeping its border and corners: the reverse of what a 9-slice renderer does, so the texture only holds what is actually shown.

The package lives in [`Packages/com.dyskotron.nineslice-optimizer`](Packages/com.dyskotron.nineslice-optimizer) (its [README](Packages/com.dyskotron.nineslice-optimizer/README.md) has the usage); the rest of this repository is the Unity project it is developed and tested in, with a sample sprite in `Assets/Samples`.

## Installation

### Via Unity Package Manager (Git URL)

1. Open Unity and go to **Window > Package Manager**
2. Click the **+** button in the top-left corner
3. Select **Add package from git URL...**
4. Enter:

```
https://github.com/dyskotron/9SliceOptimizer.git?path=Packages/com.dyskotron.nineslice-optimizer
```

To install a specific version tag:
```
https://github.com/dyskotron/9SliceOptimizer.git?path=Packages/com.dyskotron.nineslice-optimizer#v1.0.0
```

### Via manifest.json

```json
{
  "dependencies": {
    "com.dyskotron.nineslice-optimizer": "https://github.com/dyskotron/9SliceOptimizer.git?path=Packages/com.dyskotron.nineslice-optimizer"
  }
}
```

### Requirements
- **Unity 2021.3** or newer
- **2D Sprite** package (installed as a dependency)

## Developing

Open this repository as a Unity project (6000.x). The package is referenced as a local `file:` dependency, so edits in `Packages/com.dyskotron.nineslice-optimizer` apply directly. The tests are EditMode tests in the Test Runner (the package is listed under `testables`). `Assets/Samples/Fixture_Panel.png` is a 160 × 96 sprite with a 20 / 14 / 28 / 18 border to try the tool on: right-click it, *9-Slice › Optimize 9-Slice…*.

## License

MIT
