# roundrobin-1

- mode: roundrobin, time control: 10+0.1, games: 210/210, wall time 22.4 min
- ratings: maximum likelihood over all games, anchored to Stockfish UCI_Elo settings

| # | engine | Elo | ±95% | games | W | D | L | score | as white | as black | avg depth | losses by |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | weiss | 3015 | 180 | 60 | 58 | 1 | 1 | 97.5% | 96.7% | 98.3% | 19.8 | time_forfeit 1 |
| 2 | stash-v25.0 | 2769 | 125 | 60 | 43 | 6 | 11 | 76.7% | 73.3% | 80.0% | 22.2 | adjudication 11 |
| 3 | stash-v21.0 | 2597 | 112 | 60 | 31 | 6 | 23 | 56.7% | 58.3% | 55.0% | 20.3 | adjudication 23 |
| 4 | chessai | 2557 | 111 | 60 | 28 | 6 | 26 | 51.7% | 58.3% | 45.0% | 12.4 | adjudication 25, checkmate 1 |
| 5 | stash-v17.0 | 2477 | 112 | 60 | 21 | 8 | 31 | 41.7% | 41.7% | 41.7% | 18.0 | adjudication 30, checkmate 1 |
| 6 | sf-2400 | 2311 | 125 | 60 | 12 | 3 | 45 | 22.5% | 28.3% | 16.7% | 22.1 | adjudication 13, checkmate 30, time_forfeit 2 |
| 7 | sf-2000 | 2089 | 172 | 60 | 1 | 2 | 57 | 3.3% | 6.7% | 0.0% | 25.0 | adjudication 20, checkmate 36, time_forfeit 1 |

## Crosstable (row engine's points vs column engine)

| | weiss | stash-v25.0 | stash-v21.0 | chessai | stash-v17.0 | sf-2400 | sf-2000 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| weiss | - | 10/10 | 10/10 | 10/10 | 8.5/10 | 10/10 | 10/10 |
| stash-v25.0 | 0/10 | - | 8.5/10 | 10/10 | 8.5/10 | 9/10 | 10/10 |
| stash-v21.0 | 0/10 | 1.5/10 | - | 6/10 | 8.5/10 | 8/10 | 10/10 |
| chessai | 0/10 | 0/10 | 4/10 | - | 7/10 | 10/10 | 10/10 |
| stash-v17.0 | 1.5/10 | 1.5/10 | 1.5/10 | 3/10 | - | 8/10 | 9.5/10 |
| sf-2400 | 0/10 | 1/10 | 2/10 | 0/10 | 2/10 | - | 8.5/10 |
| sf-2000 | 0/10 | 0/10 | 0/10 | 0/10 | 0.5/10 | 1.5/10 | - |
