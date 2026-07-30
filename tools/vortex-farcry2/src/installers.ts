import Bluebird from 'bluebird';
import * as nodeFs from 'fs';
import * as path from 'path';
import { fs, log, types, util } from 'vortex-api';

import { GAME_ID, MODTYPE_FCSE_LOADER, MODTYPE_FCSE_PLUGIN } from './constants';
import { gamePath } from './game';
import * as jackall from './jackall';
import { ask, dismiss, notify } from './ui';

const FCSE_LOADER = 'fcse.exe';
const PLUGINS_DIR = 'plugins';
const DATA_WIN32_DIR = 'data_win32';

/** The game's own binaries. No content mod, FCSE loader or plugin has any reason to ship these. */
const SUSPICIOUS_BINARIES = ['farcry2.exe', 'dunia.dll', 'fc2.dll'];

/** Claims every non-empty archive: only makeInstaller can tell what one actually is. */
export function testSupported(files: string[], gameId: string): Promise<types.ISupportedResult> {
  return Promise.resolve({
    supported: gameId === GAME_ID && files.length > 0,
    requiredFiles: [],
  });
}

/**
 * Decides what a downloaded archive is. Three buckets, checked in this order, and an archive is
 * exactly one of them:
 *
 *   1. Legacy mod - a patch.dat/patch.fat pair anywhere. These predate the extension, so no structure
 *      can be forced on them; jackall converts the pair and ignores the rest of the archive.
 *   2. FCSE plugin - a .dll under a plugins\ folder. Extra files alongside it are normal.
 *   3. Asset mod - rooted under a literal Data_Win32\ folder. Strict on purpose: a mod either uses
 *      the convention or it doesn't, with no guessing in between.
 *
 * Only the legacy bucket touches jackall-cli, because converting one means diffing against the game's
 * own archives. The other two are pure string work over the file list.
 */
export function makeInstaller(api: types.IExtensionApi) {
  return async (
    files: string[], destinationPath: string, gameId: string,
  ): Promise<types.IInstallResult> => {
    if (gameId !== GAME_ID) {
      throw new Error(`Far Cry 2 installer called for ${gameId}.`);
    }

    const plainFiles = files.filter(file => !file.endsWith(path.sep));
    const modName = path.basename(destinationPath).replace(/\.installing$/, '');

    // Before any classification: a rogue FarCry2.exe is a concern whichever bucket this lands in.
    await warnIfBundlingGameBinaries(api, plainFiles, modName);

    const legacyPair = findLegacyPatchPair(plainFiles);
    if (legacyPair !== undefined) {
      await warnAboutIgnoredExtras(api, plainFiles, legacyPair, modName);

      const gameRoot = gamePath(api);
      if (gameRoot === undefined) {
        throw new util.SetupError(
          'Vortex hasn\'t found your Far Cry 2 installation yet. A legacy patch.dat/patch.fat mod '
          + 'can only be converted once the game is discovered, because the conversion works by '
          + 'diffing against the game\'s own archives.');
      }
      return installLegacyPatch(
        api, gameRoot, resolveExtractionRoot(destinationPath, plainFiles), modName);
    }

    const loader = plainFiles.find(file => path.basename(file).toLowerCase() === FCSE_LOADER);
    if (loader !== undefined) {
      const root = path.dirname(loader);
      return withModType(rebase(plainFiles, root === '.' ? '' : root), MODTYPE_FCSE_LOADER);
    }

    const pluginRoot = findPluginsRoot(plainFiles);
    if (pluginRoot !== undefined) {
      return withModType(rebase(plainFiles, pluginRoot), MODTYPE_FCSE_PLUGIN);
    }

    const dataRoot = findDataWin32Root(plainFiles);
    if (dataRoot !== undefined) {
      log('info', 'Far Cry 2: staging mod layer', { root: dataRoot });
      return { instructions: rebase(plainFiles, dataRoot) };
    }

    throw new util.DataInvalid(
      'This doesn\'t look like a Far Cry 2 mod. It has to be exactly one of: a replacement '
      + 'patch.dat/patch.fat pair (anywhere in the archive), an FCSE plugin (a .dll under a '
      + '"plugins" folder), or a set of game files rooted under a "Data_Win32" folder.');
  };
}

/**
 * Mid-install the staging path carries a `.installing` suffix while the extracted files sit beside
 * it, and which of the two Vortex passes has moved between versions - so probe instead of assuming.
 */
function resolveExtractionRoot(destinationPath: string, files: string[]): string {
  const probe = files[0];
  const candidates = [destinationPath, destinationPath.replace(/\.installing$/, '')];
  const found = candidates.find(dir =>
    probe !== undefined && nodeFs.existsSync(path.join(dir, probe)));

  if (found === undefined) {
    log('error', 'Far Cry 2: could not locate the extracted archive', { destinationPath, probe });
    throw new util.SetupError(
      'Vortex extracted this mod somewhere the Far Cry 2 extension could not find '
      + `(looked beside "${destinationPath}"). This usually means the extension needs updating for `
      + 'your version of Vortex.');
  }
  return found;
}

/** Copy instructions with `root` stripped off the front of every path. */
function rebase(files: string[], root: string): types.IInstruction[] {
  const prefix = root.length === 0 ? '' : normalize(root) + path.sep;
  return files
    // Roots come back lowercased, and Windows compares paths case-insensitively regardless.
    .filter(file => prefix === '' || normalize(file).startsWith(prefix))
    .map(file => ({
      type: 'copy',
      source: file,
      destination: prefix === '' ? file : file.substring(prefix.length),
    }));
}

function withModType(instructions: types.IInstruction[], modType: string): types.IInstallResult {
  return { instructions: [...instructions, { type: 'setmodtype', value: modType }] };
}

function normalize(input: string): string {
  return input.replace(/[\\/]/g, path.sep).toLowerCase();
}

/**
 * The old way native mods worked: patching FarCry2.exe/Dunia.dll directly. FCSE replaced that with
 * plugin DLLs, so this extension has no build story for one that still does it.
 */
async function warnIfBundlingGameBinaries(
  api: types.IExtensionApi, files: string[], modName: string,
): Promise<void> {
  const matches = files.filter(f => SUSPICIOUS_BINARIES.includes(path.basename(f).toLowerCase()));
  if (matches.length === 0) {
    return;
  }

  await confirm(api, 'This looks like a legacy binary-patch mod',
    `"${modName}" contains ${matches.join(', ')} - it replaces Far Cry 2's own executable or `
    + 'the Dunia engine DLL directly.\n\n'
    + 'That style of mod isn\'t supported anymore. Native-code mods now go through FCSE (Far Cry '
    + 'Script Extender), which loads plugin DLLs from a `plugins\\` folder instead of patching the '
    + 'game\'s own binaries - so if this is meant to add functionality to the game, it should be '
    + 'repackaged as an FCSE plugin rather than shipping a replacement exe/DLL.\n\n'
    + 'Installing it as-is will not do anything useful through this extension, and overwriting your '
    + 'own FarCry2.exe/Dunia.dll with an unknown copy is risky on its own. Only continue if you '
    + 'understand exactly what this file changes and trust where it came from.');
}

/** Real downloads bundle readmes and screenshots alongside the pair, and only the pair converts. */
async function warnAboutIgnoredExtras(
  api: types.IExtensionApi, files: string[], pair: { fat: string; dat: string }, modName: string,
): Promise<void> {
  const extras = files.filter(file => file !== pair.fat && file !== pair.dat);
  if (extras.length === 0) {
    return;
  }

  await confirm(api, 'This archive contains more than patch.dat/patch.fat',
    `"${modName}" is a legacy patch.dat/patch.fat mod, but the archive also contains `
    + `${extras.length} other file(s) (readmes, screenshots, alternate versions, and the like).\n\n`
    + 'Only the patch.dat/patch.fat pair itself gets converted - everything else will be silently '
    + 'left out of the install.\n\nContinue installing just the patch.dat/patch.fat conversion?');
}

async function confirm(api: types.IExtensionApi, title: string, text: string): Promise<void> {
  const result = await ask(api, 'question', title, { text }, [
    { label: 'Cancel install' },
    { label: 'Install anyway' },
  ]);

  if (result.action !== 'Install anyway') {
    throw new util.UserCanceled();
  }
}

/** A patch.fat with a patch.dat right beside it, anywhere - the whole of what makes a legacy mod. */
function findLegacyPatchPair(files: string[]): { fat: string; dat: string } | undefined {
  for (const file of files) {
    if (path.basename(file).toLowerCase() !== 'patch.fat') {
      continue;
    }
    const dir = normalize(path.dirname(file));
    const dat = files.find(f =>
      normalize(path.dirname(f)) === dir && path.basename(f).toLowerCase() === 'patch.dat');
    if (dat !== undefined) {
      return { fat: file, dat };
    }
  }
  return undefined;
}

/** The prefix up to and including a literal Data_Win32\ folder, wrapper folder allowed above it. */
function findDataWin32Root(files: string[]): string | undefined {
  for (const file of files) {
    const segments = normalize(file).split(path.sep);
    const index = segments.indexOf(DATA_WIN32_DIR);
    if (index >= 0) {
      return segments.slice(0, index + 1).join(path.sep);
    }
  }
  return undefined;
}

/** The folder a plugins\ directory lives in, so plugin DLLs land as bin\plugins\x.dll. */
function findPluginsRoot(files: string[]): string | undefined {
  const dll = files.find(file => {
    const segments = normalize(file).split(path.sep);
    return segments.length >= 2
      && segments[segments.length - 2] === PLUGINS_DIR
      && path.extname(file).toLowerCase() === '.dll';
  });
  return dll === undefined ? undefined : path.dirname(dll);
}

/**
 * Converts a legacy patch mod at install time, so everything downstream sees an ordinary layer. The
 * result is `generatefile`, not `copy`: these files aren't in the archive, they're the difference
 * between its patch.dat and the game's own - a handful of entries out of ~200,000.
 */
async function installLegacyPatch(
  api: types.IExtensionApi, gameRoot: string, sourceRoot: string, modName: string,
): Promise<types.IInstallResult> {
  const id = `farcry2-import-${modName}`;
  const title = 'Converting a Far Cry 2 patch mod';
  notify(api, { id, type: 'activity', title, message: 'Reading the game\'s archives…' });

  const outDir = path.join(util.getVortexPath('temp'), `jackall-legacy-${Date.now().toString(36)}`);
  try {
    const result = await jackall.importLegacy(gameRoot, sourceRoot, outDir, modName, {
      onProgress: message => {
        log('info', `Far Cry 2 (jackall-cli): ${message}`);
        notify(api, { id, type: 'activity', title, message });
      },
    });

    const staged = await listFiles(outDir);
    const instructions: types.IInstruction[] = await Promise.all(staged.map(async rel => ({
      type: 'generatefile' as const,
      data: await fs.readFileAsync(path.join(outDir, rel)),
      destination: rel,
    })));

    notify(api, {
      type: 'success',
      title: 'Far Cry 2 patch mod converted',
      message: `${result.imported} file(s) and ${result.fragmentsImported} .fcb fragment(s) differ `
        + 'from the base game and were kept; the rest was vanilla data.',
      displayMS: 10000,
    });

    return { instructions };
  } finally {
    dismiss(api, id);
    await fs.removeAsync(outDir).catch(() => undefined);
  }
}

async function listFiles(root: string, prefix = ''): Promise<string[]> {
  const entries = await nodeFs.promises.readdir(path.join(root, prefix), { withFileTypes: true });
  const nested = await Promise.all(entries.map(entry => entry.isDirectory()
    ? listFiles(root, path.join(prefix, entry.name))
    : Promise.resolve([path.join(prefix, entry.name)])));
  return nested.flat();
}

/** FCSE's two mod types. Neither goes through JackAll - a plugin DLL never reaches patch.dat. */
export function registerModTypes(context: types.IExtensionContext): void {
  const isFarCry2 = (gameId: string) => gameId === GAME_ID;
  const binPath = () => path.join(gamePath(context.api) ?? '', 'bin');

  // mergeMods: true is load-bearing. Without it these inherit the game's `mergeMods: mod => mod.id`
  // and land in bin\<mod id>\ and bin\plugins\<mod id>\, where FCSE looks for neither.
  context.registerModType(
    MODTYPE_FCSE_PLUGIN, 25, isFarCry2,
    () => path.join(binPath(), PLUGINS_DIR),
    () => Bluebird.resolve(false),
    { name: 'FCSE Plugin', mergeMods: true });

  context.registerModType(
    MODTYPE_FCSE_LOADER, 25, isFarCry2,
    () => binPath(),
    () => Bluebird.resolve(false),
    { name: 'FCSE', mergeMods: true });
}
