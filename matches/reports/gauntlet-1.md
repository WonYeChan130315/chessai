# gauntlet-1

- mode: gauntlet, time control: 3+0.03, games: 11/12, wall time 0.7 min
- ratings: maximum likelihood over all games, anchored to Stockfish UCI_Elo settings

| # | engine | Elo | ±95% | games | W | D | L | score | as white | as black | avg depth | losses by |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | chessai | 1766 | 295 | 11 | 9 | 2 | 0 | 90.9% | 83.3% | 100.0% | 11.9 |  |
| 2 | stash-v17.0 | 1575 | 352 | 5 | 0 | 2 | 3 | 20.0% | 0.0% | 33.3% | 14.2 | adjudication 3 |
| 3 | sf-1320 | 1320 | 540 | 6 | 0 | 0 | 6 | 0.0% | 0.0% | 0.0% | 24.6 | checkmate 6 |

## Crosstable (row engine's points vs column engine)

| | chessai | stash-v17.0 | sf-1320 |
| --- | --- | --- | --- |
| chessai | - | 4/5 | 6/6 |
| stash-v17.0 | 1/5 | - | - |
| sf-1320 | 0/6 | - | - |
