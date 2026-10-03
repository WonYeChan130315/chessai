#include "position.h"
#include <algorithm>
#include <cstring>
#include <sstream>

namespace Zobrist {
U64 psq[12][64];
U64 castle[16];
U64 ep[8];
U64 side;

void init() {
    U64 s = 0x1234567890ABCDEFULL;
    auto next = [&]() {
        s ^= s >> 12;
        s ^= s << 25;
        s ^= s >> 27;
        return s * 2685821657736338717ULL;
    };
    for (auto& p : psq)
        for (auto& k : p) k = next();
    for (auto& k : castle) k = next();
    for (auto& k : ep) k = next();
    side = next();
}
} // namespace Zobrist

// Castling rights kept when a move touches a square.
static int castleMask[64];
static bool castleMaskReady = false;

static void initCastleMask() {
    for (int& m : castleMask) m = 15;
    castleMask[0] &= ~WQ;
    castleMask[7] &= ~WK;
    castleMask[4] &= ~(WK | WQ);
    castleMask[56] &= ~BQ;
    castleMask[63] &= ~BK;
    castleMask[60] &= ~(BK | BQ);
    castleMaskReady = true;
}

void Position::put(int p, int sq) {
    board[sq] = p;
    pieces[p] |= sqBB(sq);
    colorBB[colorOf(p)] |= sqBB(sq);
    occ |= sqBB(sq);
    key ^= Zobrist::psq[p][sq];
}

void Position::remove(int sq) {
    int p = board[sq];
    board[sq] = NO_PIECE;
    pieces[p] &= ~sqBB(sq);
    colorBB[colorOf(p)] &= ~sqBB(sq);
    occ &= ~sqBB(sq);
    key ^= Zobrist::psq[p][sq];
}

void Position::movePiece(int from, int to) {
    int p = board[from];
    remove(from);
    put(p, to);
}

U64 Position::computeKey() const {
    U64 k = 0;
    for (int sq = 0; sq < 64; sq++)
        if (board[sq] != NO_PIECE) k ^= Zobrist::psq[board[sq]][sq];
    k ^= Zobrist::castle[castling];
    if (ep != NO_SQ) k ^= Zobrist::ep[fileOf(ep)];
    if (side == BLACK) k ^= Zobrist::side;
    return k;
}

bool Position::setFen(const std::string& fenStr) {
    if (!castleMaskReady) initCastleMask();
    std::memset(pieces, 0, sizeof(pieces));
    colorBB[0] = colorBB[1] = occ = 0;
    for (int& b : board) b = NO_PIECE;
    key = 0;
    ply = 0;

    std::istringstream ss(fenStr);
    std::string placement, stm, cast, eps;
    int hm = 0, fm = 1;
    if (!(ss >> placement >> stm)) return false;
    ss >> cast >> eps >> hm >> fm;

    int r = 7, f = 0;
    for (char c : placement) {
        if (c == '/') { r--; f = 0; continue; }
        if (c >= '1' && c <= '8') { f += c - '0'; continue; }
        const char* idx = std::strchr("PNBRQKpnbrqk", c);
        if (!idx || r < 0 || f > 7) return false;
        int p = int(idx - "PNBRQKpnbrqk");
        put(p, r * 8 + f);
        f++;
    }
    if (popcount(pieces[makePiece(WHITE, KING)]) != 1 || popcount(pieces[makePiece(BLACK, KING)]) != 1)
        return false;

    side = (stm == "b") ? BLACK : WHITE;
    castling = 0;
    for (char c : cast) {
        if (c == 'K') castling |= WK;
        if (c == 'Q') castling |= WQ;
        if (c == 'k') castling |= BK;
        if (c == 'q') castling |= BQ;
    }
    ep = NO_SQ;
    if (eps.size() == 2 && eps[0] >= 'a' && eps[0] <= 'h') {
        int e = (eps[1] - '1') * 8 + (eps[0] - 'a');
        // Keep the ep square only if a pawn can actually capture there.
        if (BB::pawnAttacks[side ^ 1][e] & pieces[makePiece(side, PAWN)]) ep = e;
    }
    halfmove = hm;
    fullmove = fm;
    key = computeKey();
    return true;
}

std::string Position::fen() const {
    std::string s;
    for (int r = 7; r >= 0; r--) {
        int empty = 0;
        for (int f = 0; f < 8; f++) {
            int p = board[r * 8 + f];
            if (p == NO_PIECE) { empty++; continue; }
            if (empty) { s += char('0' + empty); empty = 0; }
            s += "PNBRQKpnbrqk"[p];
        }
        if (empty) s += char('0' + empty);
        if (r) s += '/';
    }
    s += side == WHITE ? " w " : " b ";
    std::string c;
    if (castling & WK) c += 'K';
    if (castling & WQ) c += 'Q';
    if (castling & BK) c += 'k';
    if (castling & BQ) c += 'q';
    s += c.empty() ? "-" : c;
    s += ' ';
    s += ep == NO_SQ ? "-" : sqName(ep);
    s += ' ' + std::to_string(halfmove) + ' ' + std::to_string(fullmove);
    return s;
}

bool Position::attacked(int sq, int by) const {
    if (BB::pawnAttacks[by ^ 1][sq] & pieces[makePiece(by, PAWN)]) return true;
    if (BB::knightAttacks[sq] & pieces[makePiece(by, KNIGHT)]) return true;
    if (BB::kingAttacks[sq] & pieces[makePiece(by, KING)]) return true;
    U64 bq = pieces[makePiece(by, BISHOP)] | pieces[makePiece(by, QUEEN)];
    if (BB::bishopAttacks(sq, occ) & bq) return true;
    U64 rq = pieces[makePiece(by, ROOK)] | pieces[makePiece(by, QUEEN)];
    if (BB::rookAttacks(sq, occ) & rq) return true;
    return false;
}

bool Position::make(Move m) {
    Undo& u = undo[ply];
    u.captured = NO_PIECE;
    u.castling = castling;
    u.ep = ep;
    u.halfmove = halfmove;
    u.key = key;

    const int from = fromSq(m), to = toSq(m), flag = flagOf(m);
    const int us = side, them = us ^ 1;
    const int p = board[from];

    if (ep != NO_SQ) key ^= Zobrist::ep[fileOf(ep)];
    ep = NO_SQ;
    key ^= Zobrist::castle[castling];
    halfmove++;

    if (flag == EP_CAPTURE) {
        int capSq = to + (us == WHITE ? -8 : 8);
        u.captured = board[capSq];
        remove(capSq);
    } else if (isCapture(m)) {
        u.captured = board[to];
        remove(to);
        halfmove = 0;
    }

    if (isPromo(m)) {
        remove(from);
        put(makePiece(us, promoType(m)), to);
    } else {
        movePiece(from, to);
    }

    if (typeOf(p) == PAWN) {
        halfmove = 0;
        if (flag == DOUBLE_PUSH) {
            int e = from + (us == WHITE ? 8 : -8);
            if (BB::pawnAttacks[us][e] & pieces[makePiece(them, PAWN)]) {
                ep = e;
                key ^= Zobrist::ep[fileOf(e)];
            }
        }
    }

    if (flag == KING_CASTLE) movePiece(us == WHITE ? 7 : 63, us == WHITE ? 5 : 61);
    else if (flag == QUEEN_CASTLE) movePiece(us == WHITE ? 0 : 56, us == WHITE ? 3 : 59);

    castling &= castleMask[from] & castleMask[to];
    key ^= Zobrist::castle[castling];

    side = them;
    key ^= Zobrist::side;
    if (us == BLACK) fullmove++;
    ply++;

    if (attacked(kingSq(us), them)) {
        unmake(m);
        return false;
    }
    return true;
}

void Position::unmake(Move m) {
    ply--;
    side ^= 1;
    const int us = side;
    const Undo& u = undo[ply];
    if (us == BLACK) fullmove--;

    const int from = fromSq(m), to = toSq(m), flag = flagOf(m);

    if (flag == KING_CASTLE) movePiece(us == WHITE ? 5 : 61, us == WHITE ? 7 : 63);
    else if (flag == QUEEN_CASTLE) movePiece(us == WHITE ? 3 : 59, us == WHITE ? 0 : 56);

    if (isPromo(m)) {
        remove(to);
        put(makePiece(us, PAWN), from);
    } else {
        movePiece(to, from);
    }

    if (flag == EP_CAPTURE) put(u.captured, to + (us == WHITE ? -8 : 8));
    else if (isCapture(m)) put(u.captured, to);

    castling = u.castling;
    ep = u.ep;
    halfmove = u.halfmove;
    key = u.key;
}

void Position::makeNull() {
    Undo& u = undo[ply];
    u.captured = NO_PIECE;
    u.castling = castling;
    u.ep = ep;
    u.halfmove = halfmove;
    u.key = key;
    if (ep != NO_SQ) key ^= Zobrist::ep[fileOf(ep)];
    ep = NO_SQ;
    side ^= 1;
    key ^= Zobrist::side;
    halfmove++;
    ply++;
}

void Position::unmakeNull() {
    ply--;
    side ^= 1;
    const Undo& u = undo[ply];
    ep = u.ep;
    halfmove = u.halfmove;
    key = u.key;
}

bool Position::isRepetition() const {
    int limit = std::max(0, ply - halfmove);
    for (int i = ply - 2; i >= limit; i -= 2)
        if (undo[i].key == key) return true;
    return false;
}

int Position::repetitionCount() const {
    int n = 0;
    int limit = std::max(0, ply - halfmove);
    for (int i = ply - 2; i >= limit; i -= 2)
        if (undo[i].key == key) n++;
    return n;
}

bool Position::insufficientMaterial() const {
    if (pieces[makePiece(WHITE, PAWN)] | pieces[makePiece(BLACK, PAWN)] |
        pieces[makePiece(WHITE, ROOK)] | pieces[makePiece(BLACK, ROOK)] |
        pieces[makePiece(WHITE, QUEEN)] | pieces[makePiece(BLACK, QUEEN)])
        return false;
    U64 minors = pieces[makePiece(WHITE, KNIGHT)] | pieces[makePiece(BLACK, KNIGHT)] |
                 pieces[makePiece(WHITE, BISHOP)] | pieces[makePiece(BLACK, BISHOP)];
    return popcount(minors) <= 1;
}

bool Position::hasNonPawnMaterial(int c) const {
    return pieces[makePiece(c, KNIGHT)] | pieces[makePiece(c, BISHOP)] |
           pieces[makePiece(c, ROOK)] | pieces[makePiece(c, QUEEN)];
}

std::string Position::pretty() const {
    std::string s;
    for (int r = 7; r >= 0; r--) {
        s += char('1' + r);
        s += ' ';
        for (int f = 0; f < 8; f++) {
            int p = board[r * 8 + f];
            s += p == NO_PIECE ? '.' : "PNBRQKpnbrqk"[p];
            s += ' ';
        }
        s += '\n';
    }
    s += "  a b c d e f g h\n";
    s += "fen " + fen() + "\n";
    return s;
}
