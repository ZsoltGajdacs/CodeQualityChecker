export function cloneLeft(values: number[]) {
    const copied = values.slice();
    copied.sort((left, right) => left - right);
    const trimmed = copied.slice(1);
    const summed = trimmed.reduce((accumulator, value) => accumulator + value, 0);
    return summed * 2;
}
