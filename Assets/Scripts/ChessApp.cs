using System.Collections.Generic;
using UnityEngine;

public enum GameMode { HumanVsEngine = 0, EngineVsEngine = 1, Tournament = 2 }

// Game flow: whose turn it is, asking engines for moves, applying moves, detecting the end.
// Drawing and input live in BoardGui.cs (same partial class).
public partial class ChessApp : MonoBehaviour
{
    List<EngineEntry> engines = new List<EngineEntry>();
    List<Opening> openings = new List<Opening>();
    Referee referee;

    // Settings
    GameMode mode = GameMode.HumanVsEngine;
    int opponentIdx = 0;
    int whiteIdx = 0, blackIdx = 0;
    int humanColorChoice = 0; // 0 white, 1 black, 2 random
    float thinkSeconds = 1f;
    float moveDelay = 1f;
    bool randomOpening = true;
    bool paused;

    // Game state
    readonly List<string> moves = new List<string>();
    int bookPlies;
    string openingName = "";
    UciEngine whiteEngine, blackEngine; // null = human
    string whiteName = "", blackName = "";
    bool humanIsWhite = true;
    bool gameOver;
    string resultText = "";
    string error = "";

    // Engine thinking
    bool thinking;
    float thinkStart;
    float nextMoveAt;

    // Latest search info (white's point of view)
    string infoEngine = "";
    string infoScore = "";
    int infoScoreCp;        // white's point of view; mate = +-10000
    int infoDepth;
    long infoNodes, infoNps, infoTimeMs;
    string infoPv = "";

    // Principal variation of the latest search, and the ply it starts from.
    readonly List<string> pvMoves = new List<string>();
    int pvBasePly;

    // How the best move and score evolved with depth during the current search.
    class DepthLine
    {
        public int Depth;
        public string Move, Score;
        public long Nodes;
        public bool Changed;
    }
    readonly List<DepthLine> depthLog = new List<DepthLine>();

    // chessai's "info string stats" counters for the current search.
    readonly Dictionary<string, long> searchStats = new Dictionary<string, long>();

    // Search score after each ply (white's point of view, centipawns); NaN = not searched.
    readonly List<float> evalHistory = new List<float>();

    bool WhiteToMove => referee != null && referee.WhiteToMove;
    UciEngine EngineToMove => WhiteToMove ? whiteEngine : blackEngine;
    bool HumanToMove => !gameOver && mode == GameMode.HumanVsEngine && WhiteToMove == humanIsWhite;

    void Start()
    {
        Application.runInBackground = true;
        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.15f, 0.14f);
        }

        engines = EngineConfig.LoadEngines();
        openings = EngineConfig.LoadOpenings();
        if (engines.Count == 0)
        {
            error = "StreamingAssets/engines.json 을 읽지 못했습니다.";
            return;
        }
        blackIdx = Mathf.Min(engines.Count - 1, 3);

        referee = new Referee();
        if (!referee.Start(EngineConfig.ResolvePath(EngineConfig.RefereePath)))
        {
            error = "심판 엔진 실행 실패: " + referee.LastError;
            referee = null;
            return;
        }
        InitGui();
        NewGame();
    }

    void NewGame()
    {
        StopPlayers();
        moves.Clear();
        evalHistory.Clear();
        pvMoves.Clear();
        bookPlies = 0;
        openingName = "";
        gameOver = false;
        resultText = "";
        thinking = false;
        paused = false;
        ClearInfo();
        ClearSelection();

        if (mode == GameMode.Tournament)
        {
            // Games are played by tools/tournament.py; this mode only watches.
            whiteName = blackName = "";
            nextPoll = 0;
            return;
        }

        if (mode == GameMode.HumanVsEngine)
        {
            humanIsWhite = humanColorChoice == 0 || (humanColorChoice == 2 && Random.value < 0.5f);
            var e = StartEngine(engines[opponentIdx]);
            if (humanIsWhite) { blackEngine = e; whiteName = "사람"; blackName = engines[opponentIdx].name; }
            else { whiteEngine = e; whiteName = engines[opponentIdx].name; blackName = "사람"; }
            flipped = !humanIsWhite;
        }
        else
        {
            whiteEngine = StartEngine(engines[whiteIdx]);
            blackEngine = StartEngine(engines[blackIdx]);
            whiteName = engines[whiteIdx].name;
            blackName = engines[blackIdx].name;
            flipped = false;
            if (randomOpening && openings.Count > 0)
            {
                var op = openings[Random.Range(0, openings.Count)];
                moves.AddRange(op.Moves);
                bookPlies = op.Moves.Count;
                openingName = op.Name;
            }
        }

        RefreshReferee();
        nextMoveAt = Time.time + 0.5f;
    }

    UciEngine StartEngine(EngineEntry entry)
    {
        var e = new UciEngine(entry.name);
        if (!e.Start(EngineConfig.ResolvePath(entry.path), EngineConfig.Options(entry)))
            error = $"{entry.name} 실행 실패: {e.LastError}";
        e.Send("ucinewgame");
        return e;
    }

    void StopPlayers()
    {
        whiteEngine?.Dispose();
        blackEngine?.Dispose();
        whiteEngine = blackEngine = null;
    }

    void RefreshReferee()
    {
        if (referee == null) return;
        if (!referee.Update(moves))
        {
            error = "심판 응답 없음";
            return;
        }
        string s = referee.Status;
        if (s == "ongoing") return;

        gameOver = true;
        thinking = false;
        string winner = referee.WhiteToMove ? "흑" : "백";
        switch (s)
        {
            case "checkmate": resultText = $"{winner} 승 (체크메이트)"; break;
            case "stalemate": resultText = "무승부 (스테일메이트)"; break;
            case "fifty": resultText = "무승부 (50수 규칙)"; break;
            case "repetition": resultText = "무승부 (3회 반복)"; break;
            case "insufficient": resultText = "무승부 (기물 부족)"; break;
            default: resultText = "종료: " + s; break;
        }
    }

    void ApplyMove(string mv)
    {
        if (gameOver) return;
        if (!referee.Legal.Contains(mv))
        {
            string side = WhiteToMove ? "백" : "흑";
            gameOver = true;
            resultText = $"{side} 불법 수 {mv} → {(WhiteToMove ? "흑" : "백")} 승";
            return;
        }
        moves.Add(mv);
        RefreshReferee();
        nextMoveAt = Time.time + (mode == GameMode.EngineVsEngine ? moveDelay : 0.15f);
    }

    void Resign()
    {
        if (gameOver) return;
        gameOver = true;
        thinking = false;
        resultText = humanIsWhite ? "흑 승 (백 기권)" : "백 승 (흑 기권)";
    }

    void Update()
    {
        if (mode == GameMode.Tournament)
        {
            TournamentUpdate();
            return;
        }
        if (referee == null || gameOver) return;
        var eng = EngineToMove;
        if (eng == null) return; // human to move

        if (!thinking)
        {
            if (paused || Time.time < nextMoveAt) return;
            eng.Drain();
            string pos = "position startpos";
            if (moves.Count > 0) pos += " moves " + string.Join(" ", moves);
            eng.Send(pos);
            eng.Send($"go movetime {Mathf.RoundToInt(thinkSeconds * 1000)}");
            thinking = true;
            thinkStart = Time.time;
            ClearInfo();
            infoEngine = eng.Name;
            pvBasePly = moves.Count;
            return;
        }

        while (eng.TryRead(out var line))
        {
            if (line.StartsWith("info string stats "))
            {
                ParseStats(line);
            }
            else if (line.StartsWith("info ") && line.Contains(" pv "))
            {
                ParseInfo(line);
            }
            else if (line.StartsWith("bestmove"))
            {
                thinking = false;
                var parts = line.Split(' ');
                if (infoDepth > 0) RecordEval(moves.Count, infoScoreCp);
                ApplyMove(parts.Length > 1 ? parts[1] : "(none)");
                return;
            }
        }

        if (!eng.IsRunning || Time.time - thinkStart > thinkSeconds + 10f)
        {
            thinking = false;
            gameOver = true;
            resultText = $"{eng.Name} 응답 없음 → {(WhiteToMove ? "흑" : "백")} 승";
        }
    }

    void ParseInfo(string line)
    {
        var t = line.Split(' ');
        int depth = infoDepth;
        for (int i = 1; i < t.Length - 1; i++)
        {
            switch (t[i])
            {
                case "depth": int.TryParse(t[++i], out depth); break;
                case "nodes": long.TryParse(t[++i], out infoNodes); break;
                case "nps": long.TryParse(t[++i], out infoNps); break;
                case "time": long.TryParse(t[++i], out infoTimeMs); break;
                case "score":
                {
                    string kind = t[++i];
                    int.TryParse(t[++i], out int v);
                    if (!WhiteToMove) v = -v;
                    if (kind == "mate")
                    {
                        infoScore = v > 0 ? $"백 메이트 {v}수" : $"흑 메이트 {-v}수";
                        infoScoreCp = v > 0 ? 10000 : -10000;
                    }
                    else
                    {
                        infoScore = (v / 100f).ToString("+0.00;-0.00;0.00");
                        infoScoreCp = v;
                    }
                    break;
                }
                case "pv":
                {
                    pvMoves.Clear();
                    for (int k = i + 1; k < t.Length; k++) pvMoves.Add(t[k]);
                    infoPv = string.Join(" ", pvMoves.GetRange(0, Mathf.Min(pvMoves.Count, 8)));
                    i = t.Length;
                    break;
                }
            }
        }
        infoDepth = depth;

        if (pvMoves.Count > 0)
        {
            string best = pvMoves[0];
            var last = depthLog.Count > 0 ? depthLog[depthLog.Count - 1] : null;
            if (last != null && last.Depth == depth)
            {
                last.Move = best;
                last.Score = infoScore;
                last.Nodes = infoNodes;
            }
            else
            {
                depthLog.Add(new DepthLine
                {
                    Depth = depth, Move = best, Score = infoScore, Nodes = infoNodes,
                    Changed = last != null && last.Move != best,
                });
            }
        }
    }

    // "info string stats depth 8 qnodes 36083 ttprobes 19165 ..." (chessai only)
    void ParseStats(string line)
    {
        var t = line.Split(' ');
        for (int i = 2; i + 1 < t.Length; i += 2)
            if (long.TryParse(t[i + 1], out long v)) searchStats[t[i]] = v;
    }

    void RecordEval(int ply, int whiteCp)
    {
        while (evalHistory.Count <= ply) evalHistory.Add(float.NaN);
        evalHistory[ply] = whiteCp;
    }

    void ClearInfo()
    {
        infoEngine = infoScore = infoPv = "";
        infoDepth = 0;
        infoScoreCp = 0;
        infoNodes = infoNps = infoTimeMs = 0;
        depthLog.Clear();
        searchStats.Clear();
    }

    void OnDestroy() => Shutdown();
    void OnApplicationQuit() => Shutdown();

    void Shutdown()
    {
        StopPlayers();
        referee?.Dispose();
        referee = null;
    }
}
