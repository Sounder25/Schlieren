#!/usr/bin/env python3
"""Diagnose EELS fixture structure and count variants."""

import json
import sys
from pathlib import Path
from collections import Counter

def analyze_fixture(path):
    """Analyze a single fixture file and return case counts."""
    try:
        with open(path) as f:
            data = json.load(f)
        
        total_cases = 0
        total_variants = 0
        
        for test_name, test_data in data.items():
            if not isinstance(test_data, dict):
                continue
            
            # Check for 'post' field (published format)
            if 'post' in test_data:
                post = test_data['post']
                for fork, variants in post.items():
                    if isinstance(variants, list):
                        variant_count = len(variants)
                        total_variants += variant_count
                        total_cases += 1  # One "case" can have multiple variants
                        
        return total_cases, total_variants
    except Exception as e:
        print(f"Error reading {path}: {e}", file=sys.stderr)
        return 0, 0

def main():
    fixtures_root = Path("fixtures/state_tests")
    if not fixtures_root.exists():
        print(f"Fixtures root not found: {fixtures_root}")
        return
    
    print(f"Analyzing fixtures in: {fixtures_root.absolute()}\n")
    
    total_files = 0
    total_cases = 0
    total_variants = 0
    fork_counts = Counter()
    
    for json_file in fixtures_root.rglob("*.json"):
        cases, variants = analyze_fixture(json_file)
        if cases > 0 or variants > 0:
            fork = json_file.parent.name
            fork_counts[fork] += variants
            total_files += 1
            total_cases += cases
            total_variants += variants
            
            if variants > 10:  # Only show files with many variants
                print(f"{json_file.relative_to(fixtures_root)}: {cases} cases, {variants} variants")
    
    print(f"\n{'='*60}")
    print(f"Total fixture files: {total_files}")
    print(f"Total test cases: {total_cases}")
    print(f"Total variants (post-states): {total_variants}")
    print(f"\nBy fork:")
    for fork, count in sorted(fork_counts.items()):
        print(f"  {fork}: {count} variants")
    
    print(f"\nNote: Scrutor's harness loads variants as 'cases'.")
    print(f"Expected loaded case count (Cancun only): ~{fork_counts.get('cancun', 0)}")

if __name__ == "__main__":
    main()
