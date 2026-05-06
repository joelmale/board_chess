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
        private string transientMessage = string.Empty;
        private float transientMessageUntil;

        private GUIStyle titleStyle;
        private GUIStyle statusStyle;
        private GUIStyle hintStyle;
        private GUIStyle pieceStyle;
        private GUIStyle coordinateStyle;
        private GUIStyle buttonStyle;

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
                if (contact.Phase == PointerPhase.Began && layout.ResetRect.Contains(contact.GuiPosition))
                {
                    ResetGame();
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
            transientMessage = string.Empty;
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
            GUI.Label(new Rect(panel.x + 24f, panel.y + 92f, panel.width - 48f, 88f), status, statusStyle);

            string checkText = rules.IsInCheck(rules.Turn) && !rules.Winner.HasValue ? "King is under attack" : "Drag a piece or tap source then destination";
            GUI.Label(new Rect(panel.x + 24f, panel.y + 188f, panel.width - 48f, 92f), checkText, hintStyle);

            DrawRect(layout.ResetRect, new Color(0.78f, 0.34f, 0.20f));
            GUI.Label(layout.ResetRect, "Reset", buttonStyle);
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
            Rect resetRect = new(panelX + 24f, panelRect.yMax - 96f, Mathf.Min(220f, panelWidth - 48f), 62f);

            return new BoardLayout(boardRect, panelRect, resetRect, boardSize / 8f);
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
        }

        private readonly struct BoardLayout
        {
            public readonly Rect BoardRect;
            public readonly Rect PanelRect;
            public readonly Rect ResetRect;
            public readonly float SquareSize;

            public BoardLayout(Rect boardRect, Rect panelRect, Rect resetRect, float squareSize)
            {
                BoardRect = boardRect;
                PanelRect = panelRect;
                ResetRect = resetRect;
                SquareSize = squareSize;
            }
        }
    }
}
