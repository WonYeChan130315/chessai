# smoke

- mode: roundrobin, time control: 2+0.02, games: 6/6, wall time 0.2 min
- ratings: maximum likelihood over all games, anchored to Stockfish UCI_Elo settings

| # | engine | Elo | ±95% | games | W | D | L | score | as white | as black | avg depth | losses by |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | stash-v17.0 | 1650 | 409 | 4 | 3 | 1 | 0 | 87.5% | 75.0% | 100.0% | 22.2 |  |
| 2 | chessai | 1557 | 383 | 4 | 2 | 1 | 1 | 62.5% | 50.0% | 75.0% | 11.4 | checkmate 1 |
| 3 | sf-1320 | 1320 | 459 | 4 | 0 | 0 | 4 | 0.0% | 0.0% | 0.0% | 24.1 | adjudication 1, checkmate 3 |

## Crosstable (row engine's points vs column engine)

| | stash-v17.0 | chessai | sf-1320 |
| --- | --- | --- | --- |
| stash-v17.0 | - | 1.5/2 | 2/2 |
| chessai | 0.5/2 | - | 2/2 |
| sf-1320 | 0/2 | 0/2 | - |
