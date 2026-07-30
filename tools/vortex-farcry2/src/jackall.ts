import { spawn } from 'child_process';
import * as path from 'path';

// Every piece of Far Cry 2 mod semantics - hashing a path to an archive entry, merging two mods' edits
// to the same .fcb, writing patch.dat - lives in JackAll and is reached from here. Its `mod` commands
// all take --json: one object on stdout, progress lines on stderr. The interfaces below cover the
// fields this extension reads, not the CLI's whole output.

interface Envelope {
  ok: boolean;
  error?: string;
}

export interface StatusResult extends Envelope {
  valid: boolean;
  hasVanillaBackup?: boolean;
  /** patch.dat looks modded and there is no backup to build from - a build has to refuse. */
  needsVanillaConfirmation?: boolean;
}

export interface ImportLegacyResult extends Envelope {
  imported: number;
  fragmentsImported: number;
}

export interface BuildResult extends Envelope {
  overriddenEntries: number;
  addedEntries: number;
  /** Fragments two mods both edited. A headless build has nobody to ask, so load order wins. */
  conflicts: Array<{
    fragmentId: string;
    winningLayer: string;
    earlierLayers: string[];
  }>;
}

export class JackAllError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'JackAllError';
  }
}

/** JACKALL_MI points at a local `dotnet build` output instead of the bundled exe. */
function cliPath(): string {
  return process.env.JACKALL_MI ?? path.join(__dirname, 'bin', 'jackall-mi.exe');
}

export interface RunOptions {
  /** Called for each stderr line - the CLI's progress channel. */
  onProgress?: (line: string) => void;
}

async function run<T extends Envelope>(args: string[], options: RunOptions = {}): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const child = spawn(cliPath(), [...args, '--json'], { windowsHide: true });

    let out = '';
    let tail = '';
    let last = '';

    child.stdout.setEncoding('utf8');
    child.stdout.on('data', (chunk: string) => { out += chunk; });

    child.stderr.setEncoding('utf8');
    child.stderr.on('data', (chunk: string) => {
      // Progress arrives as whole lines; hold back the partial one at the end until it completes.
      tail += chunk;
      const lines = tail.split(/\r?\n/);
      tail = lines.pop() ?? '';
      for (const line of lines) {
        if (line.length > 0) {
          last = line;
          options.onProgress?.(line);
        }
      }
    });

    child.on('error', err => reject(
      new JackAllError(`Couldn't run jackall-mi (${cliPath()}): ${err.message}`)));

    child.on('close', () => {
      let parsed: T;
      try {
        parsed = JSON.parse(out.trim()) as T;
      } catch {
        // No document means the CLI died before writing one - a crash, a missing dependency, a
        // truncated pipe. Whatever it last managed to say is the only evidence there is.
        reject(new JackAllError(
          `jackall-mi produced no result. ${tail.trim() || last || 'No output.'}`));
        return;
      }
      if (!parsed.ok) {
        reject(new JackAllError(parsed.error ?? 'jackall-mi reported an unspecified failure.'));
        return;
      }
      resolve(parsed);
    });
  });
}

export function status(gamePath: string): Promise<StatusResult> {
  return run<StatusResult>(['mod', 'status', '--game', gamePath]);
}

export function importLegacy(
  gamePath: string, from: string, outDir: string, name: string, options?: RunOptions,
): Promise<ImportLegacyResult> {
  return run<ImportLegacyResult>(
    ['mod', 'import-legacy', '--game', gamePath, '--from', from, '--out', outDir, '--name', name],
    options);
}

/**
 * Rebuilds patch.dat/patch.fat from the vanilla backup plus layerDirs, in the order given - later
 * layers win. Passing no layers is meaningful: it restores the patch to stock.
 */
export function build(
  gamePath: string, layerDirs: string[], options?: RunOptions & { force?: boolean },
): Promise<BuildResult> {
  const args = ['mod', 'build', '--game', gamePath];
  layerDirs.forEach(dir => args.push('--layer', dir));
  if (options?.force) {
    args.push('--force');
  }
  return run<BuildResult>(args, options);
}

export function restore(gamePath: string, options?: RunOptions): Promise<Envelope> {
  return run<Envelope>(['mod', 'restore', '--game', gamePath], options);
}
