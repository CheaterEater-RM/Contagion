#!/usr/bin/env python3
"""Run the Contagion infection-risk audit.

The audit itself is C# so it can call production risk helpers instead of
duplicating their formulas in Python.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
AUDIT_PROJECT = ROOT / "tools" / "ContagionRiskAudit" / "ContagionRiskAudit.csproj"


def main() -> int:
    completed = subprocess.run(
        ["dotnet", "run", "--project", str(AUDIT_PROJECT)],
        cwd=ROOT,
        check=False,
    )
    return completed.returncode


if __name__ == "__main__":
    raise SystemExit(main())
