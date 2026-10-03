#include "bitboard.h"
#include <vector>

namespace BB {

U64 pawnAttacks[2][64];
U64 knightAttacks[64];
U64 kingAttacks[64];
U64 passedMask[2][64];
U64 adjacentFiles[8];
Magic rookMagics[64];
Magic bishopMagics[64];

static U64 rookTable[64][4096];
static U64 bishopTable[64][512];

static const int ROOK_DIRS[4][2] = {{1, 0}, {-1, 0}, {0, 1}, {0, -1}};
static const int BISHOP_DIRS[4][2] = {{1, 1}, {1, -1}, {-1, 1}, {-1, -1}};

static bool onBoard(int r, int f) { return r >= 0 && r < 8 && f >= 0 && f < 8; }

static U64 slide(int sq, U64 occ, const int dirs[4][2]) {
    U64 att = 0;
    for (int d = 0; d < 4; d++) {
        int r = rankOf(sq) + dirs[d][0], f = fileOf(sq) + dirs[d][1];
        while (onBoard(r, f)) {
            int s = r * 8 + f;
            att |= sqBB(s);
            if (occ & sqBB(s)) break;
            r += dirs[d][0];
            f += dirs[d][1];
        }
    }
    return att;
}

// Relevant occupancy: every ray square except the last one before the edge.
static U64 relevantMask(int sq, const int dirs[4][2]) {
    U64 mask = 0;
    for (int d = 0; d < 4; d++) {
        int r = rankOf(sq) + dirs[d][0], f = fileOf(sq) + dirs[d][1];
        while (onBoard(r + dirs[d][0], f + dirs[d][1])) {
            mask |= sqBB(r * 8 + f);
            r += dirs[d][0];
            f += dirs[d][1];
        }
    }
    return mask;
}

static U64 rngState = 0x9E3779B97F4A7C15ULL;
static U64 rnd() {
    rngState ^= rngState >> 12;
    rngState ^= rngState << 25;
    rngState ^= rngState >> 27;
    return rngState * 2685821657736338717ULL;
}

static void initMagic(int sq, const int dirs[4][2], Magic& m, U64* table) {
    m.mask = relevantMask(sq, dirs);
    int bits = popcount(m.mask);
    m.shift = 64 - bits;
    m.attacks = table;

    int n = 1 << bits;
    std::vector<U64> occ(n), att(n), used(n);
    U64 b = 0;
    int i = 0;
    do {
        occ[i] = b;
        att[i] = slide(sq, b, dirs);
        i++;
        b = (b - m.mask) & m.mask;
    } while (b);

    for (;;) {
        U64 magic = rnd() & rnd() & rnd();
        if (popcount((m.mask * magic) >> 56) < 6) continue;
        std::fill(used.begin(), used.end(), 0);
        bool ok = true;
        for (int k = 0; k < n && ok; k++) {
            U64 idx = (occ[k] * magic) >> m.shift;
            if (!used[idx]) used[idx] = att[k];
            else if (used[idx] != att[k]) ok = false;
        }
        if (ok) {
            m.magic = magic;
            for (int k = 0; k < n; k++) table[(occ[k] * magic) >> m.shift] = att[k];
            return;
        }
    }
}

void init() {
    for (int sq = 0; sq < 64; sq++) {
        int r = rankOf(sq), f = fileOf(sq);
        auto set = [&](U64& bb, int dr, int df) {
            if (onBoard(r + dr, f + df)) bb |= sqBB((r + dr) * 8 + f + df);
        };
        pawnAttacks[WHITE][sq] = pawnAttacks[BLACK][sq] = 0;
        set(pawnAttacks[WHITE][sq], 1, -1);
        set(pawnAttacks[WHITE][sq], 1, 1);
        set(pawnAttacks[BLACK][sq], -1, -1);
        set(pawnAttacks[BLACK][sq], -1, 1);

        knightAttacks[sq] = 0;
        const int kn[8][2] = {{1, 2}, {2, 1}, {2, -1}, {1, -2}, {-1, -2}, {-2, -1}, {-2, 1}, {-1, 2}};
        for (auto& d : kn) set(knightAttacks[sq], d[0], d[1]);

        kingAttacks[sq] = 0;
        for (int dr = -1; dr <= 1; dr++)
            for (int df = -1; df <= 1; df++)
                if (dr || df) set(kingAttacks[sq], dr, df);
    }

    for (int f = 0; f < 8; f++)
        adjacentFiles[f] = (f > 0 ? fileBB(f - 1) : 0) | (f < 7 ? fileBB(f + 1) : 0);

    for (int sq = 0; sq < 64; sq++) {
        U64 files = fileBB(fileOf(sq)) | adjacentFiles[fileOf(sq)];
        U64 ahead = 0, behind = 0;
        for (int r = rankOf(sq) + 1; r < 8; r++) ahead |= rankBB(r);
        for (int r = rankOf(sq) - 1; r >= 0; r--) behind |= rankBB(r);
        passedMask[WHITE][sq] = files & ahead;
        passedMask[BLACK][sq] = files & behind;
    }

    for (int sq = 0; sq < 64; sq++) {
        initMagic(sq, ROOK_DIRS, rookMagics[sq], rookTable[sq]);
        initMagic(sq, BISHOP_DIRS, bishopMagics[sq], bishopTable[sq]);
    }
}

} // namespace BB
