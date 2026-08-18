import Bluebird from 'bluebird';
import * as nodeFs from 'fs';
import * as path from 'path';
import { fs, log, types, util } from 'vortex-api';

import { GAME_ID, MODTYPE_FCSE_LOADER } from './constants';
import { gamePath } from './game';
import * as jackall from './jackall';
import { ask, dismiss, notify } from './ui';

const FCSE_LOADER = 'fcse.exe';
const PLUGINS_DIR = 'plugins';
const MODS_DIR = 'mods';
const FCSE_PAGE_URL = 'https://jbebe.github.io/farcry-sdk/fcse';

/** The game's own binaries. No content mod, FCSE loader or plugin has any reason to ship these. */
const SUSPICIOUS_BINARIES = ['farcry2.exe', 'dunia.dll', 'fc2.dll'];
/** How many staged files a legacy import reads at once — see installLegacyPatch. */
const READ_BATCH = 32;

/** Claims every non-empty archive: only makeInstaller can tell what one actually is. */
export function testSupported(files: string[], gameId: string): Promise<types.ISupportedResult> {
  return Promise.resolve({
    supported: gameId === GAME_ID && files.length > 0,
    requiredFiles: [],
  });
}

/**
 * Decides what a downloaded archive is, checked in this order:
 *
 *   1. Legacy mod - a patch.dat anywhere (its patch.fat has to sit beside it). These predate the
 *      extension, so no structure can be forced on them; jackall converts the pair and ignores the
 *      rest of the archive.
 *   2. FCSE itself - FCSE.exe anywhere, deployed to bin\.
 *   3. Everything else is a layer, shaped by two reserved folders at the archive root, alone or
 *      together: plugins\ (an FCSE plugin - at least one .dll or .lua, at any depth) and mods\
 *      (game files at any depth, e.g. mods\worlds\…). The archive is staged as-is: JackAll reads
 *      both folders natively at build time, compiling mods\ into patch.dat and syncing plugins\
 *      into bin\plugins.
 *
 * Only the legacy bucket touches jackall-mi at install time, because converting one means diffing
 * against the game's own archives. The rest is pure string work over the file list.
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
    if (plainFiles.some(file => path.basename(file).toLowerCase() === 'patch.dat')) {
      throw new util.DataInvalid(
        'This archive contains a patch.dat but no patch.fat beside it. A legacy mod needs the '
        + 'pair - the .fat is the index that makes the .dat readable.');
    }

    const loader = plainFiles.find(file => path.basename(file).toLowerCase() === FCSE_LOADER);
    if (loader !== undefined) {
      const root = path.dirname(loader);
      return withModType(
        copyUnder(plainFiles, root === '.' ? '' : root, { stripRoot: true }), MODTYPE_FCSE_LOADER);
    }

    const roots = layerRoots(plainFiles);
    if (roots.length > 0) {
      if (roots.includes(PLUGINS_DIR)) {
        await warnIfFcseMissing(api, gamePath(api));
      }
      log('info', 'Far Cry 2: staging mod layer', { roots });
      return { instructions: roots.flatMap(root => copyUnder(plainFiles, root)) };
    }

    const buried = findBuriedReservedDir(plainFiles);
    throw new util.DataInvalid(
      'This doesn\'t look like a Far Cry 2 mod. It has to contain either a patch.dat with its '
      + 'patch.fat beside it (a legacy full-patch mod), or - at the top level of the archive - a '
      + '"plugins" folder holding an FCSE plugin (a .dll or .lua at any depth) and/or a "mods" '
      + 'folder holding game files (e.g. mods\\worlds\\…).'
      + (buried === undefined
        ? ''
        : ` This archive has "${buried}" - repack it with "${path.basename(buried)}" at the top `
          + 'level.'));
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
/** Copy instructions for the files under `root`, staged at the path they already have - `stripRoot`
 * drops that folder instead, for a payload whose own root is the destination. */
function copyUnder(
  files: string[], root: string, options: { stripRoot?: boolean } = {},
): types.IInstruction[] {
  // Normalized on both sides: Windows compares paths case-insensitively regardless.
  const prefix = root === '' ? '' : normalize(root) + path.sep;
  return files
    .filter(file => normalize(file).startsWith(prefix))
    .map(file => ({
      type: 'copy' as const,
      source: file,
      destination: (options.stripRoot === true ? file.substring(prefix.length) : file)
        .replace(/[\\/]/g, path.sep),
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

/**
 * Detects the file on disk rather than checking mod state, so this catches FCSE installed by hand
 * (outside Vortex) just as well as FCSE installed as a mod.
 */
async function warnIfFcseMissing(api: types.IExtensionApi, gameRoot: string | undefined): Promise<void> {
  if (gameRoot === undefined || nodeFs.existsSync(path.join(gameRoot, 'bin', FCSE_LOADER))) {
    return;
  }

  const result = await ask(api, 'question', 'Far Cry Script Extender (FCSE) not found', {
    text: 'This mod contains an FCSE plugin, but FCSE itself doesn\'t look installed - without it, '
      + 'plugin files are deployed but never loaded by the game.',
  }, [
    { label: 'Download' },
    { label: 'Continue' },
  ]);

  if (result.action === 'Download') {
    await util.opn(FCSE_PAGE_URL).catch(() => undefined);
  }
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

/** A patch.dat with a patch.fat right beside it, anywhere - the whole of what makes a legacy mod. */
function findLegacyPatchPair(files: string[]): { fat: string; dat: string } | undefined {
  for (const file of files) {
    if (path.basename(file).toLowerCase() !== 'patch.dat') {
      continue;
    }
    const dir = normalize(path.dirname(file));
    const fat = files.find(f =>
      normalize(path.dirname(f)) === dir && path.basename(f).toLowerCase() === 'patch.fat');
    if (fat !== undefined) {
      return { fat, dat: file };
    }
  }
  return undefined;
}

/** FCSE loads .dll and .lua alike, nested or flat. */
function isPluginFile(file: string): boolean {
  const extension = path.extname(file).toLowerCase();
  return extension === '.dll' || extension === '.lua';
}

/** Whether `file` sits anywhere under the top-level folder `dirName`. */
function isUnder(file: string, dirName: string): boolean {
  return normalize(file).startsWith(dirName + path.sep);
}

/** The reserved folders this archive carries: mods\ needs a file of any kind, plugins\ one FCSE
 * plugin file. Only the top level counts, so mods\plugins\x.dll is game data and plugins\mods\…
 * plugin data. */
function layerRoots(files: string[]): string[] {
  return [
    ...(files.some(file => isUnder(file, MODS_DIR)) ? [MODS_DIR] : []),
    ...(files.some(file => isUnder(file, PLUGINS_DIR) && isPluginFile(file)) ? [PLUGINS_DIR] : []),
  ];
}

/** A reserved folder the archive buries under a wrapper - the one packaging mistake worth naming
 * in the rejection, since it looks like a layer but resolves to nothing. */
function findBuriedReservedDir(files: string[]): string | undefined {
  for (const file of files) {
    // Segments as packaged, not normalized: this ends up in a message telling someone what to move.
    const segments = file.split(/[\\/]/);
    const index = segments.findIndex((segment, at) =>
      at > 0 && at < segments.length - 1
      && [MODS_DIR, PLUGINS_DIR].includes(segment.toLowerCase()));
    if (index !== -1) {
      return segments.slice(0, index + 1).join(path.sep);
    }
  }
  return undefined;
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
        log('info', `Far Cry 2 (jackall-mi): ${message}`);
        notify(api, { id, type: 'activity', title, message });
      },
    });

    // One file per changed archetype or placed entity, so a big mod stages thousands of small XMLs.
    // Read them a batch at a time: opening every one at once runs a machine out of file handles,
    // while one at a time pays a full event-loop round trip per file.
    const staged = await listFiles(outDir);
    const instructions: types.IInstruction[] = [];
    for (let i = 0; i < staged.length; i += READ_BATCH) {
      const batch = staged.slice(i, i + READ_BATCH);
      instructions.push(...await Promise.all(batch.map(async rel => ({
        type: 'generatefile' as const,
        data: await fs.readFileAsync(path.join(outDir, rel)),
        destination: rel,
      }))));
    }

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

/**
 * FCSE itself, the one thing that isn't a layer. A plugin has no mod type of its own: it rides in a
 * layer's plugins\ folder, which JackAll deploys at build time.
 */
export function registerModTypes(context: types.IExtensionContext): void {
  // mergeMods: true is load-bearing. Without it this inherits the game's `mergeMods: mod => mod.id`
  // and lands in bin\<mod id>\, where FCSE looks for nothing.
  context.registerModType(
    MODTYPE_FCSE_LOADER, 25, (gameId: string) => gameId === GAME_ID,
    () => path.join(gamePath(context.api) ?? '', 'bin'),
    () => Bluebird.resolve(false),
    { name: 'FCSE', mergeMods: true });
}
