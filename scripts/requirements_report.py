"""Builds a per-requirement test report from a TRX file.

Each xUnit test in AIVES.Tests carries [Trait("Requirement", "FR-xx")] on its class and/or method
(several are allowed). TRX files do not keep xUnit traits, so the traits are read from the test
source here and joined to the results by test name. Requirement titles and the number of written
test cases come from docs/Test-Cases-By-Requirement.md.

Usage: python scripts/requirements_report.py <results.trx> [--out report.md] [--only FR-08]
Exit code 1 when a test failed or no results were found.
"""
from __future__ import annotations

import argparse
import os
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TESTS = ROOT / "AIVES.Tests"
CASES_DOC = ROOT / "docs" / "Test-Cases-By-Requirement.md"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

TRAIT_RE = re.compile(r'^\s*\[Trait\("Requirement",\s*"([^"]+)"\)\]')
CLASS_RE = re.compile(r"^(\s*)(?:public |internal )?(?:sealed |static |abstract )*(?:partial )?class (\w+)")
TEST_ATTR_RE = re.compile(r"^\s*\[\w*(?:Fact|Theory)\b")
METHOD_RE = re.compile(r"public (?:async )?[\w<>\[\]?]+ (\w+)\(")
NAMESPACE_RE = re.compile(r"^namespace ([\w.]+)")

EXTRA_TITLES = {
    "NFR-ARCH": "Kiến trúc 3 lớp (phi chức năng)",
    "NFR-DB": "Migration & SQL Server (phi chức năng)",
}


def source_traits() -> dict[str, set[str]]:
    """Fully qualified test method name -> requirements, from class and method traits."""
    result: dict[str, set[str]] = {}
    for path in sorted(TESTS.glob("*.cs")):
        namespace = "AIVES.Tests"
        pending: list[str] = []
        classes: list[tuple[int, str, set[str]]] = []  # (indent, name, traits)
        lines = path.read_text(encoding="utf-8-sig").splitlines()
        for i, line in enumerate(lines):
            if m := NAMESPACE_RE.match(line):
                namespace = m.group(1).rstrip(";")
            if m := TRAIT_RE.match(line):
                pending.append(m.group(1))
                continue
            if m := CLASS_RE.match(line):
                indent = len(m.group(1))
                classes = [c for c in classes if c[0] < indent]
                classes.append((indent, m.group(2), set(pending)))
                pending = []
                continue
            if TEST_ATTR_RE.match(line):
                for nxt in lines[i + 1:i + 12]:
                    if TRAIT_RE.match(nxt):
                        pending.append(TRAIT_RE.match(nxt).group(1))
                    if mm := METHOD_RE.search(nxt):
                        if classes:
                            owner = classes[-1]
                            name = f"{namespace}.{owner[1]}.{mm.group(1)}"
                            result[name] = owner[2] | set(pending)
                        break
                pending = []
                continue
            if line.strip() and not line.strip().startswith(("[", "//", "///")):
                pending = []
    return result


def documented_requirements() -> dict[str, tuple[str, int]]:
    """FR-xx -> (title, number of TC rows) from the test-case document."""
    reqs: dict[str, tuple[str, int]] = {}
    if not CASES_DOC.exists():
        return reqs
    current = None
    for line in CASES_DOC.read_text(encoding="utf-8").splitlines():
        if m := re.match(r"^## (FR-\d+)\s+(.*)", line):
            current = m.group(1)
            reqs[current] = (m.group(2).strip(), 0)
        elif line.startswith("## "):
            current = None
        elif current and re.match(r"^\|\s*TC-\d+-\d+", line):
            title, count = reqs[current]
            reqs[current] = (title, count + 1)
    return reqs


def trx_results(path: Path) -> dict[str, list[tuple[str, str]]]:
    """Method name (theory arguments stripped) -> [(display name, outcome)]."""
    root = ET.parse(path).getroot()
    method_of = {}
    for ut in root.iterfind("t:TestDefinitions/t:UnitTest", NS):
        tm = ut.find("t:TestMethod", NS)
        method_of[ut.get("id")] = f"{tm.get('className')}.{tm.get('name').split('(')[0]}"
    results = defaultdict(list)
    for r in root.iterfind("t:Results/t:UnitTestResult", NS):
        name = method_of.get(r.get("testId"), r.get("testName").split("(")[0])
        results[name].append((r.get("testName"), r.get("outcome")))
    return results


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("trx", nargs="+", type=Path)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--only", help="Report a single requirement, e.g. FR-08")
    args = parser.parse_args()

    traits = source_traits()
    docs = documented_requirements()
    results: dict[str, list[tuple[str, str]]] = defaultdict(list)
    for trx in args.trx:
        for name, runs in trx_results(trx).items():
            results[name].extend(runs)
    if not results:
        print("No test results found.", file=sys.stderr)
        return 1

    per_req: dict[str, dict[str, list[str]]] = defaultdict(lambda: defaultdict(list))
    untagged = []
    for name, runs in sorted(results.items()):
        reqs = traits.get(name)
        if not reqs:
            untagged.append(name)
            reqs = {"(chưa gắn)"}
        for display, outcome in runs:
            bucket = {"Passed": "passed", "Failed": "failed"}.get(outcome, "skipped")
            for req in reqs:
                per_req[req][bucket].append(display)

    order = sorted(set(docs) | set(per_req) - {"(chưa gắn)"},
                   key=lambda r: (r.startswith("NFR"), r))
    if args.only:
        order = [r for r in order if r == args.only]

    total_failed = sum(1 for runs in results.values() for _, o in runs if o == "Failed")
    lines = ["## Kết quả kiểm thử theo yêu cầu", ""]
    lines.append("| Yêu cầu | Ca trong tài liệu | Test tự động | ✅ Đạt | ❌ Lỗi | ⏭️ Bỏ qua | Trạng thái |")
    lines.append("|---|---:|---:|---:|---:|---:|---|")
    for req in order:
        title, cases = docs.get(req, (EXTRA_TITLES.get(req, ""), 0))
        b = per_req.get(req, {})
        p, f, s = len(b.get("passed", [])), len(b.get("failed", [])), len(b.get("skipped", []))
        status = "❌ Có lỗi" if f else ("⚠️ Chưa có test tự động" if p + f + s == 0
                                       else ("⚠️ Chỉ có test bị bỏ qua" if p == 0 else "✅ Đạt"))
        lines.append(f"| **{req}** {title} | {cases or '–'} | {p + f + s} | {p} | {f} | {s} | {status} |")

    failed = [(req, d) for req in order for d in per_req.get(req, {}).get("failed", [])]
    if failed:
        lines += ["", "### Test lỗi", ""] + [f"- **{req}**: `{d}`" for req, d in failed]
    if untagged and not args.only:
        lines += ["", f"### Test chưa gắn yêu cầu ({len(untagged)})", "",
                  "Thêm `[Trait(\"Requirement\", \"FR-xx\")]` vào lớp hoặc phương thức test:", ""]
        lines += [f"- `{n}`" for n in untagged]
    lines += ["", "Bỏ qua = test cần SQL Server, khoá Gemini hoặc model giọng nói mà lần chạy này không có. "
              "Ca kiểm thử thủ công: `docs/Test-Cases-By-Requirement.md`."]

    report = "\n".join(lines) + "\n"
    print(report)
    if args.out:
        args.out.write_text(report, encoding="utf-8")
    if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(summary, "a", encoding="utf-8") as fh:
            fh.write(report)
    for name in untagged if not args.only else []:
        print(f"::warning title=Test chưa gắn yêu cầu::{name}")
    return 1 if total_failed else 0


if __name__ == "__main__":
    sys.exit(main())
