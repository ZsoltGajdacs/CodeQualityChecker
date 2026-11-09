#!/usr/bin/env python3
"""Compare stable-input performance runs; output parity is a hard gate, timing is evidence."""
import json
import statistics
import sys


def median(row, field):
    return statistics.median(sample[field] for sample in row['samples'])


def compare(before, after):
    if before.keys() != after.keys():
        raise ValueError('Benchmark stage sets differ')
    print('| Stage | Before ms | After ms | Time change | Allocation change | Git before/after |')
    print('| --- | ---: | ---: | ---: | ---: | ---: |')
    operations = []
    for name, old in before.items():
        new = after[name]
        if 'hash' not in old:
            operations.append(f"{name}: exact comparisons {old['duplicateComparisons']:,} → {new['duplicateComparisons']:,}")
            continue
        if old['hash'] != new['hash']:
            raise ValueError('Output changed: ' + name)
        elapsed = 100 * (median(new, 'milliseconds') / median(old, 'milliseconds') - 1)
        allocated = 100 * (median(new, 'allocatedBytes') / median(old, 'allocatedBytes') - 1)
        print(f"| {name} | {median(old, 'milliseconds'):.2f} | {median(new, 'milliseconds'):.2f} | {elapsed:+.1f}% | {allocated:+.1f}% | {median(old, 'gitProcesses'):g}/{median(new, 'gitProcesses'):g} |")

    print('\n' + '\n'.join(operations))


if __name__ == '__main__':
    if len(sys.argv) != 3:
        raise SystemExit('Usage: compare.py before.json after.json')
    with open(sys.argv[1]) as source:
        baseline = {row['name']: row for row in json.load(source)}
    with open(sys.argv[2]) as source:
        optimized = {row['name']: row for row in json.load(source)}
    compare(baseline, optimized)
