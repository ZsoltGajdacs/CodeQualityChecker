import { readFileSync } from 'node:fs';
import { analyze } from './analysis.mjs';

try {
    const snapshot = JSON.parse(readFileSync(0, 'utf8'));
    process.stdout.write(JSON.stringify(analyze(snapshot)));
} catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
    process.exitCode = 1;
}
