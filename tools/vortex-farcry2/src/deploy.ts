import * as crypto from 'crypto';
import * as nodeFs from 'fs';
import * as path from 'path';
import { log, selectors, types } from 'vortex-api';

import { GAME_ID } from './constants';
import { gamePath, layersPath } from './game';
import * as jackall from './jackall';
import { orderedLayerMods } from './loadOrder';
import { ask, dismiss, notify, notifyError } from './ui';

const NOTIFICATION_ID = 'farcry2-jackall-build';

/**
 * The signature {@link jackall.build} was last actually run with — see {@link buildSignature}.
 * `undefined` means "no successful build yet this session", which always misses and forces a real
 * build, exactly like today's unconditional behaviour.
 */
let lastBuildSignature: string | undefined;

/**
 * Deployment is only half the job for Far Cry 2.
 *
 * The engine reads mods out of `Data_Win32\patch.dat` and nowhere else, so copying files into the
 * game folder achieves nothing on its own — what Vortex deploys is a set of per-mod *layer*
 * folders, and the real artifact is compiled from them afterwards, here. Purge is the mirror image:
 * removing the layer folders doesn't unmod anything, restoring the pristine patch archive does.
 *
 * Every build starts from `patch.dat.vanilla`, never from what's currently on disk, so this is
 * idempotent: deploying twice produces identical bytes, and a mod that's been disabled genuinely
 * disappears rather than lingering in an accumulated patch.
 */
export function registerEvents(api: types.IExtensionApi): void {
  api.onAsync('did-deploy', async (profileId: string) => {
    if (!isOurProfile(api, profileId)) {
      return;
    }
    await rebuild(api, 'deploy');
  });

  api.onAsync('did-purge', async (profileId: string) => {
    if (!isOurProfile(api, profileId)) {
      return;
    }
    await restore(api);
  });
}

function isOurProfile(api: types.IExtensionApi, profileId: string): boolean {
  const profile = selectors.profileById(api.getState(), profileId);
  return profile?.gameId === GAME_ID;
}

/**
 * Recompiles patch.dat from the currently enabled layers, in load order.
 *
 * The build itself has no incremental form — "always rebuild from vanilla" is exactly what makes
 * the result predictable — but whether to run it at all does: a `'deploy'` trigger skips the rebuild
 * when {@link buildSignature} shows the layers haven't changed since the last one (see there for
 * why `did-deploy` needs that). `trigger === 'manual'` (the toolbar button) always runs for real,
 * since forcing a rebuild is the entire point of that button.
 */
export async function rebuild(api: types.IExtensionApi, trigger: 'deploy' | 'manual'): Promise<void> {
  const gameRoot = gamePath(api);
  if (gameRoot === undefined) {
    return;
  }

  try {
    const status = await jackall.status(gameRoot);
    if (!status.valid) {
      throw new Error(status.error ?? 'That folder is not a usable Far Cry 2 install.');
    }
    if (status.needsVanillaConfirmation === true) {
      notifyError(api,
        'Far Cry 2 mods were not applied',
        'patch.dat already looks modded and there is no pristine backup to build from. Restore the '
        + 'original game files (in Steam: right-click the game, Verify integrity of game files), '
        + 'then deploy again.',
        { allowReport: false });
      return;
    }

    const layers = await resolveLayerDirs(api, gameRoot);
    const signature = await buildSignature(layers);

    // `did-deploy` fires for every mod type Vortex deploys, including FCSE plugins/loader - which
    // patch.dat never reads (resolveLayerDirs only resolves MODTYPE_LAYER mods). Without this check,
    // installing or toggling one of those would still trigger a full, pointless jackall-cli rebuild.
    // The manual "Rebuild patch.dat" button exists specifically to force one, so it always runs for
    // real regardless of what this signature says.
    if (trigger === 'deploy' && signature === lastBuildSignature) {
      log('info', 'Far Cry 2: patch.dat already reflects the enabled layers, skipping rebuild', {
        layers: layers.length,
      });
      return;
    }

    notify(api, {
      id: NOTIFICATION_ID,
      type: 'activity',
      title: 'Building patch.dat',
      message: `Applying ${layers.length} mod(s)…`,
    });

    const result = await jackall.build(gameRoot, layers, {
      onProgress: message => notify(api, {
        id: NOTIFICATION_ID, type: 'activity', title: 'Building patch.dat', message,
      }),
    });
    lastBuildSignature = signature;

    dismiss(api, NOTIFICATION_ID);
    notify(api, {
      type: 'success',
      title: trigger === 'deploy' ? 'Far Cry 2 mods applied' : 'patch.dat rebuilt',
      message: layers.length === 0
        ? 'No mods enabled — patch.dat is back to stock.'
        : `${result.overriddenEntries} file(s) replaced and ${result.addedEntries} added across `
          + `${layers.length} mod(s).`,
      displayMS: 8000,
    });

    if (result.conflicts.length > 0) {
      notifyFragmentConflicts(api, result.conflicts);
    }
  } catch (err) {
    dismiss(api, NOTIFICATION_ID);
    notifyError(api, 'Failed to build Far Cry 2\'s patch.dat', err as Error, {
      allowReport: false,
      message: 'Your game files are untouched — JackAll writes to a temporary file and only swaps '
        + 'it in once the build has finished.',
    });
  }
}

/**
 * Fragment-level conflicts jackall-cli resolved by load order rather than refusing to build (see
 * `BuildResult.conflicts`) - a headless build has nobody to ask, so it takes the higher-priority
 * mod's edit outright and reports it here instead.
 *
 * The notification itself stays a one-liner regardless of how many conflicts there were - its
 * `message` is meant for a line or two, not a report that grows with every mod combination in the
 * load order, so the full per-conflict list only ever shows up in the dialog behind the "Show
 * conflicts" action, never crammed into the toast.
 */
function notifyFragmentConflicts(
  api: types.IExtensionApi, conflicts: jackall.BuildResult['conflicts'],
): void {
  notify(api, {
    type: 'warning',
    title: `${conflicts.length} mod conflict(s) resolved by load order`,
    message: 'These mods edit the exact same part of the same file differently; the mod lower in '
      + 'your load order won each time.',
    allowSuppress: true,
    actions: [{
      title: 'Show conflicts',
      action: dismiss => {
        showConflictDetails(api, conflicts);
        dismiss();
      },
    }],
  });
}

/** The full per-conflict list, on demand - see {@link notifyFragmentConflicts}. */
function showConflictDetails(api: types.IExtensionApi, conflicts: jackall.BuildResult['conflicts']): void {
  const lines = conflicts.map(c =>
    `"${c.fragmentId}": "${c.winningLayer}" overrode ${c.earlierLayers.join(', ')} (load order).`);

  void ask(api, 'info', 'Mod conflicts resolved by load order', {
    text: 'These mods edit the exact same part of the same file differently. The mod lower in your '
      + 'load order won each time - reorder your mods if that\'s not what you want, or open '
      + 'JackAll.App to hand-merge the edits.',
    message: lines.join('\n'),
  }, [
    { label: 'Close' },
  ]);
}

/** Restores the pristine patch archive, undoing every build. */
export async function restore(api: types.IExtensionApi): Promise<void> {
  const gameRoot = gamePath(api);
  if (gameRoot === undefined) {
    return;
  }

  try {
    const status = await jackall.status(gameRoot);
    if (!status.valid || status.hasVanillaBackup !== true) {
      // Nothing was ever built here, so there is nothing to undo - and saying so would be noise.
      return;
    }

    await jackall.restore(gameRoot);
    // The next deploy must rebuild for real even if the enabled layers haven't changed - patch.dat
    // itself just moved (back to vanilla), so the last signature no longer describes what's on disk.
    lastBuildSignature = undefined;
    notify(api, {
      type: 'success',
      title: 'Far Cry 2 restored',
      message: 'patch.dat and patch.fat are back to their original, unmodded state.',
      displayMS: 8000,
    });
  } catch (err) {
    notifyError(api, 'Failed to restore Far Cry 2\'s patch.dat', err as Error,
      { allowReport: false });
  }
}

/**
 * A fingerprint of exactly what {@link jackall.build} is about to read: which layer directories,
 * in what order (order is baked in by hashing each directory's own path alongside its contents),
 * and every file inside each one, by relative path/size/mtime. Two builds with an identical
 * signature would produce byte-identical patch.dat/patch.fat, so a rebuild between them is
 * skippable - see the `did-deploy` handler in {@link registerEvents} for why that matters.
 *
 * Deliberately cheap relative to what it's guarding: stat'ing every deployed file is a lot less work
 * than jackall-cli actually reparsing and recompiling every .fcb container in the patch.
 */
async function buildSignature(layerDirs: string[]): Promise<string> {
  const hash = crypto.createHash('sha1');
  for (const dir of layerDirs) {
    hash.update(dir);
    const files = (await listFilesWithStats(dir)).sort();
    hash.update(files.join('\n'));
  }
  return hash.digest('hex');
}

async function listFilesWithStats(root: string, prefix = ''): Promise<string[]> {
  const entries = await nodeFs.promises.readdir(path.join(root, prefix), { withFileTypes: true });
  const nested = await Promise.all(entries.map(async entry => {
    const rel = path.join(prefix, entry.name);
    if (entry.isDirectory()) {
      return listFilesWithStats(root, rel);
    }
    const stat = await nodeFs.promises.stat(path.join(root, rel));
    return [`${rel}:${stat.size}:${stat.mtimeMs}`];
  }));
  return nested.flat();
}

/**
 * The deployed folder for each enabled layer mod, in load order — the exact `--layer` list.
 *
 * The folder name is `mod.id` because the game registration pins it there
 * (`mergeMods: mod => mod.id`), which is the whole reason for using the function form: no guessing
 * at what Vortex named the directory. A mod whose folder is missing is skipped with a warning
 * rather than failing the build, since that means Vortex hasn't deployed it (yet) and silently
 * including a path that isn't there would abort the whole deploy over one mod.
 */
async function resolveLayerDirs(api: types.IExtensionApi, gameRoot: string): Promise<string[]> {
  const mods = await orderedLayerMods(api);
  const root = layersPath(gameRoot);

  return mods
    .map(mod => path.join(root, mod.id))
    .filter(dir => {
      if (nodeFs.existsSync(dir)) {
        return true;
      }
      log('warn', 'Far Cry 2: skipping a mod with no deployed files', { dir });
      return false;
    });
}
