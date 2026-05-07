using System.Collections.Generic;
using UnityEngine;

namespace BattleChess
{
    public sealed class BoardChessGame : MonoBehaviour
    {
        private readonly ChessRules rules = new();
        private readonly BoardPointerInput pointerInput = new();

        private Vector2Int? selectedSquare;
        private List<ChessMove> selectedMoves = new();
        private int? activePointerId;
        private Vector2 dragPosition;
        private Vector2Int? pendingPromotionFrom;
        private Vector2Int? pendingPromotionTo;
        private string transientMessage = string.Empty;
        private float transientMessageUntil;

        private GUIStyle titleStyle;
        private GUIStyle statusStyle;
        private GUIStyle hintStyle;
        private GUIStyle pieceStyle;
        private GUIStyle coordinateStyle;
        private GUIStyle buttonStyle;
        private GUIStyle disabledButtonStyle;
        private GUIStyle sectionStyle;
        private GUIStyle moveHistoryStyle;
        private GUIStyle futureMoveHistoryStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<BoardChessGame>() != null)
            {
                return;
            }

            GameObject host = new("Board Chess Game");
            DontDestroyOnLoad(host);
            host.AddComponent<BoardChessGame>();
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            rules.ResetGame();
        }

        private void Update()
        {
            BoardLayout layout = CalculateLayout();
            IReadOnlyList<PointerContact> contacts = pointerInput.Poll();
            bool sawActivePointer = false;

            foreach (PointerContact contact in contacts)
            {
                if (contact.Phase == PointerPhase.Began && HandleCommandContact(contact.GuiPosition, layout))
                {
                    continue;
                }

                if (pendingPromotionFrom.HasValue)
                {
                    continue;
                }

                if (!activePointerId.HasValue && contact.Phase == PointerPhase.Began)
                {
                    BeginContact(contact);
                    continue;
                }

                if (activePointerId.HasValue && contact.Id == activePointerId.Value)
                {
                    sawActivePointer = true;
                    dragPosition = contact.GuiPosition;
                    if (contact.IsFinished)
                    {
                        EndContact(contact);
                    }
                }
            }

            if (activePointerId.HasValue && !sawActivePointer && contacts.Count == 0)
            {
                EndContact(new PointerContact(activePointerId.Value, dragPosition, PointerPhase.Ended));
            }
        }

        private bool HandleCommandContact(Vector2 guiPosition, BoardLayout layout)
        {
            if (layout.ResetRect.Contains(guiPosition))
            {
                ResetGame();
                return true;
            }

            if (pendingPromotionFrom.HasValue)
            {
                for (int i = 0; i < layout.PromotionChoiceRects.Length; i++)
                {
                    if (layout.PromotionChoiceRects[i].Contains(guiPosition))
                    {
                        CompletePromotion(PromotionChoices[i]);
                        return true;
                    }
                }

                return true;
            }

            if (layout.UndoRect.Contains(guiPosition))
            {
                UndoMove();
                return true;
            }

            if (layout.RedoRect.Contains(guiPosition))
            {
                RedoMove();
                return true;
            }

            return false;
        }

        private void BeginContact(PointerContact contact)
        {
            dragPosition = contact.GuiPosition;

            if (!TryGetSquare(contact.GuiPosition, out Vector2Int square))
            {
                return;
            }

            ChessPiece piece = rules.GetPiece(square);
            if (!piece.IsEmpty && piece.Color == rules.Turn)
            {
                selectedSquare = square;
                selectedMoves = rules.GetLegalMoves(square);
                activePointerId = contact.Id;
                return;
            }

            if (selectedSquare.HasValue)
            {
                activePointerId = contact.Id;
            }
        }

        private void EndContact(PointerContact contact)
        {
            activePointerId = null;

            if (!selectedSquare.HasValue || !TryGetSquare(contact.GuiPosition, out Vector2Int destination))
            {
                return;
            }

            Vector2Int from = selectedSquare.Value;
            if (destination == from)
            {
                return;
            }

            int moveIndex = selectedMoves.FindIndex(candidate => candidate.To == destination);
            if (moveIndex >= 0 && selectedMoves[moveIndex].IsPromotion)
            {
                pendingPromotionFrom = from;
                pendingPromotionTo = destination;
                selectedSquare = null;
                selectedMoves.Clear();
                transientMessage = "Choose promotion";
                transientMessageUntil = Time.unscaledTime + 10f;
                return;
            }

            if (rules.TryMove(from, destination, out string message))
            {
                selectedSquare = null;
                selectedMoves.Clear();
                transientMessage = string.Empty;
            }
            else
            {
                transientMessage = message;
                transientMessageUntil = Time.unscaledTime + 1.4f;
            }
        }

        private void ResetGame()
        {
            rules.ResetGame();
            selectedSquare = null;
            selectedMoves.Clear();
            activePointerId = null;
            pendingPromotionFrom = null;
            pendingPromotionTo = null;
            transientMessage = string.Empty;
        }

        private void UndoMove()
        {
            if (!rules.TryUndo())
            {
                return;
            }

            ClearInteractionState();
            transientMessage = "Move undone";
            transientMessageUntil = Time.unscaledTime + 1.2f;
        }

        private void RedoMove()
        {
            if (!rules.TryRedo())
            {
                return;
            }

            ClearInteractionState();
            transientMessage = "Move redone";
            transientMessageUntil = Time.unscaledTime + 1.2f;
        }

        private void CompletePromotion(PieceType promotionType)
        {
            if (!pendingPromotionFrom.HasValue || !pendingPromotionTo.HasValue)
            {
                return;
            }

            Vector2Int from = pendingPromotionFrom.Value;
            Vector2Int to = pendingPromotionTo.Value;
            pendingPromotionFrom = null;
            pendingPromotionTo = null;

            if (rules.TryMove(from, to, promotionType, out string message))
            {
                ClearInteractionState();
                transientMessage = string.Empty;
                return;
            }

            ClearInteractionState();
            transientMessage = message;
            transientMessageUntil = Time.unscaledTime + 1.4f;
        }

        private void ClearInteractionState()
        {
            selectedSquare = null;
            selectedMoves.Clear();
            activePointerId = null;
            pendingPromotionFrom = null;
            pendingPromotionTo = null;
        }

        private void OnGUI()
        {
            EnsureStyles();
            BoardLayout layout = CalculateLayout();

            DrawBackground();
            DrawSidePanel(layout);
            DrawBoard(layout);
        }

        private void DrawBackground()
        {
            DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0.08f, 0.11f, 0.09f));
            DrawRect(new Rect(0, 0, Screen.width, Screen.height * 0.16f), new Color(0.02f, 0.035f, 0.03f, 0.35f));
        }

        private void DrawSidePanel(BoardLayout layout)
        {
            Rect panel = layout.PanelRect;
            DrawRect(panel, new Color(0.92f, 0.83f, 0.62f, 0.12f));

            GUI.Label(new Rect(panel.x + 24f, panel.y + 24f, panel.width - 48f, 54f), "Board Chess", titleStyle);

            string status = Time.unscaledTime < transientMessageUntil && !string.IsNullOrEmpty(transientMessage)
                ? transientMessage
                : rules.StatusText;
            GUI.Label(new Rect(panel.x + 24f, panel.y + 82f, panel.width - 48f, 58f), status, statusStyle);

            string feedbackText = BuildFeedbackText();
            GUI.Label(new Rect(panel.x + 24f, panel.y + 142f, panel.width - 48f, 42f), feedbackText, hintStyle);

            bool controlsEnabled = !pendingPromotionFrom.HasValue;
            DrawButton(layout.UndoRect, "Undo", controlsEnabled && rules.CanUndo, new Color(0.34f, 0.50f, 0.36f));
            DrawButton(layout.RedoRect, "Redo", controlsEnabled && rules.CanRedo, new Color(0.34f, 0.50f, 0.36f));

            if (pendingPromotionFrom.HasValue)
            {
                DrawPromotionPrompt(layout);
            }

            DrawMoveHistory(layout);

            DrawButton(layout.ResetRect, "Reset", true, new Color(0.78f, 0.34f, 0.20f));
        }

        private void DrawBoard(BoardLayout layout)
        {
            DrawRect(layout.BoardRect, new Color(0.05f, 0.04f, 0.03f));

            for (int rank = 7; rank >= 0; rank--)
            {
                for (int file = 0; file < 8; file++)
                {
                    Vector2Int square = new(file, rank);
                    Rect squareRect = GetSquareRect(layout, square);
                    bool light = (file + rank) % 2 == 0;
                    DrawRect(squareRect, light ? new Color(0.78f, 0.70f, 0.52f) : new Color(0.27f, 0.42f, 0.25f));

                    if (IsLastMoveSquare(square))
                    {
                        DrawRect(Shrink(squareRect, layout.SquareSize * 0.08f), new Color(0.20f, 0.53f, 0.85f, 0.40f));
                    }

                    if (selectedSquare.HasValue && selectedSquare.Value == square)
                    {
                        DrawRect(Shrink(squareRect, layout.SquareSize * 0.08f), new Color(1f, 0.85f, 0.20f, 0.65f));
                    }
                    else if (IsLegalDestination(square))
                    {
                        DrawRect(Shrink(squareRect, layout.SquareSize * 0.24f), new Color(1f, 0.86f, 0.17f, 0.42f));
                    }

                    DrawCoordinateIfNeeded(squareRect, file, rank, layout.SquareSize);
                    DrawPieceAtSquare(layout, square);
                }
            }

            if (selectedSquare.HasValue && activePointerId.HasValue)
            {
                ChessPiece selected = rules.GetPiece(selectedSquare.Value);
                if (!selected.IsEmpty)
                {
                    DrawPieceLabel(new Rect(dragPosition.x - layout.SquareSize * 0.5f, dragPosition.y - layout.SquareSize * 0.5f, layout.SquareSize, layout.SquareSize), selected);
                }
            }
        }

        private void DrawPromotionPrompt(BoardLayout layout)
        {
            DrawRect(layout.PromotionRect, new Color(0.07f, 0.10f, 0.08f, 0.92f));
            GUI.Label(new Rect(layout.PromotionRect.x + 10f, layout.PromotionRect.y + 5f, layout.PromotionRect.width - 20f, 20f), "Promote pawn", sectionStyle);

            for (int i = 0; i < layout.PromotionChoiceRects.Length; i++)
            {
                DrawButton(layout.PromotionChoiceRects[i], PieceLabel(new ChessPiece(PromotionChoices[i], rules.Turn)), true, new Color(0.72f, 0.61f, 0.34f));
            }
        }

        private void DrawMoveHistory(BoardLayout layout)
        {
            DrawRect(layout.HistoryRect, new Color(0.03f, 0.05f, 0.04f, 0.36f));
            GUI.Label(new Rect(layout.HistoryRect.x + 10f, layout.HistoryRect.y + 7f, layout.HistoryRect.width - 20f, 24f), "Move History", sectionStyle);

            IReadOnlyList<MoveRecord> history = rules.History;
            if (history.Count == 0)
            {
                GUI.Label(new Rect(layout.HistoryRect.x + 10f, layout.HistoryRect.y + 36f, layout.HistoryRect.width - 20f, 28f), "No moves yet", hintStyle);
                return;
            }

            const float rowHeight = 22f;
            int maxRows = Mathf.Max(1, Mathf.FloorToInt((layout.HistoryRect.height - 40f) / rowHeight));
            int start = Mathf.Max(0, history.Count - maxRows);
            float y = layout.HistoryRect.y + 34f;

            for (int i = start; i < history.Count; i++)
            {
                MoveRecord move = history[i];
                bool active = i < rules.ActiveMoveCount;
                bool current = active && i == rules.ActiveMoveCount - 1;
                Rect rowRect = new(layout.HistoryRect.x + 8f, y, layout.HistoryRect.width - 16f, rowHeight);

                if (current)
                {
                    DrawRect(rowRect, new Color(0.20f, 0.53f, 0.85f, 0.22f));
                }

                GUI.Label(rowRect, FormatMoveHistoryLine(i, move), active ? moveHistoryStyle : futureMoveHistoryStyle);
                y += rowHeight;
            }
        }

        private void DrawButton(Rect rect, string label, bool enabled, Color color)
        {
            DrawRect(rect, enabled ? color : new Color(0.28f, 0.30f, 0.28f, 0.70f));
            GUI.Label(rect, label, enabled ? buttonStyle : disabledButtonStyle);
        }

        private void DrawPieceAtSquare(BoardLayout layout, Vector2Int square)
        {
            if (selectedSquare.HasValue && activePointerId.HasValue && selectedSquare.Value == square)
            {
                return;
            }

            ChessPiece piece = rules.GetPiece(square);
            if (piece.IsEmpty)
            {
                return;
            }

            DrawPieceLabel(GetSquareRect(layout, square), piece);
        }

        private void DrawPieceLabel(Rect rect, ChessPiece piece)
        {
            pieceStyle.fontSize = Mathf.RoundToInt(rect.height * 0.40f);
            pieceStyle.normal.textColor = piece.Color == PieceColor.White ? new Color(0.96f, 0.94f, 0.86f) : new Color(0.08f, 0.08f, 0.07f);

            Rect shadow = rect;
            shadow.x += 2f;
            shadow.y += 2f;
            Color original = pieceStyle.normal.textColor;
            pieceStyle.normal.textColor = new Color(0f, 0f, 0f, piece.Color == PieceColor.White ? 0.55f : 0.22f);
            GUI.Label(shadow, PieceLabel(piece), pieceStyle);
            pieceStyle.normal.textColor = original;
            GUI.Label(rect, PieceLabel(piece), pieceStyle);
        }

        private void DrawCoordinateIfNeeded(Rect squareRect, int file, int rank, float squareSize)
        {
            coordinateStyle.fontSize = Mathf.RoundToInt(squareSize * 0.12f);
            if (rank == 0)
            {
                GUI.Label(new Rect(squareRect.x + 5f, squareRect.yMax - squareSize * 0.18f, squareSize * 0.4f, squareSize * 0.18f), ((char)('a' + file)).ToString(), coordinateStyle);
            }

            if (file == 0)
            {
                GUI.Label(new Rect(squareRect.x + 5f, squareRect.y + 4f, squareSize * 0.4f, squareSize * 0.18f), (rank + 1).ToString(), coordinateStyle);
            }
        }

        private bool IsLegalDestination(Vector2Int square)
        {
            for (int i = 0; i < selectedMoves.Count; i++)
            {
                if (selectedMoves[i].To == square)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsLastMoveSquare(Vector2Int square)
        {
            MoveRecord? lastMove = rules.LastMove;
            if (!lastMove.HasValue)
            {
                return false;
            }

            MoveRecord move = lastMove.Value;
            return square == new Vector2Int(move.FromFile, move.FromRank)
                   || square == new Vector2Int(move.ToFile, move.ToRank);
        }

        private string BuildFeedbackText()
        {
            if (pendingPromotionFrom.HasValue)
            {
                return "Choose the piece for promotion";
            }

            if (rules.IsInCheck(rules.Turn) && !rules.Winner.HasValue)
            {
                return "King is under attack";
            }

            MoveRecord? lastMove = rules.LastMove;
            if (lastMove.HasValue)
            {
                MoveRecord move = lastMove.Value;
                if (move.IsCheckmate)
                {
                    return $"Last: {move.Notation} - checkmate";
                }

                if (move.GivesCheck)
                {
                    return $"Last: {move.Notation} - check";
                }

                if (move.IsCapture)
                {
                    return $"Last: {move.Notation} - capture";
                }

                return $"Last: {move.Notation}";
            }

            return "Drag a piece or tap source then destination";
        }

        private bool TryGetSquare(Vector2 guiPosition, out Vector2Int square)
        {
            BoardLayout layout = CalculateLayout();
            square = default;

            if (!layout.BoardRect.Contains(guiPosition))
            {
                return false;
            }

            int file = Mathf.FloorToInt((guiPosition.x - layout.BoardRect.x) / layout.SquareSize);
            int row = Mathf.FloorToInt((guiPosition.y - layout.BoardRect.y) / layout.SquareSize);
            square = new Vector2Int(file, 7 - row);
            return file >= 0 && file < 8 && row >= 0 && row < 8;
        }

        private static Rect GetSquareRect(BoardLayout layout, Vector2Int square)
        {
            int row = 7 - square.y;
            return new Rect(
                layout.BoardRect.x + square.x * layout.SquareSize,
                layout.BoardRect.y + row * layout.SquareSize,
                layout.SquareSize,
                layout.SquareSize);
        }

        private static BoardLayout CalculateLayout()
        {
            float margin = Mathf.Max(20f, Screen.height * 0.035f);
            float boardSize = Mathf.Min(Screen.height - margin * 2f, Screen.width * 0.68f);
            boardSize = Mathf.Floor(boardSize / 8f) * 8f;

            float boardX = Screen.width * 0.52f - boardSize * 0.5f;
            float boardY = (Screen.height - boardSize) * 0.5f;
            Rect boardRect = new(boardX, boardY, boardSize, boardSize);

            float panelX = margin;
            float panelY = boardY;
            float panelWidth = Mathf.Max(280f, boardX - margin * 1.8f);
            Rect panelRect = new(panelX, panelY, panelWidth, boardSize);
            float buttonWidth = Mathf.Min(220f, panelWidth - 48f);
            Rect undoRect = new(panelX + 24f, panelY + 190f, buttonWidth * 0.5f - 6f, 42f);
            Rect redoRect = new(undoRect.xMax + 12f, undoRect.y, undoRect.width, undoRect.height);
            Rect promotionRect = new(panelX + 24f, panelY + 244f, panelWidth - 48f, 58f);
            Rect[] promotionChoiceRects = new Rect[PromotionChoices.Length];
            float choiceGap = 6f;
            float choiceWidth = (promotionRect.width - choiceGap * 3f - 20f) / 4f;
            for (int i = 0; i < promotionChoiceRects.Length; i++)
            {
                promotionChoiceRects[i] = new Rect(promotionRect.x + 10f + i * (choiceWidth + choiceGap), promotionRect.y + 28f, choiceWidth, 24f);
            }

            Rect resetRect = new(panelX + 24f, panelRect.yMax - 74f, buttonWidth, 50f);
            Rect historyRect = new(panelX + 24f, panelY + 318f, panelWidth - 48f, Mathf.Max(80f, resetRect.y - (panelY + 318f) - 16f));

            return new BoardLayout(boardRect, panelRect, resetRect, undoRect, redoRect, historyRect, promotionRect, promotionChoiceRects, boardSize / 8f);
        }

        private static Rect Shrink(Rect rect, float amount)
        {
            return new Rect(rect.x + amount, rect.y + amount, rect.width - amount * 2f, rect.height - amount * 2f);
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static string PieceLabel(ChessPiece piece)
        {
            string label = piece.Type switch
            {
                PieceType.King => "K",
                PieceType.Queen => "Q",
                PieceType.Rook => "R",
                PieceType.Bishop => "B",
                PieceType.Knight => "N",
                PieceType.Pawn => "P",
                _ => string.Empty
            };

            return piece.Color == PieceColor.White ? label : label.ToLowerInvariant();
        }

        private static string FormatMoveHistoryLine(int index, MoveRecord move)
        {
            int moveNumber = index / 2 + 1;
            string prefix = index % 2 == 0 ? $"{moveNumber}." : $"{moveNumber}...";
            return $"{prefix} {move.Notation}";
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.RoundToInt(Screen.height * 0.038f),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.96f, 0.90f, 0.72f) }
            };

            statusStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = Mathf.RoundToInt(Screen.height * 0.032f),
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(0.98f, 0.96f, 0.88f) }
            };

            hintStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = Mathf.RoundToInt(Screen.height * 0.021f),
                wordWrap = true,
                normal = { textColor = new Color(0.78f, 0.82f, 0.70f) }
            };

            pieceStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };

            coordinateStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.04f, 0.05f, 0.04f, 0.55f) }
            };

            buttonStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Screen.height * 0.026f),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            disabledButtonStyle = new GUIStyle(buttonStyle)
            {
                normal = { textColor = new Color(0.72f, 0.76f, 0.70f) }
            };

            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.RoundToInt(Screen.height * 0.018f),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.88f, 0.70f) }
            };

            moveHistoryStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.RoundToInt(Screen.height * 0.018f),
                normal = { textColor = new Color(0.96f, 0.96f, 0.88f) }
            };

            futureMoveHistoryStyle = new GUIStyle(moveHistoryStyle)
            {
                normal = { textColor = new Color(0.58f, 0.62f, 0.56f) }
            };
        }

        private readonly struct BoardLayout
        {
            public readonly Rect BoardRect;
            public readonly Rect PanelRect;
            public readonly Rect ResetRect;
            public readonly Rect UndoRect;
            public readonly Rect RedoRect;
            public readonly Rect HistoryRect;
            public readonly Rect PromotionRect;
            public readonly Rect[] PromotionChoiceRects;
            public readonly float SquareSize;

            public BoardLayout(
                Rect boardRect,
                Rect panelRect,
                Rect resetRect,
                Rect undoRect,
                Rect redoRect,
                Rect historyRect,
                Rect promotionRect,
                Rect[] promotionChoiceRects,
                float squareSize)
            {
                BoardRect = boardRect;
                PanelRect = panelRect;
                ResetRect = resetRect;
                UndoRect = undoRect;
                RedoRect = redoRect;
                HistoryRect = historyRect;
                PromotionRect = promotionRect;
                PromotionChoiceRects = promotionChoiceRects;
                SquareSize = squareSize;
            }
        }

        private static readonly PieceType[] PromotionChoices =
        {
            PieceType.Queen,
            PieceType.Rook,
            PieceType.Bishop,
            PieceType.Knight
        };
    }
}
