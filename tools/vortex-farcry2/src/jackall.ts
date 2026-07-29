import { spawn } from 'child_process';
import * as path from 'path';

/**
 * The only place in this extension that knows how to reach JackAll.
 *
 * Every piece of Far Cry 2 mod semantics — hashing a relative path to an archive entry, merging two
 * mods' edits to the same `.fcb`, deciding what a legacy patch archive actually changed, writing
 * patch.dat — lives in JackAll and is reached through `jackall-cli`. Nothing here reimplements any
 * of it, so the extension can't drift from the tool it's a front-end for.
 *
 * The CLI's `mod` commands all take `--json`: exactly one object on stdout, progress on stderr.
 * That's why this can be a thin, honest wrapper instead of output scraping.
 */

/** Every `--json` response carries this discriminator; failures carry a message with it. */
interface JackAllEnvelope {
  ok: boolean;
  error?: string;
}

export interface StatusResult extends JackAllEnvelope {
  gamePath: string;
  valid: boolean;
  dataDir?: string;
  patchFat?: string;
  patchDat?: string;
  hasVanillaBackup?: boolean;
  looksModded?: boolean;
  patchEntries?: number;
  /** The one state a build must refuse: a modded-looking patch with no backup to fall back to. */
  needsVanillaConfirmation?: boolean;
}

export interface ImportLegacyResult extends JackAllEnvelope {
  outDir: string;
  name: string;
  totalEntries: number;
  imported: number;
  fragmentsImported: number;
  skipped: number;
  stagedFiles: number;
}

export interface BuildResult extends JackAllEnvelope {
  patchFat: string;
  patchDat: string;
  totalEntries: number;
  vanillaEntries: number;
  overriddenEntries: number;
  addedEntries: number;
  outputBytes: number;
  layers: Array<{
    index: number;
    path: string;
    name: string;
    wholeFileOverrides: number;
    fragmentOverrides: number;
  }>;
  /**
   * Fragments where two mods genuinely collided - the CLI has nobody to ask, so it built anyway,
   * letting the higher-priority layer win outright (same rule a whole-file override already
   * follows) instead of refusing the whole build. Empty on a build with no such collisions.
   */
  conflicts: Array<{
    fragmentId: string;
    isNewEntry: boolean;
    winningLayer: string;
    earlierLayers: string[];
  }>;
}

/** Raised for anything the CLI itself reported — the message is already user-facing. */
export class JackAllError extends Error {
  constructor(message: string, public readonly stderr: string) {
    super(message);
    this.name = 'JackAllError';
  }
}

/**
 * The bundled CLI. `JACKALL_CLI` overrides it, which is how you point a Vortex install at a local
 * `dotnet build` output instead of reassembling the extension after every change.
 */
export function cliPath(): string {
  return process.env.JACKALL_CLI ?? path.join(__dirname, 'bin', 'jackall-cli.exe');
}

export interface RunOptions {
  /** Called for each stderr line — the CLI's progress channel. */
  onProgress?: (line: string) => void;
}

async function run<T extends JackAllEnvelope>(args: string[], options: RunOptions = {}): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const child = spawn(cliPath(), [...args, '--json'], { windowsHide: true });

    let stdout = '';
    let stderr = '';
    let stderrTail = '';

    child.stdout.setEncoding('utf8');
    child.stdout.on('data', (chunk: string) => { stdout += chunk; });

    child.stderr.setEncoding('utf8');
    child.stderr.on('data', (chunk: string) => {
      stderr += chunk;
      // Progress arrives as whole lines; hold back the partial one at the end until it completes.
      stderrTail += chunk;
      const lines = stderrTail.split(/\r?\n/);
      stderrTail = lines.pop() ?? '';
      lines.filter(line => line.length > 0).forEach(line => options.onProgress?.(line));
    });

    child.on('error', err => reject(new JackAllError(
      `Couldn't run jackall-cli (${cliPath()}): ${err.message}`, stderr)));

    child.on('close', () => {
      let parsed: T;
      try {
        parsed = JSON.parse(stdout.trim()) as T;
      } catch {
        // The only way here is the CLI dying before it wrote its document - a crash, a missing
        // dependency, a truncated pipe. stderr is the only evidence, so it has to be surfaced.
        reject(new JackAllError(
          `jackall-cli produced no result. ${lastLine(stderr) || 'No output.'}`, stderr));
        return;
      }

      if (!parsed.ok) {
        reject(new JackAllError(parsed.error ?? 'jackall-cli reported an unspecified failure.', stderr));
        return;
      }
      resolve(parsed);
    });
  });
}

function lastLine(text: string): string {
  const lines = text.trim().split(/\r?\n/).filter(line => line.length > 0);
  return lines[lines.length - 1] ?? '';
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
 * Recompiles patch.dat/patch.fat from the vanilla backup plus `layerDirs`, **in the order given** —
 * later layers win, exactly as in JackAll's own mod list. Passing no layers is meaningful and
 * supported: it restores the patch to stock.
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

export function restore(gamePath: string, options?: RunOptions): Promise<JackAllEnvelope> {
  return run<JackAllEnvelope>(['mod', 'restore', '--game', gamePath], options);
}
