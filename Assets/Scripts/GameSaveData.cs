using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleChess
{
    [Serializable]
    public sealed class GameSaveData
    {
        public int version = 1;
        public SavedPiece[] board = Array.Empty<SavedPiece>();
        public bool hasEnPassantTarget;
        public int enPassantFile;
        public int enPassantRank;
        public PieceColor turn;
        public bool hasWinner;
        public PieceColor winner;
        public bool isDraw;
        public string statusText = string.Empty;
        public int activeMoveCount;
        public List<SavedMoveRecord> history = new();
        public List<SavedPiece> capturedByWhite = new();
        public List<SavedPiece> capturedByBlack = new();

        public static GameSaveData FromSnapshot(GameSnapshot snapshot)
        {
            GameSaveData data = new()
            {
                board = new SavedPiece[64],
                hasEnPassantTarget = snapshot.HasEnPassantTarget,
                enPassantFile = snapshot.EnPassantTarget.x,
                enPassantRank = snapshot.EnPassantTarget.y,
                turn = snapshot.Turn,
                hasWinner = snapshot.HasWinner,
                winner = snapshot.Winner,
                isDraw = snapshot.IsDraw,
                statusText = snapshot.StatusText,
                activeMoveCount = snapshot.ActiveMoveCount
            };

            for (int file = 0; file < 8; file++)
            {
                for (int rank = 0; rank < 8; rank++)
                {
                    data.board[file * 8 + rank] = SavedPiece.FromPiece(snapshot.GetPiece(file, rank));
                }
            }

            for (int i = 0; i < snapshot.History.Count; i++)
            {
                data.history.Add(SavedMoveRecord.FromRecord(snapshot.History[i]));
            }

            for (int i = 0; i < snapshot.CapturedByWhite.Count; i++)
            {
                data.capturedByWhite.Add(SavedPiece.FromPiece(snapshot.CapturedByWhite[i]));
            }

            for (int i = 0; i < snapshot.CapturedByBlack.Count; i++)
            {
                data.capturedByBlack.Add(SavedPiece.FromPiece(snapshot.CapturedByBlack[i]));
            }

            return data;
        }

        public GameSnapshot ToSnapshot()
        {
            ChessPiece[,] snapshotBoard = new ChessPiece[8, 8];
            if (board != null)
            {
                for (int file = 0; file < 8; file++)
                {
                    for (int rank = 0; rank < 8; rank++)
                    {
                        int index = file * 8 + rank;
                        if (index < board.Length)
                        {
                            snapshotBoard[file, rank] = board[index].ToPiece();
                        }
                    }
                }
            }

            List<MoveRecord> restoredHistory = new();
            if (history != null)
            {
                for (int i = 0; i < history.Count; i++)
                {
                    restoredHistory.Add(history[i].ToRecord());
                }
            }

            List<ChessPiece> restoredCapturedByWhite = RestorePieces(capturedByWhite);
            List<ChessPiece> restoredCapturedByBlack = RestorePieces(capturedByBlack);
            Vector2Int? enPassantTarget = hasEnPassantTarget
                ? new Vector2Int(enPassantFile, enPassantRank)
                : (Vector2Int?)null;

            return new GameSnapshot(
                snapshotBoard,
                enPassantTarget,
                turn,
                hasWinner ? winner : (PieceColor?)null,
                isDraw,
                statusText,
                restoredHistory,
                Mathf.Clamp(activeMoveCount, 0, restoredHistory.Count),
                restoredCapturedByWhite,
                restoredCapturedByBlack);
        }

        private static List<ChessPiece> RestorePieces(List<SavedPiece> pieces)
        {
            List<ChessPiece> restored = new();
            if (pieces == null)
            {
                return restored;
            }

            for (int i = 0; i < pieces.Count; i++)
            {
                ChessPiece piece = pieces[i].ToPiece();
                if (!piece.IsEmpty)
                {
                    restored.Add(piece);
                }
            }

            return restored;
        }
    }

    [Serializable]
    public struct SavedPiece
    {
        public PieceType type;
        public PieceColor color;
        public bool hasMoved;

        public static SavedPiece FromPiece(ChessPiece piece)
        {
            return new SavedPiece
            {
                type = piece.Type,
                color = piece.Color,
                hasMoved = piece.HasMoved
            };
        }

        public ChessPiece ToPiece()
        {
            return new ChessPiece
            {
                Type = type,
                Color = color,
                HasMoved = hasMoved
            };
        }
    }

    [Serializable]
    public struct SavedMoveRecord
    {
        public int fromFile;
        public int fromRank;
        public int toFile;
        public int toRank;
        public PieceType movedType;
        public PieceColor movedColor;
        public PieceType capturedType;
        public PieceColor capturedColor;
        public bool isCastling;
        public bool isEnPassant;
        public bool isPromotion;
        public PieceType promotionType;
        public bool givesCheck;
        public bool isCheckmate;
        public string notation;

        public static SavedMoveRecord FromRecord(MoveRecord record)
        {
            return new SavedMoveRecord
            {
                fromFile = record.FromFile,
                fromRank = record.FromRank,
                toFile = record.ToFile,
                toRank = record.ToRank,
                movedType = record.MovedType,
                movedColor = record.MovedColor,
                capturedType = record.CapturedType,
                capturedColor = record.CapturedColor,
                isCastling = record.IsCastling,
                isEnPassant = record.IsEnPassant,
                isPromotion = record.IsPromotion,
                promotionType = record.PromotionType,
                givesCheck = record.GivesCheck,
                isCheckmate = record.IsCheckmate,
                notation = record.Notation
            };
        }

        public MoveRecord ToRecord()
        {
            return new MoveRecord(
                fromFile, fromRank, toFile, toRank,
                movedType, movedColor,
                capturedType, capturedColor,
                isCastling, isEnPassant,
                isPromotion, promotionType,
                givesCheck, isCheckmate,
                notation);
        }
    }
}
