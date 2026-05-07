using System.Collections.Generic;
using UnityEngine;

namespace BattleChess
{
    /// <summary>
    /// Immutable view of the rules engine state. Use <see cref="ChessRules.GetSnapshot"/>
    /// to capture a point in time and <see cref="ChessRules.RestoreFromSnapshot"/> to
    /// rewind to it. The board is exposed as a flat 64-entry array indexed by
    /// <c>file * 8 + rank</c>.
    /// </summary>
    public sealed class GameSnapshot
    {
        private readonly ChessPiece[] board;
        private readonly List<MoveRecord> history;
        private readonly List<ChessPiece> capturedByWhite;
        private readonly List<ChessPiece> capturedByBlack;

        public bool HasEnPassantTarget { get; }
        public Vector2Int EnPassantTarget { get; }
        public PieceColor Turn { get; }
        public bool HasWinner { get; }
        public PieceColor Winner { get; }
        public bool IsDraw { get; }
        public string StatusText { get; }

        public IReadOnlyList<MoveRecord> History => history;
        public IReadOnlyList<ChessPiece> CapturedByWhite => capturedByWhite;
        public IReadOnlyList<ChessPiece> CapturedByBlack => capturedByBlack;

        internal GameSnapshot(
            ChessPiece[,] sourceBoard,
            Vector2Int? enPassantTarget,
            PieceColor turn,
            PieceColor? winner,
            bool isDraw,
            string statusText,
            IReadOnlyList<MoveRecord> sourceHistory,
            IReadOnlyList<ChessPiece> sourceCapturedByWhite,
            IReadOnlyList<ChessPiece> sourceCapturedByBlack)
        {
            board = new ChessPiece[64];
            for (int file = 0; file < 8; file++)
            {
                for (int rank = 0; rank < 8; rank++)
                {
                    board[file * 8 + rank] = sourceBoard[file, rank];
                }
            }

            HasEnPassantTarget = enPassantTarget.HasValue;
            EnPassantTarget = enPassantTarget.GetValueOrDefault();
            Turn = turn;
            HasWinner = winner.HasValue;
            Winner = winner.GetValueOrDefault();
            IsDraw = isDraw;
            StatusText = statusText;

            history = new List<MoveRecord>(sourceHistory);
            capturedByWhite = new List<ChessPiece>(sourceCapturedByWhite);
            capturedByBlack = new List<ChessPiece>(sourceCapturedByBlack);
        }

        public ChessPiece GetPiece(int file, int rank)
        {
            if (file < 0 || file >= 8 || rank < 0 || rank >= 8)
            {
                return default;
            }

            return board[file * 8 + rank];
        }

        internal void CopyBoardInto(ChessPiece[,] destination)
        {
            for (int file = 0; file < 8; file++)
            {
                for (int rank = 0; rank < 8; rank++)
                {
                    destination[file, rank] = board[file * 8 + rank];
                }
            }
        }
    }
}
