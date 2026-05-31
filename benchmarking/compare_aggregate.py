#!/usr/bin/env python3
"""Aggregate three BDN JSON reports into a side-by-side comparison table.

Usage (called by compare.sh):
    python benchmarking/compare_aggregate.py \
        --herald       .compare-out/herald \
        --serilog      .compare-out/serilog \
        --compat       .compare-out/serilog-compat \
        --arities      2,4 \
        --out          .compare-out

Output:
    Console: tab-separated table with ns + bytes columns per arity.
    File:    .compare-out/comparison.tsv
"""
import argparse
import glob
import json
import pathlib
import sys
from typing import Optional


def load_report(artifacts_dir: str) -> list[dict]:
    """Find and load the BDN full-report JSON from the artifacts directory.

    BDN writes the report under results/ when --artifacts is set, but the
    exact subdirectory can vary by BDN version. Try both locations.
    """
    base = pathlib.Path(artifacts_dir)

    # BDN writes either *-report-full.json or *-report-full-compressed.json
    # depending on the version. Try both names, results/ first then root.
    patterns = [
        str(base / "results" / "*-report-full.json"),
        str(base / "results" / "*-report-full-compressed.json"),
        str(base / "*-report-full.json"),
        str(base / "*-report-full-compressed.json"),
    ]
    files: list[str] = []
    for pat in patterns:
        files = glob.glob(pat)
        if files:
            break
    if not files:
        print(f"  WARNING: No full-report JSON found in {artifacts_dir}",
              file=sys.stderr)
        return []

    with open(files[0]) as f:
        data = json.load(f)
    return data.get("Benchmarks", [])


def arity_from_method(method: str, arities: list[str]) -> Optional[str]:
    """Identify arity from a benchmark's full method name.

    Canonical rows use Compare_ArityN_AllStrings (checked first).
    Exploratory rows embed the word-form (Two, Four, Eight, …).
    """
    # Canonical check: exact substring
    for a in arities:
        if f"Compare_Arity{a}_AllStrings" in method:
            return a

    # Exploratory check: word-token mapping
    word_map = {
        "Zero": "0", "One": "1", "Two": "2", "Four": "4",
        "Eight": "8", "Twelve": "12", "Sixteen": "16",
    }
    for word, num in word_map.items():
        if num in arities and word in method:
            return num

    return None


def parse_benchmarks(benchmarks: list[dict], arities: list[str]) -> dict:
    """Return {arity_str: {"ns": float, "bytes": int}} for matched benchmarks.

    Takes the first match per arity — canonical rows appear exactly once,
    and exploratory rows are grouped by the arity filter word.
    """
    result: dict[str, dict] = {}
    for b in benchmarks:
        # BDN reports use either FullName or Method depending on version.
        method = b.get("FullName", "") + b.get("Method", "")
        arity = arity_from_method(method, arities)
        if arity is None:
            continue
        ns = b.get("Statistics", {}).get("Mean", 0.0)
        byt = b.get("Memory", {}).get("BytesAllocatedPerOperation", 0)
        if arity not in result:
            result[arity] = {"ns": ns, "bytes": byt}
    return result


def format_bytes(b: int) -> str:
    """Human-readable bytes: 0, 40 B, 1.2 KB, etc."""
    if b == 0:
        return "0"
    if b < 1024:
        return f"{b} B"
    return f"{b / 1024:.1f} KB"


def main() -> None:
    ap = argparse.ArgumentParser(
        description="Aggregate BDN reports into a three-way comparison table."
    )
    ap.add_argument("--herald",  required=True, help="herald artifacts dir")
    ap.add_argument("--serilog", required=True, help="serilog artifacts dir")
    ap.add_argument("--compat",  required=True, help="serilog-compat artifacts dir")
    ap.add_argument("--arities", required=True, help="comma-separated arity list, e.g. 2,4")
    ap.add_argument("--out",     required=True, help="output directory for TSV")
    args = ap.parse_args()

    arities = args.arities.split(",")

    herald_data  = parse_benchmarks(load_report(args.herald),  arities)
    serilog_data = parse_benchmarks(load_report(args.serilog), arities)
    compat_data  = parse_benchmarks(load_report(args.compat),  arities)

    libraries = [
        ("Herald native",          herald_data),
        ("Herald Serilog-compat",  compat_data),
        ("Real Serilog 4.3.1",     serilog_data),
    ]

    # Build header: one pair of columns per arity
    arity_cols = "\t".join(
        f"Arity {a} (ns)\tArity {a} (B)" for a in arities
    )
    header = f"Library\t{arity_cols}"

    rows: list[str] = []
    for name, data in libraries:
        cells = []
        for a in arities:
            if a in data:
                cells.append(f"{data[a]['ns']:.1f}\t{data[a]['bytes']}")
            else:
                cells.append("-\t-")
        rows.append(f"{name}\t" + "\t".join(cells))

    # Console output
    print(header)
    for row in rows:
        print(row)

    # TSV file
    out_path = pathlib.Path(args.out) / "comparison.tsv"
    with open(out_path, "w") as f:
        f.write(header + "\n")
        f.write("\n".join(rows) + "\n")
    print(f"\nTSV written to {out_path}")


if __name__ == "__main__":
    main()
