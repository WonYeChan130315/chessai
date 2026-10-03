#include "search.h"
#include "eval.h"
#include <algorithm>
#include <cmath>
#include <cstring>

// ---------------------------------------------------------------- TT

void TranspositionTable::resize(size_t mb) {
    size_t bytes = std::max<size_t>(1, mb) * 1024 * 1024;
    size_t n = 1;
    while (n * 2 * sizeof(TTEntry) <= bytes) n *= 2;
    table.assign(n, TTEntry{});
    mask = n - 1;
}

void TranspositionTable::clear() { std::fill(table.begin(), table.end(), TTEntry{}); }

TTEntry* TranspositionTable::probe(U64 key, bool& hit) {
    TTEntry* e = &table[key & mask];
    hit = e->flag != TT_NONE && e->key32 == uint32_t(key >> 32);
    return e;
}

void TranspositionTable::store(U64 key, int score, Move move, int depth, int flag) {
    TTEntry* e = &table[key & mask];
    bool same = e->key32 == uint32_t(key >> 32);
    // Keep the old move when the new search found none for the same position.
    if (same && !move) move = e->move;
    if (!same || depth >= e->depth || flag == TT_EXACT) {
        e->key32 = uint32_t(key >> 32);
        e->score = int16_t(score);
        e->move = move;
        e->depth = uint8_t(std::max(0, depth));
        e->flag = uint8_t(flag);
    }
}

static int scoreToTT(int s, int ply) { return s >= MATE_BOUND ? s + ply : s <= -MATE_BOUND ? s - ply : s; }
static int scoreFromTT(int s, int ply) { return s >= MATE_BOUND ? s - ply : s <= -MATE_BOUND ? s + ply : s; }

// ---------------------------------------------------------------- Searcher

static int LMR[64][64];

Searcher::Searcher() {
    for (int d = 1; d < 64; d++)
        for (int m = 1; m < 64; m++)
            LMR[d][m] = int(0.75 + std::log(d) * std::log(m) / 2.25);
    tt.resize(64);
    clear();
}

void Searcher::clear() {
    tt.clear();
    std::memset(killers, 0, sizeof(killers));
    std::memset(history, 0, sizeof(history));
}

long long Searcher::elapsedMs() const {
    return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - startTime).count();
}

void Searcher::checkTime() {
    if (completedDepth < 1) return; // always finish depth 1 so there is a move to play
    if (timed && elapsedMs() >= hardMs) stop = true;
    if (nodeLimit && nodes >= nodeLimit) stop = true;
}

void Searcher::scoreMoves(const Position& pos, MoveList& list, int* scores, Move ttMove, int ply) {
    for (int i = 0; i < list.count; i++) {
        Move m = list.moves[i];
        if (m == ttMove) scores[i] = 2000000;
        else if (isCapture(m)) {
            int victim = flagOf(m) == EP_CAPTURE ? PAWN : typeOf(pos.board[toSq(m)]);
            int attacker = typeOf(pos.board[fromSq(m)]);
            scores[i] = 1000000 + PIECE_VALUE[victim] * 16 - attacker + (isPromo(m) ? 500000 : 0);
        } else if (isPromo(m)) scores[i] = promoType(m) == QUEEN ? 1400000 : -100000;
        else if (m == killers[ply][0]) scores[i] = 900000;
        else if (m == killers[ply][1]) scores[i] = 800000;
        else scores[i] = history[pos.side][fromSq(m)][toSq(m)];
    }
}

static Move pickNext(MoveList& list, int* scores, int i) {
    int best = i;
    for (int j = i + 1; j < list.count; j++)
        if (scores[j] > scores[best]) best = j;
    std::swap(list.moves[i], list.moves[best]);
    std::swap(scores[i], scores[best]);
    return list.moves[i];
}

int Searcher::qsearch(Position& pos, int alpha, int beta, int ply) {
    if ((nodes & 2047) == 0) checkTime();
    if (stop) return 0;
    if (pos.insufficientMaterial()) return 0;

    int stand = evaluate(pos);
    if (ply >= MAX_PLY - 1) return stand;
    if (stand >= beta) return stand;
    if (stand > alpha) alpha = stand;

    MoveList list;
    generateMoves(pos, list, true);
    int scores[MAX_MOVES];
    scoreMoves(pos, list, scores, 0, ply);

    for (int i = 0; i < list.count; i++) {
        Move m = pickNext(list, scores, i);
        if (!isPromo(m)) {
            int victim = flagOf(m) == EP_CAPTURE ? PAWN : typeOf(pos.board[toSq(m)]);
            if (stand + PIECE_VALUE[victim] + 200 < alpha) { stats.deltaPrunes++; continue; } // delta pruning
        }
        if (!pos.make(m)) continue;
        nodes++;
        stats.qnodes++;
        int score = -qsearch(pos, -beta, -alpha, ply + 1);
        pos.unmake(m);
        if (stop) return 0;
        if (score > alpha) {
            alpha = score;
            if (score >= beta) return score;
        }
    }
    return alpha;
}

int Searcher::negamax(Position& pos, int alpha, int beta, int depth, int ply, bool pvNode, bool doNull) {
    if ((nodes & 2047) == 0) checkTime();
    if (stop) return 0;
    pvLen[ply] = ply;

    const bool root = ply == 0;
    if (!root) {
        if (pos.halfmove >= 100 || pos.isRepetition() || pos.insufficientMaterial()) return 0;
        alpha = std::max(alpha, -MATE + ply);
        beta = std::min(beta, MATE - ply - 1);
        if (alpha >= beta) return alpha;
    }

    const bool inCheck = pos.inCheck();
    if (inCheck) depth++;
    if (depth <= 0) return qsearch(pos, alpha, beta, ply);
    if (ply >= MAX_PLY - 1) return evaluate(pos);

    bool hit;
    TTEntry* e = tt.probe(pos.key, hit);
    stats.ttProbes++;
    if (hit) stats.ttHits++;
    Move ttMove = hit ? e->move : 0;
    if (hit && !pvNode && e->depth >= depth) {
        int s = scoreFromTT(e->score, ply);
        if (e->flag == TT_EXACT || (e->flag == TT_LOWER && s >= beta) || (e->flag == TT_UPPER && s <= alpha))
            return s;
    }

    const int staticEval = inCheck ? -INF : evaluate(pos);

    if (pruning && !pvNode && !inCheck) {
        // Reverse futility pruning
        if (depth <= 6 && staticEval - 80 * depth >= beta && std::abs(beta) < MATE_BOUND) {
            stats.rfpCuts++;
            return staticEval;
        }

        // Null move pruning
        if (doNull && depth >= 3 && staticEval >= beta && pos.hasNonPawnMaterial(pos.side)) {
            int R = 3 + depth / 6;
            pos.makeNull();
            int s = -negamax(pos, -beta, -beta + 1, depth - 1 - R, ply + 1, false, false);
            pos.unmakeNull();
            if (stop) return 0;
            if (s >= beta) {
                stats.nullCuts++;
                return s >= MATE_BOUND ? beta : s;
            }
        }
    }

    MoveList list;
    generateMoves(pos, list, false);
    int scores[MAX_MOVES];
    scoreMoves(pos, list, scores, ttMove, ply);

    const int origAlpha = alpha;
    int best = -INF, legal = 0;
    Move bestMove = 0;

    for (int i = 0; i < list.count; i++) {
        Move m = pickNext(list, scores, i);
        if (!pos.make(m)) continue;
        legal++;
        nodes++;
        const bool quiet = !isCapture(m) && !isPromo(m);
        const bool givesCheck = pos.inCheck();

        int score;
        if (legal == 1) {
            score = -negamax(pos, -beta, -alpha, depth - 1, ply + 1, pvNode, true);
        } else {
            int r = 0;
            if (pruning && depth >= 3 && legal > 3 && quiet && !inCheck && !givesCheck) {
                r = LMR[std::min(depth, 63)][std::min(legal, 63)];
                if (pvNode) r--;
                if (m == killers[ply][0] || m == killers[ply][1]) r--;
                r = std::clamp(r, 0, depth - 2);
                if (r > 0) stats.lmrSearches++;
            }
            score = -negamax(pos, -alpha - 1, -alpha, depth - 1 - r, ply + 1, false, true);
            if (score > alpha && r > 0) {
                stats.lmrResearches++;
                score = -negamax(pos, -alpha - 1, -alpha, depth - 1, ply + 1, false, true);
            }
            if (score > alpha && score < beta) score = -negamax(pos, -beta, -alpha, depth - 1, ply + 1, true, true);
        }
        pos.unmake(m);
        if (stop) return 0;

        if (score > best) {
            best = score;
            bestMove = m;
            if (score > alpha) {
                alpha = score;
                pv[ply][ply] = m;
                for (int j = ply + 1; j < pvLen[ply + 1]; j++) pv[ply][j] = pv[ply + 1][j];
                pvLen[ply] = std::max(pvLen[ply + 1], ply + 1);
                if (alpha >= beta) {
                    stats.cutoffs++;
                    if (legal == 1) stats.firstMoveCutoffs++;
                    if (quiet) {
                        if (killers[ply][0] != m) {
                            killers[ply][1] = killers[ply][0];
                            killers[ply][0] = m;
                        }
                        int& h = history[pos.side][fromSq(m)][toSq(m)];
                        h += depth * depth;
                        if (h > 400000) {
                            for (auto& a : history)
                                for (auto& b : a)
                                    for (int& c : b) c /= 2;
                        }
                    }
                    break;
                }
            }
        }
    }

    if (!legal) return inCheck ? -MATE + ply : 0;

    int flag = best >= beta ? TT_LOWER : best > origAlpha ? TT_EXACT : TT_UPPER;
    tt.store(pos.key, scoreToTT(best, ply), bestMove, depth, flag);
    return best;
}

static std::string scoreString(int s) {
    if (s >= MATE_BOUND) return "mate " + std::to_string((MATE - s + 1) / 2);
    if (s <= -MATE_BOUND) return "mate -" + std::to_string((MATE + s + 1) / 2);
    return "cp " + std::to_string(s);
}

Move Searcher::think(Position& pos, const Limits& lim, const std::function<void(const std::string&)>& out) {
    startTime = std::chrono::steady_clock::now();
    stop = false;
    nodes = 0;
    stats = Stats{};
    completedDepth = 0;
    nodeLimit = lim.nodes;
    timed = false;

    const int overhead = 30;
    if (lim.movetime > 0) {
        timed = true;
        softMs = hardMs = std::max(1, lim.movetime - overhead);
    } else if (!lim.infinite && (pos.side == WHITE ? lim.wtime : lim.btime) >= 0) {
        timed = true;
        long long time = pos.side == WHITE ? lim.wtime : lim.btime;
        long long inc = pos.side == WHITE ? lim.winc : lim.binc;
        long long mtg = lim.movestogo > 0 ? std::min(lim.movestogo, 40) : 30;
        long long avail = std::max(1LL, time - overhead);
        softMs = avail / mtg + inc * 3 / 4;
        hardMs = std::min(avail / 3, softMs * 4);
        softMs = std::min(softMs, hardMs);
        softMs = std::max(1LL, softMs);
        hardMs = std::max(1LL, hardMs);
    }

    std::vector<Move> rootMoves = legalMoves(pos);
    if (rootMoves.empty()) return 0;
    Move bestMove = rootMoves[0];
    int score = 0;

    for (int depth = 1; depth <= lim.depth; depth++) {
        int alpha = -INF, beta = INF, window = 25;
        if (depth >= 5) {
            alpha = std::max(-INF, score - window);
            beta = std::min(INF, score + window);
        }
        int s;
        for (;;) {
            s = negamax(pos, alpha, beta, depth, 0, true, false);
            if (stop) break;
            if (s <= alpha) { alpha = std::max(-INF, alpha - window); window *= 2; }
            else if (s >= beta) { beta = std::min(INF, beta + window); window *= 2; }
            else break;
        }
        if (stop) break;

        score = s;
        completedDepth = depth;
        if (pvLen[0] > 0 && pv[0][0]) bestMove = pv[0][0];
        lastScore = score;

        long long ms = elapsedMs();
        std::string line = "info depth " + std::to_string(depth) + " score " + scoreString(score) +
                           " nodes " + std::to_string(nodes) + " nps " +
                           std::to_string(ms > 0 ? nodes * 1000 / ms : nodes) + " time " + std::to_string(ms) + " pv";
        for (int i = 0; i < pvLen[0]; i++) line += " " + moveToUci(pv[0][i]);
        out(line);
        out("info string stats depth " + std::to_string(depth) + " qnodes " + std::to_string(stats.qnodes) +
            " ttprobes " + std::to_string(stats.ttProbes) + " tthits " + std::to_string(stats.ttHits) +
            " cutoffs " + std::to_string(stats.cutoffs) + " firstcut " + std::to_string(stats.firstMoveCutoffs) +
            " nullcuts " + std::to_string(stats.nullCuts) + " rfpcuts " + std::to_string(stats.rfpCuts) +
            " lmr " + std::to_string(stats.lmrSearches) + " lmrresearch " + std::to_string(stats.lmrResearches) +
            " deltaprune " + std::to_string(stats.deltaPrunes));

        if (timed && ms >= softMs) break;
        if (nodeLimit && nodes >= nodeLimit) break;
        if (std::abs(score) >= MATE_BOUND && depth > 6 && !lim.infinite && timed) break;
    }
    return bestMove;
}
