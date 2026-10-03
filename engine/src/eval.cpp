#include "eval.h"

using namespace BB;

const int PIECE_VALUE[6] = {100, 320, 330, 500, 900, 0};

static const int MG_VALUE[6] = {82, 337, 365, 477, 1025, 0};
static const int EG_VALUE[6] = {94, 281, 297, 512, 936, 0};
static const int PHASE_INC[6] = {0, 1, 1, 2, 4, 0};

// Piece-square tables from White's point of view, rank 8 first (index = sq ^ 56 for White).
// clang-format off
static const int PAWN_PST[64] = {
     0,  0,  0,  0,  0,  0,  0,  0,
    50, 50, 50, 50, 50, 50, 50, 50,
    10, 10, 20, 30, 30, 20, 10, 10,
     5,  5, 10, 25, 25, 10,  5,  5,
     0,  0,  0, 20, 20,  0,  0,  0,
     5, -5,-10,  0,  0,-10, -5,  5,
     5, 10, 10,-20,-20, 10, 10,  5,
     0,  0,  0,  0,  0,  0,  0,  0};
static const int KNIGHT_PST[64] = {
   -50,-40,-30,-30,-30,-30,-40,-50,
   -40,-20,  0,  0,  0,  0,-20,-40,
   -30,  0, 10, 15, 15, 10,  0,-30,
   -30,  5, 15, 20, 20, 15,  5,-30,
   -30,  0, 15, 20, 20, 15,  0,-30,
   -30,  5, 10, 15, 15, 10,  5,-30,
   -40,-20,  0,  5,  5,  0,-20,-40,
   -50,-40,-30,-30,-30,-30,-40,-50};
static const int BISHOP_PST[64] = {
   -20,-10,-10,-10,-10,-10,-10,-20,
   -10,  0,  0,  0,  0,  0,  0,-10,
   -10,  0,  5, 10, 10,  5,  0,-10,
   -10,  5,  5, 10, 10,  5,  5,-10,
   -10,  0, 10, 10, 10, 10,  0,-10,
   -10, 10, 10, 10, 10, 10, 10,-10,
   -10,  5,  0,  0,  0,  0,  5,-10,
   -20,-10,-10,-10,-10,-10,-10,-20};
static const int ROOK_PST[64] = {
     0,  0,  0,  0,  0,  0,  0,  0,
     5, 10, 10, 10, 10, 10, 10,  5,
    -5,  0,  0,  0,  0,  0,  0, -5,
    -5,  0,  0,  0,  0,  0,  0, -5,
    -5,  0,  0,  0,  0,  0,  0, -5,
    -5,  0,  0,  0,  0,  0,  0, -5,
    -5,  0,  0,  0,  0,  0,  0, -5,
     0,  0,  0,  5,  5,  0,  0,  0};
static const int QUEEN_PST[64] = {
   -20,-10,-10, -5, -5,-10,-10,-20,
   -10,  0,  0,  0,  0,  0,  0,-10,
   -10,  0,  5,  5,  5,  5,  0,-10,
    -5,  0,  5,  5,  5,  5,  0, -5,
     0,  0,  5,  5,  5,  5,  0, -5,
   -10,  5,  5,  5,  5,  5,  0,-10,
   -10,  0,  5,  0,  0,  0,  0,-10,
   -20,-10,-10, -5, -5,-10,-10,-20};
static const int KING_MG_PST[64] = {
   -30,-40,-40,-50,-50,-40,-40,-30,
   -30,-40,-40,-50,-50,-40,-40,-30,
   -30,-40,-40,-50,-50,-40,-40,-30,
   -30,-40,-40,-50,-50,-40,-40,-30,
   -20,-30,-30,-40,-40,-30,-30,-20,
   -10,-20,-20,-20,-20,-20,-20,-10,
    20, 20,  0,  0,  0,  0, 20, 20,
    20, 30, 10,  0,  0, 10, 30, 20};
static const int KING_EG_PST[64] = {
   -50,-40,-30,-20,-20,-30,-40,-50,
   -30,-20,-10,  0,  0,-10,-20,-30,
   -30,-10, 20, 30, 30, 20,-10,-30,
   -30,-10, 30, 40, 40, 30,-10,-30,
   -30,-10, 30, 40, 40, 30,-10,-30,
   -30,-10, 20, 30, 30, 20,-10,-30,
   -30,-30,  0,  0,  0,  0,-30,-30,
   -50,-30,-30,-30,-30,-30,-30,-50};
// clang-format on

static const int* MG_PST[6] = {PAWN_PST, KNIGHT_PST, BISHOP_PST, ROOK_PST, QUEEN_PST, KING_MG_PST};
static const int* EG_PST[6] = {PAWN_PST, KNIGHT_PST, BISHOP_PST, ROOK_PST, QUEEN_PST, KING_EG_PST};

// Passed pawn bonus by relative rank (0 = own back rank).
static const int PASSED_MG[8] = {0, 5, 10, 15, 25, 40, 60, 0};
static const int PASSED_EG[8] = {0, 10, 15, 25, 45, 75, 120, 0};

static const int MOB_MG[6] = {0, 4, 3, 2, 1, 0};
static const int MOB_EG[6] = {0, 4, 4, 3, 2, 0};
static const int TEMPO = 10;

// Accumulates middlegame/endgame scores per term and colour.
static void evalTerms(const Position& pos, int mg[EVAL_TERMS][2], int eg[EVAL_TERMS][2], int& phase) {
    for (int t = 0; t < EVAL_TERMS; t++) mg[t][0] = mg[t][1] = eg[t][0] = eg[t][1] = 0;
    phase = 0;

    for (int c = WHITE; c <= BLACK; c++) {
        const int them = c ^ 1;
        const U64 ownPawns = pos.pieces[makePiece(c, PAWN)];
        const U64 enemyPawns = pos.pieces[makePiece(them, PAWN)];
        // Squares attacked by enemy pawns are excluded from mobility.
        U64 enemyPawnAtt = 0;
        for (U64 b = enemyPawns; b;) enemyPawnAtt |= pawnAttacks[them][popLsb(b)];
        const U64 mobArea = ~pos.colorBB[c] & ~enemyPawnAtt;

        for (int pt = PAWN; pt <= KING; pt++) {
            U64 bb = pos.pieces[makePiece(c, pt)];
            while (bb) {
                int sq = popLsb(bb);
                int idx = c == WHITE ? sq ^ 56 : sq;
                mg[T_MATERIAL][c] += MG_VALUE[pt];
                eg[T_MATERIAL][c] += EG_VALUE[pt];
                mg[T_PST][c] += MG_PST[pt][idx];
                eg[T_PST][c] += EG_PST[pt][idx];
                phase += PHASE_INC[pt];

                if (pt == PAWN) {
                    int f = fileOf(sq);
                    int rel = c == WHITE ? rankOf(sq) : 7 - rankOf(sq);
                    if (!(passedMask[c][sq] & enemyPawns)) {
                        mg[T_PAWNS][c] += PASSED_MG[rel];
                        eg[T_PAWNS][c] += PASSED_EG[rel];
                    }
                    if (!(adjacentFiles[f] & ownPawns)) {
                        mg[T_PAWNS][c] -= 10;
                        eg[T_PAWNS][c] -= 15;
                    }
                } else if (pt != KING) {
                    U64 att;
                    if (pt == KNIGHT) att = knightAttacks[sq];
                    else if (pt == BISHOP) att = bishopAttacks(sq, pos.occ);
                    else if (pt == ROOK) att = rookAttacks(sq, pos.occ);
                    else att = queenAttacks(sq, pos.occ);
                    int mob = popcount(att & mobArea);
                    mg[T_MOBILITY][c] += MOB_MG[pt] * (mob - 4);
                    eg[T_MOBILITY][c] += MOB_EG[pt] * (mob - 4);

                    if (pt == ROOK) {
                        U64 file = fileBB(fileOf(sq));
                        if (!(file & (ownPawns | enemyPawns))) { mg[T_ROOKS][c] += 20; eg[T_ROOKS][c] += 10; }
                        else if (!(file & ownPawns)) { mg[T_ROOKS][c] += 10; eg[T_ROOKS][c] += 5; }
                    }
                }
            }
        }

        // Doubled pawns
        for (int f = 0; f < 8; f++) {
            int n = popcount(ownPawns & fileBB(f));
            if (n > 1) {
                mg[T_PAWNS][c] -= 10 * (n - 1);
                eg[T_PAWNS][c] -= 20 * (n - 1);
            }
        }

        if (popcount(pos.pieces[makePiece(c, BISHOP)]) >= 2) {
            mg[T_BISHOPS][c] += 30;
            eg[T_BISHOPS][c] += 50;
        }
    }
    if (phase > 24) phase = 24;
}

static int taper(int mg, int eg, int phase) { return (mg * phase + eg * (24 - phase)) / 24; }

int evaluate(const Position& pos) {
    int mg[EVAL_TERMS][2], eg[EVAL_TERMS][2], phase;
    evalTerms(pos, mg, eg, phase);
    int mgScore = 0, egScore = 0;
    for (int t = 0; t < EVAL_TERMS; t++) {
        mgScore += mg[t][WHITE] - mg[t][BLACK];
        egScore += eg[t][WHITE] - eg[t][BLACK];
    }
    int score = taper(mgScore, egScore, phase);
    // Tempo bonus for the side to move.
    return (pos.side == WHITE ? score : -score) + TEMPO;
}

EvalBreakdown evaluateBreakdown(const Position& pos) {
    int mg[EVAL_TERMS][2], eg[EVAL_TERMS][2];
    EvalBreakdown b{};
    evalTerms(pos, mg, eg, b.phase);
    for (int t = 0; t < EVAL_TERMS; t++)
        b.terms[t] = taper(mg[t][WHITE] - mg[t][BLACK], eg[t][WHITE] - eg[t][BLACK], b.phase);
    b.tempo = pos.side == WHITE ? TEMPO : -TEMPO;
    int e = evaluate(pos);
    b.total = pos.side == WHITE ? e : -e;
    return b;
}

static U64 attacksOf(const Position& pos, int c, int pt, int sq) {
    switch (pt) {
        case PAWN: return pawnAttacks[c][sq];
        case KNIGHT: return knightAttacks[sq];
        case BISHOP: return bishopAttacks(sq, pos.occ);
        case ROOK: return rookAttacks(sq, pos.occ);
        case QUEEN: return queenAttacks(sq, pos.occ);
        default: return kingAttacks[sq];
    }
}

// One token per piece: "<sq>,<piece>,<pst>,<mobility>,<structure>,<flags>". Values are tapered
// centipawns from the piece owner's point of view; structure covers pawn and rook-file terms.
// Flags: P passed, I isolated, D doubled, O rook on open file, S rook on semi-open file, - none.
std::string evalSquaresString(const Position& pos) {
    int phase = 0;
    for (int p = 0; p < 12; p++) phase += popcount(pos.pieces[p]) * PHASE_INC[typeOf(p)];
    if (phase > 24) phase = 24;

    std::string out = "evalsquares";
    for (int c = WHITE; c <= BLACK; c++) {
        const int them = c ^ 1;
        const U64 ownPawns = pos.pieces[makePiece(c, PAWN)];
        const U64 enemyPawns = pos.pieces[makePiece(them, PAWN)];
        U64 enemyPawnAtt = 0;
        for (U64 b = enemyPawns; b;) enemyPawnAtt |= pawnAttacks[them][popLsb(b)];
        const U64 mobArea = ~pos.colorBB[c] & ~enemyPawnAtt;

        for (int pt = PAWN; pt <= KING; pt++) {
            U64 bb = pos.pieces[makePiece(c, pt)];
            while (bb) {
                int sq = popLsb(bb);
                int idx = c == WHITE ? sq ^ 56 : sq;
                int pst = taper(MG_PST[pt][idx], EG_PST[pt][idx], phase);
                int mobMg = 0, mobEg = 0, stMg = 0, stEg = 0;
                std::string flags;

                if (pt == PAWN) {
                    int f = fileOf(sq);
                    int rel = c == WHITE ? rankOf(sq) : 7 - rankOf(sq);
                    if (!(passedMask[c][sq] & enemyPawns)) { stMg += PASSED_MG[rel]; stEg += PASSED_EG[rel]; flags += 'P'; }
                    if (!(adjacentFiles[f] & ownPawns)) { stMg -= 10; stEg -= 15; flags += 'I'; }
                    int n = popcount(ownPawns & fileBB(f));
                    if (n > 1) { stMg -= 10 * (n - 1) / n; stEg -= 20 * (n - 1) / n; flags += 'D'; }
                } else if (pt != KING) {
                    int mob = popcount(attacksOf(pos, c, pt, sq) & mobArea);
                    mobMg = MOB_MG[pt] * (mob - 4);
                    mobEg = MOB_EG[pt] * (mob - 4);
                    if (pt == ROOK) {
                        U64 file = fileBB(fileOf(sq));
                        if (!(file & (ownPawns | enemyPawns))) { stMg += 20; stEg += 10; flags += 'O'; }
                        else if (!(file & ownPawns)) { stMg += 10; stEg += 5; flags += 'S'; }
                    }
                }
                out += " " + sqName(sq) + "," + "PNBRQKpnbrqk"[makePiece(c, pt)] + "," + std::to_string(pst) + "," +
                       std::to_string(taper(mobMg, mobEg, phase)) + "," + std::to_string(taper(stMg, stEg, phase)) +
                       "," + (flags.empty() ? "-" : flags);
            }
        }
    }
    return out;
}

// "control" + 64 tokens "<white attackers>/<black attackers>" for a1..h8.
std::string controlString(const Position& pos) {
    int cnt[2][64] = {};
    for (int c = WHITE; c <= BLACK; c++)
        for (int pt = PAWN; pt <= KING; pt++) {
            U64 bb = pos.pieces[makePiece(c, pt)];
            while (bb) {
                U64 att = attacksOf(pos, c, pt, popLsb(bb));
                while (att) cnt[c][popLsb(att)]++;
            }
        }
    std::string out = "control";
    for (int sq = 0; sq < 64; sq++) out += " " + std::to_string(cnt[WHITE][sq]) + "/" + std::to_string(cnt[BLACK][sq]);
    return out;
}
