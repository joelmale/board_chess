# Board Chess

Board Chess is a Unity chess prototype built for touch-first play on Board hardware, with a mouse/touch fallback for normal Unity Editor testing.

The game supports two-player chess with legal move validation, turn enforcement, check, checkmate, stalemate, castling, en passant, captures, and automatic promotion to queen.

## Project Status

This is an early playable prototype. It currently renders the board and pieces with Unity IMGUI at runtime and bootstraps itself into any loaded scene.

Physical glyph piece support is not implemented yet. The current interaction model is finger, touch, or mouse input only.

## Requirements

- Unity 6000.4.5f1
- Unity Hub
- Optional: Board SDK package for Board hardware builds

The project was originally created on Unity 2022.3 LTS and has been upgraded to Unity 6000.4.5f1.

## Open In Unity

Open this folder as the Unity project root:

```text
/Users/JoelN/Coding/BoardGames/battlechess/Chess
```

In Unity Hub:

1. Click `Add` or `Add project from disk`.
2. Select the `Chess` folder.
3. Open it with Unity `6000.4.5f1`.
4. Let Unity finish importing packages and compiling scripts.
5. Open `Assets/Scenes/FirstRun.unity`.
6. Press Play.

`FirstRun.unity` contains a main camera and a `Chess Game Controller` object with `BoardChessGame` attached. `BoardChessGame` also creates the runtime game object automatically if you open another scene without one.

## Editor Controls

- Drag a piece to move it.
- Tap or click a piece, then tap or click a destination square.
- Use the `Reset` button to restart the game.

Only legal moves are accepted. Illegal moves display a short status message.

## Board Hardware Setup

For Board hardware builds, install and configure the Board SDK:

1. Install the Board SDK tarball through `Window > Package Manager > + > Add package from tarball...`.
2. Run `Board > Configure Unity Project...`.
3. Apply the recommended Android and input settings.
4. Open `Edit > Project Settings > Board > Input Settings`.
5. Load and select a Piece Set Model if required by the SDK setup.

Board builds typically require Android, API level 33 or newer, IL2CPP, ARM64, and Landscape Left orientation. Use the Board setup wizard as the source of truth for current SDK requirements.

The app polls `Board.Input.BoardInput.GetActiveContacts(BoardContactType.Finger)` by reflection when the SDK is installed. Without the SDK, the Unity Editor fallback uses normal mouse and touch input.

## Repository Layout

```text
Assets/
  Scripts/
    BoardChessGame.cs       Runtime game bootstrap, rendering, and interaction flow
    BoardPointerInput.cs    Board SDK input adapter with Unity input fallback
    ChessRules.cs           Chess board state, legal move generation, and game rules
Packages/
  manifest.json             Unity package dependencies
ProjectSettings/
  ProjectVersion.txt        Unity editor version
README.md
```

## Version Control

Commit Unity source assets, `.meta` files, package files, and project settings.

Do not commit generated Unity folders such as `Library`, `Temp`, `Obj`, `Logs`, `Build`, `Builds`, or `UserSettings`. The included `.gitignore` is configured for those Unity-generated files.

## License

Recommended license: MIT.

MIT is a good fit for this prototype if you want other people to be able to use, modify, and learn from the code with minimal restrictions while keeping attribution and warranty disclaimers. Add a `LICENSE` file before publishing the repository publicly.
