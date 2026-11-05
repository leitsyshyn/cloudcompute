#!/usr/bin/env python3
"""Deterministic benchmark inputs, ported unchanged from the RPP lab generator
(rpp/scripts/benchmark_common.py). For the same profile and size it produces byte-identical
files, so local, cloud and RPP runs use the same data. Stdlib only, so it can run in a plain
python container inside the cluster.

  python3 scripts/datagen.py --out-dir DIR natural:100 natural:500 highcard:10 --edge-cases
Files are named <profile>_<size>mb.txt; the seed is derived as in RPP's "suite" mode.
"""
from __future__ import annotations

import argparse
import random
import sys
import time
from pathlib import Path
from typing import Any

MB = 1024 * 1024
DEFAULT_SEED = 12345
SUPPORTED_PROFILES = ("natural", "lowcard", "highcard", "boundary")

_NATURAL_WORDS = (
    "the", "quick", "brown", "fox", "version", "jumps", "over", "lazy", "dog", "generated",
    "scale", "benchmark", "cluster", "memory", "process", "thread", "input", "output", "kernel",
    "apple", "banana", "orange", "signal", "token", "parser", "report", "timing", "phase", "count",
    "merge", "collect", "reduce", "gather", "runtime", "throughput", "latency", "cache", "local",
    "distributed", "worker", "result", "deterministic", "validate", "correctness", "serial",
    "parallel",
)
_LOWCARD_WORDS = ("apple", "banana", "orange", "grape", "pear", "mpi", "openmp", "sequential")
_DELIMITERS = (" ", "  ", ", ", ". ", "! ", "? ", "\n", "\n\n", "\t")

EDGE_CASES = {
    "edge_empty.txt": b"",
    "edge_delims.txt": b" ,.;\n\t!!  ",
    "edge_tiny.txt": b"Hello, WORLD! hello wor\nld 42 x",
    "edge_oneword.txt": b"abc",
}


def derive_suite_seed(base_seed: int, profile: str, size_mb: int) -> int:
    return base_seed + SUPPORTED_PROFILES.index(profile) * 100000 + size_mb


def apply_case_variant(word: str, rng: random.Random) -> str:
    variant = rng.randrange(4)
    if variant == 0:
        return word
    if variant == 1:
        return word.upper()
    if variant == 2:
        return word.title()
    return "".join(character.upper() if index % 2 == 0 else character for index, character in enumerate(word))


def make_boundary_word(token_index: int, length: int) -> str:
    pattern = f"boundary{token_index:08d}segment"
    return (pattern * ((length // len(pattern)) + 1))[:length]


def next_generated_chunk(profile: str, rng: random.Random, state: dict[str, Any]) -> str:
    if profile == "natural":
        fragments: list[str] = []
        for _ in range(rng.randint(2, 5)):
            word_count = rng.randint(8, 18)
            parts: list[str] = []
            for word_index in range(word_count):
                if rng.random() < 0.14:
                    token = str(2000 + rng.randrange(0, 50))
                else:
                    token = apply_case_variant(rng.choice(_NATURAL_WORDS), rng)
                parts.append(token)
                if word_index != word_count - 1:
                    parts.append(rng.choice((" ", " ", " ", ", ", "; ", " - ")))
            parts.append(rng.choice((".\n", "!\n", "?\n", ", version 2026.\n", "\n")))
            fragments.append("".join(parts))
        return "".join(fragments)

    if profile == "lowcard":
        fragments = []
        for _ in range(rng.randint(64, 128)):
            fragments.append(apply_case_variant(rng.choice(_LOWCARD_WORDS), rng))
            fragments.append(rng.choice(_DELIMITERS))
        return "".join(fragments)

    if profile == "highcard":
        fragments = []
        recent: list[str] = state.setdefault("recent_tokens", [])
        next_index = state.setdefault("next_index", 1)
        for _ in range(rng.randint(48, 96)):
            if recent and rng.random() < 0.12:
                token = rng.choice(recent)
            else:
                token = f"word{next_index:08d}"
                next_index += 1
                recent.append(token)
                if len(recent) > 256:
                    del recent[0]
            fragments.append(token)
            fragments.append(rng.choice((" ", "\n", " ", ", ", "; ")))
        state["next_index"] = next_index
        return "".join(fragments)

    if profile == "boundary":
        fragments = []
        next_index = state.setdefault("next_index", 1)
        for _ in range(rng.randint(2, 5)):
            word = make_boundary_word(next_index, rng.choice((257, 511, 1023, 2047, 4095, 8191)))
            next_index += 1
            if rng.random() < 0.25:
                word = word.upper()
            fragments.append(word)
            fragments.append(rng.choice((" ", "\n", "\t", " | ")))
        state["next_index"] = next_index
        return "".join(fragments)

    raise ValueError(f"unsupported profile: {profile}")


def generate(profile: str, size_mb: int, output_path: Path, base_seed: int = DEFAULT_SEED) -> int:
    rng = random.Random(derive_suite_seed(base_seed, profile, size_mb))
    state: dict[str, Any] = {}
    requested, written = size_mb * MB, 0
    tmp = output_path.with_suffix(".tmp")
    with tmp.open("w", encoding="ascii", newline="") as output:
        while written < requested:
            chunk = next_generated_chunk(profile, rng, state)
            output.write(chunk)
            written += len(chunk)
    tmp.replace(output_path)
    return output_path.stat().st_size


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("specs", nargs="*", help="profile:size_mb, e.g. natural:100")
    parser.add_argument("--out-dir", type=Path, required=True)
    parser.add_argument("--seed", type=int, default=DEFAULT_SEED)
    parser.add_argument("--edge-cases", action="store_true", help="also write the edge_*.txt files")
    parser.add_argument("--skip-existing", action="store_true")
    args = parser.parse_args()

    args.out_dir.mkdir(parents=True, exist_ok=True)
    if args.edge_cases:
        for name, data in EDGE_CASES.items():
            (args.out_dir / name).write_bytes(data)
    for spec in args.specs:
        profile, _, size = spec.partition(":")
        if profile not in SUPPORTED_PROFILES or not size.isdigit():
            sys.exit(f"bad spec {spec!r}; expected one of {SUPPORTED_PROFILES} followed by :size_mb")
        path = args.out_dir / f"{profile}_{size}mb.txt"
        if args.skip_existing and path.exists():
            print(f"{path.name}: exists, skipped", flush=True)
            continue
        started = time.perf_counter()
        size_bytes = generate(profile, int(size), path, args.seed)
        print(f"{path.name}: {size_bytes} bytes in {time.perf_counter() - started:.1f}s", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
