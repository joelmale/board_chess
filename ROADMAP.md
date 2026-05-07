# Battle Chess Upgrade Roadmap

## Summary

Build the full approved roadmap: harden the chess rules with tests, improve player UX, replace the IMGUI prototype with a 3D tabletop scene, add learning tools for new chess players, add persistence, and finish Board SDK integration for physical glyph pieces. Target Unity `6000.4.5f1`.

## Key Changes

- Introduce a tested game-state layer: move records, undo snapshots, promotion choices, captured-piece tracking, and save/load DTOs.
- Replace runtime IMGUI board rendering with 3D scene objects: board tiles, 3D piece prefabs, click/touch raycast input, move/capture animations, and responsive tabletop camera.
- Add a UI overlay for status, move history, promotion selection, captured pieces, reset, undo, and save/load.
- Add a Learning Mode dropdown with optional coaching overlays for at-risk pieces, threatened squares, legal moves, checks, pins, and simple tactical warnings.
- Add full Board SDK setup: package install/config, Android settings, Board input module, glyph contact adapter, simulator workflow, and piece-to-chess mapping.
- Keep mouse/touch fallback working in Editor and desktop builds.

## Commit Plan

Feature 1. ~~`test: add chess rules test harness`~~ Completed
   - Add Unity Test Framework if needed.
   - Cover opening moves, illegal moves, check, checkmate, stalemate, castling, en passant, and promotion.

Feature 2. ~~`refactor: separate chess game state from presentation`~~ Completed
   - Add `MoveRecord`, `GameSnapshot`, promotion API, undo stack, captured pieces, and move notation.
   - Preserve current playable behavior.

Feature 3. ~~`feat: add gameplay UX controls`~~ Completed
   - Add move history, last-move highlight, check/capture feedback, undo, and promotion choice UI.
   - Update README controls.

Feature 3.5. ~~`feat: add selectable board camera views`~~ Completed
   - Add a side-panel view selector for common chess perspectives.
   - Support overhead, 3/4, white-side, and black-side camera presets.
   - Keep camera changes instant and independent from move history, undo, redo, and Board input.

Feature 4. ~~`feat: build 3d chess board and pieces`~~ Completed
   - Create `FirstRun` 3D board scene with tile GameObjects, piece prefabs, materials, camera, lighting, and raycast input.
   - Move pieces via scene animation instead of IMGUI labels.

Feature 5. ~~`feat: add responsive tabletop layout and captured pieces`~~ Completed
   - Support landscape tabletop play, Editor window resizing, and Board-like aspect ratios.
   - Display captured pieces and current turn clearly without blocking the board.

Feature 6. ~~`feat: add learning mode overlays`~~ Completed
   - Add a `Learning Mode` button/dropdown in the gameplay UI.
   - Allow players to toggle beginner-friendly overlays such as:
     - Highlight at-risk pieces.
     - Highlight threatened squares.
     - Highlight legal moves for the selected piece.
     - Highlight checking lines against the king.
     - Mark pinned pieces.
     - Warn when a move would leave a high-value piece undefended.
   - Keep all learning overlays optional so normal two-player play remains uncluttered.

Feature 7. ~~`feat: add save and load game`~~ Completed
   - Serialize current board, turn, move history, captured pieces, castling/en passant state, undo history, and game result.
   - Add local save/load buttons for Editor and desktop.

Feature 8. `feat: integrate board sdk glyph input` Deferred until glyph pieces are available
   - Install/configure Board SDK and Android settings.
   - Add typed or reflection-safe adapter for finger and glyph contacts.
   - Map physical pieces to chess pieces/squares, support simulator testing, and document required Piece Set Model setup.

Feature 9. ~~`docs: update first-run and board deployment guide`~~ Completed
   - Document Editor play, test commands, Board simulator use, Android/Board build settings, and known limitations.

## Test Plan

- Run Unity EditMode tests for all chess rules and game-state APIs.
- Run PlayMode smoke tests for first-run scene load, piece selection, legal move, capture, undo, promotion, save/load, and reset.
- Manual Editor pass: mouse drag, tap source/destination, illegal move feedback, checkmate/stalemate messages.
- Manual 3D pass: verify camera framing, raycast accuracy, animations, captured pieces, and responsive layout.
- Manual Learning Mode pass: verify each dropdown option can be toggled independently, overlays update after every move/undo/redo, and disabled options leave the board clean.
- Board pass: run Board simulator for finger and glyph contacts, then Android build/deploy once SDK, `bdb`, and Piece Set Model are available.

## Assumptions

- Visual target is 3D pieces and a 3D tabletop board.
- Board hardware target is full SDK setup, not just documentation.
- The Board SDK tarball, `bdb`, and Piece Set Model will be supplied locally during implementation.
- Do not commit proprietary SDK tarballs or `.tflite` model files unless their license explicitly allows it.
- Keep generated Unity folders such as `Library`, `Temp`, `Logs`, and build outputs out of git.
