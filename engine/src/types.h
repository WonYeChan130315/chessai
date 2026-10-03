#pragma once
#include <cstdint>
#include <string>

using U64 = uint64_t;
using Move = uint16_t;

enum Color : int { WHITE = 0, BLACK = 1 };
enum PieceType : int { PAWN = 0, KNIGHT, BISHOP, ROOK, QUEEN, KING, NO_PT };

// piece = color * 6 + type
constexpr int NO_PIECE = 12;
constexpr int NO_SQ = 64;
constexpr int MAX_PLY = 128;
constexpr int MAX_MOVES = 256;
constexpr int MAX_GAME_PLY = 2048;

constexpr int INF = 32000;
constexpr int MATE = 31000;
constexpr int MATE_BOUND = MATE - MAX_PLY;

inline int makePiece(int c, int pt) { return c * 6 + pt; }
inline int colorOf(int p) { return p / 6; }
inline int typeOf(int p) { return p % 6; }

inline int fileOf(int sq) { return sq & 7; }
inline int rankOf(int sq) { return sq >> 3; }

// Castling rights
enum : int { WK = 1, WQ = 2, BK = 4, BQ = 8 };

// Move encoding: from(6) | to(6) | flags(4)
enum MoveFlag : int {
    QUIET = 0, DOUBLE_PUSH = 1, KING_CASTLE = 2, QUEEN_CASTLE = 3,
    CAPTURE = 4, EP_CAPTURE = 5,
    PROMO_N = 8, PROMO_B = 9, PROMO_R = 10, PROMO_Q = 11,
    PROMO_CAP_N = 12, PROMO_CAP_B = 13, PROMO_CAP_R = 14, PROMO_CAP_Q = 15
};

inline Move encodeMove(int from, int to, int flag) { return Move(from | (to << 6) | (flag << 12)); }
inline int fromSq(Move m) { return m & 63; }
inline int toSq(Move m) { return (m >> 6) & 63; }
inline int flagOf(Move m) { return m >> 12; }
inline bool isCapture(Move m) { return flagOf(m) & 4; }
inline bool isPromo(Move m) { return flagOf(m) & 8; }
inline int promoType(Move m) { return KNIGHT + (flagOf(m) & 3); }

inline std::string sqName(int sq) {
    return std::string{char('a' + fileOf(sq)), char('1' + rankOf(sq))};
}

inline std::string moveToUci(Move m) {
    if (!m) return "0000";
    std::string s = sqName(fromSq(m)) + sqName(toSq(m));
    if (isPromo(m)) s += "nbrq"[flagOf(m) & 3];
    return s;
}

inline int popcount(U64 b) { return __builtin_popcountll(b); }
inline int lsb(U64 b) { return __builtin_ctzll(b); }
inline int popLsb(U64& b) { int s = lsb(b); b &= b - 1; return s; }
inline U64 sqBB(int sq) { return 1ULL << sq; }

struct MoveList {
    Move moves[MAX_MOVES];
    int count = 0;
    void add(Move m) { moves[count++] = m; }
};
