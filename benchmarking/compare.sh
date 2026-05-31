#!/usr/bin/env bash
# benchmarking/compare.sh — three-way Herald/Compat/Serilog arity comparison
#
# Usage:
#   bash benchmarking/compare.sh --arities 2,4
#   bash benchmarking/compare.sh --arities 2,4 --canonical
#
# Net10 only (per the CLAUDE.md benchmark discipline).
# Canonical rows share the same method name across all three projects so
# results aggregate cleanly. Exploratory rows use project-local names.
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
while [[ $# -gt 0 ]]; do
    case "$1" in
        --arities)   ARITIES="$2";  shift 2 ;;
        --canonical) CANONICAL=true; shift   ;;
        *) echo "Unknown arg: $1"; exit 1 ;;
    esac
done
[[ -z "$ARITIES" ]] && { echo "Usage: $0 --arities 2,4 [--canonical]"; exit 1; }

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
FILTER_ARGS=()
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
    "$REPO_ROOT/benchmarking/comparisons/net10/herald/Herald.Comparison.csproj"

run_project "serilog" \
    "$REPO_ROOT/benchmarking/comparisons/net10/serilog/Serilog.Comparison.csproj"

run_project "serilog-compat" \
    "$REPO_ROOT/benchmarking/comparisons/net10/serilog-compat/SerilogCompat.Comparison.csproj"

echo ""
echo "=== Aggregating results ==="
python "$SCRIPT_DIR/compare_aggregate.py" \
    --herald       "$OUT_DIR/herald" \
    --serilog      "$OUT_DIR/serilog" \
    --compat       "$OUT_DIR/serilog-compat" \
    --arities      "$ARITIES" \
    --out          "$OUT_DIR"
