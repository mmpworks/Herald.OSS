#!/usr/bin/env bash
# benchmarking/compare.sh — three-way Herald/Compat/Serilog comparison
#
# Usage:
#   bash benchmarking/compare.sh --arities 2,4
#   bash benchmarking/compare.sh --arities 1,2,4,8,12,16
#   bash benchmarking/compare.sh --arities 2,4 --canonical
#   bash benchmarking/compare.sh --scenario destructure
#   bash benchmarking/compare.sh --scenario serilog-canonical
#
# Net10 only (per the CLAUDE.md benchmark discipline).
#
# Named scenarios (--scenario):
#   destructure       — the {@Position} destructure family (arity 1,2,4,8,12,16).
#                       Tests: var position = new { Latitude=25, Longitude=134 };
#                              log.Information("Processed {@Position} in {Elapsed:000} ms.", position, 34);
#                              ...scaled with int telemetry props at higher arities.
#   serilog-canonical — single arity-2 row: the verbatim serilog.net docs example.
#
# Output: .compare-out/<run-id>/ — each run gets its own directory so
#   results from different scenarios/arity sets never clobber each other.
#   A .compare-out/<run-id>/comparison.tsv is written on completion.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
BASE_OUT="$SCRIPT_DIR/.compare-out"
mkdir -p "$BASE_OUT"

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

# --- scenario / filter resolution ---
FILTER_ARGS=()
RUN_ID=""

if [[ -n "$SCENARIO" ]]; then
    case "$SCENARIO" in
        "serilog-canonical")
            FILTER_ARGS=(--filter "*Compare_Arity2_SerilogCanonical*")
            RUN_ID="scenario-serilog-canonical"
            ;;
        "destructure")
            # {@Position} family: SerilogDestructureBenchmarks.Canonical_Arity{1,2,4,8,12,16}
            # Identical method names across all three projects — single filter covers all.
            FILTER_ARGS=(--filter "*Canonical_Arity*")
            RUN_ID="scenario-destructure"
            ;;
        *)
            echo "Unknown scenario: $SCENARIO"
            echo "Supported scenarios: destructure, serilog-canonical"
            exit 1
            ;;
    esac
    echo "=== Three-way benchmark: scenario=$SCENARIO ==="
    echo "    Filters: ${FILTER_ARGS[*]}"
    echo ""
else
    [[ -z "$ARITIES" ]] && {
        echo "Usage: $0 --arities 2,4 [--canonical]  OR  $0 --scenario <name>"
        echo "Scenarios: destructure, serilog-canonical"
        exit 1
    }

    # arity → word-token map for exploratory (non-canonical) method names
    declare -A ARITY_WORD=(
        [0]="Zero" [1]="One" [2]="Two" [4]="Four"
        [8]="Eight" [12]="Twelve" [16]="Sixteen"
    )

    # BDN takes a single --filter followed by N space-separated glob patterns.
    PATTERNS=()
    IFS=',' read -ra ARITY_LIST <<< "$ARITIES"
    for A in "${ARITY_LIST[@]}"; do
        if $CANONICAL; then
            PATTERNS+=("*Compare_Arity${A}_AllStrings*")
        else
            WORD="${ARITY_WORD[$A]:-}"
            [[ -z "$WORD" ]] && {
                echo "No word mapping for arity $A (supported: 0 1 2 4 8 12 16)"
                exit 1
            }
            PATTERNS+=("*${WORD}*")
        fi
    done
    FILTER_ARGS=(--filter "${PATTERNS[@]}")

    # Derive a stable run ID from the arity list and canonical flag.
    CANON_SUFFIX=$( $CANONICAL && echo "-canonical" || echo "" )
    RUN_ID="arities-$(echo "$ARITIES" | tr ',' '-')${CANON_SUFFIX}"

    echo "=== Three-way benchmark: arities=$ARITIES canonical=$CANONICAL ==="
    echo "    Filters: ${FILTER_ARGS[*]}"
    echo "    Run ID:  $RUN_ID"
    echo ""
fi

# Each run writes to its own subdirectory so results never clobber each other.
OUT_DIR="$BASE_OUT/$RUN_ID"
mkdir -p "$OUT_DIR"

# --- run one project ---
run_project() {
    local NAME="$1"
    local CSPROJ="$2"
    local SUBDIR="$OUT_DIR/$NAME"
    mkdir -p "$SUBDIR"
    echo "--- Running $NAME ---"
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
AGGREGATE_LABEL="${ARITIES:-scenario-${SCENARIO}}"
python "$SCRIPT_DIR/compare_aggregate.py" \
    --herald       "$OUT_DIR/herald" \
    --serilog      "$OUT_DIR/serilog" \
    --compat       "$OUT_DIR/serilog-compat" \
    --arities      "$AGGREGATE_LABEL" \
    --out          "$OUT_DIR"

echo ""
echo "Results written to: $OUT_DIR"
