#!/usr/bin/env python3
"""Engine-vs-engine match runner (python-chess).

Examples:
  python3 tools/match.py --a chessai --b sf-1600 --games 80
  python3 tools/match.py --a chessai --gauntlet sf-1320,sf-1600,sf-2000 --games 80 --name final
"""
import argparse
import csv
import json
import math
import os
import sys
import time
from concurrent.futures import ProcessPoolExecutor, as_completed

import chess
import chess.engine
import chess.pgn

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def load_engines():
    with open(os.path.join(ROOT, "tools", "engines.json")) as f:
        engines = {e["name"]: e for e in json.load(f)}
    for e in engines.values():
        e["cmd"] = [c if os.path.isabs(c) or os.sep not in c else os.path.join(ROOT, c) for c in e["cmd"]]
    return engines


def load_openings():
    openings = []
    with open(os.path.join(ROOT, "tools", "openings.txt")) as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            name, moves = [s.strip() for s in line.split("|")]
            board = chess.Board()
            try:
                for m in moves.split():
                    board.push_uci(m)
            except ValueError:
                print(f"skip invalid opening: {name}", file=sys.stderr)
                continue
            openings.append((name, moves.split()))
    return openings


def open_engine(spec):
    eng = chess.engine.SimpleEngine.popen_uci(spec["cmd"], stderr=open(os.devnull, "w"))
    opts = {k: v for k, v in spec.get("options", {}).items() if k in eng.options}
    if opts:
        eng.configure(opts)
    return eng


# Worker slot number, set per worker process by tournament.py so each worker
# writes its live board to its own file.
LIVE_SLOT = 0


def init_worker(counter):
    global LIVE_SLOT
    with counter.get_lock():
        LIVE_SLOT = counter.value
        counter.value += 1


def write_json_atomic(path, data):
    tmp = f"{path}.tmp{os.getpid()}"
    with open(tmp, "w") as f:
        json.dump(data, f, ensure_ascii=False)
    os.replace(tmp, path)


def write_live(task, board, clocks, last_eval, last_depth, status, result=None, termination=None):
    live_dir = task.get("live_dir")
    if not live_dir:
        return
    write_json_atomic(os.path.join(live_dir, f"board_{LIVE_SLOT}.json"), {
        "slot": LIVE_SLOT, "game": task.get("game_id", 0), "round": task["round"],
        "white": task["white"]["name"], "black": task["black"]["name"],
        "fen": board.fen(), "moves": [m.uci() for m in board.move_stack],
        "lastMove": board.move_stack[-1].uci() if board.move_stack else "",
        "whiteClock": round(clocks[chess.WHITE], 2), "blackClock": round(clocks[chess.BLACK], 2),
        "eval": last_eval, "depth": last_depth, "opening": task["opening_name"],
        "status": status, "result": result or "", "termination": termination or "",
    })


def next_name(prefix):
    """prefix-1, prefix-2, ... — the first one with no report yet."""
    n = 1
    while any(os.path.exists(os.path.join(ROOT, "matches", d, f"{prefix}-{n}{ext}"))
              for d, ext in (("reports", ".md"), ("reports", ".csv"), ("raw", ".pgn"))):
        n += 1
    return f"{prefix}-{n}"


def play_game(task):
    """Plays one game. Returns a dict with the result and the PGN text."""
    white, black = task["white"], task["black"]
    base, inc = task["base"], task["inc"]
    board = chess.Board()
    for m in task["opening"]:
        board.push_uci(m)
    opening_plies = len(board.move_stack)

    engines = {}
    result, termination = None, None
    comments = []
    clocks = {chess.WHITE: base, chess.BLACK: base}
    scores = {chess.WHITE: [], chess.BLACK: []}
    depths = {chess.WHITE: [], chess.BLACK: []}
    last_eval, last_depth = "", 0
    write_live(task, board, clocks, last_eval, last_depth, "playing")
    try:
        engines[chess.WHITE] = open_engine(white)
        engines[chess.BLACK] = open_engine(black)
        for e in engines.values():
            e.ping()
        while True:
            outcome = board.outcome(claim_draw=True)
            if outcome:
                result = outcome.result()
                termination = outcome.termination.name.lower()
                break
            if len(board.move_stack) >= 600:
                result, termination = "1/2-1/2", "max_moves"
                break

            side = board.turn
            limit = chess.engine.Limit(white_clock=clocks[chess.WHITE], black_clock=clocks[chess.BLACK],
                                       white_inc=inc, black_inc=inc)
            t0 = time.perf_counter()
            try:
                res = engines[side].play(board, limit, info=chess.engine.INFO_SCORE | chess.engine.INFO_BASIC)
            except (chess.engine.EngineError, chess.engine.EngineTerminatedError) as ex:
                result = "0-1" if side == chess.WHITE else "1-0"
                termination = f"engine_error ({ex.__class__.__name__})"
                break
            elapsed = time.perf_counter() - t0
            clocks[side] -= elapsed
            if clocks[side] < 0:
                result = "0-1" if side == chess.WHITE else "1-0"
                termination = "time_forfeit"
                break
            clocks[side] += inc

            if res.move is None or res.move not in board.legal_moves:
                result = "0-1" if side == chess.WHITE else "1-0"
                termination = f"illegal_move ({res.move})"
                break

            score = res.info.get("score")
            cp = None
            if score is not None:
                cp = score.pov(side).score(mate_score=100000)
                scores[side].append(cp)
            depth = res.info.get("depth", 0)
            depths[side].append(depth)
            comments.append(f"{'' if cp is None else f'{cp / 100:+.2f}'}/{depth} {elapsed:.2f}s")
            board.push(res.move)
            if cp is not None:
                white_cp = cp if side == chess.WHITE else -cp
                last_eval = (f"M{'+' if white_cp > 0 else '-'}" if abs(white_cp) >= 90000
                             else f"{white_cp / 100:+.2f}")
            last_depth = depth
            write_live(task, board, clocks, last_eval, last_depth, "playing")

            # Adjudication
            w, b = scores[chess.WHITE], scores[chess.BLACK]
            if len(w) >= 5 and len(b) >= 5:
                if all(s >= 1000 for s in w[-5:]) and all(s <= -1000 for s in b[-5:]):
                    result, termination = "1-0", "adjudication"
                    break
                if all(s <= -1000 for s in w[-5:]) and all(s >= 1000 for s in b[-5:]):
                    result, termination = "0-1", "adjudication"
                    break
            if board.fullmove_number > 40 and len(w) >= 4 and len(b) >= 4:
                if all(abs(s) <= 10 for s in w[-4:] + b[-4:]):
                    result, termination = "1/2-1/2", "adjudication"
                    break
    finally:
        for e in engines.values():
            try:
                e.quit()
            except Exception:
                try:
                    e.close()
                except Exception:
                    pass

    game = chess.pgn.Game()
    game.headers.update({
        "Event": task["event"], "Site": "local", "Date": "????.??.??",
        "Round": str(task["round"]), "White": white["name"], "Black": black["name"], "Result": result,
        "TimeControl": f"{base:g}+{inc:g}", "Opening": task["opening_name"], "Termination": termination,
    })
    node = game
    for i, move in enumerate(board.move_stack):
        node = node.add_variation(move)
        if i >= opening_plies:
            node.comment = comments[i - opening_plies]
        elif i == opening_plies - 1:
            node.comment = "book"
    pgn = str(game)
    write_live(task, board, clocks, last_eval, last_depth, "finished", result, termination)
    avg = lambda xs: round(sum(xs) / len(xs), 1) if xs else 0
    return {"white": white["name"], "black": black["name"], "result": result, "termination": termination,
            "plies": len(board.move_stack), "pgn": pgn, "moves": [m.uci() for m in board.move_stack],
            "opening": task["opening_name"], "book_plies": opening_plies, "round": task["round"],
            "game_id": task.get("game_id", 0),
            "white_depth": avg(depths[chess.WHITE]), "black_depth": avg(depths[chess.BLACK])}


def elo_from_score(s):
    s = min(max(s, 1e-4), 1 - 1e-4)
    return -400 * math.log10(1 / s - 1)


def summarize(name, games):
    w = sum(1 for g in games if (g["result"] == "1-0" and g["white"] == name) or (g["result"] == "0-1" and g["black"] == name))
    l = sum(1 for g in games if (g["result"] == "0-1" and g["white"] == name) or (g["result"] == "1-0" and g["black"] == name))
    d = len(games) - w - l
    n = len(games)
    s = (w + d / 2) / n if n else 0.5
    var = ((w * (1 - s) ** 2 + d * (0.5 - s) ** 2 + l * s ** 2) / n) if n else 0
    se = math.sqrt(var / n) if n else 0
    lo, hi = elo_from_score(s - 1.96 * se), elo_from_score(s + 1.96 * se)
    return {"w": w, "d": d, "l": l, "n": n, "score": s, "elo": elo_from_score(s), "lo": lo, "hi": hi}


def estimate_rating(rows):
    """Maximum-likelihood rating of engine A against opponents with known Elo."""
    anchored = [r for r in rows if r.get("opp_elo") is not None and r["n"] > 0]
    # With only 100% or 0% scores the likelihood has no maximum: the rating is undetermined.
    if not anchored or all(r["score"] in (0.0, 1.0) for r in anchored):
        return None
    def loglik(R):
        ll = 0.0
        for r in anchored:
            p = 1 / (1 + 10 ** ((r["opp_elo"] - R) / 400))
            p = min(max(p, 1e-9), 1 - 1e-9)
            pts = r["w"] + r["d"] / 2
            ll += pts * math.log(p) + (r["n"] - pts) * math.log(1 - p)
        return ll
    best = max(range(0, 3600), key=loglik)
    info = 0.0
    for r in anchored:
        p = 1 / (1 + 10 ** ((r["opp_elo"] - best) / 400))
        info += r["n"] * p * (1 - p) * (math.log(10) / 400) ** 2
    se = 1 / math.sqrt(info) if info > 0 else float("inf")
    return best, 1.96 * se


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--a", required=True, help="engine under test")
    ap.add_argument("--b", help="single opponent")
    ap.add_argument("--gauntlet", help="comma-separated opponents")
    ap.add_argument("--games", type=int, default=40, help="games per opponent (even)")
    ap.add_argument("--tc", default="10+0.1", help="base+inc in seconds")
    ap.add_argument("--concurrency", type=int, default=4)
    ap.add_argument("--name", default=None)
    args = ap.parse_args()

    engines = load_engines()
    openings = load_openings()
    opponents = [args.b] if args.b else args.gauntlet.split(",")
    for n in [args.a] + opponents:
        if n not in engines:
            sys.exit(f"unknown engine: {n}")
    base, inc = (float(x) for x in args.tc.split("+"))
    name = args.name or next_name(f"{args.a}-vs-{'-'.join(opponents)}")
    event = f"{name} ({args.tc})"

    tasks = []
    for opp in opponents:
        for i in range(args.games):
            op_name, op_moves = openings[(i // 2) % len(openings)]
            a_white = i % 2 == 0
            tasks.append({
                "white": engines[args.a] if a_white else engines[opp],
                "black": engines[opp] if a_white else engines[args.a],
                "opp": opp, "base": base, "inc": inc, "opening": op_moves, "opening_name": op_name,
                "round": i + 1, "event": event,
            })

    os.makedirs(os.path.join(ROOT, "matches", "raw"), exist_ok=True)
    os.makedirs(os.path.join(ROOT, "matches", "reports"), exist_ok=True)
    pgn_path = os.path.join(ROOT, "matches", "raw", f"{name}.pgn")
    print(f"{len(tasks)} games, tc {args.tc}, concurrency {args.concurrency} -> {pgn_path}", flush=True)

    results = {opp: [] for opp in opponents}
    t0 = time.time()
    with open(pgn_path, "w") as pgn_file, ProcessPoolExecutor(max_workers=args.concurrency) as pool:
        futures = {pool.submit(play_game, t): t for t in tasks}
        for k, fut in enumerate(as_completed(futures), 1):
            t = futures[fut]
            try:
                g = fut.result()
            except Exception as ex:
                print(f"game crashed in runner: {ex}", flush=True)
                continue
            results[t["opp"]].append(g)
            pgn_file.write(g["pgn"] + "\n\n")
            pgn_file.flush()
            s = summarize(args.a, results[t["opp"]])
            print(f"[{k}/{len(tasks)}] {g['white']} vs {g['black']}: {g['result']} ({g['termination']}, "
                  f"{g['plies']} plies) | {args.a} vs {t['opp']}: +{s['w']} ={s['d']} -{s['l']}", flush=True)

    rows = []
    for opp in opponents:
        s = summarize(args.a, results[opp])
        s["opp"] = opp
        s["opp_elo"] = engines[opp].get("elo")
        terms = {}
        for g in results[opp]:
            key = g["termination"].split(" ")[0]
            terms[key] = terms.get(key, 0) + 1
        s["terms"] = terms
        rows.append(s)
    rating = estimate_rating(rows)
    minutes = (time.time() - t0) / 60

    csv_path = os.path.join(ROOT, "matches", "reports", f"{name}.csv")
    with open(csv_path, "w", newline="") as f:
        wr = csv.writer(f)
        wr.writerow(["opponent", "opponent_elo", "games", "wins", "draws", "losses", "score", "elo_diff", "elo_lo", "elo_hi"])
        for r in rows:
            wr.writerow([r["opp"], r["opp_elo"] or "", r["n"], r["w"], r["d"], r["l"], f"{r['score']:.3f}",
                         f"{r['elo']:.0f}", f"{r['lo']:.0f}", f"{r['hi']:.0f}"])

    md = [f"# {name}", "", f"- engine: `{args.a}`", f"- time control: {args.tc}, concurrency {args.concurrency}",
          f"- games: {sum(r['n'] for r in rows)}, wall time {minutes:.1f} min", ""]
    md += ["| opponent | opp Elo | games | W | D | L | score | Elo diff (95%) | terminations |",
           "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for r in rows:
        terms = ", ".join(f"{k} {v}" for k, v in sorted(r["terms"].items()))
        md.append(f"| {r['opp']} | {r['opp_elo'] or '-'} | {r['n']} | {r['w']} | {r['d']} | {r['l']} | "
                  f"{r['score'] * 100:.1f}% | {r['elo']:+.0f} [{r['lo']:+.0f}, {r['hi']:+.0f}] | {terms} |")
    if rating:
        md += ["", f"**Estimated rating of {args.a}: {rating[0]} ± {rating[1]:.0f}** "
                   "(maximum likelihood vs Stockfish UCI_Elo anchors; anchors are calibrated at longer time controls)"]
    md_path = os.path.join(ROOT, "matches", "reports", f"{name}.md")
    with open(md_path, "w") as f:
        f.write("\n".join(md) + "\n")

    print("\n".join(md))
    print(f"\nreport: {md_path}\npgn: {pgn_path}")


if __name__ == "__main__":
    main()
