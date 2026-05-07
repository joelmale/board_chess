using NUnit.Framework;
using UnityEngine;

namespace BattleChess.Tests
{
    /// <summary>
    /// Tests for the post-refactor state-tracking APIs: history, captured
    /// pieces, undo, snapshots, the explicit-promotion overload, and notation.
    /// </summary>
    [TestFixture]
    public class ChessRulesStateTests
    {
        private ChessRules rules;

        [SetUp]
        public void SetUp()
        {
            rules = new ChessRules();
            rules.ResetGame();
        }

        // ------------------------------------------------------------------
        // History and last move
        // ------------------------------------------------------------------

        [Test]
        public void History_StartsEmpty()
        {
            Assert.AreEqual(0, rules.History.Count);
            Assert.IsFalse(rules.LastMove.HasValue);
        }

        [Test]
        public void History_GrowsWithEachMove()
        {
            Assert.IsTrue(Move(4, 1, 4, 3));   // e4
            Assert.IsTrue(Move(4, 6, 4, 4));   // e5
            Assert.IsTrue(Move(6, 0, 5, 2));   // Nf3

            Assert.AreEqual(3, rules.History.Count);
            Assert.IsTrue(rules.LastMove.HasValue);
            Assert.AreEqual(PieceType.Knight, rules.LastMove.Value.MovedType);
            Assert.AreEqual(PieceColor.White, rules.LastMove.Value.MovedColor);
        }

        // ------------------------------------------------------------------
        // Captured pieces
        // ------------------------------------------------------------------

        [Test]
        public void CapturedByWhite_TracksBlackPiecesTaken()
        {
            Assert.IsTrue(Move(4, 1, 4, 3));   // e4
            Assert.IsTrue(Move(3, 6, 3, 4));   // d5
            Assert.IsTrue(Move(4, 3, 3, 4));   // exd5

            Assert.AreEqual(1, rules.CapturedByWhite.Count);
            Assert.AreEqual(PieceType.Pawn, rules.CapturedByWhite[0].Type);
            Assert.AreEqual(PieceColor.Black, rules.CapturedByWhite[0].Color);
            Assert.AreEqual(0, rules.CapturedByBlack.Count);
        }

        [Test]
        public void EnPassant_RecordsCapturedPawn()
        {
            Assert.IsTrue(Move(4, 1, 4, 3));   // e4
            Assert.IsTrue(Move(0, 6, 0, 5));   // a6
            Assert.IsTrue(Move(4, 3, 4, 4));   // e5
            Assert.IsTrue(Move(3, 6, 3, 4));   // d5
            Assert.IsTrue(Move(4, 4, 3, 5));   // exd6 e.p.

            Assert.AreEqual(1, rules.CapturedByWhite.Count);
            Assert.AreEqual(PieceType.Pawn, rules.CapturedByWhite[0].Type);
            Assert.AreEqual(PieceColor.Black, rules.CapturedByWhite[0].Color);
        }

        // ------------------------------------------------------------------
        // Undo
        // ------------------------------------------------------------------

        [Test]
        public void Undo_StartsDisabled()
        {
            Assert.IsFalse(rules.CanUndo);
            Assert.IsFalse(rules.TryUndo());
        }

        [Test]
        public void Undo_RestoresPriorPosition()
        {
            Assert.IsTrue(Move(4, 1, 4, 3));   // e4

            Assert.IsTrue(rules.CanUndo);
            Assert.IsTrue(rules.TryUndo());

            Assert.AreEqual(PieceType.Pawn, rules.GetPiece(new Vector2Int(4, 1)).Type);
            Assert.IsTrue(rules.GetPiece(new Vector2Int(4, 3)).IsEmpty);
            Assert.AreEqual(PieceColor.White, rules.Turn);
            Assert.AreEqual(0, rules.History.Count);
            Assert.IsFalse(rules.CanUndo);
        }

        [Test]
        public void Undo_RestoresTurnAndCaptures()
        {
            Assert.IsTrue(Move(4, 1, 4, 3));
            Assert.IsTrue(Move(3, 6, 3, 4));
            Assert.IsTrue(Move(4, 3, 3, 4));   // exd5 - capture

            Assert.AreEqual(1, rules.CapturedByWhite.Count);

            Assert.IsTrue(rules.TryUndo());

            Assert.AreEqual(0, rules.CapturedByWhite.Count);
            Assert.AreEqual(PieceColor.White, rules.Turn);
            Assert.AreEqual(PieceType.Pawn, rules.GetPiece(new Vector2Int(3, 4)).Type);
            Assert.AreEqual(PieceColor.Black, rules.GetPiece(new Vector2Int(3, 4)).Color);
        }

        [Test]
        public void Undo_AfterCheckmate_ResumesPlay()
        {
            // Fool's Mate.
            Assert.IsTrue(Move(5, 1, 5, 2));   // f3
            Assert.IsTrue(Move(4, 6, 4, 4));   // e5
            Assert.IsTrue(Move(6, 1, 6, 3));   // g4
            Assert.IsTrue(Move(3, 7, 7, 3));   // Qh4#

            Assert.AreEqual(PieceColor.Black, rules.Winner);
            Assert.IsTrue(rules.TryUndo());

            Assert.IsFalse(rules.Winner.HasValue);
            Assert.IsFalse(rules.IsDraw);
            Assert.AreEqual(PieceColor.Black, rules.Turn);
            // Black queen back on d8.
            Assert.AreEqual(PieceType.Queen, rules.GetPiece(new Vector2Int(3, 7)).Type);
        }

        // ------------------------------------------------------------------
        // Snapshot round-trip
        // ------------------------------------------------------------------

        [Test]
        public void Snapshot_RoundTrip_RestoresExactState()
        {
            Assert.IsTrue(Move(4, 1, 4, 3));
            Assert.IsTrue(Move(4, 6, 4, 4));
            Assert.IsTrue(Move(6, 0, 5, 2));   // Nf3
            GameSnapshot snapshot = rules.GetSnapshot();

            // Mutate further.
            Assert.IsTrue(Move(1, 7, 2, 5));   // Nc6
            Assert.IsTrue(Move(5, 0, 4, 1));   // Be2

            rules.RestoreFromSnapshot(snapshot);

            Assert.AreEqual(3, rules.History.Count);
            Assert.AreEqual(PieceColor.Black, rules.Turn);
            Assert.AreEqual(PieceType.Knight, rules.GetPiece(new Vector2Int(5, 2)).Type);
            Assert.IsTrue(rules.GetPiece(new Vector2Int(2, 5)).IsEmpty);
            Assert.IsTrue(rules.GetPiece(new Vector2Int(4, 1)).IsEmpty);
            Assert.IsFalse(rules.CanUndo);
        }

        // ------------------------------------------------------------------
        // Explicit promotion API
        // ------------------------------------------------------------------

        [Test]
        public void Promotion_ToKnight_ProducesKnight()
        {
            Assert.IsTrue(Move(4, 1, 4, 3));   // e4
            Assert.IsTrue(Move(3, 6, 3, 4));   // d5
            Assert.IsTrue(Move(4, 3, 3, 4));   // exd5
            Assert.IsTrue(Move(4, 6, 4, 5));   // e6
            Assert.IsTrue(Move(3, 4, 3, 5));   // d6
            Assert.IsTrue(Move(4, 5, 4, 4));   // e5
            Assert.IsTrue(Move(3, 5, 2, 6));   // dxc7
            Assert.IsTrue(Move(0, 6, 0, 5));   // a6

            // Promote by capturing the b8 knight - underpromote to a knight.
            bool ok = rules.TryMove(
                new Vector2Int(2, 6), new Vector2Int(1, 7),
                PieceType.Knight, out _);
            Assert.IsTrue(ok);

            ChessPiece promoted = rules.GetPiece(new Vector2Int(1, 7));
            Assert.AreEqual(PieceType.Knight, promoted.Type);
            Assert.AreEqual(PieceColor.White, promoted.Color);

            Assert.IsTrue(rules.LastMove.HasValue);
            Assert.AreEqual(PieceType.Knight, rules.LastMove.Value.PromotionType);
        }

        // ------------------------------------------------------------------
        // Notation
        // ------------------------------------------------------------------

        [Test]
        public void Notation_PawnPushAndKnightMove()
        {
            Assert.IsTrue(Move(4, 1, 4, 3));   // e4
            Assert.AreEqual("e4", rules.LastMove.Value.Notation);

            Assert.IsTrue(Move(4, 6, 4, 4));   // e5
            Assert.AreEqual("e5", rules.LastMove.Value.Notation);

            Assert.IsTrue(Move(6, 0, 5, 2));   // Nf3
            Assert.AreEqual("Nf3", rules.LastMove.Value.Notation);
        }

        [Test]
        public void Notation_PawnCaptureUsesFile()
        {
            Assert.IsTrue(Move(4, 1, 4, 3));   // e4
            Assert.IsTrue(Move(3, 6, 3, 4));   // d5
            Assert.IsTrue(Move(4, 3, 3, 4));   // exd5

            Assert.AreEqual("exd5", rules.LastMove.Value.Notation);
        }

        [Test]
        public void Notation_CastlingKingside()
        {
            Assert.IsTrue(Move(6, 0, 5, 2));   // Nf3
            Assert.IsTrue(Move(6, 7, 5, 5));   // Nf6
            Assert.IsTrue(Move(4, 1, 4, 2));   // e3
            Assert.IsTrue(Move(4, 6, 4, 5));   // e6
            Assert.IsTrue(Move(5, 0, 4, 1));   // Be2
            Assert.IsTrue(Move(5, 7, 4, 6));   // Be7
            Assert.IsTrue(Move(4, 0, 6, 0));   // O-O

            Assert.AreEqual("O-O", rules.LastMove.Value.Notation);
        }

        [Test]
        public void Notation_CheckmateGetsHash()
        {
            // Fool's Mate.
            Assert.IsTrue(Move(5, 1, 5, 2));   // f3
            Assert.IsTrue(Move(4, 6, 4, 4));   // e5
            Assert.IsTrue(Move(6, 1, 6, 3));   // g4
            Assert.IsTrue(Move(3, 7, 7, 3));   // Qh4#

            StringAssert.EndsWith("#", rules.LastMove.Value.Notation);
            Assert.IsTrue(rules.LastMove.Value.IsCheckmate);
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
    }
}
