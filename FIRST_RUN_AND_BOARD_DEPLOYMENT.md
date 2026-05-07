# First Run And Board Deployment Guide

This guide covers the current touch-first chess build. Physical glyph-piece input is intentionally deferred until glyph pieces are available.

## Unity First Run

1. Open the project root in Unity `6000.4.5f1`.
2. Open `Assets/Scenes/FirstRun.unity`.
3. Press Play.
4. Use mouse or touch to drag a 3D piece, or tap a source square and then a destination square.

The app creates its 3D board, pieces, lighting, and input colliders at runtime from `BoardChessGame`.

## Current Controls

- `Undo` / `Redo`: step through the active move timeline.
- `Save` / `Load`: write and restore `battle-chess-save.json` from Unity's `Application.persistentDataPath`.
- `Top`, `3/4`, `White`, `Black`: switch camera perspective.
- `Learning Mode`: show legal moves, threatened squares, at-risk pieces, pins, or tactical warnings.
- `Reset`: start a fresh game.

## Android Board Build Settings

Use these settings for the current Board test build:

- Unity: `6000.4.5f1`
- Platform: Android
- Scripting backend: IL2CPP
- Target architecture: ARM64
- Minimum SDK: 33
- Target SDK: 33 or newer
- Orientation: Landscape Left
- Graphics API: Vulkan only, if the device accepts it

If Vulkan fails on the device, use OpenGLES3 only as the fallback and capture a fresh logcat. Avoid Auto Graphics API while debugging GPU crashes.

## Board SDK Settings

Before building, verify:

1. `Board > Configure Unity Project...` has been applied.
2. `Edit > Project Settings > Board > Input Settings` has an active Piece Set Model.
3. The build log reports a configured model, for example `arcade_v1.3.7.tflite`.

The current app polls Board finger contacts through `BoardPointerInput` and falls back to Unity mouse/touch input in the Editor.

## Build And Install

Build from Unity with `Development Build` enabled while testing. After Unity creates the APK, install it through your normal Board deployment flow or `adb`:

```bash
adb install -r path/to/Chess.apk
```

Watch logs while testing:

```bash
adb logcat -s Unity CRASH DEBUG
```

## Known Limitations

- Physical glyph-piece mapping is not implemented yet.
- Save files are local to the app install and may be removed if the app data is cleared.
- The 3D pieces are generated from Unity primitives, not production art assets.
- The UI is IMGUI-based for iteration speed and can be replaced with Unity UI Toolkit or uGUI later.
