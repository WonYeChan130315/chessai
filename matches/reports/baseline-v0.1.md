# baseline-v0.1

- engine: `chessai`
- time control: 10+0.1, concurrency 3
- games: 120, wall time 17.3 min

| opponent | opp Elo | games | W | D | L | score | Elo diff (95%) | terminations |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| sf-1600 | 1600 | 40 | 39 | 1 | 0 | 98.8% | +759 [+568, +1600] | adjudication 14, checkmate 25, threefold_repetition 1 |
| sf-2000 | 2000 | 40 | 37 | 0 | 3 | 92.5% | +436 [+292, +1600] | adjudication 20, checkmate 16, time_forfeit 4 |
| sf-2400 | 2400 | 40 | 31 | 2 | 7 | 80.0% | +241 [+132, +422] | adjudication 20, checkmate 19, threefold_repetition 1 |

**Estimated rating of chessai: 2589 ± 114** (maximum likelihood vs Stockfish UCI_Elo anchors; anchors are calibrated at longer time controls)
