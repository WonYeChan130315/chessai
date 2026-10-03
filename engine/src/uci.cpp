#include "eval.h"
#include "search.h"
#include <algorithm>
#include <chrono>
#include <iostream>
#include <mutex>
#include <sstream>
#include <thread>

static const char* START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

static std::mutex outMutex;
static void say(const std::string& s) {
    std::lock_guard<std::mutex> lock(outMutex);
    std::cout << s << std::endl;
}

namespace {

Position game;
Searcher searcher;
std::thread worker;

void stopSearch() {
    searcher.stop = true;
    if (worker.joinable()) worker.join();
}

void cmdPosition(std::istringstream& ss) {
    std::string token, fen;
    ss >> token;
    if (token == "startpos") {
        fen = START_FEN;
        ss >> token; // "moves" (optional)
    } else if (token == "fen") {
        while (ss >> token && token != "moves") fen += token + " ";
    } else {
        return;
    }
    if (!game.setFen(fen)) {
        say("info string invalid fen");
        game.setFen(START_FEN);
        return;
    }
    while (ss >> token) {
        Move m = parseUciMove(game, token);
        if (!m) {
            say("info string illegal move " + token);
            break;
        }
        game.make(m);
    }
}

void cmdGo(std::istringstream& ss) {
    Limits lim;
    std::string token;
    while (ss >> token) {
        if (token == "wtime") ss >> lim.wtime;
        else if (token == "btime") ss >> lim.btime;
        else if (token == "winc") ss >> lim.winc;
        else if (token == "binc") ss >> lim.binc;
        else if (token == "movestogo") ss >> lim.movestogo;
        else if (token == "movetime") ss >> lim.movetime;
        else if (token == "depth") ss >> lim.depth;
        else if (token == "nodes") ss >> lim.nodes;
        else if (token == "infinite") lim.infinite = true;
    }
    lim.depth = std::clamp(lim.depth, 1, MAX_PLY - 1);
    stopSearch();
    searcher.stop = false;
    worker = std::thread([lim]() {
        Position p = game;
        Move best = searcher.think(p, lim, say);
        say("bestmove " + moveToUci(best));
    });
}

void cmdSetOption(std::istringstream& ss) {
    std::string token, name, value;
    ss >> token; // "name"
    while (ss >> token && token != "value") name += (name.empty() ? "" : " ") + token;
    while (ss >> token) value += (value.empty() ? "" : " ") + token;
    if (name == "Hash") {
        stopSearch();
        searcher.tt.resize(std::max(1, std::stoi(value)));
    } else if (name == "Pruning") {
        searcher.pruning = value == "true";
    }
}

void cmdStatus() {
    auto moves = legalMoves(game);
    bool check = game.inCheck();
    std::string s;
    if (moves.empty()) s = check ? "checkmate" : "stalemate";
    else if (game.halfmove >= 100) s = "fifty";
    else if (game.repetitionCount() >= 2) s = "repetition";
    else if (game.insufficientMaterial()) s = "insufficient";
    else s = "ongoing";
    say("status " + s + " check " + (check ? "1" : "0") + " side " + (game.side == WHITE ? "w" : "b"));
}

void cmdPerft(int depth) {
    auto start = std::chrono::steady_clock::now();
    U64 total = 0;
    for (Move m : legalMoves(game)) {
        game.make(m);
        U64 n = depth <= 1 ? 1 : perft(game, depth - 1);
        game.unmake(m);
        total += n;
        say(moveToUci(m) + ": " + std::to_string(n));
    }
    long long ms = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - start).count();
    say("nodes " + std::to_string(total) + " time " + std::to_string(ms));
}

void cmdBench(int depth) {
    const char* fens[] = {
        START_FEN,
        "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
        "r1bq1rk1/pp2bppp/2n1pn2/3p4/2PP4/2N1PN2/PP2BPPP/R2QKB1R w KQ - 0 8",
        "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1",
        "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10",
    };
    long long total = 0;
    auto start = std::chrono::steady_clock::now();
    for (const char* f : fens) {
        Position p;
        p.setFen(f);
        searcher.clear();
        Limits lim;
        lim.depth = depth;
        searcher.think(p, lim, [](const std::string&) {});
        total += searcher.nodes;
    }
    long long ms = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - start).count();
    say("bench nodes " + std::to_string(total) + " time " + std::to_string(ms) + " nps " +
        std::to_string(ms > 0 ? total * 1000 / ms : total));
}

} // namespace

void uciLoop(int argc, char** argv) {
    game.setFen(START_FEN);

    if (argc > 1 && std::string(argv[1]) == "bench") {
        cmdBench(argc > 2 ? std::stoi(argv[2]) : 10);
        return;
    }

    std::string line;
    while (std::getline(std::cin, line)) {
        std::istringstream ss(line);
        std::string cmd;
        ss >> cmd;
        if (cmd == "uci") {
            say("id name chessai 0.1");
            say("id author won-yechan");
            say("option name Hash type spin default 64 min 1 max 4096");
            say("option name Threads type spin default 1 min 1 max 1");
            say("option name Pruning type check default true");
            say("uciok");
        } else if (cmd == "isready") {
            say("readyok");
        } else if (cmd == "ucinewgame") {
            stopSearch();
            searcher.clear();
        } else if (cmd == "setoption") {
            cmdSetOption(ss);
        } else if (cmd == "position") {
            stopSearch();
            cmdPosition(ss);
        } else if (cmd == "go") {
            cmdGo(ss);
        } else if (cmd == "stop") {
            stopSearch();
        } else if (cmd == "quit") {
            stopSearch();
            break;
        } else if (cmd == "legalmoves") {
            std::string s = "legalmoves";
            for (Move m : legalMoves(game)) s += " " + moveToUci(m);
            say(s);
        } else if (cmd == "status") {
            cmdStatus();
        } else if (cmd == "fen") {
            say("fen " + game.fen());
        } else if (cmd == "d") {
            say(game.pretty());
        } else if (cmd == "eval") {
            say("eval " + std::to_string(evaluate(game)) + " (side to move)");
        } else if (cmd == "evalsquares") {
            say(evalSquaresString(game));
        } else if (cmd == "control") {
            say(controlString(game));
        } else if (cmd == "evalbreak") {
            // Per-term static evaluation from White's point of view (centipawns).
            EvalBreakdown b = evaluateBreakdown(game);
            const char* names[EVAL_TERMS] = {"material", "pst", "pawns", "mobility", "rooks", "bishops"};
            std::string s = "evalbreak";
            for (int t = 0; t < EVAL_TERMS; t++) s += std::string(" ") + names[t] + " " + std::to_string(b.terms[t]);
            s += " tempo " + std::to_string(b.tempo) + " total " + std::to_string(b.total) +
                 " phase " + std::to_string(b.phase);
            say(s);
        } else if (cmd == "perft") {
            int d = 1;
            ss >> d;
            cmdPerft(d);
        } else if (cmd == "bench") {
            int d = 10;
            ss >> d;
            cmdBench(d);
        }
    }
    stopSearch();
}
