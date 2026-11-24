import { createHash } from 'node:crypto';
import ts from 'typescript';

function digest(text) {
    return text === undefined ? undefined : createHash('sha256').update(text).digest('hex');
}

export class ResolutionHost {
    #observations = new Map();

    constructor(root) {
        this.root = this.realpath(root);
        this.getCurrentDirectory = () => this.root;
    }

    useCaseSensitiveFileNames = ts.sys.useCaseSensitiveFileNames;
    fileExists = file => this.#observe('file', [file], ts.sys.fileExists);
    directoryExists = directory => this.#observe('dir', [directory], ts.sys.directoryExists);
    realpath = file => this.#observe('realpath', [file], ts.sys.realpath);
    readFile = file => this.#observe('read', [file], ts.sys.readFile, digest);
    getDirectories = directory => this.#observe('directories', [directory], ts.sys.getDirectories, sortedEntries);
    readDirectory = (...args) => this.#observe('list', args, ts.sys.readDirectory, sortedEntries);

    #observe(operation, args, read, summarize = value => value) {
        const key = JSON.stringify([operation, ...args]);
        const previous = this.#observations.get(key);
        if (previous) return previous.value;
        const value = read(...args);
        const observed = summarize(value);
        this.#observations.set(key, { value, check: () => observed === summarize(read(...args)) });
        return value;
    }

    isStable() {
        return Array.from(this.#observations.values()).every(observation => observation.check());
    }
}

function sortedEntries(entries) {
    return JSON.stringify([...entries].sort());
}
