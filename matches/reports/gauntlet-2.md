# gauntlet-2

- mode: gauntlet, time control: 30+0.3, games: 64/90, wall time 21.1 min
- ratings: maximum likelihood over all games, anchored to Stockfish UCI_Elo settings

| # | engine | Elo | ±95% | games | W | D | L | score | as white | as black | avg depth | losses by |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | weiss | 2874 | 532 | 7 | 7 | 0 | 0 | 100.0% | 100.0% | 100.0% | 23.6 |  |
| 2 | stash-v25.0 | 2659 | 330 | 7 | 5 | 2 | 0 | 85.7% | 83.3% | 87.5% | 22.3 |  |
| 3 | stash-v37.0 | 2659 | 330 | 7 | 5 | 2 | 0 | 85.7% | 83.3% | 87.5% | 21.0 |  |
| 4 | sf-2800 | 2595 | 297 | 7 | 5 | 1 | 1 | 78.6% | 83.3% | 75.0% | 18.4 | adjudication 1 |
| 5 | stash-v21.0 | 2595 | 297 | 7 | 4 | 3 | 0 | 78.6% | 83.3% | 75.0% | 19.3 |  |
| 6 | chessai | 2404 | 117 | 64 | 26 | 9 | 29 | 47.7% | 44.4% | 51.8% | 14.6 | adjudication 28, checkmate 1 |
| 7 | sf-2400 | 2360 | 259 | 7 | 3 | 0 | 4 | 42.9% | 0.0% | 75.0% | 20.9 | adjudication 2, checkmate 2 |
| 8 | stash-v17.0 | 2066 | 389 | 7 | 0 | 1 | 6 | 7.1% | 16.7% | 0.0% | 18.9 | adjudication 5, engine_error 1 |
| 9 | sf-2000 | 1933 | 532 | 7 | 0 | 0 | 7 | 0.0% | 0.0% | 0.0% | 32.0 | adjudication 1, checkmate 6 |
| 10 | sf-1600 | 1912 | 526 | 8 | 0 | 0 | 8 | 0.0% | 0.0% | 0.0% | 33.2 | adjudication 1, checkmate 7 |

## Crosstable (row engine's points vs column engine)

| | weiss | stash-v25.0 | stash-v37.0 | sf-2800 | stash-v21.0 | chessai | sf-2400 | stash-v17.0 | sf-2000 | sf-1600 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| weiss | - | - | - | - | - | 7/7 | - | - | - | - |
| stash-v25.0 | - | - | - | - | - | 6/7 | - | - | - | - |
| stash-v37.0 | - | - | - | - | - | 6/7 | - | - | - | - |
| sf-2800 | - | - | - | - | - | 5.5/7 | - | - | - | - |
| stash-v21.0 | - | - | - | - | - | 5.5/7 | - | - | - | - |
| chessai | 0/7 | 1/7 | 1/7 | 1.5/7 | 1.5/7 | - | 4/7 | 6.5/7 | 7/7 | 8/8 |
| sf-2400 | - | - | - | - | - | 3/7 | - | - | - | - |
| stash-v17.0 | - | - | - | - | - | 0.5/7 | - | - | - | - |
| sf-2000 | - | - | - | - | - | 0/7 | - | - | - | - |
| sf-1600 | - | - | - | - | - | 0/8 | - | - | - | - |
