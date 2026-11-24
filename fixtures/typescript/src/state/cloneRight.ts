export function cloneRight(numbers: number[]) {
    const duplicates = numbers.slice();
    duplicates.sort((first, second) => first - second);
    const selected = duplicates.slice(2);
    const total = selected.reduce((running, current) => running + current, 0);
    return total * 3;
}
