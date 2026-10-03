#pragma once
#include "types.h"

namespace BB {

extern U64 pawnAttacks[2][64];
extern U64 knightAttacks[64];
extern U64 kingAttacks[64];
extern U64 passedMask[2][64];
extern U64 adjacentFiles[8];

struct Magic {
    U64 mask;
    U64 magic;
    U64* attacks;
    int shift;
};
extern Magic rookMagics[64];
extern Magic bishopMagics[64];

void init();

inline U64 rookAttacks(int sq, U64 occ) {
    const Magic& m = rookMagics[sq];
    return m.attacks[((occ & m.mask) * m.magic) >> m.shift];
}
inline U64 bishopAttacks(int sq, U64 occ) {
    const Magic& m = bishopMagics[sq];
    return m.attacks[((occ & m.mask) * m.magic) >> m.shift];
}
inline U64 queenAttacks(int sq, U64 occ) { return rookAttacks(sq, occ) | bishopAttacks(sq, occ); }

constexpr U64 FILE_A = 0x0101010101010101ULL;
constexpr U64 RANK_1 = 0xFFULL;

inline U64 fileBB(int f) { return FILE_A << f; }
inline U64 rankBB(int r) { return RANK_1 << (8 * r); }

} // namespace BB
