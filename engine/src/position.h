#pragma once
#include "bitboard.h"
#include <string>
#include <vector>

namespace Zobrist {
extern U64 psq[12][64];
extern U64 castle[16];
extern U64 ep[8];
extern U64 side;
void init();
}

struct Undo {
    int captured;
    int castling;
    int ep;
    int halfmove;
    U64 key;
};

class Position {
public:
    U64 pieces[12];
    U64 colorBB[2];
    U64 occ;
    int board[64];
    int side;
    int castling;
    int ep;
    int halfmove;
    int fullmove;
    U64 key;

    // History since the position was set up (game moves + search moves).
    int ply = 0;
    Undo undo[MAX_GAME_PLY];

    bool setFen(const std::string& fen);
    std::string fen() const;

    // Returns false (and leaves the position unchanged) if the move is illegal.
    bool make(Move m);
    void unmake(Move m);
    void makeNull();
    void unmakeNull();

    bool attacked(int sq, int by) const;
    int kingSq(int c) const { return lsb(pieces[makePiece(c, KING)]); }
    bool inCheck() const { return attacked(kingSq(side), side ^ 1); }

    bool isRepetition() const;   // any earlier occurrence (used in search)
    int repetitionCount() const; // number of earlier occurrences
    bool insufficientMaterial() const;
    bool hasNonPawnMaterial(int c) const;

    std::string pretty() const;

private:
    void put(int p, int sq);
    void remove(int sq);
    void movePiece(int from, int to);
    U64 computeKey() const;
};

// movegen.cpp
void generateMoves(const Position& pos, MoveList& list, bool capturesOnly);
std::vector<Move> legalMoves(Position& pos);
Move parseUciMove(Position& pos, const std::string& s);
U64 perft(Position& pos, int depth);
