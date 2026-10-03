#include "position.h"

using namespace BB;

static void addPromos(MoveList& list, int from, int to, bool capture, bool queenOnly) {
    int base = capture ? PROMO_CAP_N : PROMO_N;
    if (queenOnly) {
        list.add(encodeMove(from, to, base + 3));
        return;
    }
    for (int i = 3; i >= 0; i--) list.add(encodeMove(from, to, base + i));
}

void generateMoves(const Position& pos, MoveList& list, bool capturesOnly) {
    list.count = 0;
    const int us = pos.side, them = us ^ 1;
    const U64 own = pos.colorBB[us], enemies = pos.colorBB[them], empty = ~pos.occ;

    // Pawns
    const int up = us == WHITE ? 8 : -8;
    const U64 promoFrom = us == WHITE ? rankBB(6) : rankBB(1);
    const U64 doubleTo = us == WHITE ? rankBB(3) : rankBB(4);
    U64 pawns = pos.pieces[makePiece(us, PAWN)];
    while (pawns) {
        int from = popLsb(pawns);
        int to = from + up;
        U64 caps = pawnAttacks[us][from] & enemies;
        if (sqBB(from) & promoFrom) {
            if (empty & sqBB(to)) addPromos(list, from, to, false, capturesOnly);
            while (caps) addPromos(list, from, popLsb(caps), true, capturesOnly);
            continue;
        }
        if (!capturesOnly && (empty & sqBB(to))) {
            list.add(encodeMove(from, to, QUIET));
            int to2 = to + up;
            if ((sqBB(to2) & doubleTo) && (empty & sqBB(to2))) list.add(encodeMove(from, to2, DOUBLE_PUSH));
        }
        while (caps) list.add(encodeMove(from, popLsb(caps), CAPTURE));
        if (pos.ep != NO_SQ && (pawnAttacks[us][from] & sqBB(pos.ep)))
            list.add(encodeMove(from, pos.ep, EP_CAPTURE));
    }

    // Pieces
    const U64 targetMask = capturesOnly ? enemies : ~own;
    for (int pt = KNIGHT; pt <= KING; pt++) {
        U64 bb = pos.pieces[makePiece(us, pt)];
        while (bb) {
            int from = popLsb(bb);
            U64 att;
            switch (pt) {
                case KNIGHT: att = knightAttacks[from]; break;
                case BISHOP: att = bishopAttacks(from, pos.occ); break;
                case ROOK: att = rookAttacks(from, pos.occ); break;
                case QUEEN: att = queenAttacks(from, pos.occ); break;
                default: att = kingAttacks[from]; break;
            }
            att &= targetMask;
            while (att) {
                int to = popLsb(att);
                list.add(encodeMove(from, to, (enemies & sqBB(to)) ? CAPTURE : QUIET));
            }
        }
    }

    // Castling
    if (capturesOnly) return;
    if (us == WHITE) {
        if ((pos.castling & WK) && !(pos.occ & (sqBB(5) | sqBB(6))) &&
            !pos.attacked(4, them) && !pos.attacked(5, them) && !pos.attacked(6, them))
            list.add(encodeMove(4, 6, KING_CASTLE));
        if ((pos.castling & WQ) && !(pos.occ & (sqBB(1) | sqBB(2) | sqBB(3))) &&
            !pos.attacked(4, them) && !pos.attacked(3, them) && !pos.attacked(2, them))
            list.add(encodeMove(4, 2, QUEEN_CASTLE));
    } else {
        if ((pos.castling & BK) && !(pos.occ & (sqBB(61) | sqBB(62))) &&
            !pos.attacked(60, them) && !pos.attacked(61, them) && !pos.attacked(62, them))
            list.add(encodeMove(60, 62, KING_CASTLE));
        if ((pos.castling & BQ) && !(pos.occ & (sqBB(57) | sqBB(58) | sqBB(59))) &&
            !pos.attacked(60, them) && !pos.attacked(59, them) && !pos.attacked(58, them))
            list.add(encodeMove(60, 58, QUEEN_CASTLE));
    }
}

std::vector<Move> legalMoves(Position& pos) {
    MoveList list;
    generateMoves(pos, list, false);
    std::vector<Move> out;
    for (int i = 0; i < list.count; i++) {
        if (pos.make(list.moves[i])) {
            pos.unmake(list.moves[i]);
            out.push_back(list.moves[i]);
        }
    }
    return out;
}

Move parseUciMove(Position& pos, const std::string& s) {
    for (Move m : legalMoves(pos))
        if (moveToUci(m) == s) return m;
    return 0;
}

U64 perft(Position& pos, int depth) {
    MoveList list;
    generateMoves(pos, list, false);
    U64 nodes = 0;
    for (int i = 0; i < list.count; i++) {
        Move m = list.moves[i];
        if (!pos.make(m)) continue;
        nodes += depth <= 1 ? 1 : perft(pos, depth - 1);
        pos.unmake(m);
    }
    return nodes;
}
