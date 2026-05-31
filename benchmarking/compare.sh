#!/usr/bin/env bash
# benchmarking/compare.sh — three-way Herald/Compat/Serilog arity comparison
#
# Usage:
#   bash benchmarking/compare.sh --arities 2,4
#   bash benchmarking/compare.sh --arities 2,4 --canonical
#   bash benchmarking/compare.sh --scenario serilog-canonical
#
# Net10 only (per the CLAUDE.md benchmark discipline).
# Canonical rows share the same method name across all three projects so
# results aggregate cleanly. Exploratory rows use project-local names.
#
# Named scenarios (--scenario):
#   serilog-canonical  — the verbatim Serilog docs example:
#                        log.Information("Processed {@Position} in {Elapsed:000} ms.", position, elapsedMs)
#                        Runs Compare_Arity2_SerilogCanonical across all three projects.
#
# Output: .compare-out/ (TSV + raw BDN JSON) — gitignored scratch dir.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
OUT_DIR="$SCRIPT_DIR/.compare-out"
mkdir -p "$OUT_DIR"

# --- arg parsing ---
ARITIES=""
CANONICAL=false
SCENARIO=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --arities)   ARITIES="$2";   shift 2 ;;
        --canonical) CANONICAL=true;  shift   ;;
        --scenario)  SCENARIO="$2";  shift 2 ;;
        *) echo "Unknown arg: $1"; exit 1 ;;
    esac
done

# --- scenario handling (takes precedence over --arities / --canonical) ---
FILTER_ARGS=()
if [[ -n "$SCENARIO" ]]; then
    case "$SCENARIO" in
        "serilog-canonical")
            FILTER_ARGS+=(--filter "*Compare_Arity2_SerilogCanonical*")
            ;;
        *)
            echo "Unknown scenario: $SCENARIO"
            echo "Supported scenarios: serilog-canonical"
            exit 1
            ;;
    esac
    echo "=== Three-way benchmark: scenario=$SCENARIO ==="
    echo "    Filters: ${FILTER_ARGS[*]}"
    echo ""
else
    [[ -z "$ARITIES" ]] && { echo "Usage: $0 --arities 2,4 [--canonical]  OR  $0 --scenario serilog-canonical"; exit 1; }

    # --- arity → exploratory filter-word mapping ---
    declare -A ARITY_WORD=(
        [0]="Zero"
        [1]="One"
        [2]="Two"
        [4]="Four"
        [8]="Eight"
        [12]="Twelve"
        [16]="Sixteen"
    )

    # Build --filter args: canonical rows use a predictable shared name;
    # exploratory rows use the word-token filter that matches existing methods.
    IFS=',' read -ra ARITY_LIST <<< "$ARITIES"
    for A in "${ARITY_LIST[@]}"; do
        if $CANONICAL; then
            FILTER_ARGS+=(--filter "*Compare_Arity${A}_AllStrings*")
        else
            WORD="${ARITY_WORD[$A]:-}"
            [[ -z "$WORD" ]] && { echo "No word mapping for arity $A (supported: 0 1 2 4 8 12 16)"; exit 1; }
            FILTER_ARGS+=(--filter "*${WORD}*")
        fi
    done

    echo "=== Three-way benchmark: arities=$ARITIES canonical=$CANONICAL ==="
    echo "    Filters: ${FILTER_ARGS[*]}"
    echo ""
fi

# --- run one project, place BDN artifacts in a named subdirectory ---
run_project() {
    local NAME="$1"
    local CSPROJ="$2"
    local SUBDIR="$OUT_DIR/$NAME"
    mkdir -p "$SUBDIR"
    echo "--- Running $NAME ---"
    # --framework goes to dotnet run (before --); BDN args go after --.
    dotnet run -c Release --project "$CSPROJ" --framework net10.0 -- \
        "${FILTER_ARGS[@]}" \
        --memory \
        --exporters json \
        --artifacts "$SUBDIR"
    echo ""
}

run_project "herald" \
    "$REPO_ROOT/benchmarking/comparisons/net10/herald/HeraldOSS.Bench.Herald.csproj"

run_project "serilog" \
    "$REPO_ROOT/benchmarking/comparisons/net10/serilog/HeraldOSS.Bench.Serilog.csproj"

run_project "serilog-compat" \
    "$REPO_ROOT/benchmarking/comparisons/net10/serilog-compat/HeraldOSS.Bench.SerilogCompat.csproj"

echo ""
echo "=== Aggregating results ==="
# When running a named scenario, pass the scenario label instead of arities
# so compare_aggregate.py can label the output file correctly.
AGGREGATE_ARITIES="${ARITIES:-scenario-${SCENARIO}}"
python "$SCRIPT_DIR/compare_aggregate.py" \
    --herald       "$OUT_DIR/herald" \
    --serilog      "$OUT_DIR/serilog" \
    --compat       "$OUT_DIR/serilog-compat" \
    --arities      "$AGGREGATE_ARITIES" \
    --out          "$OUT_DIR"
