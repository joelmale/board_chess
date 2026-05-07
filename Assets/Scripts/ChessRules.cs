using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BattleChess
{
    public enum PieceColor
    {
        White,
        Black
    }

    public enum PieceType
    {
        None,
        Pawn,
        Knight,
        Bishop,
        Rook,
        Queen,
        King
    }

    public struct ChessPiece
    {
        public PieceType Type;
        public PieceColor Color;
        public bool HasMoved;

        public bool IsEmpty => Type == PieceType.None;

        public ChessPiece(PieceType type, PieceColor color)
        {
            Type = type;
            Color = color;
            HasMoved = false;
        }
    }

    public readonly struct ChessMove
    {
        public readonly Vector2Int From;
        public readonly Vector2Int To;
        public readonly bool IsCastling;
        public readonly bool IsEnPassant;
        public readonly bool IsPromotion;

        public ChessMove(Vector2Int from, Vector2Int to, bool isCastling = false, bool isEnPassant = false, bool isPromotion = false)
        {
            From = from;
            To = to;
            IsCastling = isCastling;
            IsEnPassant = isEnPassant;
            IsPromotion = isPromotion;
        }
    }

    public sealed class ChessRules
    {
        private readonly ChessPiece[,] board = new ChessPiece[8, 8];
        private Vector2Int? enPassantTarget;

        private readonly List<MoveRecord> history = new();
        private readonly List<ChessPiece> capturedByWhite = new();
        private readonly List<ChessPiece> capturedByBlack = new();
        private readonly List<UndoFrame> undoFrames = new();
        private int activeMoveCount;

        public PieceColor Turn { get; private set; } = PieceColor.White;
        public PieceColor? Winner { get; private set; }
        public bool IsDraw { get; private set; }
        public string StatusText { get; private set; } = "White to move";

        public IReadOnlyList<MoveRecord> History => history;
        public IReadOnlyList<ChessPiece> CapturedByWhite => capturedByWhite;
        public IReadOnlyList<ChessPiece> CapturedByBlack => capturedByBlack;
        public int ActiveMoveCount => activeMoveCount;
        public bool CanUndo => activeMoveCount > 0
                               && activeMoveCount <= undoFrames.Count
                               && undoFrames[activeMoveCount - 1].IsValid;
        public bool CanRedo => activeMoveCount < history.Count;

        public MoveRecord? LastMove
            => activeMoveCount > 0 ? history[activeMoveCount - 1] : (MoveRecord?)null;

        public ChessPiece GetPiece(Vector2Int square)
        {
            return IsInside(square) ? board[square.x, square.y] : default;
        }

        public void ResetGame()
        {
            for (int file = 0; file < 8; file++)
            {
                for (int rank = 0; rank < 8; rank++)
                {
                    board[file, rank] = default;
                }
            }

            PlaceBackRank(PieceColor.White, 0);
            PlaceBackRank(PieceColor.Black, 7);

            for (int file = 0; file < 8; file++)
            {
                board[file, 1] = new ChessPiece(PieceType.Pawn, PieceColor.White);
                board[file, 6] = new ChessPiece(PieceType.Pawn, PieceColor.Black);
            }

            Turn = PieceColor.White;
            Winner = null;
            IsDraw = false;
            enPassantTarget = null;
            StatusText = "White to move";

            history.Clear();
            capturedByWhite.Clear();
            capturedByBlack.Clear();
            undoFrames.Clear();
            activeMoveCount = 0;
        }

        /// <summary>
        /// Attempts a move. Pawns reaching the last rank are promoted to queen.
        /// Use the overload that takes a <see cref="PieceType"/> to choose another
        /// promotion piece.
        /// </summary>
        public bool TryMove(Vector2Int from, Vector2Int to, out string message)
        {
            return TryMove(from, to, PieceType.Queen, out message);
        }

        /// <summary>
        /// Attempts a move with an explicit promotion piece. Promotion piece is
        /// only consulted when the move is a pawn promotion; otherwise it is
        /// ignored. Legal values: Queen, Rook, Bishop, Knight.
        /// </summary>
        public bool TryMove(Vector2Int from, Vector2Int to, PieceType promotionType, out string message)
        {
            message = string.Empty;

            if (Winner.HasValue || IsDraw)
            {
                message = "Game is over. Tap Reset for a new game.";
                return false;
            }

            List<ChessMove> legalMoves = GetLegalMoves(from);
            int moveIndex = legalMoves.FindIndex(candidate => candidate.To == to);
            if (moveIndex < 0)
            {
                message = "Illegal move";
                return false;
            }

            ChessMove move = legalMoves[moveIndex];
            PieceType resolvedPromotion = ResolvePromotionType(move, promotionType);

            TrimFutureHistory();

            // Capture data is gathered BEFORE applying the move so that en passant
            // can pick the captured pawn off its actual square (not the destination).
            ChessPiece movingPiece = board[from.x, from.y];
            ChessPiece capturedPiece = ResolveCapturedPiece(move);

            PushUndoFrame();
            ApplyMove(board, move, true, resolvedPromotion);

            PieceColor mover = Turn;
            Turn = Opponent(Turn);
            UpdateGameStatus();

            if (capturedPiece.Type != PieceType.None)
            {
                if (mover == PieceColor.White)
                {
                    capturedByWhite.Add(capturedPiece);
                }
                else
                {
                    capturedByBlack.Add(capturedPiece);
                }
            }

            bool givesCheck = IsKingInCheck(board, Turn);
            bool isCheckmate = Winner.HasValue && Winner.Value == mover;

            string notation = BuildNotation(
                from, to, movingPiece, capturedPiece, move,
                resolvedPromotion, givesCheck, isCheckmate);

            history.Add(new MoveRecord(
                from.x, from.y, to.x, to.y,
                movingPiece.Type, movingPiece.Color,
                capturedPiece.Type, capturedPiece.Color,
                move.IsCastling, move.IsEnPassant,
                move.IsPromotion, resolvedPromotion,
                givesCheck, isCheckmate,
                notation));
            activeMoveCount = history.Count;

            return true;
        }

        public List<ChessMove> GetLegalMoves(Vector2Int from)
        {
            ChessPiece piece = GetPiece(from);
            if (piece.IsEmpty || piece.Color != Turn || Winner.HasValue || IsDraw)
            {
                return new List<ChessMove>();
            }

            return GenerateLegalMoves(from, board, Turn);
        }

        public bool IsInCheck(PieceColor color)
        {
            return IsKingInCheck(board, color);
        }

        /// <summary>
        /// Reverses the most recently applied move. Returns false if there is
        /// no move to undo.
        /// </summary>
        public bool TryUndo()
        {
            if (!CanUndo)
            {
                return false;
            }

            RestoreUndoFrame(undoFrames[activeMoveCount - 1]);
            activeMoveCount--;

            return true;
        }

        /// <summary>
        /// Reapplies the next move after an undo. Returns false when the
        /// current position is already at the end of the move timeline.
        /// </summary>
        public bool TryRedo()
        {
            if (!CanRedo)
            {
                return false;
            }

            MoveRecord record = history[activeMoveCount];
            ChessMove move = new(
                new Vector2Int(record.FromFile, record.FromRank),
                new Vector2Int(record.ToFile, record.ToRank),
                record.IsCastling,
                record.IsEnPassant,
                record.IsPromotion);

            undoFrames[activeMoveCount] = new UndoFrame(
                board, enPassantTarget, Turn, Winner, IsDraw, StatusText,
                capturedByWhite.Count, capturedByBlack.Count);

            ApplyMove(board, move, true, record.PromotionType);
            Turn = Opponent(Turn);
            UpdateGameStatus();

            if (record.CapturedType != PieceType.None)
            {
                ChessPiece captured = new(record.CapturedType, record.CapturedColor);
                if (record.MovedColor == PieceColor.White)
                {
                    capturedByWhite.Add(captured);
                }
                else
                {
                    capturedByBlack.Add(captured);
                }
            }

            activeMoveCount++;
            return true;
        }

        /// <summary>Captures the current state for save/load or external inspection.</summary>
        public GameSnapshot GetSnapshot()
        {
            return new GameSnapshot(
                board, enPassantTarget, Turn, Winner, IsDraw, StatusText,
                history, activeMoveCount, capturedByWhite, capturedByBlack);
        }

        /// <summary>Restores a previously captured snapshot. Clears the undo stack.</summary>
        public void RestoreFromSnapshot(GameSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.CopyBoardInto(board);
            enPassantTarget = snapshot.HasEnPassantTarget ? snapshot.EnPassantTarget : (Vector2Int?)null;
            Turn = snapshot.Turn;
            Winner = snapshot.HasWinner ? snapshot.Winner : (PieceColor?)null;
            IsDraw = snapshot.IsDraw;
            StatusText = snapshot.StatusText;

            history.Clear();
            history.AddRange(snapshot.History);
            activeMoveCount = Mathf.Clamp(snapshot.ActiveMoveCount, 0, history.Count);
            capturedByWhite.Clear();
            capturedByWhite.AddRange(snapshot.CapturedByWhite);
            capturedByBlack.Clear();
            capturedByBlack.AddRange(snapshot.CapturedByBlack);
            undoFrames.Clear();
            for (int i = 0; i < history.Count; i++)
            {
                undoFrames.Add(default);
            }
        }

        private static PieceType ResolvePromotionType(ChessMove move, PieceType requested)
        {
            if (!move.IsPromotion)
            {
                return PieceType.None;
            }

            switch (requested)
            {
                case PieceType.Queen:
                case PieceType.Rook:
                case PieceType.Bishop:
                case PieceType.Knight:
                    return requested;
                default:
                    return PieceType.Queen;
            }
        }

        private ChessPiece ResolveCapturedPiece(ChessMove move)
        {
            if (move.IsEnPassant)
            {
                return board[move.To.x, move.From.y];
            }

            ChessPiece destination = board[move.To.x, move.To.y];
            return destination.IsEmpty ? default : destination;
        }

        private void PushUndoFrame()
        {
            undoFrames.Add(new UndoFrame(
                board, enPassantTarget, Turn, Winner, IsDraw, StatusText,
                capturedByWhite.Count, capturedByBlack.Count));
        }

        private void RestoreUndoFrame(UndoFrame frame)
        {
            for (int file = 0; file < 8; file++)
            {
                for (int rank = 0; rank < 8; rank++)
                {
                    board[file, rank] = frame.Board[file, rank];
                }
            }

            enPassantTarget = frame.EnPassantTarget;
            Turn = frame.Turn;
            Winner = frame.Winner;
            IsDraw = frame.IsDraw;
            StatusText = frame.StatusText;

            TrimList(capturedByWhite, frame.CapturedByWhiteCount);
            TrimList(capturedByBlack, frame.CapturedByBlackCount);
        }

        private void TrimFutureHistory()
        {
            if (activeMoveCount >= history.Count)
            {
                return;
            }

            int removeCount = history.Count - activeMoveCount;
            history.RemoveRange(activeMoveCount, removeCount);
            undoFrames.RemoveRange(activeMoveCount, removeCount);
        }

        private static void TrimList<T>(List<T> list, int targetCount)
        {
            if (targetCount < 0)
            {
                targetCount = 0;
            }

            if (targetCount >= list.Count)
            {
                return;
            }

            list.RemoveRange(targetCount, list.Count - targetCount);
        }

        private void PlaceBackRank(PieceColor color, int rank)
        {
            board[0, rank] = new ChessPiece(PieceType.Rook, color);
            board[1, rank] = new ChessPiece(PieceType.Knight, color);
            board[2, rank] = new ChessPiece(PieceType.Bishop, color);
            board[3, rank] = new ChessPiece(PieceType.Queen, color);
            board[4, rank] = new ChessPiece(PieceType.King, color);
            board[5, rank] = new ChessPiece(PieceType.Bishop, color);
            board[6, rank] = new ChessPiece(PieceType.Knight, color);
            board[7, rank] = new ChessPiece(PieceType.Rook, color);
        }

        private List<ChessMove> GenerateLegalMoves(Vector2Int from, ChessPiece[,] state, PieceColor movingColor)
        {
            List<ChessMove> legalMoves = new();

            foreach (ChessMove move in GeneratePseudoMoves(from, state, true))
            {
                ChessPiece[,] copy = (ChessPiece[,])state.Clone();
                ApplyMove(copy, move, false, PieceType.Queen);
                if (!IsKingInCheck(copy, movingColor))
                {
                    legalMoves.Add(move);
                }
            }

            return legalMoves;
        }

        private IEnumerable<ChessMove> GeneratePseudoMoves(Vector2Int from, ChessPiece[,] state, bool includeCastling)
        {
            ChessPiece piece = state[from.x, from.y];
            if (piece.IsEmpty)
            {
                yield break;
            }

            switch (piece.Type)
            {
                case PieceType.Pawn:
                    foreach (ChessMove move in GeneratePawnMoves(from, state, piece))
                    {
                        yield return move;
                    }
                    break;
                case PieceType.Knight:
                    foreach (Vector2Int offset in KnightOffsets)
                    {
                        Vector2Int to = from + offset;
                        if (CanLandOn(to, state, piece.Color))
                        {
                            yield return new ChessMove(from, to);
                        }
                    }
                    break;
                case PieceType.Bishop:
                    foreach (ChessMove move in GenerateSlidingMoves(from, state, piece.Color, BishopDirections))
                    {
                        yield return move;
                    }
                    break;
                case PieceType.Rook:
                    foreach (ChessMove move in GenerateSlidingMoves(from, state, piece.Color, RookDirections))
                    {
                        yield return move;
                    }
                    break;
                case PieceType.Queen:
                    foreach (ChessMove move in GenerateSlidingMoves(from, state, piece.Color, QueenDirections))
                    {
                        yield return move;
                    }
                    break;
                case PieceType.King:
                    foreach (Vector2Int offset in QueenDirections)
                    {
                        Vector2Int to = from + offset;
                        if (CanLandOn(to, state, piece.Color))
                        {
                            yield return new ChessMove(from, to);
                        }
                    }

                    if (includeCastling)
                    {
                        foreach (ChessMove move in GenerateCastlingMoves(from, state, piece))
                        {
                            yield return move;
                        }
                    }
                    break;
            }
        }

        private IEnumerable<ChessMove> GeneratePawnMoves(Vector2Int from, ChessPiece[,] state, ChessPiece piece)
        {
            int direction = piece.Color == PieceColor.White ? 1 : -1;
            int startRank = piece.Color == PieceColor.White ? 1 : 6;
            int promotionRank = piece.Color == PieceColor.White ? 7 : 0;

            Vector2Int oneForward = new(from.x, from.y + direction);
            if (IsInside(oneForward) && state[oneForward.x, oneForward.y].IsEmpty)
            {
                yield return new ChessMove(from, oneForward, isPromotion: oneForward.y == promotionRank);

                Vector2Int twoForward = new(from.x, from.y + (direction * 2));
                if (from.y == startRank && state[twoForward.x, twoForward.y].IsEmpty)
                {
                    yield return new ChessMove(from, twoForward);
                }
            }

            for (int dx = -1; dx <= 1; dx += 2)
            {
                Vector2Int diagonal = new(from.x + dx, from.y + direction);
                if (!IsInside(diagonal))
                {
                    continue;
                }

                ChessPiece target = state[diagonal.x, diagonal.y];
                if (!target.IsEmpty && target.Color != piece.Color && target.Type != PieceType.King)
                {
                    yield return new ChessMove(from, diagonal, isPromotion: diagonal.y == promotionRank);
                }

                if (enPassantTarget.HasValue && enPassantTarget.Value == diagonal)
                {
                    ChessPiece adjacent = state[diagonal.x, from.y];
                    if (adjacent.Type == PieceType.Pawn && adjacent.Color != piece.Color)
                    {
                        yield return new ChessMove(from, diagonal, isEnPassant: true);
                    }
                }
            }
        }

        private IEnumerable<ChessMove> GenerateSlidingMoves(
            Vector2Int from,
            ChessPiece[,] state,
            PieceColor movingColor,
            IReadOnlyList<Vector2Int> directions)
        {
            foreach (Vector2Int direction in directions)
            {
                Vector2Int to = from + direction;
                while (IsInside(to))
                {
                    ChessPiece target = state[to.x, to.y];
                    if (target.IsEmpty)
                    {
                        yield return new ChessMove(from, to);
                    }
                    else
                    {
                        if (target.Color != movingColor && target.Type != PieceType.King)
                        {
                            yield return new ChessMove(from, to);
                        }

                        break;
                    }

                    to += direction;
                }
            }
        }

        private IEnumerable<ChessMove> GenerateCastlingMoves(Vector2Int from, ChessPiece[,] state, ChessPiece king)
        {
            if (king.Type != PieceType.King || king.HasMoved || IsKingInCheck(state, king.Color))
            {
                yield break;
            }

            int rank = king.Color == PieceColor.White ? 0 : 7;
            if (from != new Vector2Int(4, rank))
            {
                yield break;
            }

            if (CanCastle(state, king.Color, rank, rookFile: 7, throughFile: 5, destinationFile: 6))
            {
                yield return new ChessMove(from, new Vector2Int(6, rank), isCastling: true);
            }

            if (CanCastle(state, king.Color, rank, rookFile: 0, throughFile: 3, destinationFile: 2))
            {
                yield return new ChessMove(from, new Vector2Int(2, rank), isCastling: true);
            }
        }

        private bool CanCastle(ChessPiece[,] state, PieceColor color, int rank, int rookFile, int throughFile, int destinationFile)
        {
            ChessPiece rook = state[rookFile, rank];
            if (rook.Type != PieceType.Rook || rook.Color != color || rook.HasMoved)
            {
                return false;
            }

            int step = rookFile > 4 ? 1 : -1;
            for (int file = 4 + step; file != rookFile; file += step)
            {
                if (!state[file, rank].IsEmpty)
                {
                    return false;
                }
            }

            PieceColor attacker = Opponent(color);
            return !IsSquareAttacked(state, new Vector2Int(throughFile, rank), attacker)
                   && !IsSquareAttacked(state, new Vector2Int(destinationFile, rank), attacker);
        }

        private void ApplyMove(ChessPiece[,] state, ChessMove move, bool updateState, PieceType promotionType)
        {
            ChessPiece movingPiece = state[move.From.x, move.From.y];
            state[move.From.x, move.From.y] = default;

            if (move.IsEnPassant)
            {
                state[move.To.x, move.From.y] = default;
            }

            if (move.IsCastling)
            {
                int rank = move.From.y;
                bool kingSide = move.To.x > move.From.x;
                int rookFromFile = kingSide ? 7 : 0;
                int rookToFile = kingSide ? 5 : 3;
                ChessPiece rook = state[rookFromFile, rank];
                rook.HasMoved = true;
                state[rookFromFile, rank] = default;
                state[rookToFile, rank] = rook;
            }

            movingPiece.HasMoved = true;
            if (move.IsPromotion)
            {
                movingPiece.Type = promotionType == PieceType.None ? PieceType.Queen : promotionType;
            }

            state[move.To.x, move.To.y] = movingPiece;

            if (!updateState)
            {
                return;
            }

            enPassantTarget = null;
            if (movingPiece.Type == PieceType.Pawn && Mathf.Abs(move.To.y - move.From.y) == 2)
            {
                enPassantTarget = new Vector2Int(move.From.x, (move.From.y + move.To.y) / 2);
            }
        }

        private void UpdateGameStatus()
        {
            bool inCheck = IsKingInCheck(board, Turn);
            bool hasLegalMove = AnyLegalMove(Turn);

            if (!hasLegalMove && inCheck)
            {
                Winner = Opponent(Turn);
                StatusText = $"{Winner.Value} wins by checkmate";
                return;
            }

            if (!hasLegalMove)
            {
                IsDraw = true;
                StatusText = "Draw by stalemate";
                return;
            }

            StatusText = inCheck ? $"{Turn} to move - check" : $"{Turn} to move";
        }

        private bool AnyLegalMove(PieceColor color)
        {
            for (int file = 0; file < 8; file++)
            {
                for (int rank = 0; rank < 8; rank++)
                {
                    ChessPiece piece = board[file, rank];
                    if (!piece.IsEmpty && piece.Color == color && GenerateLegalMoves(new Vector2Int(file, rank), board, color).Count > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsKingInCheck(ChessPiece[,] state, PieceColor color)
        {
            Vector2Int kingSquare = FindKing(state, color);
            return kingSquare.x >= 0 && IsSquareAttacked(state, kingSquare, Opponent(color));
        }

        private static Vector2Int FindKing(ChessPiece[,] state, PieceColor color)
        {
            for (int file = 0; file < 8; file++)
            {
                for (int rank = 0; rank < 8; rank++)
                {
                    ChessPiece piece = state[file, rank];
                    if (piece.Type == PieceType.King && piece.Color == color)
                    {
                        return new Vector2Int(file, rank);
                    }
                }
            }

            return new Vector2Int(-1, -1);
        }

        private bool IsSquareAttacked(ChessPiece[,] state, Vector2Int square, PieceColor attacker)
        {
            for (int file = 0; file < 8; file++)
            {
                for (int rank = 0; rank < 8; rank++)
                {
                    ChessPiece piece = state[file, rank];
                    if (!piece.IsEmpty && piece.Color == attacker && PieceAttacksSquare(state, new Vector2Int(file, rank), square, piece))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool PieceAttacksSquare(ChessPiece[,] state, Vector2Int from, Vector2Int target, ChessPiece piece)
        {
            Vector2Int delta = target - from;

            switch (piece.Type)
            {
                case PieceType.Pawn:
                    int direction = piece.Color == PieceColor.White ? 1 : -1;
                    return delta.y == direction && Mathf.Abs(delta.x) == 1;
                case PieceType.Knight:
                    return (Mathf.Abs(delta.x) == 1 && Mathf.Abs(delta.y) == 2)
                           || (Mathf.Abs(delta.x) == 2 && Mathf.Abs(delta.y) == 1);
                case PieceType.Bishop:
                    return Mathf.Abs(delta.x) == Mathf.Abs(delta.y) && IsPathClear(state, from, target, new Vector2Int(Mathf.Clamp(delta.x, -1, 1), Mathf.Clamp(delta.y, -1, 1)));
                case PieceType.Rook:
                    return (delta.x == 0 || delta.y == 0) && IsPathClear(state, from, target, new Vector2Int(Mathf.Clamp(delta.x, -1, 1), Mathf.Clamp(delta.y, -1, 1)));
                case PieceType.Queen:
                    bool diagonal = Mathf.Abs(delta.x) == Mathf.Abs(delta.y);
                    bool straight = delta.x == 0 || delta.y == 0;
                    return (diagonal || straight) && IsPathClear(state, from, target, new Vector2Int(Mathf.Clamp(delta.x, -1, 1), Mathf.Clamp(delta.y, -1, 1)));
                case PieceType.King:
                    return Mathf.Abs(delta.x) <= 1 && Mathf.Abs(delta.y) <= 1;
                default:
                    return false;
            }
        }

        private static bool IsPathClear(ChessPiece[,] state, Vector2Int from, Vector2Int target, Vector2Int step)
        {
            if (step == Vector2Int.zero)
            {
                return false;
            }

            Vector2Int current = from + step;
            while (current != target)
            {
                if (!state[current.x, current.y].IsEmpty)
                {
                    return false;
                }

                current += step;
            }

            return true;
        }

        private static bool CanLandOn(Vector2Int square, ChessPiece[,] state, PieceColor movingColor)
        {
            if (!IsInside(square))
            {
                return false;
            }

            ChessPiece target = state[square.x, square.y];
            return target.IsEmpty || (target.Color != movingColor && target.Type != PieceType.King);
        }

        private static bool IsInside(Vector2Int square)
        {
            return square.x >= 0 && square.x < 8 && square.y >= 0 && square.y < 8;
        }

        private static PieceColor Opponent(PieceColor color)
        {
            return color == PieceColor.White ? PieceColor.Black : PieceColor.White;
        }

        // ------------------------------------------------------------------
        // Notation
        // ------------------------------------------------------------------

        private static string BuildNotation(
            Vector2Int from, Vector2Int to,
            ChessPiece movingPiece, ChessPiece capturedPiece,
            ChessMove move, PieceType promotionType,
            bool givesCheck, bool isCheckmate)
        {
            StringBuilder builder = new();

            if (move.IsCastling)
            {
                builder.Append(to.x > from.x ? "O-O" : "O-O-O");
            }
            else
            {
                bool isPawn = movingPiece.Type == PieceType.Pawn;
                if (isPawn)
                {
                    if (capturedPiece.Type != PieceType.None)
                    {
                        builder.Append(FileChar(from.x));
                    }
                }
                else
                {
                    builder.Append(PieceLetter(movingPiece.Type));
                }

                if (capturedPiece.Type != PieceType.None)
                {
                    builder.Append('x');
                }

                builder.Append(FileChar(to.x));
                builder.Append(RankChar(to.y));

                if (move.IsPromotion)
                {
                    builder.Append('=');
                    builder.Append(PieceLetter(promotionType == PieceType.None ? PieceType.Queen : promotionType));
                }
            }

            if (isCheckmate)
            {
                builder.Append('#');
            }
            else if (givesCheck)
            {
                builder.Append('+');
            }

            return builder.ToString();
        }

        private static char FileChar(int file) => (char)('a' + file);

        private static char RankChar(int rank) => (char)('1' + rank);

        private static string PieceLetter(PieceType type)
        {
            return type switch
            {
                PieceType.King => "K",
                PieceType.Queen => "Q",
                PieceType.Rook => "R",
                PieceType.Bishop => "B",
                PieceType.Knight => "N",
                _ => string.Empty
            };
        }

        // ------------------------------------------------------------------
        // Undo bookkeeping
        // ------------------------------------------------------------------

        private readonly struct UndoFrame
        {
            public readonly bool IsValid;
            public readonly ChessPiece[,] Board;
            public readonly Vector2Int? EnPassantTarget;
            public readonly PieceColor Turn;
            public readonly PieceColor? Winner;
            public readonly bool IsDraw;
            public readonly string StatusText;
            public readonly int CapturedByWhiteCount;
            public readonly int CapturedByBlackCount;

            public UndoFrame(
                ChessPiece[,] sourceBoard,
                Vector2Int? enPassantTarget,
                PieceColor turn,
                PieceColor? winner,
                bool isDraw,
                string statusText,
                int capturedByWhiteCount,
                int capturedByBlackCount)
            {
                IsValid = true;
                Board = (ChessPiece[,])sourceBoard.Clone();
                EnPassantTarget = enPassantTarget;
                Turn = turn;
                Winner = winner;
                IsDraw = isDraw;
                StatusText = statusText;
                CapturedByWhiteCount = capturedByWhiteCount;
                CapturedByBlackCount = capturedByBlackCount;
            }
        }

        private static readonly Vector2Int[] KnightOffsets =
        {
            new(1, 2), new(2, 1), new(2, -1), new(1, -2),
            new(-1, -2), new(-2, -1), new(-2, 1), new(-1, 2)
        };

        private static readonly Vector2Int[] BishopDirections =
        {
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
        };

        private static readonly Vector2Int[] RookDirections =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1)
        };

        private static readonly Vector2Int[] QueenDirections =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
        };
    }
}
