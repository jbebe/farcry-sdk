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

/** Signature of the last build this session. Undefined always misses, forcing a real one. */
let lastSignature: string | undefined;

/**
 * The engine reads mods out of Data_Win32\patch.dat and nowhere else, so deploying files is only half
 * the job: Vortex deploys one layer folder per mod, and patch.dat gets compiled from them here.
 * Purging is the mirror image - restoring the pristine archive, not removing files.
 */
export function registerEvents(api: types.IExtensionApi): void {
  api.onAsync('did-deploy', async (profileId: string) => {
    if (isOurProfile(api, profileId)) {
      await rebuild(api, 'deploy');
    }
  });

  api.onAsync('did-purge', async (profileId: string) => {
    if (isOurProfile(api, profileId)) {
      await restore(api);
    }
  });
}

function isOurProfile(api: types.IExtensionApi, profileId: string): boolean {
  const profile = selectors.profileById(api.getState(), profileId);
  return profile?.gameId === GAME_ID;
}

/**
 * Recompiles patch.dat from the enabled layers, in load order, always starting from the vanilla
 * backup - so deploying twice produces identical bytes and a disabled mod genuinely disappears.
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
    const signature = await signatureOf(layers);

    // did-deploy fires for every mod type, FCSE plugins and loader included, and none of those reach
    // patch.dat. The toolbar button exists to force a rebuild, so it ignores this.
    if (trigger === 'deploy' && signature === lastSignature) {
      log('info', 'Far Cry 2: patch.dat already matches the enabled layers, skipping rebuild');
      return;
    }

    notify(api, {
      id: NOTIFICATION_ID,
      type: 'activity',
      title: 'Building patch.dat',
      message: `Applying ${layers.length} mod(s)…`,
    });

    const result = await jackall.build(gameRoot, layers, {
      onProgress: message => {
        log('info', `Far Cry 2 (jackall-mi): ${message}`);
        notify(api, { id: NOTIFICATION_ID, type: 'activity', title: 'Building patch.dat', message });
      },
    });
    lastSignature = signature;

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
      notifyConflicts(api, result.conflicts);
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

/** Restores the pristine patch archive, undoing every build. */
export async function restore(api: types.IExtensionApi): Promise<void> {
  const gameRoot = gamePath(api);
  if (gameRoot === undefined) {
    return;
  }

  try {
    const status = await jackall.status(gameRoot);
    if (!status.valid || status.hasVanillaBackup !== true) {
      // Nothing was ever built here, so there is nothing to undo.
      return;
    }

    await jackall.restore(gameRoot);
    // patch.dat just moved back to vanilla, so the last signature no longer describes what's there.
    lastSignature = undefined;
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

function notifyConflicts(
  api: types.IExtensionApi, conflicts: jackall.BuildResult['conflicts'],
): void {
  notify(api, {
    type: 'warning',
    title: `${conflicts.length} mod conflict(s) resolved by load order`,
    message: 'These mods change the same part of the same thing differently; the mod lower in '
      + 'your load order won each time.',
    allowSuppress: true,
    actions: [{
      title: 'Show conflicts',
      // The list grows with the load order, so it belongs in a dialog rather than a toast.
      action: close => {
        showConflicts(api, conflicts);
        close();
      },
    }],
  });
}

function showConflicts(api: types.IExtensionApi, conflicts: jackall.BuildResult['conflicts']): void {
  const lines = conflicts.map(c => {
    const what = c.isNewEntry
      ? `both add "${c.fragmentId}" with different content`
      : `both edit "${c.fragmentId}"`;
    return `${c.container}: "${c.winningLayer}" and ${c.earlierLayers.join(', ')} ${what} - `
      + `"${c.winningLayer}" was kept (load order).`;
  });

  void ask(api, 'info', 'Mod conflicts resolved by load order', {
    text: 'Each line is one archetype or placed entity two mods changed differently. Mods touching '
      + 'different parts of the same file never reach this list. The mod lower in your load order '
      + 'won each time - reorder your mods if that\'s not what you want, or open JackAll.App to '
      + 'hand-merge the edits.',
    message: lines.join('\n'),
  }, [
    { label: 'Close' },
  ]);
}

/**
 * Fingerprints what a build would read: each layer directory, in order, and every file in it by path,
 * size and mtime. Stat'ing all of that is far cheaper than recompiling every .fcb in the patch.
 */
async function signatureOf(layerDirs: string[]): Promise<string> {
  const hash = crypto.createHash('sha1');
  for (const dir of layerDirs) {
    hash.update(dir);
    hash.update((await statFiles(dir)).sort().join('\n'));
  }
  return hash.digest('hex');
}

async function statFiles(root: string, prefix = ''): Promise<string[]> {
  const entries = await nodeFs.promises.readdir(path.join(root, prefix), { withFileTypes: true });
  const nested = await Promise.all(entries.map(async entry => {
    const rel = path.join(prefix, entry.name);
    if (entry.isDirectory()) {
      return statFiles(root, rel);
    }
    const stat = await nodeFs.promises.stat(path.join(root, rel));
    return [`${rel}:${stat.size}:${stat.mtimeMs}`];
  }));
  return nested.flat();
}

/** The deployed folder for each enabled layer mod, in load order - the --layer list. */
async function resolveLayerDirs(api: types.IExtensionApi, gameRoot: string): Promise<string[]> {
  const mods = await orderedLayerMods(api);
  const root = layersPath(gameRoot);

  return mods
    .map(mod => path.join(root, mod.id))
    .filter(dir => {
      if (nodeFs.existsSync(dir)) {
        return true;
      }
      // Vortex hasn't deployed it yet. Skipping beats failing the whole deploy over one mod.
      log('warn', 'Far Cry 2: skipping a mod with no deployed files', { dir });
      return false;
    });
}
