#!/usr/bin/env python3
"""Multi-engine tournament with live state for the Unity viewer.

Examples:
  # chessai against every listed engine
  python3 tools/tournament.py --mode gauntlet --hero chessai \
      --engines sf-1600,sf-2000,sf-2400,stash-v17.0,stash-v21.0,weiss --games 20
  # every engine against every other engine
  python3 tools/tournament.py --mode roundrobin --engines chessai,sf-2000,stash-v21.0,weiss --games 10

Live files (read by Unity "토너먼트 관전"): matches/live/board_<slot>.json, standings.json, games.jsonl
"""
import argparse
import glob
import json
import math
import multiprocessing as mp
import os
import sys
import time
from concurrent.futures import ProcessPoolExecutor, as_completed

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import match  # noqa: E402

ROOT = match.ROOT
LIVE = os.path.join(ROOT, "matches", "live")
STOP_FILE = os.path.join(ROOT, "matches", "STOP")


def points(result, name, g):
    if result == "1/2-1/2":
        return 0.5
    white_won = result == "1-0"
    return 1.0 if (g["white"] == name) == white_won else 0.0


def compute_ratings(names, games, anchors):
    """Bradley-Terry maximum likelihood (draw = half point) via MM iterations."""
    idx = {n: i for i, n in enumerate(names)}
    N = len(names)
    n = [[0.0] * N for _ in range(N)]
    s = [[0.0] * N for _ in range(N)]
    for g in games:
        a, b = idx[g["white"]], idx[g["black"]]
        pa = points(g["result"], g["white"], g)
        n[a][b] += 1
        n[b][a] += 1
        s[a][b] += pa
        s[b][a] += 1 - pa
    real_n = [row[:] for row in n]
    # One virtual draw per played pairing keeps 100%/0% scores finite.
    for i in range(N):
        for j in range(N):
            if i != j and real_n[i][j] > 0:
                n[i][j] += 1
                s[i][j] += 0.5

    gamma = [1.0] * N
    for _ in range(3000):
        new = gamma[:]
        for i in range(N):
            denom = sum(n[i][j] / (gamma[i] + gamma[j]) for j in range(N) if j != i and n[i][j] > 0)
            wins = sum(s[i])
            if denom > 0 and wins > 0:
                new[i] = wins / denom
        gm = math.exp(sum(math.log(max(g, 1e-12)) for g in new) / N)
        new = [g / gm for g in new]
        if max(abs(a - b) for a, b in zip(new, gamma)) < 1e-9:
            gamma = new
            break
        gamma = new

    elo = [400 * math.log10(max(g, 1e-12)) for g in gamma]
    played = [sum(real_n[i]) > 0 for i in range(N)]
    anchored = [i for i in range(N) if names[i] in anchors and played[i]]
    if anchored:
        offset = sum(anchors[names[i]] - elo[i] for i in anchored) / len(anchored)
    else:
        offset = 1500 - sum(elo) / N
    elo = [e + offset for e in elo]

    ci = []
    for i in range(N):
        info = 0.0
        for j in range(N):
            if j != i and real_n[i][j] > 0:
                p = 1 / (1 + 10 ** ((elo[j] - elo[i]) / 400))
                info += real_n[i][j] * p * (1 - p)
        ci.append(1.96 * 400 / math.log(10) / math.sqrt(info) if info > 0 else 0)
    return {names[i]: (elo[i], ci[i]) for i in range(N)}, bool(anchored)


def standings(names, games, engines, meta):
    anchors = {n: engines[n]["elo"] for n in names if engines[n].get("elo") is not None}
    ratings, anchored = compute_ratings(names, games, anchors)
    rows = []
    for name in names:
        mine = [g for g in games if name in (g["white"], g["black"])]
        w = sum(1 for g in mine if points(g["result"], name, g) == 1)
        l = sum(1 for g in mine if points(g["result"], name, g) == 0)
        d = len(mine) - w - l
        as_w = [g for g in mine if g["white"] == name]
        as_b = [g for g in mine if g["black"] == name]
        pct = lambda gs: round(100 * sum(points(g["result"], name, g) for g in gs) / len(gs), 1) if gs else 0
        depths = [g["white_depth"] if g["white"] == name else g["black_depth"] for g in mine]
        r, c = ratings[name]
        rows.append({
            "name": name, "games": len(mine), "w": w, "d": d, "l": l,
            "score": pct(mine), "rating": round(r), "ci": round(c),
            "anchorElo": engines[name].get("elo") or 0,
            "whiteScore": pct(as_w), "blackScore": pct(as_b),
            "avgPlies": round(sum(g["plies"] for g in mine) / len(mine), 1) if mine else 0,
            "avgDepth": round(sum(depths) / len(depths), 1) if depths else 0,
            "losses": {k: v for k, v in _loss_reasons(name, mine).items()},
        })
    rows.sort(key=lambda r: -r["rating"])

    order = [r["name"] for r in rows]
    cross = []
    for a in order:
        cells = []
        for b in order:
            if a == b:
                cells.append("")
                continue
            gs = [g for g in games if {g["white"], g["black"]} == {a, b}]
            if not gs:
                cells.append("")
                continue
            pts = sum(points(g["result"], a, g) for g in gs)
            cells.append(f"{pts:g}/{len(gs)}")
        cross.append({"cells": cells})

    for r in rows:  # JsonUtility cannot read dictionaries; flatten for Unity
        r["lossReasons"] = ", ".join(f"{k} {v}" for k, v in sorted(r.pop("losses").items()))
    return dict(meta, engines=rows, crossNames=order, cross=cross, anchored=anchored, done=len(games))


def _loss_reasons(name, games):
    out = {}
    for g in games:
        if points(g["result"], name, g) == 0:
            key = g["termination"].split(" ")[0]
            out[key] = out.get(key, 0) + 1
    return out


def write_report(path, st, minutes):
    lines = [f"# {st['name']}", "",
             f"- mode: {st['mode']}, time control: {st['tc']}, games: {st['done']}/{st['total']}, "
             f"wall time {minutes:.1f} min",
             "- ratings: maximum likelihood over all games"
             + (", anchored to Stockfish UCI_Elo settings" if st["anchored"] else ", relative (mean 1500)"), "",
             "| # | engine | Elo | ±95% | games | W | D | L | score | as white | as black | avg depth | losses by |",
             "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for i, r in enumerate(st["engines"], 1):
        lines.append(f"| {i} | {r['name']} | {r['rating']} | {r['ci']} | {r['games']} | {r['w']} | {r['d']} | {r['l']} | "
                     f"{r['score']}% | {r['whiteScore']}% | {r['blackScore']}% | {r['avgDepth']} | {r['lossReasons']} |")
    lines += ["", "## Crosstable (row engine's points vs column engine)", "",
              "| | " + " | ".join(st["crossNames"]) + " |",
              "| --- " * (len(st["crossNames"]) + 1) + "|"]
    for name, row in zip(st["crossNames"], st["cross"]):
        lines.append(f"| {name} | " + " | ".join(c or "-" for c in row["cells"]) + " |")
    with open(path, "w") as f:
        f.write("\n".join(lines) + "\n")
    return "\n".join(lines)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", choices=["gauntlet", "roundrobin"], default="gauntlet")
    ap.add_argument("--hero", default="chessai", help="engine under test (gauntlet)")
    ap.add_argument("--engines", required=True, help="comma-separated engine names")
    ap.add_argument("--games", type=int, default=10, help="games per pairing (even)")
    ap.add_argument("--tc", default="10+0.1")
    ap.add_argument("--concurrency", type=int, default=4)
    ap.add_argument("--name", default=None)
    args = ap.parse_args()

    engines = match.load_engines()
    openings = match.load_openings()
    others = [e for e in args.engines.split(",") if e]
    names = ([args.hero] + [e for e in others if e != args.hero]) if args.mode == "gauntlet" else others
    for n in names:
        if n not in engines:
            sys.exit(f"unknown engine: {n} (see tools/engines.json)")
    if args.mode == "gauntlet":
        pairs = [(args.hero, o) for o in names[1:]]
    else:
        pairs = [(a, b) for i, a in enumerate(names) for b in names[i + 1:]]
    base, inc = (float(x) for x in args.tc.split("+"))
    name = args.name or match.next_name(args.mode)

    # Interleave pairings round by round so the standings fill in evenly.
    tasks = []
    for i in range(args.games):
        for p, (a, b) in enumerate(pairs):
            op_name, op_moves = openings[(p * 7 + i // 2) % len(openings)]
            a_white = i % 2 == 0
            tasks.append({
                "white": engines[a] if a_white else engines[b], "black": engines[b] if a_white else engines[a],
                "base": base, "inc": inc, "opening": op_moves, "opening_name": op_name,
                "round": i + 1, "event": f"{name} ({args.tc})", "live_dir": LIVE, "game_id": len(tasks) + 1,
            })

    os.makedirs(LIVE, exist_ok=True)
    for f in glob.glob(os.path.join(LIVE, "*")):
        os.remove(f)
    if os.path.exists(STOP_FILE):
        os.remove(STOP_FILE)
    os.makedirs(os.path.join(ROOT, "matches", "raw"), exist_ok=True)
    os.makedirs(os.path.join(ROOT, "matches", "reports"), exist_ok=True)
    pgn_path = os.path.join(ROOT, "matches", "raw", f"{name}.pgn")
    games_path = os.path.join(LIVE, "games.jsonl")
    meta = {"name": name, "mode": args.mode, "tc": args.tc, "total": len(tasks), "running": True,
            "concurrency": args.concurrency}
    match.write_json_atomic(os.path.join(LIVE, "standings.json"), standings(names, [], engines, meta))
    print(f"{name}: {len(names)} engines, {len(pairs)} pairings, {len(tasks)} games, tc {args.tc}, "
          f"concurrency {args.concurrency}", flush=True)

    games = []
    t0 = time.time()
    counter = mp.Value("i", 0)
    with open(pgn_path, "w") as pgn_file, open(games_path, "a") as games_file, \
            ProcessPoolExecutor(max_workers=args.concurrency, initializer=match.init_worker,
                                initargs=(counter,)) as pool:
        futures = [pool.submit(match.play_game, t) for t in tasks]
        for k, fut in enumerate(as_completed(futures), 1):
            if fut.cancelled():
                continue
            try:
                g = fut.result()
            except Exception as ex:
                print(f"runner error: {ex}", flush=True)
                continue
            games.append(g)
            pgn_file.write(g["pgn"] + "\n\n")
            pgn_file.flush()
            games_file.write(json.dumps({
                "id": g["game_id"], "round": g["round"], "white": g["white"], "black": g["black"],
                "result": g["result"], "termination": g["termination"], "opening": g["opening"],
                "plies": g["plies"], "bookPlies": g["book_plies"], "moves": g["moves"]}, ensure_ascii=False) + "\n")
            games_file.flush()
            st = standings(names, games, engines, meta)
            match.write_json_atomic(os.path.join(LIVE, "standings.json"), st)
            print(f"[{k}/{len(tasks)}] {g['white']} - {g['black']}: {g['result']} ({g['termination']})", flush=True)

            # Stop requested (Unity "중지" button writes this file): drop queued games,
            # let the games already on the boards finish.
            if os.path.exists(STOP_FILE) and not meta.get("stopping"):
                meta["stopping"] = True
                for f in futures:
                    f.cancel()
                print("stop requested: finishing games in progress", flush=True)

    if os.path.exists(STOP_FILE):
        os.remove(STOP_FILE)
    meta["running"] = False
    st = standings(names, games, engines, meta)
    match.write_json_atomic(os.path.join(LIVE, "standings.json"), st)
    report = write_report(os.path.join(ROOT, "matches", "reports", f"{name}.md"), st, (time.time() - t0) / 60)
    print(report)
    print(f"\npgn: {pgn_path}")


if __name__ == "__main__":
    main()
