using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

[Serializable]
public class LiveBoard
{
    public int slot, game, round, depth;
    public string white, black, fen, lastMove, eval, opening, status, result, termination;
    public float whiteClock, blackClock;
    public string[] moves;
}

[Serializable]
public class StandingRow
{
    public string name, lossReasons;
    public int games, w, d, l, rating, ci, anchorElo;
    public float score, whiteScore, blackScore, avgPlies, avgDepth;
}

[Serializable]
public class CrossRow
{
    public string[] cells;
}

[Serializable]
public class Standings
{
    public string name, mode, tc;
    public int total, done, concurrency;
    public bool running, anchored, stopping;
    public StandingRow[] engines;
    public string[] crossNames;
    public CrossRow[] cross;
}

[Serializable]
public class FinishedGame
{
    public int id, round, plies, bookPlies;
    public string white, black, result, termination, opening;
    public string[] moves;
}

// "토너먼트 관전": shows the games tools/tournament.py is playing (matches/live/*.json),
// the standings, and lets you replay finished games. Games run headless at full speed;
// this view only reads their state.
public partial class ChessApp
{
    static readonly string[] TcOptions = { "10+0.1", "30+0.3", "60+0.6" };
    static readonly string[] TcLabels = { "10초 (빠름)", "30초 (보기 좋음)", "60초" };

    // Presets: name, mode, engine list
    static readonly string[][] Presets =
    {
        new[] { "내 엔진 vs 모든 엔진 (건틀릿)", "gauntlet",
            "sf-1600,sf-2000,sf-2400,sf-2800,stash-v17.0,stash-v21.0,stash-v25.0,stash-v37.0,weiss" },
        new[] { "모든 엔진끼리 (리그전)", "roundrobin",
            "chessai,sf-2000,sf-2400,stash-v17.0,stash-v21.0,stash-v25.0,weiss" },
        new[] { "가지치기 효과 (켬 vs 끔)", "gauntlet", "chessai-nopruning" },
    };

    string LiveDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "matches", "live"));
    string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

    int presetIdx = 0;
    int tcIdx = 1;
    int gamesPerPair = 10;
    int concurrency = 4;

    Standings standings;
    readonly List<LiveBoard> liveBoards = new List<LiveBoard>();
    readonly List<FinishedGame> finished = new List<FinishedGame>();
    long gamesFileLength;
    float nextPoll;
    string tourMessage = "";
    Process tourProcess;

    int focusedSlot = -1;          // live board shown large, -1 = grid
    FinishedGame replay;           // finished game being replayed
    int replayPly;
    int replayBoardPly = -1;
    readonly char[] replayBoard = new char[64];
    bool replayAuto;
    float replayNext;
    Vector2 standingsScroll, gamesScroll;
    readonly char[] scratchBoard = new char[64];

    // ---------------------------------------------------------------- data

    void TournamentUpdate()
    {
        if (Time.time >= nextPoll)
        {
            nextPoll = Time.time + 0.3f;
            PollLive();
        }
        if (replay != null && replayAuto && Time.time >= replayNext)
        {
            replayNext = Time.time + 0.6f;
            if (replayPly < replay.moves.Length) replayPly++;
            else replayAuto = false;
        }
    }

    void PollLive()
    {
        try
        {
            string dir = LiveDir;
            if (!Directory.Exists(dir)) return;

            string sPath = Path.Combine(dir, "standings.json");
            if (File.Exists(sPath)) standings = JsonUtility.FromJson<Standings>(File.ReadAllText(sPath));

            liveBoards.Clear();
            foreach (var f in Directory.GetFiles(dir, "board_*.json"))
            {
                var b = JsonUtility.FromJson<LiveBoard>(File.ReadAllText(f));
                if (b != null) liveBoards.Add(b);
            }
            liveBoards.Sort((a, b) => a.slot.CompareTo(b.slot));

            string gPath = Path.Combine(dir, "games.jsonl");
            if (File.Exists(gPath))
            {
                var info = new FileInfo(gPath);
                if (info.Length < gamesFileLength)
                {
                    finished.Clear();
                    gamesFileLength = 0;
                }
                if (info.Length > gamesFileLength)
                {
                    using (var fs = new FileStream(gPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Seek(gamesFileLength, SeekOrigin.Begin);
                        using (var sr = new StreamReader(fs))
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (line.Trim().Length == 0) continue;
                                var g = JsonUtility.FromJson<FinishedGame>(line);
                                if (g != null) finished.Add(g);
                            }
                        }
                    }
                    gamesFileLength = info.Length;
                }
            }
            else if (finished.Count > 0)
            {
                finished.Clear();
                gamesFileLength = 0;
            }
        }
        catch (Exception e)
        {
            // Files are replaced atomically, but a read can still race a rename; try again next poll.
            tourMessage = "읽기 재시도: " + e.Message;
        }
    }

    bool TournamentRunning => tourProcess != null && !tourProcess.HasExited || (standings != null && standings.running);

    static string FindPython()
    {
        string[] candidates =
        {
            "/Library/Frameworks/Python.framework/Versions/3.13/bin/python3",
            "/opt/homebrew/bin/python3",
            "/usr/local/bin/python3",
            "/usr/bin/python3",
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return "python3";
    }

    void StartTournament()
    {
        var p = Presets[presetIdx];
        string args = $"tools/tournament.py --mode {p[1]} --hero chessai --engines {p[2]} " +
                      $"--games {gamesPerPair} --tc {TcOptions[tcIdx]} --concurrency {concurrency}";
        try
        {
            var psi = new ProcessStartInfo("/bin/sh",
                $"-c \"'{FindPython()}' {args} > matches/tournament.log 2>&1\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = ProjectRoot,
            };
            Directory.CreateDirectory(Path.Combine(ProjectRoot, "matches"));
            tourProcess = Process.Start(psi);
            finished.Clear();
            gamesFileLength = 0;
            replay = null;
            focusedSlot = -1;
            tourMessage = "토너먼트 시작: " + p[0];
        }
        catch (Exception e)
        {
            tourMessage = "시작 실패: " + e.Message;
        }
    }

    void StopTournament()
    {
        File.WriteAllText(Path.Combine(ProjectRoot, "matches", "STOP"), "stop");
        tourMessage = "중지 요청됨: 지금 두고 있는 판만 끝나면 멈춥니다.";
    }

    // ---------------------------------------------------------------- drawing

    void DrawBoardAt(Rect r, char[] board, string lastMove, bool flip)
    {
        float s = r.width / 8f;
        int lf = -1, lt = -1;
        if (!string.IsNullOrEmpty(lastMove) && lastMove.Length >= 4)
        {
            lf = SqIndex(lastMove.Substring(0, 2));
            lt = SqIndex(lastMove.Substring(2, 2));
        }
        for (int sqi = 0; sqi < 64; sqi++)
        {
            int f = sqi & 7, rk = sqi >> 3;
            int col = flip ? 7 - f : f, row = flip ? rk : 7 - rk;
            var cell = new Rect(r.x + col * s, r.y + row * s, s, s);
            Fill(cell, ((f + rk) % 2 == 1) ? LightSq : DarkSq);
            if (sqi == lf || sqi == lt) Fill(cell, LastMoveTint);
            char p = board[sqi];
            if (p != '.' && pieceTex.TryGetValue(p, out var tex) && tex != null) GUI.DrawTexture(cell, tex);
        }
    }

    static string Clock(float sec)
    {
        if (sec < 0) sec = 0;
        return sec >= 60 ? $"{(int)sec / 60}:{(int)sec % 60:00}" : sec.ToString("0.0");
    }

    void DrawTournamentBoards()
    {
        var area = new Rect(boardRect.x, U(16), boardRect.width, Screen.height - U(32));

        if (replay != null)
        {
            DrawReplay(area);
            return;
        }

        var focused = liveBoards.Find(b => b.slot == focusedSlot);
        if (focused != null)
        {
            DrawLiveBoardLarge(area, focused);
            return;
        }
        focusedSlot = -1;

        if (liveBoards.Count == 0)
        {
            GUI.Label(new Rect(area.x, area.y + U(40), area.width, U(60)),
                "진행 중인 경기가 없습니다. 오른쪽에서 토너먼트를 시작하세요.", label);
            return;
        }

        // Grid: 1 board full size, 2 side by side, 3-4 in a 2x2 grid.
        int n = Mathf.Min(liveBoards.Count, 4);
        int cols = n == 1 ? 1 : 2;
        int rows = n <= 2 ? 1 : 2;
        float header = U(48);
        float cellW = area.width / cols;
        float cellH = area.height / rows;
        float bsize = Mathf.Floor(Mathf.Min(cellW - U(16), cellH - header - U(16)) / 8f) * 8f;

        for (int i = 0; i < n; i++)
        {
            var b = liveBoards[i];
            float x = area.x + (i % cols) * cellW;
            float y = area.y + (i / cols) * cellH;
            var br = new Rect(x, y + header, bsize, bsize);
            DrawLiveHeader(new Rect(x, y, bsize, header), b);
            if (Event.current.type == EventType.Repaint)
            {
                Referee.FenToBoard(b.fen, scratchBoard);
                DrawBoardAt(br, scratchBoard, b.lastMove, false);
                if (b.status == "finished")
                {
                    var banner_ = new Rect(br.x, br.center.y - U(24), br.width, U(48));
                    Fill(banner_, new Color(0, 0, 0, 0.65f));
                    GUI.Label(banner_, $"{b.result}  ({TerminationKo(b.termination)})", banner);
                }
            }
            if (Event.current.type == EventType.MouseDown && br.Contains(Event.current.mousePosition))
            {
                focusedSlot = b.slot;
                Event.current.Use();
            }
        }
    }

    void DrawLiveHeader(Rect r, LiveBoard b)
    {
        GUI.Label(new Rect(r.x, r.y, r.width, U(24)),
            $"<b>{b.white}</b> ({Clock(b.whiteClock)})  vs  <b>{b.black}</b> ({Clock(b.blackClock)})", rich);
        GUI.Label(new Rect(r.x, r.y + U(23), r.width, U(24)),
            $"#{b.game} · {b.opening} · {b.moves?.Length ?? 0}수 · 평가 {(string.IsNullOrEmpty(b.eval) ? "-" : b.eval)} · 깊이 {b.depth}",
            small);
    }

    void DrawLiveBoardLarge(Rect area, LiveBoard b)
    {
        if (GUI.Button(new Rect(area.x, area.y, U(140), U(28)), "◀ 전체 보기", button)) { focusedSlot = -1; return; }
        DrawLiveHeader(new Rect(area.x, area.y + U(34), area.width, U(48)), b);
        float size = Mathf.Floor(Mathf.Min(area.width, area.height - U(90)) / 8f) * 8f;
        var br = new Rect(area.x, area.y + U(86), size, size);
        if (Event.current.type == EventType.Repaint)
        {
            Referee.FenToBoard(b.fen, scratchBoard);
            DrawBoardAt(br, scratchBoard, b.lastMove, false);
        }
    }

    void DrawReplay(Rect area)
    {
        var g = replay;
        if (GUI.Button(new Rect(area.x, area.y, U(140), U(28)), "◀ 실시간 보기", button)) { replay = null; return; }
        GUI.Label(new Rect(area.x + U(150), area.y + U(2), area.width - U(150), U(26)),
            $"<b>{g.white}</b> vs <b>{g.black}</b>   {g.result} ({TerminationKo(g.termination)})", rich);
        GUI.Label(new Rect(area.x, area.y + U(32), area.width, U(24)),
            $"#{g.id} · {g.opening} · {replayPly}/{g.moves.Length}수" + (replayPly <= g.bookPlies ? " (오프닝)" : ""), small);

        float size = Mathf.Floor(Mathf.Min(area.width, area.height - U(110)) / 8f) * 8f;
        var br = new Rect(area.x, area.y + U(60), size, size);

        if (replayBoardPly != replayPly && referee != null)
        {
            var sub = new List<string>(g.moves).GetRange(0, replayPly);
            if (referee.Update(sub)) Array.Copy(referee.Board, replayBoard, 64);
            replayBoardPly = replayPly;
        }
        if (Event.current.type == EventType.Repaint)
            DrawBoardAt(br, replayBoard, replayPly > 0 ? g.moves[replayPly - 1] : "", false);

        float by = br.yMax + U(8);
        if (GUI.Button(new Rect(area.x, by, U(50), U(30)), "|◀", button)) { replayPly = 0; replayAuto = false; }
        if (GUI.Button(new Rect(area.x + U(56), by, U(50), U(30)), "◀", button)) { replayPly = Mathf.Max(0, replayPly - 1); replayAuto = false; }
        if (GUI.Button(new Rect(area.x + U(112), by, U(90), U(30)), replayAuto ? "멈춤" : "자동 재생", button)) replayAuto = !replayAuto;
        if (GUI.Button(new Rect(area.x + U(208), by, U(50), U(30)), "▶", button)) { replayPly = Mathf.Min(g.moves.Length, replayPly + 1); replayAuto = false; }
        if (GUI.Button(new Rect(area.x + U(264), by, U(50), U(30)), "▶|", button)) { replayPly = g.moves.Length; replayAuto = false; }

        // Keyboard arrows step through the game.
        var e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.RightArrow) { replayPly = Mathf.Min(g.moves.Length, replayPly + 1); e.Use(); }
            if (e.keyCode == KeyCode.LeftArrow) { replayPly = Mathf.Max(0, replayPly - 1); e.Use(); }
        }
    }

    static string TerminationKo(string t)
    {
        if (string.IsNullOrEmpty(t)) return "";
        if (t.StartsWith("checkmate")) return "체크메이트";
        if (t.StartsWith("adjudication")) return "판정";
        if (t.StartsWith("stalemate")) return "스테일메이트";
        if (t.StartsWith("threefold") || t.StartsWith("fivefold")) return "3회 반복";
        if (t.StartsWith("fifty") || t.StartsWith("seventyfive")) return "50수";
        if (t.StartsWith("insufficient")) return "기물 부족";
        if (t.StartsWith("time")) return "시간패";
        if (t.StartsWith("illegal")) return "불법 수";
        if (t.StartsWith("engine_error")) return "엔진 오류";
        return t;
    }

    void DrawTournamentPanel()
    {
        GUILayout.BeginVertical(box);
        bool running = TournamentRunning;
        if (!running)
        {
            GUILayout.Label("새 토너먼트", label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", button, GUILayout.Width(U(32)))) presetIdx = (presetIdx - 1 + Presets.Length) % Presets.Length;
            GUILayout.Label(Presets[presetIdx][0], label, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", button, GUILayout.Width(U(32)))) presetIdx = (presetIdx + 1) % Presets.Length;
            GUILayout.EndHorizontal();
            GUILayout.Label("상대: " + Presets[presetIdx][2].Replace(",", ", "), small);
            GUILayout.BeginHorizontal();
            GUILayout.Label("시간", label, GUILayout.Width(U(50)));
            tcIdx = GUILayout.Toolbar(tcIdx, TcLabels, button);
            GUILayout.EndHorizontal();
            GUILayout.Label($"짝마다 {gamesPerPair}판 (색 바꿔 두 번씩) · 동시 {concurrency}판", small);
            gamesPerPair = Mathf.Max(2, Mathf.RoundToInt(GUILayout.HorizontalSlider(gamesPerPair, 2, 40) / 2f) * 2);
            concurrency = Mathf.RoundToInt(GUILayout.HorizontalSlider(concurrency, 1, 4));
            if (GUILayout.Button("토너먼트 시작", button)) StartTournament();
        }
        else
        {
            GUILayout.Label(standings != null ? $"진행 중: {standings.name}" : "시작하는 중...", label);
            if (GUILayout.Button("중지 (진행 중인 판까지만)", button)) StopTournament();
        }
        if (tourMessage != "") GUILayout.Label(tourMessage, small);
        GUILayout.EndVertical();

        if (standings != null)
        {
            GUILayout.Label($"{standings.name} · {standings.tc} · {standings.done}/{standings.total}판" +
                            (standings.running ? "" : " · 종료"), small);
            DrawStandingsTable();
        }

        GUILayout.Label($"끝난 경기 ({finished.Count}) — 눌러서 다시보기", label);
        gamesScroll = GUILayout.BeginScrollView(gamesScroll, box, GUILayout.ExpandHeight(true));
        for (int i = finished.Count - 1; i >= 0; i--)
        {
            var g = finished[i];
            if (GUILayout.Button($"#{g.id} {g.white} - {g.black}  {g.result}  ({TerminationKo(g.termination)}, {g.plies}수)",
                    listButton))
            {
                replay = g;
                replayPly = g.moves.Length;
                replayBoardPly = -1;
                replayAuto = false;
            }
        }
        GUILayout.EndScrollView();
    }

    void DrawStandingsTable()
    {
        if (standings.engines == null) return;
        standingsScroll = GUILayout.BeginScrollView(standingsScroll, box, GUILayout.Height(Mathf.Min(U(300), U(40) + U(24) * standings.engines.Length)));
        GUILayout.BeginHorizontal();
        GUILayout.Label("#", small, GUILayout.Width(U(20)));
        GUILayout.Label("엔진", small, GUILayout.Width(U(110)));
        GUILayout.Label("Elo ±", small, GUILayout.Width(U(80)));
        GUILayout.Label("득점", small, GUILayout.Width(U(52)));
        GUILayout.Label("승-무-패", small);
        GUILayout.EndHorizontal();
        for (int i = 0; i < standings.engines.Length; i++)
        {
            var r = standings.engines[i];
            bool hero = r.name == "chessai";
            var st = hero ? heroRow : small;
            GUILayout.BeginHorizontal();
            GUILayout.Label((i + 1).ToString(), st, GUILayout.Width(U(20)));
            GUILayout.Label(r.name, st, GUILayout.Width(U(110)));
            GUILayout.Label(r.games > 0 ? $"{r.rating} ±{r.ci}" : "-", st, GUILayout.Width(U(80)));
            GUILayout.Label(r.games > 0 ? $"{r.score:0}%" : "-", st, GUILayout.Width(U(52)));
            GUILayout.Label($"{r.w}-{r.d}-{r.l}", st);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
        GUILayout.Label(standings.anchored
            ? "Elo는 Stockfish 제한 설정(UCI_Elo)을 기준점으로 계산"
            : "Elo는 상대값 (평균 1500)", small);
    }
}
