#!/usr/bin/env python3
"""CRAP metric for C# methods, read from a Coverlet Cobertura report.

    CRAP(m) = complexity(m)^2 * (1 - coverage(m))^3 + complexity(m)

Complexity and per-method line coverage both live in the Cobertura XML, so one test run
with `--collect:"XPlat Code Coverage"` is enough. Exits 1 when a method is above the
threshold, so a quality gate can fail on it.
"""

import argparse
import sys
import xml.etree.ElementTree as ElementTree


def methods(report):
    for cls in report.iter("class"):
        filename = cls.get("filename") or ""
        if is_generated(filename):
            continue
        for method in cls.findall("./methods/method"):
            lines = method.findall("./lines/line")
            covered = sum(1 for line in lines if int(line.get("hits", "0")) > 0)
            coverage = 1.0 if not lines else covered / len(lines)
            complexity = int(method.get("complexity") or 1)
            yield filename, cls.get("name"), method.get("name"), complexity, coverage


def is_generated(filename):
    """Regex sources and other generated code are not ours to make simple."""
    return "/obj/" in filename or filename.endswith(".g.cs")


def crap(complexity, coverage):
    return complexity**2 * (1 - coverage) ** 3 + complexity


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("coverage", help="coverage.cobertura.xml from coverlet")
    parser.add_argument("--threshold", type=float, default=30.0)
    parser.add_argument("--top", type=int, default=15)
    arguments = parser.parse_args()

    report = ElementTree.parse(arguments.coverage).getroot()

    scored = sorted(
        (
            (crap(complexity, coverage), complexity, coverage, filename, name, method)
            for filename, name, method, complexity, coverage in methods(report)
        ),
        reverse=True,
    )

    print(f"{'CRAP':>6}  {'cplx':>4}  {'cover':>6}  method")
    for score, complexity, coverage, filename, name, method in scored[: arguments.top]:
        print(f"{score:6.1f}  {complexity:4d}  {coverage * 100:5.1f}%  {name}.{method}  ({filename})")

    over = [entry for entry in scored if entry[0] > arguments.threshold]
    print()
    print(f"{len(scored)} methods, {len(over)} above CRAP {arguments.threshold:g}")

    return 1 if over else 0


if __name__ == "__main__":
    sys.exit(main())
