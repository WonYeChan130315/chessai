using System;
using System.Collections.Generic;

// Asks a dedicated chessai process for the rules: legal moves, game status and the board.
// Unity itself contains no chess rules.
public class Referee : IDisposable
{
    public List<string> Legal { get; } = new List<string>();
    public string Status { get; private set; } = "ongoing";
    public bool InCheck { get; private set; }
    public bool WhiteToMove { get; private set; } = true;
    public string Fen { get; private set; } = "";
    public char[] Board { get; } = new char[64]; // a1 = 0, '.' = empty, uppercase = white
    public string LastError => engine.LastError;

    // chessai's static evaluation split by term, White's point of view (centipawns).
    public static readonly string[] EvalTermKeys = { "material", "pst", "pawns", "mobility", "rooks", "bishops", "tempo" };
    public int[] EvalTerms { get; } = new int[7];
    public int EvalTotal { get; private set; }
    public int EvalPhase { get; private set; }
    public bool HasEval { get; private set; }

    // Per-piece positional scores (owner's point of view, centipawns), by square.
    public class PieceEval
    {
        public char Piece;
        public int Pst, Mobility, Structure;
        public string Flags = "";
        public int Total => Pst + Mobility + Structure;
    }
    public PieceEval[] PieceEvals { get; } = new PieceEval[64];
    // Number of white / black pieces attacking each square.
    public int[] WhiteControl { get; } = new int[64];
    public int[] BlackControl { get; } = new int[64];
    public bool HasSquares { get; private set; }

    readonly UciEngine engine = new UciEngine("referee");

    public bool Start(string path)
    {
        if (!engine.Start(path, null)) return false;
        var lines = engine.Query(new[] { "isready" }, l => l == "readyok", 5000);
        return lines.Contains("readyok");
    }

    public bool Update(List<string> moves)
    {
        string pos = "position startpos";
        if (moves.Count > 0) pos += " moves " + string.Join(" ", moves);
        var lines = engine.Query(new[] { pos, "legalmoves", "status", "evalbreak", "evalsquares", "control", "fen" },
            l => l.StartsWith("fen "));

        bool gotFen = false;
        HasEval = false;
        HasSquares = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("evalsquares"))
            {
                for (int i = 0; i < 64; i++) PieceEvals[i] = null;
                var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < tokens.Length; i++)
                {
                    var f = tokens[i].Split(',');
                    if (f.Length < 6 || f[0].Length != 2) continue;
                    int sq = (f[0][1] - '1') * 8 + (f[0][0] - 'a');
                    var pe = new PieceEval { Piece = f[1][0], Flags = f[5] == "-" ? "" : f[5] };
                    int.TryParse(f[2], out pe.Pst);
                    int.TryParse(f[3], out pe.Mobility);
                    int.TryParse(f[4], out pe.Structure);
                    if (sq >= 0 && sq < 64) PieceEvals[sq] = pe;
                }
                HasSquares = true;
                continue;
            }
            if (line.StartsWith("control"))
            {
                var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < tokens.Length && i <= 64; i++)
                {
                    var wb = tokens[i].Split('/');
                    int.TryParse(wb[0], out WhiteControl[i - 1]);
                    if (wb.Length > 1) int.TryParse(wb[1], out BlackControl[i - 1]);
                }
                continue;
            }
            if (line.StartsWith("evalbreak "))
            {
                // evalbreak material X pst Y ... tempo T total S phase P
                var p = line.Split(' ');
                for (int i = 1; i + 1 < p.Length; i += 2)
                {
                    int.TryParse(p[i + 1], out int v);
                    int k = Array.IndexOf(EvalTermKeys, p[i]);
                    if (k >= 0) EvalTerms[k] = v;
                    else if (p[i] == "total") EvalTotal = v;
                    else if (p[i] == "phase") EvalPhase = v;
                }
                HasEval = true;
                continue;
            }
            if (line.StartsWith("legalmoves"))
            {
                Legal.Clear();
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < parts.Length; i++) Legal.Add(parts[i]);
            }
            else if (line.StartsWith("status "))
            {
                // status <state> check <0|1> side <w|b>
                var p = line.Split(' ');
                Status = p[1];
                InCheck = p.Length > 3 && p[3] == "1";
                WhiteToMove = p.Length > 5 && p[5] == "w";
            }
            else if (line.StartsWith("fen "))
            {
                Fen = line.Substring(4);
                FenToBoard(Fen, Board);
                gotFen = true;
            }
        }
        return gotFen;
    }

    public static void FenToBoard(string fen, char[] Board)
    {
        for (int i = 0; i < 64; i++) Board[i] = '.';
        if (string.IsNullOrEmpty(fen)) return;
        string placement = fen.Split(' ')[0];
        int rank = 7, file = 0;
        foreach (char c in placement)
        {
            if (c == '/') { rank--; file = 0; }
            else if (char.IsDigit(c)) file += c - '0';
            else { Board[rank * 8 + file] = c; file++; }
        }
    }

    public void Dispose() => engine.Dispose();
}
