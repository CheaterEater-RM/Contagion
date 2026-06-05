#!/usr/bin/env python3
"""Run the Contagion multi-pawn spread simulator.

Companion to audit_infection_risk.py. Where the audit checks single-pair chances pointwise, this
steps a GROUP of virtual pawns over time so we can tune how fast a disease saturates a caravan or
room and whether it burns out. It reuses the production risk math (ContagionRiskMath) and the live
XML profiles.

Exploratory tool — it prints a metrics table plus non-fatal "ALERT" lines when a metric falls
outside its suggested band. It is NOT part of the every-change audit gate. Always exits 0 unless the
build itself fails.

Examples:
    python tools/simulate_spread.py
    python tools/simulate_spread.py --scenario caravan --disease plague --suppression off --outdoor
    python tools/simulate_spread.py --scenario barracks --disease flu --suppression medium --ppe on --trials 500
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SIM_PROJECT = ROOT / "tools" / "ContagionSpreadSim" / "ContagionSpreadSim.csproj"


def main() -> int:
    completed = subprocess.run(
        ["dotnet", "run", "--project", str(SIM_PROJECT), "--", *sys.argv[1:]],
        cwd=ROOT,
        check=False,
    )
    return completed.returncode


if __name__ == "__main__":
    raise SystemExit(main())
