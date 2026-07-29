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

/**
 * The game's own executable and engine DLLs (see docs/docs/engine-internals/overview.md and
 * docs/docs/modding/file-manifest.md) - files no legitimate content mod, FCSE loader or FCSE plugin
 * has any reason to ship. Their presence means one of two things: the "mod" is actually a careless
 * re-zip of someone's whole game folder (installing it would do nothing useful and might silently
 * overwrite the user's own binaries via a whole-file override), or, worse, a replaced/tampered copy
 * of one of them bundled specifically to get itself run or loaded. Either way it's worth a pause
 * before proceeding, not a silent install.
 */
const SUSPICIOUS_BINARIES = ['farcry2.exe', 'dunia.dll', 'fc2.dll'];

/**
 * Decides what a downloaded archive actually is and how to stage it.
 *
 * Three buckets, checked in this order, and an archive is exactly one of them:
 *
 *   1. **Legacy mod** - contains a `patch.dat`/`patch.fat` pair anywhere. We can't force any
 *      structure on these (they predate this extension entirely), so the only thing to do is find
 *      the pair and hand it to `jackall mod import-legacy` to convert. Anything else in the archive
 *      (readmes, screenshots, alternate versions - real downloads regularly bundle a pile of this)
 *      is not part of the conversion and gets silently left out, so {@link warnAboutIgnoredExtras}
 *      says so up front rather than leaving the user to notice files are missing after the fact.
 *   2. **FCSE plugin** - a `.dll` under a `plugins\` folder (see {@link findPluginsRoot}). Extra
 *      files alongside it are normal (a plugin can ship its own data/config) and not warned about.
 *   3. **Asset mod** - has to be rooted under a literal `Data_Win32\` folder (see
 *      {@link findDataWin32Root}). Deliberately strict rather than the old score-every-candidate-root
 *      approach: a mod either uses this convention or it doesn't, with no guessing in between.
 *
 * Neither FCSE bucket nor the asset-mod bucket needs the game to be discovered at all - both are
 * pure string operations over the file list. Only the legacy-patch bucket ever calls `jackall-cli`
 * (via {@link installLegacyPatch}), because converting one genuinely requires diffing against the
 * game's own archives.
 */
export function testSupported(files: string[], gameId: string): Promise<types.ISupportedResult> {
  return Promise.resolve({
    supported: gameId === GAME_ID && files.length > 0,
    requiredFiles: [],
  });
}

export function makeInstaller(api: types.IExtensionApi) {
  return async (
    files: string[], destinationPath: string, gameId: string,
  ): Promise<types.IInstallResult> => {
    if (gameId !== GAME_ID) {
      throw new Error(`Far Cry 2 installer called for ${gameId}.`);
    }

    const plainFiles = files.filter(file => !file.endsWith(path.sep));
    const modName = path.basename(destinationPath).replace(/\.installing$/, '');

    // Checked before any classification, and regardless of which bucket this ends up in - a rogue
    // FarCry2.exe/Dunia.dll is exactly as much of a concern either way.
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
      const sourceRoot = resolveExtractionRoot(destinationPath, plainFiles);
      return installLegacyPatch(api, gameRoot, sourceRoot, modName);
    }

    const loader = plainFiles.find(file => path.basename(file).toLowerCase() === FCSE_LOADER);
    if (loader !== undefined) {
      return installFcseLoader(plainFiles, loader);
    }

    const pluginRoot = findPluginsRoot(plainFiles);
    if (pluginRoot !== undefined) {
      return installFcsePlugins(plainFiles, pluginRoot);
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
 * Vortex passes the *staging* path here, which for an in-progress install carries a `.installing`
 * suffix while the extracted files sit beside it. Rather than depend on which of the two it is
 * (that has moved between Vortex versions), probe for a file the archive is known to contain.
 *
 * Failing outright beats guessing: pointing `mod inspect` at an empty directory would come back
 * "not a Far Cry 2 mod", which sends the user off debugging their perfectly good download.
 */
function resolveExtractionRoot(destinationPath: string, files: string[]): string {
  const probe = files[0];
  const candidates = [destinationPath, destinationPath.replace(/\.installing$/, '')];
  const found = candidates.find(candidate =>
    probe !== undefined && nodeFs.existsSync(path.join(candidate, probe)));

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
    // The root comes back from JackAll normalized (lowercased), and Windows paths compare
    // case-insensitively anyway, so matching it any other way would drop real files.
    .filter(file => prefix === '' || normalize(file).startsWith(prefix))
    .map(file => ({
      type: 'copy',
      source: file,
      destination: prefix === '' ? file : file.substring(prefix.length),
    }));
}

function normalize(input: string): string {
  return input.replace(/[\\/]/g, path.sep).toLowerCase();
}

/**
 * Blocking confirmation (not a dismissable notification) when the archive contains one of
 * {@link SUSPICIOUS_BINARIES} - deliberately in the user's way rather than a toast they might not
 * read before it auto-dismisses. This is the legacy way native-code mods used to work: patching
 * FarCry2.exe/Dunia.dll directly. FCSE replaced that need - it loads plugin DLLs from a `plugins\`
 * folder instead (see {@link findPluginsRoot}/{@link installFcsePlugins}), so a mod doesn't have to
 * touch the game's own binaries to extend it anymore, and this extension has no build/merge story
 * for one that still does (unlike a `patch.dat`/`.fat` legacy content patch, which
 * `jackall mod import-legacy` converts automatically - see {@link installLegacyPatch}).
 */
async function warnIfBundlingGameBinaries(
  api: types.IExtensionApi, files: string[], modName: string,
): Promise<void> {
  const matches = files.filter(file => SUSPICIOUS_BINARIES.includes(path.basename(file).toLowerCase()));
  if (matches.length === 0) {
    return;
  }

  const result = await ask(api, 'question', 'This looks like a legacy binary-patch mod', {
    text: `"${modName}" contains ${matches.join(', ')} - it replaces Far Cry 2's own executable or `
      + 'the Dunia engine DLL directly.\n\n'
      + 'That style of mod isn\'t supported anymore. Native-code mods now go through FCSE (Far Cry '
      + 'Script Extender), which loads plugin DLLs from a `plugins\\` folder instead of patching the '
      + 'game\'s own binaries - so if this is meant to add functionality to the game, it should be '
      + 'repackaged as an FCSE plugin rather than shipping a replacement exe/DLL.\n\n'
      + 'Installing it as-is will not do anything useful through this extension, and overwriting your '
      + 'own FarCry2.exe/Dunia.dll with an unknown copy is risky on its own. Only continue if you '
      + 'understand exactly what this file changes and trust where it came from.',
  }, [
    { label: 'Cancel install' },
    { label: 'Install anyway' },
  ]);

  if (result.action !== 'Install anyway') {
    throw new util.UserCanceled();
  }
}

/**
 * Finds a `patch.fat` with a `patch.dat` sitting right beside it, anywhere in the archive - the
 * whole of what makes something a legacy mod (see {@link JackAll.Core.Mods.LegacyPatchImporter
 * `LegacyPatchImporter.FindPatchPair`} on the JackAll side, which this mirrors exactly). Plain
 * filename matching, not hash-based, so there's nothing here worth delegating to `jackall-cli` for -
 * unlike root detection, which used to need the game's own archives to score candidates against and
 * doesn't exist anymore now that an asset mod must be rooted under a literal `Data_Win32\` folder.
 */
function findLegacyPatchPair(files: string[]): { fat: string; dat: string } | undefined {
  for (const file of files) {
    if (path.basename(file).toLowerCase() !== 'patch.fat') {
      continue;
    }
    const dir = normalize(path.dirname(file));
    const dat = files.find(candidate =>
      normalize(path.dirname(candidate)) === dir && path.basename(candidate).toLowerCase() === 'patch.dat');
    if (dat !== undefined) {
      return { fat: file, dat };
    }
  }
  return undefined;
}

/**
 * The prefix up to and including a literal `Data_Win32\` folder, wherever it sits in the archive
 * (allowing for a wrapper folder above it, same as a legacy mod's patch.dat/patch.fat pair) - or
 * undefined if there isn't one. Everything under it is relative-to-Data_Win32, exactly the paths
 * `jackall-cli mod build` already expects (`worlds\…`, `generated\…`, …), so no further translation
 * is needed once this prefix is stripped.
 */
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

/**
 * A legacy mod only ever converts the patch.dat/patch.fat pair itself - real downloads regularly
 * bundle a readme, screenshots, or a whole alternate version alongside it, and all of that gets
 * silently left out of the install. Worth a heads-up before it happens rather than leaving the user
 * to notice missing files afterward.
 */
async function warnAboutIgnoredExtras(
  api: types.IExtensionApi, files: string[], pair: { fat: string; dat: string }, modName: string,
): Promise<void> {
  const extras = files.filter(file => file !== pair.fat && file !== pair.dat);
  if (extras.length === 0) {
    return;
  }

  const result = await ask(api, 'question', 'This archive contains more than patch.dat/patch.fat', {
    text: `"${modName}" is a legacy patch.dat/patch.fat mod, but the archive also contains `
      + `${extras.length} other file(s) (readmes, screenshots, alternate versions, and the like).\n\n`
      + 'Only the patch.dat/patch.fat pair itself gets converted - everything else will be silently '
      + 'left out of the install.\n\nContinue installing just the patch.dat/patch.fat conversion?',
  }, [
    { label: 'Cancel install' },
    { label: 'Install anyway' },
  ]);

  if (result.action !== 'Install anyway') {
    throw new util.UserCanceled();
  }
}

function installFcseLoader(files: string[], loader: string): types.IInstallResult {
  const root = path.dirname(loader);
  const instructions: types.IInstruction[] = rebase(files, root === '.' ? '' : root);
  instructions.push({ type: 'setmodtype', value: MODTYPE_FCSE_LOADER });
  return { instructions };
}

/** The folder a `plugins\` directory lives in, so plugin DLLs land as `bin\plugins\x.dll`. */
function findPluginsRoot(files: string[]): string | undefined {
  const dll = files.find(file => {
    const segments = normalize(file).split(path.sep);
    return segments.length >= 2
      && segments[segments.length - 2] === PLUGINS_DIR
      && path.extname(file).toLowerCase() === '.dll';
  });
  return dll === undefined ? undefined : path.dirname(dll);
}

function installFcsePlugins(files: string[], pluginRoot: string): types.IInstallResult {
  const instructions: types.IInstruction[] = rebase(files, pluginRoot);
  instructions.push({ type: 'setmodtype', value: MODTYPE_FCSE_PLUGIN });
  return { instructions };
}

/**
 * Converts a legacy full-patch mod at install time, so everything downstream sees an ordinary layer.
 *
 * The converted files come back as `generatefile` instructions rather than `copy` ones, because
 * they don't exist inside the archive Vortex extracted — they're the *difference* between that
 * archive's patch.dat and the game's own, computed by JackAll. That also sidesteps having to name a
 * source path relative to an extraction directory whose exact identity is version-dependent.
 *
 * Only genuine differences are staged (a legacy patch is ~200,000 entries of which a handful are
 * the mod), so the amount held in memory here is the size of the mod's real content, not the size
 * of the archive.
 */
async function installLegacyPatch(
  api: types.IExtensionApi, gameRoot: string, sourceRoot: string, modName: string,
): Promise<types.IInstallResult> {
  const notificationId = `farcry2-import-${modName}`;
  notify(api, {
    id: notificationId,
    type: 'activity',
    title: 'Converting a Far Cry 2 patch mod',
    message: 'Reading the game\'s archives…',
  });

  const outDir = path.join(util.getVortexPath('temp'), `jackall-legacy-${Date.now().toString(36)}`);
  try {
    const result = await jackall.importLegacy(gameRoot, sourceRoot, outDir, modName, {
      onProgress: message => notify(api, {
        id: notificationId, type: 'activity', title: 'Converting a Far Cry 2 patch mod', message,
      }),
    });

    const staged = await listFilesRecursive(outDir);
    const instructions: types.IInstruction[] = await Promise.all(staged.map(async relative => ({
      type: 'generatefile' as const,
      data: await fs.readFileAsync(path.join(outDir, relative)),
      destination: relative,
    })));

    notify(api, {
      type: 'success',
      title: 'Far Cry 2 patch mod converted',
      message: `${result.imported} file(s) and ${result.fragmentsImported} .fcb fragment(s) differ `
        + `from the base game and were kept; the rest was vanilla data.`,
      displayMS: 10000,
    });

    return { instructions };
  } finally {
    dismiss(api, notificationId);
    await fs.removeAsync(outDir).catch(() => undefined);
  }
}

async function listFilesRecursive(root: string, prefix = ''): Promise<string[]> {
  const entries = await nodeFs.promises.readdir(path.join(root, prefix), { withFileTypes: true });
  const nested = await Promise.all(entries.map(entry => entry.isDirectory()
    ? listFilesRecursive(root, path.join(prefix, entry.name))
    : Promise.resolve([path.join(prefix, entry.name)])));
  return nested.flat();
}

/**
 * FCSE's two mod types. Neither goes through JackAll: an FCSE plugin is a DLL loaded into the game
 * process at runtime, which has nothing to do with the archive pipeline patch.dat is built from.
 */
export function registerModTypes(context: types.IExtensionContext): void {
  const isFarCry2 = (gameId: string) => gameId === GAME_ID;
  const binPath = () => path.join(gamePath(context.api) ?? '', 'bin');

  // mergeMods: true is load-bearing. Without it these inherit the game's own
  // `mergeMods: mod => mod.id`, which exists to give each JackAll layer its own folder - and would
  // put FCSE.exe in bin\<mod id>\ and every plugin in bin\plugins\<mod id>\, where FCSE looks for
  // neither. These two mod types genuinely do want their files merged into one directory.
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
