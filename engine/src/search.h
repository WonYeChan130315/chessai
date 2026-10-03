#pragma once
#include "position.h"
#include <atomic>
#include <chrono>
#include <functional>
#include <string>
#include <vector>

struct Limits {
    int wtime = -1, btime = -1, winc = 0, binc = 0, movestogo = 0;
    int movetime = -1;
    int depth = MAX_PLY - 1;
    long long nodes = 0;
    bool infinite = false;
};

struct TTEntry {
    uint32_t key32;
    int16_t score;
    Move move;
    uint8_t depth;
    uint8_t flag;
};

class TranspositionTable {
public:
    void resize(size_t mb);
    void clear();
    TTEntry* probe(U64 key, bool& hit);
    void store(U64 key, int score, Move move, int depth, int flag);

private:
    std::vector<TTEntry> table;
    size_t mask = 0;
};

enum : uint8_t { TT_NONE = 0, TT_EXACT = 1, TT_LOWER = 2, TT_UPPER = 3 };

class Searcher {
public:
    Searcher();
    // Runs iterative deepening; prints UCI info through `out`; returns the best move.
    Move think(Position& pos, const Limits& limits, const std::function<void(const std::string&)>& out);
    void clear();

    std::atomic<bool> stop{false};
    TranspositionTable tt;
    bool pruning = true;
    long long nodes = 0;
    int lastScore = 0;

    // Per-search counters, reported as "info string stats ..." so a GUI can show what the search did.
    struct Stats {
        long long qnodes, ttProbes, ttHits, cutoffs, firstMoveCutoffs;
        long long nullCuts, rfpCuts, lmrSearches, lmrResearches, deltaPrunes;
    } stats{};

private:
    int negamax(Position& pos, int alpha, int beta, int depth, int ply, bool pvNode, bool doNull);
    int qsearch(Position& pos, int alpha, int beta, int ply);
    void scoreMoves(const Position& pos, MoveList& list, int* scores, Move ttMove, int ply);
    void checkTime();
    long long elapsedMs() const;

    Move killers[MAX_PLY][2];
    int history[2][64][64];
    Move pv[MAX_PLY][MAX_PLY];
    int pvLen[MAX_PLY];

    std::chrono::steady_clock::time_point startTime;
    long long softMs = 0, hardMs = 0, nodeLimit = 0;
    bool timed = false;
    int completedDepth = 0;
};
