import * as nodeFs from 'fs';
import * as path from 'path';
import { actions, log, selectors, types, util } from 'vortex-api';

import { FCSE_LOADER, GAME_ID, MODTYPE_FCSE_LOADER, PLUGINS_DIR } from './constants';
import { activeProfile, gamePath } from './game';
import { enabledLayerMods } from './loadOrder';
import { dismiss, notify } from './ui';

// https://www.nexusmods.com/farcry2/mods/368
const FCSE_NEXUS_ID = 368;
const NOTIFICATION_ID = 'farcry2-fcse-install';
const MAIN_FILE_CATEGORY = 1;

/** The fields of a Nexus v1 file entry this reads. */
interface INexusFile {
  file_id: number;
  category_id: number;
  uploaded_timestamp: number;
}

/** Once per session, so a failure doesn't reopen the browser for every plugin mod after it. */
let attempted = false;

/** FCSE as a Vortex mod, deployed or not, or on disk (also when installed by hand). Without a
 * discovered game there is nowhere to put it, so that counts as available too. */
function isFcseAvailable(api: types.IExtensionApi): boolean {
  const mods = util.getSafe(api.getState(), ['persistent', 'mods', GAME_ID], {}) as
    Record<string, types.IMod>;
  if (Object.values(mods).some(mod => mod.type === MODTYPE_FCSE_LOADER)) {
    return true;
  }
  const gameRoot = gamePath(api);
  return gameRoot === undefined || nodeFs.existsSync(path.join(gameRoot, 'bin', FCSE_LOADER));
}

/** Every session, so a player who ignored the first prompt is asked again on the next launch. */
export async function requireFcse(api: types.IExtensionApi): Promise<void> {
  if (attempted || isFcseAvailable(api)) {
    return;
  }
  const staging = selectors.installPathForGame(api.getState(), GAME_ID);
  const needing = enabledLayerMods(api).find(mod =>
    nodeFs.existsSync(path.join(staging, mod.installationPath, PLUGINS_DIR)));
  if (needing !== undefined) {
    await installFcse(api, util.renderModName(needing));
  }
}

/**
 * Downloads FCSE's newest main file from Nexus, installs and enables it. Nexus serves direct
 * downloads to premium accounts only, so anyone else gets the file's page to download it from.
 */
export async function installFcse(api: types.IExtensionApi, requiredBy: string): Promise<void> {
  if (attempted || isFcseAvailable(api)) {
    return;
  }
  attempted = true;
  notify(api, {
    id: NOTIFICATION_ID,
    type: 'activity',
    title: 'Installing FCSE',
    message: `"${requiredBy}" contains an FCSE plugin, which needs the Far Cry Script Extender.`,
    noDismiss: true,
  });

  let fileId: number | undefined;
  try {
    const files = await api.ext.nexusGetModFiles!(GAME_ID, FCSE_NEXUS_ID) as INexusFile[];
    fileId = files
      .filter(file => file.category_id === MAIN_FILE_CATEGORY)
      .sort((lhs, rhs) => rhs.uploaded_timestamp - lhs.uploaded_timestamp)[0]?.file_id;

    // Undefined on failure, which nexusDownload has already reported.
    const downloadId: string | undefined = fileId === undefined ? undefined
      : await api.ext.nexusDownload!(GAME_ID, FCSE_NEXUS_ID, fileId, undefined, false);
    if (downloadId !== undefined) {
      const modId = await util.toPromise<string>(cb => api.events.emit(
        'start-install-download', downloadId, { allowAutoEnable: false }, cb));
      const profile = activeProfile(api);
      if (profile !== undefined) {
        await actions.setModsEnabled(api, profile.id, [modId], true,
          { allowAutoDeploy: true, installed: true });
      }
      return;
    }
  } catch (err) {
    log('warn', 'Far Cry 2: FCSE download failed', { error: (err as Error).message });
  } finally {
    dismiss(api, NOTIFICATION_ID);
  }

  notify(api, {
    type: 'warning',
    title: 'Download FCSE from Nexus Mods',
    message: `"${requiredBy}" contains an FCSE plugin, which the game only loads with FCSE `
      + 'installed. Its page is open - use "Mod Manager Download" there.',
  });
  const page = `https://www.nexusmods.com/${GAME_ID}/mods/${FCSE_NEXUS_ID}?tab=files`;
  await util.opn(fileId === undefined ? page : `${page}&file_id=${fileId}`).catch(() => undefined);
}
