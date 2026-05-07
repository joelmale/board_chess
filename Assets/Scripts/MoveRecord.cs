namespace BattleChess
{
    /// <summary>
    /// Immutable record of an applied move. Carries enough information to
    /// re-render the move (notation, last-move highlight) without re-running
    /// the rules engine, and to populate captured-piece tables.
    /// </summary>
    public readonly struct MoveRecord
    {
        public readonly int FromFile;
        public readonly int FromRank;
        public readonly int ToFile;
        public readonly int ToRank;
        public readonly PieceType MovedType;
        public readonly PieceColor MovedColor;
        public readonly PieceType CapturedType;
        public readonly PieceColor CapturedColor;
        public readonly bool IsCastling;
        public readonly bool IsEnPassant;
        public readonly bool IsPromotion;
        public readonly PieceType PromotionType;
        public readonly bool GivesCheck;
        public readonly bool IsCheckmate;
        public readonly string Notation;

        public bool IsCapture => CapturedType != PieceType.None;

        public MoveRecord(
            int fromFile, int fromRank, int toFile, int toRank,
            PieceType movedType, PieceColor movedColor,
            PieceType capturedType, PieceColor capturedColor,
            bool isCastling, bool isEnPassant,
            bool isPromotion, PieceType promotionType,
            bool givesCheck, bool isCheckmate,
            string notation)
        {
            FromFile = fromFile;
            FromRank = fromRank;
            ToFile = toFile;
            ToRank = toRank;
            MovedType = movedType;
            MovedColor = movedColor;
            CapturedType = capturedType;
            CapturedColor = capturedColor;
            IsCastling = isCastling;
            IsEnPassant = isEnPassant;
            IsPromotion = isPromotion;
            PromotionType = promotionType;
            GivesCheck = givesCheck;
            IsCheckmate = isCheckmate;
            Notation = notation;
        }
    }
}
