using NUnit.Framework;
using UnityEngine;

namespace BattleChess.Tests
{
    /// <summary>
    /// EditMode tests for the chess rules engine. Move sequences use 0-based
    /// (file, rank) coordinates where (0,0) is a1 and (7,7) is h8.
    /// </summary>
    [TestFixture]
    public class ChessRulesTests
    {
        private ChessRules rules;

        [SetUp]
        public void SetUp()
        {
            rules = new ChessRules();
            rules.ResetGame();
        }

        // ------------------------------------------------------------------
        // Opening position
        // ------------------------------------------------------------------

        [Test]
        public void OpeningPosition_StartsWithWhiteToMove()
        {
            Assert.AreEqual(PieceColor.White, rules.Turn);
            Assert.IsFalse(rules.Winner.HasValue);
            Assert.IsFalse(rules.IsDraw);
        }

        [Test]
        public void OpeningPosition_BackRanksArePopulated()
        {
            // White back rank
            Assert.AreEqual(PieceType.Rook,   rules.GetPiece(new Vector2Int(0, 0)).Type);
            Assert.AreEqual(PieceType.Knight, rules.GetPiece(new Vector2Int(1, 0)).Type);
            Assert.AreEqual(PieceType.Bishop, rules.GetPiece(new Vector2Int(2, 0)).Type);
            Assert.AreEqual(PieceType.Queen,  rules.GetPiece(new Vector2Int(3, 0)).Type);
            Assert.AreEqual(PieceType.King,   rules.GetPiece(new Vector2Int(4, 0)).Type);

            // Black king
            ChessPiece blackKing = rules.GetPiece(new Vector2Int(4, 7));
            Assert.AreEqual(PieceType.King,   blackKing.Type);
            Assert.AreEqual(PieceColor.Black, blackKing.Color);

            // Pawns
            for (int file = 0; file < 8; file++)
            {
                Assert.AreEqual(PieceType.Pawn, rules.GetPiece(new Vector2Int(file, 1)).Type);
                Assert.AreEqual(PieceType.Pawn, rules.GetPiece(new Vector2Int(file, 6)).Type);
            }

            // Empty middle
            for (int rank = 2; rank <= 5; rank++)
            {
                for (int file = 0; file < 8; file++)
                {
                    Assert.IsTrue(rules.GetPiece(new Vector2Int(file, rank)).IsEmpty);
                }
            }
        }

        // ------------------------------------------------------------------
        // Pawn movement and turn order
        // ------------------------------------------------------------------

        [Test]
        public void Pawn_TwoStepFromStart_IsLegal()
        {
            Assert.IsTrue(Move(4, 1, 4, 3), "white e2-e4 should be legal");
            Assert.AreEqual(PieceColor.Black, rules.Turn);
            Assert.AreEqual(PieceType.Pawn, rules.GetPiece(new Vector2Int(4, 3)).Type);
            Assert.IsTrue(rules.GetPiece(new Vector2Int(4, 1)).IsEmpty);
        }

        [Test]
        public void Pawn_ThreeSquaresFromStart_IsIllegal()
        {
            Assert.IsFalse(rules.TryMove(new Vector2Int(4, 1), new Vector2Int(4, 4), out _));
            Assert.AreEqual(PieceColor.White, rules.Turn);
        }

        [Test]
        public void Pawn_DiagonalWithoutCapture_IsIllegal()
        {
            Assert.IsFalse(rules.TryMove(new Vector2Int(4, 1), new Vector2Int(5, 2), out string message));
            StringAssert.Contains("Illegal", message);
        }

        [Test]
        public void OutOfTurn_BlackCannotMoveOnFirstTurn()
        {
            Assert.IsFalse(rules.TryMove(new Vector2Int(4, 6), new Vector2Int(4, 4), out _));
            Assert.AreEqual(PieceColor.White, rules.Turn);
        }

        [Test]
        public void Knight_CanJumpFromOpeningPosition()
        {
            // Nf3
            Assert.IsTrue(Move(6, 0, 5, 2));
            Assert.AreEqual(PieceType.Knight, rules.GetPiece(new Vector2Int(5, 2)).Type);
        }

        // ------------------------------------------------------------------
        // Check
        // ------------------------------------------------------------------

        [Test]
        public void Check_KingInCheck_IsDetectedAfterScholarsMateThreat()
        {
            // 1.e4 e5  2.Bc4 Nc6  3.Qh5 Nf6 (defends f7) - no check yet, but
            // we can directly induce a check with a discovery-style line:
            // 1.e4 d5  2.exd5 Qxd5  3.Nc3 Qe5+ (queen pin on king)
            PlayLine(new[]
            {
                "e2-e4", "d7-d5",
                "e4xd5", "d8xd5",
                "b1-c3", "d5-e5"
            });
            Assert.IsTrue(rules.IsInCheck(PieceColor.White));
            StringAssert.Contains("check", rules.StatusText.ToLowerInvariant());
        }

        [Test]
        public void Check_MoveLeavingOwnKingInCheck_IsRejected()
        {
            // Pin the white knight: 1.d4 e6  2.Nc3 Bb4 (pins Nc3 to King)
            PlayLine(new[]
            {
                "d2-d4", "e7-e6",
                "b1-c3", "f8-b4"
            });

            // White cannot move the pinned knight - any Nc3 move exposes the king.
            // Try Nc3-d5: would leave king on e1 in check from Bb4 along a5-e1 diagonal.
            Assert.IsFalse(rules.TryMove(new Vector2Int(2, 2), new Vector2Int(3, 4), out _));
            Assert.AreEqual(PieceColor.White, rules.Turn);
        }

        // ------------------------------------------------------------------
        // Checkmate (Fool's Mate)
        // ------------------------------------------------------------------

        [Test]
        public void Checkmate_FoolsMate_DeclaresBlackWinner()
        {
            PlayLine(new[]
            {
                "f2-f3", "e7-e5",
                "g2-g4", "d8-h4"
            });

            Assert.AreEqual(PieceColor.Black, rules.Winner);
            Assert.IsFalse(rules.IsDraw);
            StringAssert.Contains("checkmate", rules.StatusText.ToLowerInvariant());
        }

        [Test]
        public void Checkmate_GameOver_BlocksFurtherMoves()
        {
            PlayLine(new[]
            {
                "f2-f3", "e7-e5",
                "g2-g4", "d8-h4"
            });

            // After mate, any move must be rejected with a game-over message.
            Assert.IsFalse(rules.TryMove(new Vector2Int(7, 6), new Vector2Int(7, 5), out string message));
            StringAssert.Contains("Game is over", message);
        }

        // ------------------------------------------------------------------
        // Stalemate (Sam Loyd's 10-move stalemate)
        // ------------------------------------------------------------------

        [Test]
        public void Stalemate_SamLoydsLine_DetectsDraw()
        {
            PlayLine(new[]
            {
                "e2-e3", "a7-a5",
                "d1-h5", "a8-a6",
                "h5xa5", "h7-h5",
                "a5xc7", "a6-h6",
                "h2-h4", "f7-f6",
                "c7xd7", "e8-f7",
                "d7xb7", "d8-d3",
                "b7xb8", "d3-h7",
                "b8xc8", "f7-g6",
                "c8-e6"
            });

            Assert.IsTrue(rules.IsDraw);
            Assert.IsFalse(rules.Winner.HasValue);
            StringAssert.Contains("stalemate", rules.StatusText.ToLowerInvariant());
        }

        // ------------------------------------------------------------------
        // Castling
        // ------------------------------------------------------------------

        [Test]
        public void Castling_KingsideWhite_IsLegalWhenPathIsClear()
        {
            PlayLine(new[]
            {
                "g1-f3", "g8-f6",   // knights out
                "e2-e3", "e7-e6",   // pawn move to free bishop
                "f1-e2", "f8-e7"    // bishops out
            });

            // O-O: king e1 to g1
            Assert.IsTrue(Move(4, 0, 6, 0), "kingside castling should be legal");
            Assert.AreEqual(PieceType.King, rules.GetPiece(new Vector2Int(6, 0)).Type);
            Assert.AreEqual(PieceType.Rook, rules.GetPiece(new Vector2Int(5, 0)).Type);
            Assert.IsTrue(rules.GetPiece(new Vector2Int(7, 0)).IsEmpty);
            Assert.IsTrue(rules.GetPiece(new Vector2Int(4, 0)).IsEmpty);
        }

        [Test]
        public void Castling_KingHasMoved_IsRejected()
        {
            PlayLine(new[]
            {
                "g1-f3", "g8-f6",
                "e2-e3", "e7-e6",
                "f1-e2", "f8-e7",
                "e1-f1", "a7-a6",   // white king step (loses castling rights)
                "f1-e1", "a6-a5"    // king back home, but HasMoved is permanent
            });

            // O-O attempt should fail because king has moved.
            Assert.IsFalse(rules.TryMove(new Vector2Int(4, 0), new Vector2Int(6, 0), out _));
            Assert.AreEqual(PieceColor.White, rules.Turn);
        }

        [Test]
        public void Castling_RookHasMoved_DisablesThatSide()
        {
            PlayLine(new[]
            {
                "g1-f3", "g8-f6",
                "e2-e3", "e7-e6",
                "f1-e2", "f8-e7",
                "h1-g1", "a7-a6",   // h-rook moves
                "g1-h1", "a6-a5"    // h-rook returns home but HasMoved is set
            });

            // O-O attempt should fail because the kingside rook has moved.
            Assert.IsFalse(rules.TryMove(new Vector2Int(4, 0), new Vector2Int(6, 0), out _));
        }

        // ------------------------------------------------------------------
        // En passant
        // ------------------------------------------------------------------

        [Test]
        public void EnPassant_RightAfterDoublePush_IsLegal()
        {
            PlayLine(new[]
            {
                "e2-e4", "a7-a6",
                "e4-e5", "d7-d5"   // black pawn jumps two squares beside white e5
            });

            // White exd6 e.p. = (4,4) -> (3,5)
            Assert.IsTrue(Move(4, 4, 3, 5));
            Assert.AreEqual(PieceType.Pawn, rules.GetPiece(new Vector2Int(3, 5)).Type);
            Assert.AreEqual(PieceColor.White, rules.GetPiece(new Vector2Int(3, 5)).Color);
            Assert.IsTrue(rules.GetPiece(new Vector2Int(3, 4)).IsEmpty,
                "captured black pawn should have been removed from d5");
        }

        [Test]
        public void EnPassant_OneMoveLater_IsNoLongerAvailable()
        {
            PlayLine(new[]
            {
                "e2-e4", "a7-a6",
                "e4-e5", "d7-d5",
                "a2-a3", "g8-f6"   // both sides play unrelated moves
            });

            // En passant window closed; exd6 should be illegal now.
            Assert.IsFalse(rules.TryMove(new Vector2Int(4, 4), new Vector2Int(3, 5), out _));
        }

        // ------------------------------------------------------------------
        // Promotion
        // ------------------------------------------------------------------

        [Test]
        public void Promotion_PawnReachingLastRank_BecomesQueen()
        {
            PlayLine(new[]
            {
                "e2-e4", "d7-d5",
                "e4xd5", "e7-e6",
                "d5-d6", "e6-e5",
                "d6xc7", "a7-a6",
                "c7xb8"             // promotes by capturing the b8 knight
            });

            ChessPiece promoted = rules.GetPiece(new Vector2Int(1, 7));
            Assert.AreEqual(PieceType.Queen, promoted.Type);
            Assert.AreEqual(PieceColor.White, promoted.Color);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private bool Move(int fromFile, int fromRank, int toFile, int toRank)
        {
            return rules.TryMove(
                new Vector2Int(fromFile, fromRank),
                new Vector2Int(toFile, toRank),
                out _);
        }

        /// <summary>
        /// Plays a sequence of moves in algebraic-style "e2-e4" or "e4xd5" notation.
        /// Both formats are accepted; the separator is informational only.
        /// </summary>
        private void PlayLine(string[] moves)
        {
            for (int i = 0; i < moves.Length; i++)
            {
                ParseMove(moves[i], out Vector2Int from, out Vector2Int to);
                bool ok = rules.TryMove(from, to, out string message);
                Assert.IsTrue(ok, $"move {i + 1} ({moves[i]}) should be legal but was rejected: {message}");
            }
        }

        private static void ParseMove(string text, out Vector2Int from, out Vector2Int to)
        {
            // Accept "e2-e4", "e4xd5", "e2 e4", etc. We only need the two squares.
            string trimmed = text.Replace("-", " ").Replace("x", " ").Replace("=", " ");
            string[] parts = trimmed.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            Assert.GreaterOrEqual(parts.Length, 2, $"could not parse move '{text}'");
            from = ParseSquare(parts[0]);
            to = ParseSquare(parts[1]);
        }

        private static Vector2Int ParseSquare(string square)
        {
            Assert.AreEqual(2, square.Length, $"expected a 2-char square, got '{square}'");
            int file = square[0] - 'a';
            int rank = square[1] - '1';
            return new Vector2Int(file, rank);
        }
    }
}
