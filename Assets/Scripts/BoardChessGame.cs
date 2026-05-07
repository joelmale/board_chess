using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

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
        private CameraView cameraView = CameraView.ThreeQuarter;
        private LearningOverlay learningOverlays = LearningOverlay.LegalMoves;

        private GUIStyle titleStyle;
        private GUIStyle statusStyle;
        private GUIStyle hintStyle;
        private GUIStyle buttonStyle;
        private GUIStyle disabledButtonStyle;
        private GUIStyle sectionStyle;
        private GUIStyle moveHistoryStyle;
        private GUIStyle futureMoveHistoryStyle;
        private GUIStyle capturedPieceStyle;
        private GUIStyle chipStyle;
        private GUIStyle activeChipStyle;
        private GUIStyle panelHeaderStyle;
        private int styledForScreenWidth;
        private int styledForScreenHeight;

        private Camera boardCamera;
        private Transform boardRoot;
        private Transform pieceRoot;
        private readonly Dictionary<Vector2Int, GameObject> pieceObjects = new();
        private readonly Renderer[,] tileRenderers = new Renderer[8, 8];
        private Material lightTileMaterial;
        private Material darkTileMaterial;
        private Material selectedTileMaterial;
        private Material legalMoveTileMaterial;
        private Material lastMoveTileMaterial;
        private Material threatenedTileMaterial;
        private Material riskTileMaterial;
        private Material pinTileMaterial;
        private Material tacticTileMaterial;
        private Material checkTileMaterial;
        private Material whitePieceMaterial;
        private Material blackPieceMaterial;
        private Material boardBaseMaterial;
        private Transform animatedPiece;
        private Vector3 animationFrom;
        private Vector3 animationTo;
        private float animationStartedAt;

        private const float TileSize = 1f;
        private const float TileThickness = 0.08f;
        private const float PieceBaseY = 0.12f;
        private const float MoveAnimationSeconds = 0.22f;
        private const string SaveFileName = "battle-chess-save.json";
        private static readonly Vector3 BoardCenter = Vector3.zero;

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
            Build3DScene();
            Sync3DPieces(false);
            Refresh3DHighlights();
        }

        private void Update()
        {
            BoardLayout layout = CalculateLayout();
            ApplyCameraViewport(layout);
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

            UpdateDraggedPiece();
            UpdatePieceAnimation();
            Refresh3DHighlights();
        }

        private bool HandleCommandContact(Vector2 guiPosition, BoardLayout layout)
        {
            if (layout.ResetRect.Contains(guiPosition))
            {
                ResetGame();
                return true;
            }

            for (int i = 0; i < layout.ViewChoiceRects.Length; i++)
            {
                if (layout.ViewChoiceRects[i].Contains(guiPosition))
                {
                    SetCameraView(CameraViewChoices[i]);
                    return true;
                }
            }

            if (layout.LearningClearRect.Contains(guiPosition))
            {
                learningOverlays = LearningOverlay.None;
                Refresh3DHighlights();
                return true;
            }

            for (int i = 0; i < layout.LearningChoiceRects.Length; i++)
            {
                if (layout.LearningChoiceRects[i].Contains(guiPosition))
                {
                    LearningOverlay choice = LearningOverlayChoices[i];
                    learningOverlays = HasLearningOverlay(choice)
                        ? learningOverlays & ~choice
                        : learningOverlays | choice;
                    Refresh3DHighlights();
                    return true;
                }
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

            if (layout.SaveRect.Contains(guiPosition))
            {
                SaveGame();
                return true;
            }

            if (layout.LoadRect.Contains(guiPosition))
            {
                LoadGame();
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
                Sync3DPieces(false);
                return;
            }

            Vector2Int from = selectedSquare.Value;
            if (destination == from)
            {
                Sync3DPieces(false);
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
                Sync3DPieces(true);
            }
            else
            {
                transientMessage = message;
                transientMessageUntil = Time.unscaledTime + 1.4f;
                Sync3DPieces(false);
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
            Sync3DPieces(false);
            Refresh3DHighlights();
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
            Sync3DPieces(false);
            Refresh3DHighlights();
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
            Sync3DPieces(true);
            Refresh3DHighlights();
        }

        private void SaveGame()
        {
            try
            {
                string path = SavePath;
                string json = JsonUtility.ToJson(GameSaveData.FromSnapshot(rules.GetSnapshot()), true);
                File.WriteAllText(path, json);

                transientMessage = "Game saved";
                transientMessageUntil = Time.unscaledTime + 1.4f;
            }
            catch (System.Exception exception)
            {
                transientMessage = $"Save failed: {exception.Message}";
                transientMessageUntil = Time.unscaledTime + 2.2f;
            }
        }

        private void LoadGame()
        {
            try
            {
                string path = SavePath;
                if (!File.Exists(path))
                {
                    transientMessage = "No saved game";
                    transientMessageUntil = Time.unscaledTime + 1.4f;
                    return;
                }

                string json = File.ReadAllText(path);
                GameSaveData data = JsonUtility.FromJson<GameSaveData>(json);
                if (data == null)
                {
                    transientMessage = "Save file is invalid";
                    transientMessageUntil = Time.unscaledTime + 1.4f;
                    return;
                }

                rules.RestoreFromSnapshot(data.ToSnapshot());
                ClearInteractionState();
                transientMessage = "Game loaded";
                transientMessageUntil = Time.unscaledTime + 1.4f;
                Sync3DPieces(false);
                Refresh3DHighlights();
            }
            catch (System.Exception exception)
            {
                transientMessage = $"Load failed: {exception.Message}";
                transientMessageUntil = Time.unscaledTime + 2.2f;
            }
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
                Sync3DPieces(true);
                return;
            }

            ClearInteractionState();
            transientMessage = message;
            transientMessageUntil = Time.unscaledTime + 1.4f;
            Sync3DPieces(false);
        }

        private void ClearInteractionState()
        {
            selectedSquare = null;
            selectedMoves.Clear();
            activePointerId = null;
            pendingPromotionFrom = null;
            pendingPromotionTo = null;
        }

        private static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        private void Build3DScene()
        {
            Ensure3DMaterials();
            Setup3DCamera();
            Setup3DLighting();

            boardRoot = new GameObject("Generated 3D Chess Board").transform;
            pieceRoot = new GameObject("Generated 3D Chess Pieces").transform;
            boardRoot.SetParent(transform);
            pieceRoot.SetParent(transform);

            GameObject baseObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseObject.name = "Board Base";
            baseObject.transform.SetParent(boardRoot);
            baseObject.transform.position = BoardCenter + new Vector3(0f, -0.08f, 0f);
            baseObject.transform.localScale = new Vector3(8.35f, 0.10f, 8.35f);
            ConfigureBoardRenderer(baseObject.GetComponent<Renderer>(), boardBaseMaterial);

            for (int file = 0; file < 8; file++)
            {
                for (int rank = 0; rank < 8; rank++)
                {
                    GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = $"{SquareName(file, rank)} Tile";
                    tile.transform.SetParent(boardRoot);
                    tile.transform.position = SquareWorldPosition(new Vector2Int(file, rank), TileThickness * 0.5f);
                    tile.transform.localScale = new Vector3(0.97f, TileThickness, 0.97f);
                    tile.AddComponent<SquareView>().Square = new Vector2Int(file, rank);

                    Renderer renderer = tile.GetComponent<Renderer>();
                    ConfigureBoardRenderer(renderer, BaseTileMaterial(file, rank));
                    tileRenderers[file, rank] = renderer;
                }
            }
        }

        private void Ensure3DMaterials()
        {
            if (lightTileMaterial != null)
            {
                return;
            }

            boardBaseMaterial = CreateMaterial("Board Base Material", new Color(0.075f, 0.060f, 0.045f), 0.25f);
            lightTileMaterial = CreateMaterial("Light Tile", new Color(0.70f, 0.61f, 0.44f), 0.28f);
            darkTileMaterial = CreateMaterial("Dark Tile", new Color(0.19f, 0.32f, 0.21f), 0.30f);
            selectedTileMaterial = CreateMaterial("Selected Tile", new Color(0.95f, 0.70f, 0.12f), 0.24f);
            legalMoveTileMaterial = CreateMaterial("Legal Move Tile", new Color(0.88f, 0.66f, 0.18f), 0.24f);
            lastMoveTileMaterial = CreateMaterial("Last Move Tile", new Color(0.16f, 0.43f, 0.68f), 0.28f);
            threatenedTileMaterial = CreateMaterial("Threatened Tile", new Color(0.48f, 0.13f, 0.12f), 0.22f);
            riskTileMaterial = CreateMaterial("At Risk Tile", new Color(0.74f, 0.25f, 0.12f), 0.22f);
            pinTileMaterial = CreateMaterial("Pinned Tile", new Color(0.39f, 0.25f, 0.61f), 0.24f);
            tacticTileMaterial = CreateMaterial("Tactical Warning Tile", new Color(0.74f, 0.50f, 0.10f), 0.22f);
            checkTileMaterial = CreateMaterial("Check Tile", new Color(0.85f, 0.08f, 0.08f), 0.20f);
            whitePieceMaterial = CreateMaterial("White Pieces", new Color(0.90f, 0.84f, 0.68f), 0.48f);
            blackPieceMaterial = CreateMaterial("Black Pieces", new Color(0.11f, 0.10f, 0.085f), 0.42f);
        }

        private void Setup3DCamera()
        {
            boardCamera = Camera.main;
            if (boardCamera == null)
            {
                GameObject cameraObject = new("Main Camera");
                boardCamera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
            }

            boardCamera.clearFlags = CameraClearFlags.SolidColor;
            boardCamera.backgroundColor = new Color(0.08f, 0.11f, 0.09f);
            boardCamera.orthographic = true;
            ApplyCameraView();
        }

        private void SetCameraView(CameraView view)
        {
            cameraView = view;
            ApplyCameraView();
        }

        private void ApplyCameraView()
        {
            if (boardCamera == null)
            {
                return;
            }

            Vector3 offset = cameraView switch
            {
                CameraView.Overhead => new Vector3(0f, 9.8f, 0.01f),
                CameraView.WhiteSide => new Vector3(0f, 5.7f, -7.9f),
                CameraView.BlackSide => new Vector3(0f, 5.7f, 7.9f),
                _ => new Vector3(0.75f, 7.8f, -7.2f)
            };

            Vector3 up = cameraView == CameraView.Overhead ? Vector3.forward : Vector3.up;
            boardCamera.orthographicSize = cameraView == CameraView.Overhead ? 5.05f : 5.65f;
            boardCamera.transform.position = BoardCenter + offset;
            boardCamera.transform.rotation = Quaternion.LookRotation(BoardCenter - boardCamera.transform.position, up);
        }

        private void ApplyCameraViewport(BoardLayout layout)
        {
            if (boardCamera == null)
            {
                return;
            }

            boardCamera.rect = layout.CameraViewport;
        }

        private void Setup3DLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.18f, 0.20f, 0.18f);
            RenderSettings.fog = false;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadowDistance = 20f;
            QualitySettings.shadowCascades = 2;

            GameObject lightObject = new("Board Key Light");
            lightObject.transform.SetParent(transform);
            lightObject.transform.rotation = Quaternion.Euler(44f, -32f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.18f;
            light.color = new Color(1f, 0.92f, 0.78f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.55f;
            light.shadowBias = 0.03f;
            light.shadowNormalBias = 0.25f;
            light.shadowNearPlane = 0.2f;

            GameObject fillObject = new("Board Fill Light");
            fillObject.transform.SetParent(transform);
            fillObject.transform.position = BoardCenter + new Vector3(-3.8f, 4.2f, -4.5f);
            fillObject.transform.rotation = Quaternion.LookRotation(BoardCenter - fillObject.transform.position);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 9f;
            fill.intensity = 0.18f;
            fill.color = new Color(0.55f, 0.70f, 1f);
            fill.shadows = LightShadows.None;
        }

        private void Sync3DPieces(bool animateLastMove)
        {
            animatedPiece = null;

            foreach (GameObject pieceObject in pieceObjects.Values)
            {
                Destroy(pieceObject);
            }

            pieceObjects.Clear();

            MoveRecord? lastMove = rules.LastMove;
            Vector2Int? animatedTo = null;
            Vector3 animatedStart = default;
            if (animateLastMove && lastMove.HasValue)
            {
                MoveRecord move = lastMove.Value;
                animatedTo = new Vector2Int(move.ToFile, move.ToRank);
                animatedStart = SquareWorldPosition(new Vector2Int(move.FromFile, move.FromRank), PieceBaseY);
            }

            for (int file = 0; file < 8; file++)
            {
                for (int rank = 0; rank < 8; rank++)
                {
                    Vector2Int square = new(file, rank);
                    ChessPiece piece = rules.GetPiece(square);
                    if (piece.IsEmpty)
                    {
                        continue;
                    }

                    GameObject pieceObject = CreatePieceObject(piece, square);
                    pieceObjects[square] = pieceObject;

                    if (animatedTo.HasValue && animatedTo.Value == square)
                    {
                        animatedPiece = pieceObject.transform;
                        animationFrom = animatedStart;
                        animationTo = SquareWorldPosition(square, PieceBaseY);
                        animationStartedAt = Time.unscaledTime;
                        animatedPiece.position = animationFrom;
                    }
                }
            }
        }

        private GameObject CreatePieceObject(ChessPiece piece, Vector2Int square)
        {
            GameObject root = new($"{piece.Color} {piece.Type} {SquareName(square.x, square.y)}");
            root.transform.SetParent(pieceRoot);
            root.transform.position = SquareWorldPosition(square, PieceBaseY);
            PieceView view = root.AddComponent<PieceView>();
            view.Square = square;

            Material material = piece.Color == PieceColor.White ? whitePieceMaterial : blackPieceMaterial;
            AddPiecePrimitive(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0f), new Vector3(0.54f, 0.08f, 0.54f), material);
            AddPiecePrimitive(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.19f, 0f), new Vector3(0.38f, 0.07f, 0.38f), material);

            switch (piece.Type)
            {
                case PieceType.Pawn:
                    AddPiecePrimitive(root.transform, PrimitiveType.Sphere, new Vector3(0f, 0.42f, 0f), new Vector3(0.34f, 0.34f, 0.34f), material);
                    break;
                case PieceType.Knight:
                    AddPiecePrimitive(root.transform, PrimitiveType.Capsule, new Vector3(0f, 0.44f, 0.03f), new Vector3(0.26f, 0.34f, 0.26f), material, Quaternion.Euler(0f, 0f, -22f));
                    AddPiecePrimitive(root.transform, PrimitiveType.Cube, new Vector3(0.07f, 0.68f, 0.02f), new Vector3(0.28f, 0.18f, 0.22f), material, Quaternion.Euler(0f, 0f, -18f));
                    break;
                case PieceType.Bishop:
                    AddPiecePrimitive(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.43f, 0f), new Vector3(0.28f, 0.23f, 0.28f), material);
                    AddPiecePrimitive(root.transform, PrimitiveType.Sphere, new Vector3(0f, 0.70f, 0f), new Vector3(0.24f, 0.30f, 0.24f), material);
                    AddPiecePrimitive(root.transform, PrimitiveType.Cube, new Vector3(0.10f, 0.74f, 0f), new Vector3(0.05f, 0.26f, 0.08f), material, Quaternion.Euler(0f, 0f, -28f));
                    break;
                case PieceType.Rook:
                    AddPiecePrimitive(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.45f, 0f), new Vector3(0.33f, 0.25f, 0.33f), material);
                    AddPiecePrimitive(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.74f, 0f), new Vector3(0.56f, 0.13f, 0.56f), material);
                    break;
                case PieceType.Queen:
                    AddPiecePrimitive(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.47f, 0f), new Vector3(0.30f, 0.28f, 0.30f), material);
                    AddPiecePrimitive(root.transform, PrimitiveType.Sphere, new Vector3(0f, 0.78f, 0f), new Vector3(0.34f, 0.24f, 0.34f), material);
                    AddPiecePrimitive(root.transform, PrimitiveType.Sphere, new Vector3(0f, 0.98f, 0f), new Vector3(0.16f, 0.16f, 0.16f), material);
                    break;
                case PieceType.King:
                    AddPiecePrimitive(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.48f, 0f), new Vector3(0.32f, 0.30f, 0.32f), material);
                    AddPiecePrimitive(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.84f, 0f), new Vector3(0.13f, 0.34f, 0.13f), material);
                    AddPiecePrimitive(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.96f, 0f), new Vector3(0.38f, 0.08f, 0.12f), material);
                    break;
            }

            return root;
        }

        private static void AddPiecePrimitive(
            Transform parent,
            PrimitiveType primitive,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            Quaternion? localRotation = null)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.transform.SetParent(parent);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation ?? Quaternion.identity;
            part.transform.localScale = localScale;
            ConfigurePieceRenderer(part.GetComponent<Renderer>(), material);
        }

        private static void ConfigureBoardRenderer(Renderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static void ConfigurePieceRenderer(Renderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void Refresh3DHighlights()
        {
            if (tileRenderers[0, 0] == null)
            {
                return;
            }

            for (int file = 0; file < 8; file++)
            {
                for (int rank = 0; rank < 8; rank++)
                {
                    tileRenderers[file, rank].sharedMaterial = BaseTileMaterial(file, rank);
                }
            }

            ApplyLearningOverlayHighlights();

            MoveRecord? lastMove = rules.LastMove;
            if (lastMove.HasValue)
            {
                MoveRecord move = lastMove.Value;
                SetTileMaterial(new Vector2Int(move.FromFile, move.FromRank), lastMoveTileMaterial);
                SetTileMaterial(new Vector2Int(move.ToFile, move.ToRank), lastMoveTileMaterial);
            }

            if (HasLearningOverlay(LearningOverlay.LegalMoves))
            {
                for (int i = 0; i < selectedMoves.Count; i++)
                {
                    SetTileMaterial(selectedMoves[i].To, legalMoveTileMaterial);
                }
            }

            if (selectedSquare.HasValue)
            {
                SetTileMaterial(selectedSquare.Value, selectedTileMaterial);
            }
        }

        private void ApplyLearningOverlayHighlights()
        {
            if (learningOverlays == LearningOverlay.None)
            {
                return;
            }

            PieceColor focus = rules.Turn;
            PieceColor opponent = OpponentColor(focus);

            if (HasLearningOverlay(LearningOverlay.ThreatenedSquares))
            {
                SetTileMaterials(rules.GetThreatenedSquares(opponent), threatenedTileMaterial);
            }

            if (HasLearningOverlay(LearningOverlay.AtRiskPieces))
            {
                SetTileMaterials(rules.GetAtRiskPieces(focus), riskTileMaterial);
            }

            if (HasLearningOverlay(LearningOverlay.Pins))
            {
                SetTileMaterials(rules.GetPinnedPieces(focus), pinTileMaterial);
            }

            if (HasLearningOverlay(LearningOverlay.Tactics))
            {
                SetTileMaterials(rules.GetHangingHighValuePieces(focus), tacticTileMaterial);
            }

            if (rules.IsInCheck(focus))
            {
                Vector2Int kingSquare = rules.GetKingSquare(focus);
                SetTileMaterial(kingSquare, checkTileMaterial);
                SetTileMaterials(rules.GetAttackersOfSquare(kingSquare, opponent), tacticTileMaterial);
            }
        }

        private void SetTileMaterials(IReadOnlyList<Vector2Int> squares, Material material)
        {
            for (int i = 0; i < squares.Count; i++)
            {
                SetTileMaterial(squares[i], material);
            }
        }

        private void SetTileMaterial(Vector2Int square, Material material)
        {
            if (square.x < 0 || square.x >= 8 || square.y < 0 || square.y >= 8)
            {
                return;
            }

            tileRenderers[square.x, square.y].sharedMaterial = material;
        }

        private void UpdatePieceAnimation()
        {
            if (animatedPiece == null)
            {
                return;
            }

            float t = Mathf.Clamp01((Time.unscaledTime - animationStartedAt) / MoveAnimationSeconds);
            Vector3 position = Vector3.Lerp(animationFrom, animationTo, Mathf.SmoothStep(0f, 1f, t));
            position.y += Mathf.Sin(t * Mathf.PI) * 0.32f;
            animatedPiece.position = position;

            if (t >= 1f)
            {
                animatedPiece.position = animationTo;
                animatedPiece = null;
            }
        }

        private void UpdateDraggedPiece()
        {
            if (!selectedSquare.HasValue || !activePointerId.HasValue)
            {
                return;
            }

            if (!pieceObjects.TryGetValue(selectedSquare.Value, out GameObject pieceObject))
            {
                return;
            }

            if (TryGetBoardPoint(dragPosition, out Vector3 boardPoint))
            {
                boardPoint.y = PieceBaseY + 0.24f;
                pieceObject.transform.position = boardPoint;
            }
        }

        private bool TryGetBoardPoint(Vector2 guiPosition, out Vector3 boardPoint)
        {
            boardPoint = default;
            if (boardCamera == null)
            {
                return false;
            }

            Vector3 screenPosition = new(guiPosition.x, Screen.height - guiPosition.y, 0f);
            if (!boardCamera.pixelRect.Contains(screenPosition))
            {
                return false;
            }

            Ray ray = boardCamera.ScreenPointToRay(screenPosition);
            Plane boardPlane = new(Vector3.up, Vector3.zero);
            if (!boardPlane.Raycast(ray, out float distance))
            {
                return false;
            }

            boardPoint = ray.GetPoint(distance);
            return true;
        }

        private static Material CreateMaterial(string name, Color color, float smoothness)
        {
            Shader shader = ResolveColorShader();
            if (shader == null)
            {
                Debug.LogError("Battle Chess could not find a compatible color shader for generated board materials.");
                return null;
            }

            Material material = new(shader)
            {
                name = name,
                color = color
            };

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            return material;
        }

        private static Shader ResolveColorShader()
        {
            Shader shader = Resources.Load<Shader>("Shaders/BattleChessColor");
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("BattleChess/Color");
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                return shader;
            }

            return Shader.Find("Sprites/Default");
        }

        private Material BaseTileMaterial(int file, int rank)
        {
            return (file + rank) % 2 == 0 ? lightTileMaterial : darkTileMaterial;
        }

        private static Vector3 SquareWorldPosition(Vector2Int square, float y)
        {
            return BoardCenter + new Vector3((square.x - 3.5f) * TileSize, y, (square.y - 3.5f) * TileSize);
        }

        private static string SquareName(int file, int rank)
        {
            return $"{(char)('a' + file)}{rank + 1}";
        }

        private void OnGUI()
        {
            EnsureStyles();
            BoardLayout layout = CalculateLayout();

            DrawSidePanel(layout);
        }

        private void DrawSidePanel(BoardLayout layout)
        {
            Rect panel = layout.PanelRect;
            DrawRect(panel, new Color(0.055f, 0.075f, 0.065f, 0.92f));

            GUI.Label(layout.TitleRect, "Board Chess", titleStyle);

            string status = Time.unscaledTime < transientMessageUntil && !string.IsNullOrEmpty(transientMessage)
                ? transientMessage
                : rules.StatusText;
            GUI.Label(layout.StatusRect, status, statusStyle);

            string feedbackText = BuildFeedbackText();
            GUI.Label(layout.FeedbackRect, feedbackText, hintStyle);
            DrawTurnSummary(layout);

            bool controlsEnabled = !pendingPromotionFrom.HasValue;
            DrawButton(layout.UndoRect, "Undo", controlsEnabled && rules.CanUndo, new Color(0.34f, 0.50f, 0.36f));
            DrawButton(layout.RedoRect, "Redo", controlsEnabled && rules.CanRedo, new Color(0.34f, 0.50f, 0.36f));
            DrawButton(layout.SaveRect, "Save", controlsEnabled, new Color(0.24f, 0.43f, 0.58f));
            DrawButton(layout.LoadRect, "Load", controlsEnabled && File.Exists(SavePath), new Color(0.24f, 0.43f, 0.58f));
            DrawViewSelector(layout);
            DrawLearningSelector(layout);
            DrawCapturedPieces(layout);

            if (pendingPromotionFrom.HasValue)
            {
                DrawPromotionPrompt(layout);
            }

            if (layout.HistoryRect.height >= 42f)
            {
                DrawMoveHistory(layout);
            }

            DrawButton(layout.ResetRect, "Reset", true, new Color(0.78f, 0.34f, 0.20f));
        }

        private void DrawTurnSummary(BoardLayout layout)
        {
            Color turnColor = rules.Turn == PieceColor.White
                ? new Color(0.78f, 0.72f, 0.55f, 0.90f)
                : new Color(0.18f, 0.18f, 0.17f, 0.92f);
            DrawRect(layout.TurnRect, turnColor);
            GUI.Label(layout.TurnRect, $"{rules.Turn} to move", buttonStyle);
        }

        private void DrawViewSelector(BoardLayout layout)
        {
            DrawPanelSection(layout.ViewRect, "View");

            for (int i = 0; i < layout.ViewChoiceRects.Length; i++)
            {
                CameraView choice = CameraViewChoices[i];
                Color color = choice == cameraView
                    ? new Color(0.18f, 0.49f, 0.78f)
                    : new Color(0.34f, 0.50f, 0.36f);
                DrawButton(layout.ViewChoiceRects[i], CameraViewLabel(choice), true, color);
            }
        }

        private void DrawLearningSelector(BoardLayout layout)
        {
            DrawPanelSection(layout.LearningRect, "Learning");
            DrawButton(layout.LearningClearRect, "Clear", learningOverlays != LearningOverlay.None, new Color(0.28f, 0.32f, 0.30f));
            GUI.Label(layout.LearningSummaryRect, BuildLearningSummary(), hintStyle);

            for (int i = 0; i < layout.LearningChoiceRects.Length; i++)
            {
                LearningOverlay choice = LearningOverlayChoices[i];
                bool active = HasLearningOverlay(choice);
                Color color = active
                    ? LearningOverlayColor(choice)
                    : new Color(0.31f, 0.36f, 0.32f);
                DrawToggleButton(layout.LearningChoiceRects[i], LearningOverlayLabel(choice), active, color);
            }
        }

        private void DrawCapturedPieces(BoardLayout layout)
        {
            DrawPanelSection(layout.CapturedRect, "Captured");

            float rowHeight = Mathf.Clamp((layout.CapturedRect.height - 32f) * 0.5f, 12f, 24f);
            Rect whiteRow = new(layout.CapturedRect.x + 10f, layout.CapturedRect.y + 28f, layout.CapturedRect.width - 20f, rowHeight);
            Rect blackRow = new(layout.CapturedRect.x + 10f, whiteRow.yMax, layout.CapturedRect.width - 20f, rowHeight);
            DrawCapturedRow(whiteRow, "White +", rules.CapturedByWhite);
            DrawCapturedRow(blackRow, "Black +", rules.CapturedByBlack);
        }

        private void DrawCapturedRow(Rect rect, string label, IReadOnlyList<ChessPiece> pieces)
        {
            GUI.Label(new Rect(rect.x, rect.y, 58f, rect.height), label, hintStyle);
            string text = BuildCapturedPiecesText(pieces);
            GUI.Label(new Rect(rect.x + 62f, rect.y, rect.width - 62f, rect.height), string.IsNullOrEmpty(text) ? "-" : text, capturedPieceStyle);
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
            DrawPanelSection(layout.HistoryRect, "Move History");

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

        private void DrawToggleButton(Rect rect, string label, bool active, Color color)
        {
            DrawRect(rect, active ? color : new Color(0.22f, 0.27f, 0.24f, 0.86f));
            GUI.Label(rect, active ? $"On {label}" : label, active ? activeChipStyle : chipStyle);
        }

        private void DrawPanelSection(Rect rect, string title)
        {
            DrawRect(rect, new Color(0.015f, 0.025f, 0.022f, 0.45f));
            GUI.Label(new Rect(rect.x + 10f, rect.y + 5f, rect.width - 20f, 22f), title, panelHeaderStyle);
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

        private string BuildLearningSummary()
        {
            if (learningOverlays == LearningOverlay.None)
            {
                return "No overlays active";
            }

            System.Text.StringBuilder builder = new();
            for (int i = 0; i < LearningOverlayChoices.Length; i++)
            {
                LearningOverlay choice = LearningOverlayChoices[i];
                if (!HasLearningOverlay(choice))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(" + ");
                }

                builder.Append(LearningOverlayLabel(choice));
            }

            return builder.ToString();
        }

        private bool HasLearningOverlay(LearningOverlay overlay)
        {
            return (learningOverlays & overlay) == overlay;
        }

        private bool TryGetSquare(Vector2 guiPosition, out Vector2Int square)
        {
            BoardLayout layout = CalculateLayout();
            square = default;

            if (layout.PanelRect.Contains(guiPosition) || boardCamera == null)
            {
                return false;
            }

            Vector3 screenPosition = new(guiPosition.x, Screen.height - guiPosition.y, 0f);
            if (!boardCamera.pixelRect.Contains(screenPosition))
            {
                return false;
            }

            Ray ray = boardCamera.ScreenPointToRay(screenPosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                PieceView pieceView = hits[i].collider.GetComponentInParent<PieceView>();
                if (pieceView != null)
                {
                    if (selectedSquare.HasValue && activePointerId.HasValue && pieceView.Square == selectedSquare.Value)
                    {
                        continue;
                    }

                    square = pieceView.Square;
                    return true;
                }

                SquareView squareView = hits[i].collider.GetComponentInParent<SquareView>();
                if (squareView != null)
                {
                    square = squareView.Square;
                    return true;
                }
            }

            if (!TryGetBoardPoint(guiPosition, out Vector3 boardPoint))
            {
                return false;
            }

            float originX = BoardCenter.x - TileSize * 4f;
            float originZ = BoardCenter.z - TileSize * 4f;
            int file = Mathf.FloorToInt((boardPoint.x - originX) / TileSize);
            int rank = Mathf.FloorToInt((boardPoint.z - originZ) / TileSize);
            if (file < 0 || file >= 8 || rank < 0 || rank >= 8)
            {
                return false;
            }

            square = new Vector2Int(file, rank);
            return true;
        }

        private static BoardLayout CalculateLayout()
        {
            float margin = Mathf.Clamp(Screen.height * 0.026f, 10f, 28f);
            float panelHeight = Screen.height - margin * 2f;
            float maxPanelWidth = Mathf.Min(430f, Screen.width * 0.46f);
            float minPanelWidth = Mathf.Min(300f, maxPanelWidth);
            float panelWidth = Mathf.Clamp(Screen.width * 0.27f, minPanelWidth, maxPanelWidth);
            float panelX = margin;
            float panelY = margin;
            Rect panelRect = new(panelX, panelY, panelWidth, panelHeight);

            float cameraX = Mathf.Clamp((panelRect.xMax + margin * 0.65f) / Screen.width, 0.23f, 0.62f);
            Rect cameraViewport = new(cameraX, 0f, 1f - cameraX - margin / Screen.width, 1f);

            float scale = Mathf.Clamp(panelHeight / 820f, 0.55f, 1f);
            float pad = Mathf.Clamp(panelWidth * 0.06f, 16f, 24f);
            float gap = Mathf.Clamp(10f * scale, 4f, 10f);
            float contentX = panelX + pad;
            float contentWidth = panelWidth - pad * 2f;
            float y = panelY + pad;

            float titleHeight = Mathf.Clamp(42f * scale, 24f, 42f);
            Rect titleRect = new(contentX, y, contentWidth, titleHeight);
            y += titleHeight + gap * 0.65f;

            float statusHeight = Mathf.Clamp(52f * scale, 28f, 52f);
            Rect statusRect = new(contentX, y, contentWidth, statusHeight);
            y += statusHeight + gap * 0.65f;

            float feedbackHeight = Mathf.Clamp(38f * scale, 22f, 38f);
            Rect feedbackRect = new(contentX, y, contentWidth, feedbackHeight);
            y += feedbackHeight + gap;

            float buttonHeight = Mathf.Clamp(40f * scale, 26f, 40f);
            Rect turnRect = new(contentX, y, contentWidth, Mathf.Clamp(34f * scale, 24f, 34f));
            y += turnRect.height + gap;

            float halfWidth = (contentWidth - gap) * 0.5f;
            Rect undoRect = new(contentX, y, halfWidth, buttonHeight);
            Rect redoRect = new(undoRect.xMax + gap, y, halfWidth, buttonHeight);
            y += buttonHeight + gap * 0.7f;

            Rect saveRect = new(contentX, y, halfWidth, buttonHeight);
            Rect loadRect = new(saveRect.xMax + gap, y, halfWidth, buttonHeight);
            y += buttonHeight + gap;

            float sectionHeaderHeight = Mathf.Clamp(28f * scale, 22f, 28f);
            float viewRectHeight = Mathf.Clamp(68f * scale, 48f, 68f);
            Rect viewRect = new(contentX, y, contentWidth, viewRectHeight);
            Rect[] viewChoiceRects = new Rect[CameraViewChoices.Length];
            float viewChoiceGap = 6f;
            float viewChoiceWidth = (viewRect.width - viewChoiceGap * 3f - 20f) / 4f;
            float viewButtonHeight = Mathf.Clamp(28f * scale, 20f, 28f);
            for (int i = 0; i < viewChoiceRects.Length; i++)
            {
                viewChoiceRects[i] = new Rect(viewRect.x + 10f + i * (viewChoiceWidth + viewChoiceGap), viewRect.y + sectionHeaderHeight, viewChoiceWidth, viewButtonHeight);
            }
            y += viewRect.height + gap;

            float learningRectHeight = Mathf.Clamp(124f * scale, 86f, 124f);
            Rect learningRect = new(contentX, y, contentWidth, learningRectHeight);
            Rect learningClearRect = new(learningRect.xMax - 62f, learningRect.y + 4f, 52f, Mathf.Clamp(22f * scale, 18f, 22f));
            Rect learningSummaryRect = new(learningRect.x + 10f, learningRect.y + sectionHeaderHeight, learningRect.width - 20f, Mathf.Clamp(18f * scale, 14f, 18f));
            Rect[] learningChoiceRects = new Rect[LearningOverlayChoices.Length];
            float learningGap = 6f;
            float learningButtonWidth = (learningRect.width - learningGap * 2f - 20f) / 3f;
            float learningButtonHeight = Mathf.Clamp(28f * scale, 20f, 28f);
            for (int i = 0; i < learningChoiceRects.Length; i++)
            {
                int column = i % 3;
                int row = i / 3;
                learningChoiceRects[i] = new Rect(
                    learningRect.x + 10f + column * (learningButtonWidth + learningGap),
                    learningRect.y + sectionHeaderHeight + 18f + row * (learningButtonHeight + learningGap),
                    learningButtonWidth,
                    learningButtonHeight);
            }
            y += learningRect.height + gap;

            float capturedHeight = Mathf.Clamp(84f * scale, 58f, 84f);
            Rect capturedRect = new(contentX, y, contentWidth, capturedHeight);
            Rect promotionRect = new(contentX, y, contentWidth, 58f);
            Rect[] promotionChoiceRects = new Rect[PromotionChoices.Length];
            float choiceGap = 6f;
            float choiceWidth = (promotionRect.width - choiceGap * 3f - 20f) / 4f;
            for (int i = 0; i < promotionChoiceRects.Length; i++)
            {
                promotionChoiceRects[i] = new Rect(promotionRect.x + 10f + i * (choiceWidth + choiceGap), promotionRect.y + 28f, choiceWidth, 24f);
            }
            y += capturedRect.height + gap;

            float resetHeight = Mathf.Clamp(50f * scale, 34f, 50f);
            Rect resetRect = new(contentX, panelRect.yMax - pad - resetHeight, contentWidth, resetHeight);
            Rect historyRect = new(contentX, y, contentWidth, Mathf.Max(0f, resetRect.y - y - gap));

            return new BoardLayout(
                panelRect, titleRect, statusRect, feedbackRect,
                resetRect, undoRect, redoRect, historyRect,
                promotionRect, promotionChoiceRects, viewRect, viewChoiceRects,
                turnRect, learningRect, learningClearRect, learningSummaryRect, learningChoiceRects, capturedRect,
                cameraViewport, saveRect, loadRect);
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

        private static string CameraViewLabel(CameraView view)
        {
            return view switch
            {
                CameraView.Overhead => "Top",
                CameraView.WhiteSide => "White",
                CameraView.BlackSide => "Black",
                _ => "3/4"
            };
        }

        private static string LearningOverlayLabel(LearningOverlay overlay)
        {
            return overlay switch
            {
                LearningOverlay.LegalMoves => "Moves",
                LearningOverlay.ThreatenedSquares => "Threats",
                LearningOverlay.AtRiskPieces => "Risk",
                LearningOverlay.Pins => "Pins",
                LearningOverlay.Tactics => "Tactics",
                _ => "Off"
            };
        }

        private static Color LearningOverlayColor(LearningOverlay overlay)
        {
            return overlay switch
            {
                LearningOverlay.ThreatenedSquares => new Color(0.55f, 0.18f, 0.16f),
                LearningOverlay.AtRiskPieces => new Color(0.84f, 0.32f, 0.18f),
                LearningOverlay.Pins => new Color(0.45f, 0.30f, 0.70f),
                LearningOverlay.Tactics => new Color(0.82f, 0.58f, 0.14f),
                LearningOverlay.LegalMoves => new Color(0.18f, 0.49f, 0.78f),
                _ => new Color(0.36f, 0.42f, 0.38f)
            };
        }

        private static string BuildCapturedPiecesText(IReadOnlyList<ChessPiece> pieces)
        {
            if (pieces.Count == 0)
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = new();
            for (int i = 0; i < pieces.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(PieceLabel(pieces[i]));
            }

            return builder.ToString();
        }

        private static PieceColor OpponentColor(PieceColor color)
        {
            return color == PieceColor.White ? PieceColor.Black : PieceColor.White;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null && styledForScreenWidth == Screen.width && styledForScreenHeight == Screen.height)
            {
                return;
            }

            styledForScreenWidth = Screen.width;
            styledForScreenHeight = Screen.height;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = UiFontSize(34f, 22, 34),
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.96f, 0.90f, 0.74f) }
            };

            statusStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = UiFontSize(27f, 18, 27),
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(0.98f, 0.96f, 0.88f) }
            };

            hintStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = UiFontSize(17f, 12, 17),
                wordWrap = true,
                normal = { textColor = new Color(0.78f, 0.82f, 0.70f) }
            };

            buttonStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = UiFontSize(20f, 13, 20),
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white }
            };

            disabledButtonStyle = new GUIStyle(buttonStyle)
            {
                normal = { textColor = new Color(0.72f, 0.76f, 0.70f) }
            };

            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = UiFontSize(15f, 11, 15),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.88f, 0.70f) }
            };

            panelHeaderStyle = new GUIStyle(sectionStyle)
            {
                normal = { textColor = new Color(0.88f, 0.90f, 0.78f) }
            };

            moveHistoryStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = UiFontSize(15f, 11, 15),
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.96f, 0.96f, 0.88f) }
            };

            futureMoveHistoryStyle = new GUIStyle(moveHistoryStyle)
            {
                normal = { textColor = new Color(0.58f, 0.62f, 0.56f) }
            };

            capturedPieceStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = UiFontSize(17f, 12, 17),
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.96f, 0.92f, 0.78f) }
            };

            chipStyle = new GUIStyle(buttonStyle)
            {
                fontSize = UiFontSize(16f, 12, 16),
                normal = { textColor = new Color(0.88f, 0.92f, 0.84f) }
            };

            activeChipStyle = new GUIStyle(chipStyle)
            {
                normal = { textColor = Color.white }
            };
        }

        private static int UiFontSize(float baseSize, int min, int max)
        {
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.72f, 1f);
            return Mathf.Clamp(Mathf.RoundToInt(baseSize * scale), min, max);
        }

        private readonly struct BoardLayout
        {
            public readonly Rect PanelRect;
            public readonly Rect TitleRect;
            public readonly Rect StatusRect;
            public readonly Rect FeedbackRect;
            public readonly Rect ResetRect;
            public readonly Rect UndoRect;
            public readonly Rect RedoRect;
            public readonly Rect HistoryRect;
            public readonly Rect PromotionRect;
            public readonly Rect[] PromotionChoiceRects;
            public readonly Rect ViewRect;
            public readonly Rect[] ViewChoiceRects;
            public readonly Rect TurnRect;
            public readonly Rect LearningRect;
            public readonly Rect LearningClearRect;
            public readonly Rect LearningSummaryRect;
            public readonly Rect[] LearningChoiceRects;
            public readonly Rect CapturedRect;
            public readonly Rect CameraViewport;
            public readonly Rect SaveRect;
            public readonly Rect LoadRect;

            public BoardLayout(
                Rect panelRect,
                Rect titleRect,
                Rect statusRect,
                Rect feedbackRect,
                Rect resetRect,
                Rect undoRect,
                Rect redoRect,
                Rect historyRect,
                Rect promotionRect,
                Rect[] promotionChoiceRects,
                Rect viewRect,
                Rect[] viewChoiceRects,
                Rect turnRect,
                Rect learningRect,
                Rect learningClearRect,
                Rect learningSummaryRect,
                Rect[] learningChoiceRects,
                Rect capturedRect,
                Rect cameraViewport,
                Rect saveRect,
                Rect loadRect)
            {
                PanelRect = panelRect;
                TitleRect = titleRect;
                StatusRect = statusRect;
                FeedbackRect = feedbackRect;
                ResetRect = resetRect;
                UndoRect = undoRect;
                RedoRect = redoRect;
                HistoryRect = historyRect;
                PromotionRect = promotionRect;
                PromotionChoiceRects = promotionChoiceRects;
                ViewRect = viewRect;
                ViewChoiceRects = viewChoiceRects;
                TurnRect = turnRect;
                LearningRect = learningRect;
                LearningClearRect = learningClearRect;
                LearningSummaryRect = learningSummaryRect;
                LearningChoiceRects = learningChoiceRects;
                CapturedRect = capturedRect;
                CameraViewport = cameraViewport;
                SaveRect = saveRect;
                LoadRect = loadRect;
            }
        }

        private enum CameraView
        {
            Overhead,
            ThreeQuarter,
            WhiteSide,
            BlackSide
        }

        [System.Flags]
        private enum LearningOverlay
        {
            None = 0,
            LegalMoves = 1 << 0,
            ThreatenedSquares = 1 << 1,
            AtRiskPieces = 1 << 2,
            Pins = 1 << 3,
            Tactics = 1 << 4
        }

        private static readonly CameraView[] CameraViewChoices =
        {
            CameraView.Overhead,
            CameraView.ThreeQuarter,
            CameraView.WhiteSide,
            CameraView.BlackSide
        };

        private static readonly LearningOverlay[] LearningOverlayChoices =
        {
            LearningOverlay.LegalMoves,
            LearningOverlay.ThreatenedSquares,
            LearningOverlay.AtRiskPieces,
            LearningOverlay.Pins,
            LearningOverlay.Tactics
        };

        private static readonly PieceType[] PromotionChoices =
        {
            PieceType.Queen,
            PieceType.Rook,
            PieceType.Bishop,
            PieceType.Knight
        };
    }
}
