using System.Collections.Generic;
using UnityEngine;

// Shows what the engine is doing: principal-variation arrows on the board, the search log
// by depth, chessai's search statistics, the evaluation broken into terms, and an eval graph.
public partial class ChessApp
{
    static readonly string[] InsightTabs = { "생각", "평가 분해", "그래프", "기보" };
    static readonly string[] EvalTermNames = { "기물", "위치 (PST)", "폰 구조", "기동성", "룩 (열린 파일)", "비숍 페어", "차례 보너스" };
    static readonly string[] OverlayNames = { "표시 없음", "기물 평가", "장악 지도" };
    static readonly Dictionary<char, string> PieceNames = new Dictionary<char, string>
    {
        { 'P', "폰" }, { 'N', "나이트" }, { 'B', "비숍" }, { 'R', "룩" }, { 'Q', "퀸" }, { 'K', "킹" },
    };

    int insightTab;
    int boardOverlay = 1; // 0 none, 1 piece evaluation, 2 square control
    Vector2 insightScroll;
    GUIStyle overlayText, tooltipText;

    // The PV moves still ahead of the current position (the engine's expected continuation).
    List<string> UpcomingPv(int max)
    {
        var result = new List<string>();
        int offset = moves.Count - pvBasePly;
        if (offset < 0 || offset > pvMoves.Count) return result;
        for (int i = 0; i < offset; i++)
            if (moves[pvBasePly + i] != pvMoves[i]) return result; // game left the predicted line
        for (int i = offset; i < pvMoves.Count && result.Count < max; i++) result.Add(pvMoves[i]);
        return result;
    }

    // ---------------------------------------------------------------- board overlays

    void EnsureOverlayStyles()
    {
        if (overlayText == null)
        {
            overlayText = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            overlayText.normal.textColor = Color.white;
            tooltipText = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false };
            tooltipText.normal.textColor = new Color(0.95f, 0.95f, 0.92f);
        }
        overlayText.fontSize = Mathf.Max(9, Mathf.RoundToInt(sq * 0.16f));
        tooltipText.fontSize = F(14);
    }

    // Small text on a dark plate inside a square corner. anchor: 0 top-right, 1 bottom-left, 2 bottom-right
    void SquareTag(Rect cell, string text, int anchor, Color textColor)
    {
        var size = overlayText.CalcSize(new GUIContent(text));
        float w = size.x + U(4), h = size.y;
        float x = anchor == 1 ? cell.x + 1 : cell.xMax - w - 1;
        float y = anchor == 0 ? cell.y + 1 : cell.yMax - h - 1;
        var r = new Rect(x, y, w, h);
        Fill(r, new Color(0, 0, 0, 0.55f));
        var old = overlayText.normal.textColor;
        overlayText.normal.textColor = textColor;
        GUI.Label(r, text, overlayText);
        overlayText.normal.textColor = old;
    }

    static string Pawns(int cp) => (cp / 100f).ToString("+0.00;-0.00;0.00");

    // Coloured squares, drawn under the pieces.
    void DrawOverlayTints()
    {
        if (referee == null || boardOverlay == 0) return;
        for (int s = 0; s < 64; s++)
        {
            var cell = SquareRect(s);
            if (boardOverlay == 1)
            {
                var pe = referee.PieceEvals[s];
                if (pe == null || Mathf.Abs(pe.Total) < 3) continue;
                float a = 0.15f + 0.45f * Mathf.Min(Mathf.Abs(pe.Total) / 50f, 1f);
                Fill(cell, pe.Total > 0 ? new Color(0.2f, 0.85f, 0.3f, a) : new Color(0.95f, 0.15f, 0.15f, a));
            }
            else
            {
                int w = referee.WhiteControl[s], b = referee.BlackControl[s];
                if (w == 0 && b == 0) continue;
                int diff = w - b;
                Color c = diff > 0 ? new Color(0.2f, 0.5f, 1f) : diff < 0 ? new Color(1f, 0.25f, 0.25f) : new Color(0.7f, 0.35f, 0.9f);
                c.a = diff == 0 ? 0.22f : 0.15f + 0.15f * Mathf.Min(Mathf.Abs(diff), 3);
                Fill(cell, c);
            }
        }
    }

    // Numbers and flags, drawn over the pieces.
    void DrawOverlayLabels()
    {
        if (referee == null || boardOverlay == 0 || sq < 36) return;
        EnsureOverlayStyles();
        for (int s = 0; s < 64; s++)
        {
            var cell = SquareRect(s);
            if (boardOverlay == 1)
            {
                var pe = referee.PieceEvals[s];
                if (pe == null) continue;
                if (char.ToUpper(pe.Piece) != 'K' || pe.Total != 0)
                    SquareTag(cell, Pawns(pe.Total), 0, pe.Total >= 0 ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.6f, 0.6f));
                string flag = FlagText(pe.Flags, true);
                if (flag != "") SquareTag(cell, flag, 1, new Color(1f, 0.85f, 0.35f));
            }
            else
            {
                int w = referee.WhiteControl[s], b = referee.BlackControl[s];
                if (w == 0 && b == 0) continue;
                SquareTag(cell, $"{w}:{b}", 2, Color.white);
            }
        }
    }

    static string FlagText(string flags, bool shortForm)
    {
        var parts = new List<string>();
        foreach (char f in flags)
        {
            switch (f)
            {
                case 'P': parts.Add(shortForm ? "통과" : "통과 폰"); break;
                case 'I': parts.Add(shortForm ? "고립" : "고립 폰"); break;
                case 'D': parts.Add(shortForm ? "중첩" : "중첩 폰"); break;
                case 'O': parts.Add(shortForm ? "열린열" : "열린 파일의 룩"); break;
                case 'S': parts.Add(shortForm ? "반열림" : "반쯤 열린 파일의 룩"); break;
            }
        }
        return string.Join(shortForm ? "·" : ", ", parts);
    }

    // Hover box explaining the square under the mouse.
    void DrawOverlayTooltip()
    {
        if (referee == null || boardOverlay == 0 || dragging || promotionPending != null) return;
        int s = SquareAt(Event.current.mousePosition);
        if (s < 0) return;
        EnsureOverlayStyles();
        var lines = new List<string>();
        if (boardOverlay == 1)
        {
            var pe = referee.PieceEvals[s];
            if (pe == null) return;
            bool white = char.IsUpper(pe.Piece);
            PieceNames.TryGetValue(char.ToUpper(pe.Piece), out string pname);
            lines.Add($"<b>{(white ? "백" : "흑")} {pname} {SqName(s)}</b>  (이 말 주인 기준)");
            lines.Add($"위치 (PST)   {Pawns(pe.Pst)}");
            if (char.ToUpper(pe.Piece) != 'P' && char.ToUpper(pe.Piece) != 'K')
                lines.Add($"기동성       {Pawns(pe.Mobility)}");
            if (pe.Structure != 0 || pe.Flags != "")
                lines.Add($"구조         {Pawns(pe.Structure)}  {FlagText(pe.Flags, false)}");
            lines.Add($"<b>합계         {Pawns(pe.Total)}</b>  (기물 가치는 별도)");
        }
        else
        {
            int w = referee.WhiteControl[s], b = referee.BlackControl[s];
            lines.Add($"<b>{SqName(s)}</b>");
            lines.Add($"백 기물 {w}개, 흑 기물 {b}개가 이 칸을 공격");
            lines.Add(w > b ? "→ 백이 장악" : b > w ? "→ 흑이 장악" : (w == 0 ? "→ 아무도 공격하지 않음" : "→ 팽팽함"));
        }
        string text = string.Join("\n", lines);
        var size = tooltipText.CalcSize(new GUIContent(text));
        var mp = Event.current.mousePosition;
        var r = new Rect(mp.x + U(16), mp.y + U(16), size.x + U(16), size.y + U(10));
        if (r.xMax > Screen.width) r.x = mp.x - r.width - U(8);
        if (r.yMax > Screen.height) r.y = mp.y - r.height - U(8);
        Fill(r, new Color(0.08f, 0.08f, 0.1f, 0.92f));
        GUI.Label(new Rect(r.x + U(8), r.y + U(5), size.x, size.y), text, tooltipText);
    }

    // Vertical bar left of the board: how much of it is white = how good the position is for White.
    void DrawEvalBarAt(Rect r)
    {
        if (referee == null) return;
        bool searched = infoDepth > 0;
        int cp = searched ? infoScoreCp : referee.EvalTotal;
        float frac = 0.5f + 0.5f * (2f / (1f + Mathf.Exp(-cp / 250f)) - 1f); // white share
        Fill(r, new Color(0.22f, 0.22f, 0.24f));
        float wh = r.height * frac;
        var whiteRect = flipped ? new Rect(r.x, r.y, r.width, wh) : new Rect(r.x, r.yMax - wh, r.width, wh);
        Fill(whiteRect, new Color(0.94f, 0.93f, 0.9f));
        Fill(new Rect(r.x, r.y + r.height / 2, r.width, 1), new Color(1f, 0.6f, 0.2f, 0.8f));

        EnsureOverlayStyles();
        string txt = Mathf.Abs(cp) >= 10000 ? "M" : (Mathf.Abs(cp) / 100f).ToString("0.0");
        bool whiteAhead = cp >= 0;
        bool atBottom = whiteAhead != flipped;
        var tr = atBottom ? new Rect(r.x, r.yMax - U(20), r.width, U(20)) : new Rect(r.x, r.y, r.width, U(20));
        var old = overlayText.normal.textColor;
        var oldSize = overlayText.fontSize;
        overlayText.fontSize = F(11);
        overlayText.normal.textColor = whiteAhead ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.95f, 0.95f, 0.95f);
        GUI.Label(tr, txt, overlayText);
        overlayText.normal.textColor = old;
        overlayText.fontSize = oldSize;
    }

    // ---------------------------------------------------------------- panel tabs

    void DrawInsightPanel()
    {
        GUILayout.BeginHorizontal();
        insightTab = GUILayout.Toolbar(insightTab, InsightTabs, button);
        GUILayout.EndHorizontal();
        insightScroll = GUILayout.BeginScrollView(insightScroll, box, GUILayout.ExpandHeight(true));
        switch (insightTab)
        {
            case 0: DrawThinkingTab(); break;
            case 1: DrawEvalBreakTab(); break;
            case 2: DrawGraphTab(); break;
            default: DrawMoveListTab(); break;
        }
        GUILayout.EndScrollView();
    }

    static string Big(long n)
    {
        if (n >= 1_000_000) return (n / 1_000_000f).ToString("0.0") + "M";
        if (n >= 1_000) return (n / 1_000f).ToString("0.0") + "k";
        return n.ToString();
    }

    long Stat(string key) => searchStats.TryGetValue(key, out long v) ? v : 0;

    void DrawThinkingTab()
    {
        if (infoEngine == "")
        {
            GUILayout.Label("엔진이 아직 생각하지 않았습니다.", small);
            return;
        }
        GUILayout.Label($"<b>{infoEngine}</b>{(thinking ? "  생각 중..." : "")}", rich);
        GUILayout.Label($"평가 (백 기준): <b>{(infoScore == "" ? "-" : infoScore)}</b>   깊이 {infoDepth}", rich);
        GUILayout.Label($"본 국면 {infoNodes:N0}개 · 초당 {Big(infoNps)} · {infoTimeMs / 1000f:0.00}초", small);

        var upcoming = UpcomingPv(6);
        if (upcoming.Count > 0)
            GUILayout.Label("예상 수순: " + string.Join("  →  ", upcoming), small);

        if (searchStats.Count > 0)
        {
            GUILayout.Space(U(6));
            GUILayout.Label("<b>탐색 통계</b> (내 엔진 내부에서 측정)", rich);
            long nodes = infoNodes > 0 ? infoNodes : 1;
            long q = Stat("qnodes"), probes = Stat("ttprobes"), hits = Stat("tthits");
            long cut = Stat("cutoffs"), first = Stat("firstcut");
            GUILayout.Label($"· 정지 탐색(QS): 전체의 {100f * q / nodes:0}% ({Big(q)}개)", small);
            GUILayout.Label($"· 해시 테이블: {Big(probes)}번 조회, {(probes > 0 ? 100f * hits / probes : 0):0}% 적중", small);
            GUILayout.Label($"· 수 정렬 정확도: 컷의 {(cut > 0 ? 100f * first / cut : 0):0}%가 첫 번째 수에서 발생", small);
            GUILayout.Label($"· 가지치기: Null Move {Big(Stat("nullcuts"))} · RFP {Big(Stat("rfpcuts"))} · " +
                            $"LMR {Big(Stat("lmr"))} (재탐색 {Big(Stat("lmrresearch"))}) · Delta {Big(Stat("deltaprune"))}", small);
        }
        else if (!infoEngine.StartsWith("chessai"))
        {
            GUILayout.Label("탐색 통계는 chessai(내 엔진)만 보여줍니다.", small);
        }

        if (depthLog.Count > 0)
        {
            GUILayout.Space(U(6));
            GUILayout.Label("<b>깊이별 생각</b> (더 깊이 볼수록 바뀌는 최선수)", rich);
            for (int i = depthLog.Count - 1; i >= 0 && i >= depthLog.Count - 14; i--)
            {
                var d = depthLog[i];
                string mark = d.Changed ? "   <color=#ffd25a>← 생각 바뀜</color>" : "";
                GUILayout.Label($"깊이 {d.Depth,2}   {d.Move,-6}  {d.Score,7}   국면 {Big(d.Nodes)}{mark}", rich);
            }
        }
    }

    void DrawEvalBreakTab()
    {
        if (referee == null || !referee.HasEval)
        {
            GUILayout.Label("평가 분해를 받지 못했습니다. (chessai 엔진이 최신인지 확인)", small);
            return;
        }
        GUILayout.Label("내 평가 함수가 현재 판을 보는 방식 (백 기준, 폰 1개 = 1.00)", small);
        float phasePct = referee.EvalPhase / 24f * 100f;
        GUILayout.Label($"게임 단계: 중반 {phasePct:0}% / 종반 {100 - phasePct:0}%", small);
        GUILayout.Space(U(4));
        for (int i = 0; i < EvalTermNames.Length; i++) DrawEvalBar(EvalTermNames[i], referee.EvalTerms[i], false);
        GUILayout.Space(U(4));
        DrawEvalBar("합계", referee.EvalTotal, true);
        GUILayout.Space(U(6));
        GUILayout.Label("막대가 오른쪽(밝음)이면 백이 유리, 왼쪽(어두움)이면 흑이 유리합니다. " +
                        "탐색 없이 판만 보고 계산한 정적 평가라서 탐색 점수와 다를 수 있습니다.", small);
    }

    void DrawEvalBar(string name, int cp, bool bold)
    {
        var row = GUILayoutUtility.GetRect(10, U(24), GUILayout.ExpandWidth(true));
        float labelW = U(120), valueW = U(56);
        GUI.Label(new Rect(row.x, row.y, labelW, row.height), name, bold ? rich : small);
        var bar = new Rect(row.x + labelW, row.y + U(4), row.width - labelW - valueW, row.height - U(8));
        if (Event.current.type == EventType.Repaint)
        {
            Fill(bar, new Color(1, 1, 1, 0.06f));
            float mid = bar.x + bar.width / 2;
            Fill(new Rect(mid - 1, bar.y - U(2), 2, bar.height + U(4)), new Color(1, 1, 1, 0.35f));
            float frac = Mathf.Clamp(cp / 300f, -1f, 1f);
            float w = Mathf.Abs(frac) * bar.width / 2;
            var fillRect = frac >= 0 ? new Rect(mid, bar.y, w, bar.height) : new Rect(mid - w, bar.y, w, bar.height);
            Fill(fillRect, frac >= 0 ? new Color(0.93f, 0.92f, 0.88f) : new Color(0.25f, 0.25f, 0.27f));
        }
        GUI.Label(new Rect(bar.xMax + U(6), row.y, valueW, row.height), (cp / 100f).ToString("+0.00;-0.00;0.00"), bold ? rich : small);
    }

    void DrawGraphTab()
    {
        GUILayout.Label("수마다 엔진 탐색 점수 (백 기준). 위 = 백 유리, 아래 = 흑 유리", small);
        var r = GUILayoutUtility.GetRect(10, U(170), GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
        {
            Fill(r, new Color(0, 0, 0, 0.25f));
            float mid = r.y + r.height / 2;
            Fill(new Rect(r.x, mid, r.width, 1), new Color(1, 1, 1, 0.4f));
            int n = Mathf.Max(40, evalHistory.Count);
            float bw = r.width / n;
            for (int i = 0; i < evalHistory.Count; i++)
            {
                float v = evalHistory[i];
                if (float.IsNaN(v)) continue;
                // Squash so +-5 pawns fill most of the height and mates hit the edge.
                float y = (2f / (1f + Mathf.Exp(-v / 250f)) - 1f) * (r.height / 2 - 2);
                var bar = y >= 0 ? new Rect(r.x + i * bw, mid - y, Mathf.Max(1, bw - 1), y)
                                 : new Rect(r.x + i * bw, mid, Mathf.Max(1, bw - 1), -y);
                Fill(bar, y >= 0 ? new Color(0.93f, 0.92f, 0.88f) : new Color(0.45f, 0.45f, 0.5f));
            }
            if (moves.Count > 0) Fill(new Rect(r.x + (moves.Count - 1) * bw, r.y, 1, r.height), new Color(1f, 0.8f, 0.2f, 0.8f));
        }
        float last = float.NaN;
        for (int i = evalHistory.Count - 1; i >= 0; i--)
            if (!float.IsNaN(evalHistory[i])) { last = evalHistory[i]; break; }
        GUILayout.Label(float.IsNaN(last) ? "아직 기록 없음" : $"최근 점수: {last / 100f:+0.00;-0.00;0.00}  ({evalHistory.Count}수)", small);
    }

    void DrawMoveListTab()
    {
        for (int i = 0; i < moves.Count; i += 2)
        {
            string line = $"{i / 2 + 1}. {moves[i]}";
            if (i + 1 < moves.Count) line += "   " + moves[i + 1];
            if (i < bookPlies) line += "   (오프닝)";
            GUILayout.Label(line, small);
        }
        if (moves.Count != lastMoveCount)
        {
            lastMoveCount = moves.Count;
            insightScroll.y = float.MaxValue;
        }
    }
}
