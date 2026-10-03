using System.Collections.Generic;
using UnityEngine;

// Board, side panel and mouse input, drawn with IMGUI.
public partial class ChessApp
{
    static readonly Color LightSq = new Color32(240, 217, 181, 255);
    static readonly Color DarkSq = new Color32(181, 136, 99, 255);
    static readonly Color LastMoveTint = new Color(0.95f, 0.85f, 0.2f, 0.45f);
    static readonly Color SelectTint = new Color(0.3f, 0.75f, 0.3f, 0.5f);
    static readonly Color CheckTint = new Color(0.9f, 0.1f, 0.1f, 0.6f);
    static readonly Color HintTint = new Color(0.1f, 0.1f, 0.1f, 0.35f);

    readonly Dictionary<char, Texture2D> pieceTex = new Dictionary<char, Texture2D>();
    Texture2D dotTex, ringTex;
    Font uiFont;
    GUIStyle label, small, title, banner, coord, button, box, error_, rich, listButton, heroRow;
    bool stylesReady;

    bool flipped;
    int selected = -1;
    bool dragging;
    Vector2 dragPos;
    string promotionPending; // "e7e8" while the promotion picker is open
    int lastMoveCount = -1;

    Rect boardRect, evalBarRect;
    float sq;

    void InitGui()
    {
        foreach (char c in "KQRBNP")
        {
            pieceTex[c] = Resources.Load<Texture2D>("Pieces/w" + c);
            pieceTex[char.ToLower(c)] = Resources.Load<Texture2D>("Pieces/b" + c);
        }
        dotTex = MakeCircle(64, 0f, 0.18f);
        ringTex = MakeCircle(64, 0.38f, 0.48f);
    }

    static Texture2D MakeCircle(int size, float inner, float outer)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Distance from the centre as a fraction of the texture size, with 1px soft edges.
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / size;
                float a = Mathf.Clamp01((outer - d) * size);
                if (inner > 0) a *= Mathf.Clamp01((d - inner) * size);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        tex.Apply();
        return tex;
    }

    // UI scale (A-/A+ buttons), saved between runs.
    float ui = -1f;
    const float UiMin = 0.8f, UiMax = 2.2f;

    float U(float v) => v * ui;
    int F(int size) => Mathf.RoundToInt(size * ui);

    void SetUiScale(float s)
    {
        ui = Mathf.Clamp(Mathf.Round(s * 20f) / 20f, UiMin, UiMax);
        PlayerPrefs.SetFloat("uiScale", ui);
        stylesReady = false;
    }

    void InitStyles()
    {
        if (ui < 0) ui = Mathf.Clamp(PlayerPrefs.GetFloat("uiScale", 1.35f), UiMin, UiMax);
        if (uiFont == null)
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Apple SD Gothic Neo", "AppleGothic", "Helvetica" }, 16);
        GUI.skin.font = uiFont;
        label = new GUIStyle(GUI.skin.label) { fontSize = F(15), wordWrap = true };
        label.normal.textColor = new Color(0.92f, 0.92f, 0.9f);
        small = new GUIStyle(label) { fontSize = F(13) };
        small.normal.textColor = new Color(0.75f, 0.75f, 0.72f);
        title = new GUIStyle(label) { fontSize = F(22), fontStyle = FontStyle.Bold };
        banner = new GUIStyle(label) { fontSize = F(26), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        banner.normal.textColor = Color.white;
        coord = new GUIStyle(label) { fontSize = F(12), fontStyle = FontStyle.Bold };
        button = new GUIStyle(GUI.skin.button) { fontSize = F(14), fixedHeight = U(28) };
        box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(F(10), F(10), F(8), F(8)) };
        error_ = new GUIStyle(label);
        error_.normal.textColor = new Color(1f, 0.45f, 0.4f);
        rich = new GUIStyle(label) { richText = true, wordWrap = false, fontSize = F(14) };
        listButton = new GUIStyle(GUI.skin.button) { fontSize = F(12), alignment = TextAnchor.MiddleLeft, fixedHeight = U(24) };
        heroRow = new GUIStyle(small) { fontStyle = FontStyle.Bold };
        heroRow.normal.textColor = new Color(1f, 0.85f, 0.35f);
        stylesReady = true;
    }

    void OnGUI()
    {
        if (!stylesReady) InitStyles();
        if (pieceTex.Count == 0) InitGui();

        float margin = U(16);
        float panelW = Mathf.Min(Mathf.Clamp(Screen.width * 0.36f, U(300), U(440)), Screen.width * 0.55f);
        float barW = mode == GameMode.Tournament ? 0f : U(22);
        float barGap = mode == GameMode.Tournament ? 0f : U(8);
        float size = Mathf.Min(Screen.height - 2 * margin, Screen.width - panelW - 3 * margin - barW - barGap);
        size = Mathf.Max(size, 200f);
        sq = Mathf.Floor(size / 8f);
        boardRect = new Rect(margin + barW + barGap, (Screen.height - sq * 8) / 2f, sq * 8, sq * 8);
        evalBarRect = new Rect(margin, boardRect.y, barW, boardRect.height);

        if (referee != null && mode == GameMode.Tournament)
        {
            DrawTournamentBoards();
        }
        else if (referee != null)
        {
            HandleInput();
            DrawBoard();
        }
        DrawPanel(new Rect(boardRect.xMax + margin, margin, panelW, Screen.height - 2 * margin));
    }

    // ---------------------------------------------------------------- board

    Rect SquareRect(int s)
    {
        int f = s & 7, r = s >> 3;
        int col = flipped ? 7 - f : f;
        int row = flipped ? r : 7 - r;
        return new Rect(boardRect.x + col * sq, boardRect.y + row * sq, sq, sq);
    }

    int SquareAt(Vector2 p)
    {
        if (!boardRect.Contains(p)) return -1;
        int col = Mathf.Clamp((int)((p.x - boardRect.x) / sq), 0, 7);
        int row = Mathf.Clamp((int)((p.y - boardRect.y) / sq), 0, 7);
        int f = flipped ? 7 - col : col;
        int r = flipped ? row : 7 - row;
        return r * 8 + f;
    }

    static string SqName(int s) => $"{(char)('a' + (s & 7))}{(char)('1' + (s >> 3))}";
    static int SqIndex(string n) => (n[1] - '1') * 8 + (n[0] - 'a');

    void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    void DrawBoard()
    {
        if (Event.current.type != EventType.Repaint) return;
        var board = referee.Board;

        int lastFrom = -1, lastTo = -1;
        if (moves.Count > 0)
        {
            string lm = moves[moves.Count - 1];
            lastFrom = SqIndex(lm.Substring(0, 2));
            lastTo = SqIndex(lm.Substring(2, 2));
        }
        int checkSq = -1;
        if (referee.InCheck)
            for (int i = 0; i < 64; i++)
                if (board[i] == (referee.WhiteToMove ? 'K' : 'k')) checkSq = i;

        for (int s = 0; s < 64; s++)
        {
            var r = SquareRect(s);
            bool light = ((s & 7) + (s >> 3)) % 2 == 1;
            Fill(r, light ? LightSq : DarkSq);
            if (s == lastFrom || s == lastTo) Fill(r, LastMoveTint);
            if (s == selected) Fill(r, SelectTint);
            if (s == checkSq) Fill(r, CheckTint);
        }
        DrawOverlayTints();

        // Coordinates: files along the bottom row, ranks along the left column,
        // each in the colour of the opposite square so it stays readable.
        for (int i = 0; i < 8; i++)
        {
            int file = flipped ? 7 - i : i;
            int bottomRank = flipped ? 7 : 0;
            coord.normal.textColor = ((file + bottomRank) % 2 == 1) ? DarkSq : LightSq;
            GUI.Label(new Rect(boardRect.x + i * sq + sq - U(14), boardRect.yMax - U(18), U(14), U(18)), ((char)('a' + file)).ToString(), coord);

            int rank = flipped ? i : 7 - i;
            int leftFile = flipped ? 7 : 0;
            coord.normal.textColor = ((leftFile + rank) % 2 == 1) ? DarkSq : LightSq;
            GUI.Label(new Rect(boardRect.x + U(3), boardRect.y + i * sq + 1, U(14), U(18)), ((char)('1' + rank)).ToString(), coord);
        }

        // Legal move hints for the selected piece
        if (selected >= 0)
        {
            string from = SqName(selected);
            foreach (var m in referee.Legal)
            {
                if (!m.StartsWith(from)) continue;
                int to = SqIndex(m.Substring(2, 2));
                var old = GUI.color;
                GUI.color = HintTint;
                GUI.DrawTexture(SquareRect(to), board[to] == '.' ? dotTex : ringTex);
                GUI.color = old;
            }
        }

        // Pieces
        for (int s = 0; s < 64; s++)
        {
            char p = board[s];
            if (p == '.' || (dragging && s == selected)) continue;
            if (pieceTex.TryGetValue(p, out var tex) && tex != null) GUI.DrawTexture(SquareRect(s), tex);
        }
        if (dragging && selected >= 0 && pieceTex.TryGetValue(board[selected], out var dt) && dt != null)
            GUI.DrawTexture(new Rect(dragPos.x - sq / 2, dragPos.y - sq / 2, sq, sq), dt);

        DrawOverlayLabels();
        DrawEvalBarAt(evalBarRect);
        if (promotionPending != null) DrawPromotionPicker();

        if (gameOver && resultText != "")
        {
            var br = new Rect(boardRect.x, boardRect.center.y - 40, boardRect.width, 80);
            Fill(br, new Color(0, 0, 0, 0.7f));
            GUI.Label(br, resultText, banner);
        }
        DrawOverlayTooltip();
    }

    Rect PromotionRect(int i)
    {
        float w = sq * 1.1f;
        float x0 = boardRect.center.x - w * 2;
        return new Rect(x0 + i * w, boardRect.center.y - w / 2, w, w);
    }

    static readonly char[] PromoPieces = { 'q', 'r', 'b', 'n' };

    void DrawPromotionPicker()
    {
        Fill(boardRect, new Color(0, 0, 0, 0.45f));
        for (int i = 0; i < 4; i++)
        {
            var r = PromotionRect(i);
            Fill(r, LightSq);
            char p = humanIsWhite ? char.ToUpper(PromoPieces[i]) : PromoPieces[i];
            if (pieceTex.TryGetValue(p, out var tex) && tex != null) GUI.DrawTexture(r, tex);
        }
    }

    // ---------------------------------------------------------------- input

    void ClearSelection()
    {
        selected = -1;
        dragging = false;
        promotionPending = null;
    }

    bool IsOwnPiece(int s)
    {
        if (s < 0) return false;
        char p = referee.Board[s];
        return p != '.' && char.IsUpper(p) == humanIsWhite;
    }

    void HandleInput()
    {
        var e = Event.current;
        if (e.button != 0) return;

        if (promotionPending != null)
        {
            if (e.type == EventType.MouseDown)
            {
                for (int i = 0; i < 4; i++)
                    if (PromotionRect(i).Contains(e.mousePosition))
                    {
                        string mv = promotionPending + PromoPieces[i];
                        ClearSelection();
                        ApplyMove(mv);
                        e.Use();
                        return;
                    }
                ClearSelection();
                e.Use();
            }
            return;
        }

        if (!HumanToMove)
        {
            if (selected >= 0) ClearSelection();
            return;
        }

        int s = SquareAt(e.mousePosition);
        switch (e.type)
        {
            case EventType.MouseDown:
                if (s < 0) { ClearSelection(); break; }
                if (selected >= 0 && s != selected && TryHumanMove(selected, s)) { e.Use(); break; }
                if (IsOwnPiece(s))
                {
                    selected = s;
                    dragging = true;
                    dragPos = e.mousePosition;
                }
                else
                {
                    ClearSelection();
                }
                e.Use();
                break;
            case EventType.MouseDrag:
                if (dragging)
                {
                    dragPos = e.mousePosition;
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                if (dragging)
                {
                    dragging = false;
                    if (s >= 0 && s != selected) TryHumanMove(selected, s);
                    e.Use();
                }
                break;
        }
    }

    // Returns true if the drop/click was a legal move (or opened the promotion picker).
    bool TryHumanMove(int from, int to)
    {
        string prefix = SqName(from) + SqName(to);
        var candidates = referee.Legal.FindAll(m => m.StartsWith(prefix));
        if (candidates.Count == 0)
        {
            if (!IsOwnPiece(to)) ClearSelection();
            return false;
        }
        if (candidates.Count > 1 || candidates[0].Length == 5)
        {
            promotionPending = prefix;
            dragging = false;
            return true;
        }
        ClearSelection();
        ApplyMove(candidates[0]);
        return true;
    }

    // ---------------------------------------------------------------- panel

    int Cycle(string caption, int idx)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(caption, label, GUILayout.Width(U(70)));
        if (GUILayout.Button("◀", button, GUILayout.Width(U(32)))) idx = (idx - 1 + engines.Count) % engines.Count;
        GUILayout.Label(engines[idx].name, label, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("▶", button, GUILayout.Width(U(32)))) idx = (idx + 1) % engines.Count;
        GUILayout.EndHorizontal();
        return idx;
    }

    bool showSettings = true;

    void DrawSettings()
    {
        GUILayout.BeginVertical(box);
        if (mode == GameMode.HumanVsEngine)
        {
            opponentIdx = Cycle("상대", opponentIdx);
            GUILayout.BeginHorizontal();
            GUILayout.Label("내 색", label, GUILayout.Width(U(70)));
            humanColorChoice = GUILayout.Toolbar(humanColorChoice, new[] { "백", "흑", "랜덤" }, button);
            GUILayout.EndHorizontal();
            GUILayout.Label($"엔진 생각 시간: {thinkSeconds:0.0}초 (짧을수록 약함)", small);
            thinkSeconds = Mathf.Round(GUILayout.HorizontalSlider(thinkSeconds, 0.1f, 5f) * 10f) / 10f;
        }
        else
        {
            whiteIdx = Cycle("백", whiteIdx);
            blackIdx = Cycle("흑", blackIdx);
            GUILayout.Label($"수당 생각 시간: {thinkSeconds:0.0}초", small);
            thinkSeconds = Mathf.Round(GUILayout.HorizontalSlider(thinkSeconds, 0.1f, 5f) * 10f) / 10f;
            GUILayout.Label($"수 사이 대기: {moveDelay:0.0}초", small);
            moveDelay = Mathf.Round(GUILayout.HorizontalSlider(moveDelay, 0f, 3f) * 10f) / 10f;
            randomOpening = GUILayout.Toggle(randomOpening, " 랜덤 오프닝으로 시작 (다음 게임부터)", label);
        }
        GUILayout.Label("설정은 '새 게임'을 눌러야 적용됩니다.", small);
        GUILayout.EndVertical();

    }

    void DrawPanel(Rect area)
    {
        GUILayout.BeginArea(area);
        GUILayout.BeginHorizontal();
        GUILayout.Label("체스 AI", title);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("A−", button, GUILayout.Width(U(40)))) SetUiScale(ui - 0.1f);
        if (GUILayout.Button("A+", button, GUILayout.Width(U(40)))) SetUiScale(ui + 0.1f);
        GUILayout.EndHorizontal();

        if (error != "") GUILayout.Label(error, error_);
        if (referee == null || engines.Count == 0)
        {
            GUILayout.EndArea();
            return;
        }

        GUILayout.Space(6);
        int newMode = GUILayout.Toolbar((int)mode, new[] { "사람 vs 엔진", "엔진 vs 엔진", "토너먼트 관전" }, button);
        if (newMode != (int)mode)
        {
            mode = (GameMode)newMode;
            NewGame();
        }
        GUILayout.Space(6);

        if (mode == GameMode.Tournament)
        {
            DrawTournamentPanel();
            GUILayout.EndArea();
            return;
        }

        if (GUILayout.Button(showSettings ? "▼ 설정 접기" : "▶ 설정 펼치기", listButton)) showSettings = !showSettings;
        if (showSettings) DrawSettings();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("새 게임", button)) NewGame();
        if (GUILayout.Button("판 뒤집기", button)) flipped = !flipped;
        if (mode == GameMode.HumanVsEngine)
        {
            if (GUILayout.Button("기권", button)) Resign();
        }
        else if (GUILayout.Button(paused ? "재개" : "일시정지", button))
        {
            paused = !paused;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.BeginVertical(box);
        GUILayout.Label($"백: {whiteName}", label);
        GUILayout.Label($"흑: {blackName}", label);
        if (openingName != "") GUILayout.Label($"오프닝: {openingName}", small);
        string turn;
        if (gameOver) turn = "결과: " + resultText;
        else if (HumanToMove) turn = $"차례: {(WhiteToMove ? "백" : "흑")} (당신)";
        else if (thinking) turn = $"{infoEngine} 생각 중...";
        else turn = paused ? "일시정지됨" : $"차례: {(WhiteToMove ? "백" : "흑")}";
        GUILayout.Label(turn, label);
        GUILayout.EndVertical();

        GUILayout.BeginHorizontal();
        GUILayout.Label("보드 표시", label, GUILayout.Width(U(80)));
        boardOverlay = GUILayout.Toolbar(boardOverlay, OverlayNames, button);
        GUILayout.EndHorizontal();
        GUILayout.Label(boardOverlay == 1 ? "초록 = 내 평가 함수가 좋게 보는 자리, 빨강 = 나쁜 자리. 말 위에 마우스를 올리면 내역이 보입니다."
                      : boardOverlay == 2 ? "파랑 = 백이 더 많이 공격하는 칸, 빨강 = 흑, 보라 = 팽팽. 숫자는 백:흑 공격 수." : "", small);
        DrawInsightPanel();

        GUILayout.EndArea();
    }
}
