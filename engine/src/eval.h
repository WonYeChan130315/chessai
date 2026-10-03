#pragma once
#include "position.h"

// Static evaluation in centipawns from the side to move's point of view.
int evaluate(const Position& pos);

extern const int PIECE_VALUE[6];

enum EvalTerm { T_MATERIAL, T_PST, T_PAWNS, T_MOBILITY, T_ROOKS, T_BISHOPS, EVAL_TERMS };

// Per-term scores from White's point of view (tapered), for the "evalbreak" command.
struct EvalBreakdown {
    int terms[EVAL_TERMS];
    int tempo;
    int total; // White's point of view, equals evaluate() for White
    int phase; // 24 = opening, 0 = pure endgame
};
EvalBreakdown evaluateBreakdown(const Position& pos);

// Per-piece positional contributions and per-square attacker counts, for GUI overlays.
std::string evalSquaresString(const Position& pos);
std::string controlString(const Position& pos);
