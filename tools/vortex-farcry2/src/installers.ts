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
 *   3. Everything else is a layer, shaped by two reserved folders that may appear alone or
 *      together, each under any wrapper: plugins\ (an FCSE plugin - at least one .dll or .lua, at
 *      any depth) and mods\ (game files, e.g. mods\worlds\…). The archive shape is staged as-is:
 *      JackAll reads both folders natively at build time, compiling mods\ into patch.dat and
 *      syncing plugins\ into bin\plugins.
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

    const legacy = findLegacyPatch(plainFiles);
    if (legacy !== undefined) {
      if (legacy.fat === undefined) {
        throw new util.DataInvalid(
          'This archive contains a patch.dat but no patch.fat beside it. A legacy mod needs the '
          + 'pair - the .fat is the index that makes the .dat readable.');
      }
      await warnAboutIgnoredExtras(api, plainFiles, { fat: legacy.fat, dat: legacy.dat }, modName);

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

    let pluginsRoot = findPluginsRoot(plainFiles);
    let modsRoot = findModsRoot(plainFiles);
    // A plugins\ nested inside mods\ is game content, not a payload - and vice versa.
    if (pluginsRoot !== undefined && modsRoot !== undefined) {
      if (pluginsRoot.startsWith(modsRoot + path.sep)) {
        pluginsRoot = undefined;
      } else if (modsRoot.startsWith(pluginsRoot + path.sep)) {
        modsRoot = undefined;
      }
    }

    if (pluginsRoot !== undefined || modsRoot !== undefined) {
      if (pluginsRoot !== undefined) {
        await warnIfFcseMissing(api, gamePath(api), modsRoot !== undefined);
      }
      log('info', 'Far Cry 2: staging mod layer', { pluginsRoot, modsRoot });
      return {
        instructions: [
          ...(modsRoot === undefined ? [] : rebaseInto(plainFiles, modsRoot, MODS_DIR)),
          ...(pluginsRoot === undefined ? [] : rebaseInto(plainFiles, pluginsRoot, PLUGINS_DIR)),
        ],
      };
    }

    throw new util.DataInvalid(
      'This doesn\'t look like a Far Cry 2 mod. It has to contain at least one of: a patch.dat (a '
      + 'legacy full-patch mod, with its patch.fat beside it), a "plugins" folder holding an FCSE '
      + 'plugin (a .dll or .lua at any depth), or a "mods" folder holding game files (e.g. '
      + 'mods\\worlds\\…). Older packages rooted under Data_Win32\\ repack by renaming that folder '
      + 'to "mods".');
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

/** rebase, then re-prefix each destination under destRoot - for content that must keep its folder
 * inside the layer rather than landing at its root. */
function rebaseInto(files: string[], root: string, destRoot: string): types.IInstruction[] {
  return rebase(files, root).map(instr =>
    ({ ...instr, destination: path.join(destRoot, instr.destination as string) }));
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
async function warnIfFcseMissing(
  api: types.IExtensionApi, gameRoot: string | undefined, combined: boolean,
): Promise<void> {
  if (gameRoot === undefined || nodeFs.existsSync(path.join(gameRoot, 'bin', FCSE_LOADER))) {
    return;
  }

  const result = await ask(api, 'question', 'Far Cry Script Extender (FCSE) not found', {
    text: `This mod ${combined ? 'includes' : 'is'} an FCSE plugin, but FCSE itself doesn't look `
      + 'installed - without it, plugin files are deployed but never loaded by the game.',
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

/**
 * A patch.dat is the whole of what makes a legacy mod; the pair's fat is undefined when it's
 * missing, so the caller can reject with the real reason rather than misreading the archive as
 * something else. A dat with its fat beside it wins over a lone dat found earlier.
 */
function findLegacyPatch(files: string[]): { fat: string | undefined; dat: string } | undefined {
  let lonely: string | undefined;
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
    lonely = lonely ?? file;
  }
  return lonely === undefined ? undefined : { fat: undefined, dat: lonely };
}

/** The prefix up to and including a literal mods\ folder, wrapper folders allowed above it. */
function findModsRoot(files: string[]): string | undefined {
  for (const file of files) {
    const segments = normalize(file).split(path.sep);
    const index = segments.indexOf(MODS_DIR);
    if (index >= 0 && index < segments.length - 1) {
      return segments.slice(0, index + 1).join(path.sep);
    }
  }
  return undefined;
}

/** The prefix up to and including a plugins\ folder holding at least one .dll or .lua at any
 * depth - FCSE loads both, nested or flat. */
function findPluginsRoot(files: string[]): string | undefined {
  for (const file of files) {
    const extension = path.extname(file).toLowerCase();
    if (extension !== '.dll' && extension !== '.lua') {
      continue;
    }
    const segments = normalize(file).split(path.sep);
    const index = segments.indexOf(PLUGINS_DIR);
    if (index >= 0 && index < segments.length - 1) {
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
 * FCSE's two mod types. The loader type is still assigned by the installer; the plugin type is
 * legacy-only - new plugin installs are plain layers whose plugins\ payload JackAll deploys - but
 * stays registered so mods installed under it keep deploying to bin\plugins.
 */
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
