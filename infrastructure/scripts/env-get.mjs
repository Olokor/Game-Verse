#!/usr/bin/env node
// Prints one value from the repo-root .env (empty if the file or key is missing). Used by the Makefile, so make never
// has to parse .env itself (values may contain characters make treats specially). Usage: node env-get.mjs KEY
import { existsSync, readFileSync } from 'node:fs';

const key = process.argv[2];
const line = existsSync('.env') ? readFileSync('.env', 'utf8').split(/\r?\n/).find((l) => l.startsWith(`${key}=`)) : undefined;
process.stdout.write(line ? line.slice(key.length + 1).trim() : '');
