import * as nodeFs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { actions, log, selectors, tooltip, types, util } from 'vortex-api';

import {
  FCSE_LOADER, FCSE_TOOL_ID, GAME_ID, MODTYPE_FCSE_LOADER, MODTYPE_LAYER, PLUGINS_DIR,
} from './constants';
import { activeProfile, fcseTool, gamePath } from './game';
import { enabledLayerMods } from './loadOrder';
import { ask, dismiss, notify } from './ui';

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

/** Once per session, so a failure doesn't reopen the browser for every plugin mod after it. The
 * warning icon still gets FCSE on click after that. */
let prompted = false;
let installing = false;

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

function hasPlugins(api: types.IExtensionApi, mod: types.IMod): boolean {
  const staging = selectors.installPathForGame(api.getState(), GAME_ID);
  return (mod.type ?? MODTYPE_LAYER) === MODTYPE_LAYER && mod.installationPath !== undefined
    && nodeFs.existsSync(path.join(staging, mod.installationPath, PLUGINS_DIR));
}

/**
 * Every session, so a player who ignored the first prompt is asked again on the next launch.
 * `requiredBy` names a mod still installing; otherwise the first enabled plugin mod is used.
 */
export async function requireFcse(api: types.IExtensionApi, requiredBy?: string): Promise<void> {
  if (prompted || isFcseAvailable(api)) {
    return;
  }
  const needing = enabledPluginMods(api)[0];
  const name = requiredBy ?? (needing === undefined ? undefined : util.renderModName(needing));
  if (name !== undefined) {
    prompted = true;
    await installFcse(api, name);
  }
}

/**
 * The first time FCSE appears in bin\, registers it as a found tool and makes it what Play starts,
 * unless the player already picked a primary tool. Found once, it is never promoted again.
 */
export function promoteFcse(api: types.IExtensionApi): void {
  const state = api.getState();
  const tool = fcseTool(api);
  const bin = tool.queryPath!() as string;
  const found = util.getSafe(
    state, ['settings', 'gameMode', 'discovered', GAME_ID, 'tools', FCSE_TOOL_ID, 'path'], undefined);
  if (bin === '' || found !== undefined) {
    return;
  }

  api.store?.dispatch(actions.addDiscoveredTool(GAME_ID, FCSE_TOOL_ID, {
    ...tool,
    path: path.join(bin, tool.executable()),
    hidden: false,
    parameters: [],
    custom: false,
  } as types.IDiscoveredTool, false));
  if (util.getSafe(state, ['settings', 'interface', 'primaryTool', GAME_ID], undefined) === undefined) {
    api.store?.dispatch(actions.setPrimaryTool(GAME_ID, FCSE_TOOL_ID));
  }
}

function enabledPluginMods(api: types.IExtensionApi): types.IMod[] {
  return enabledLayerMods(api).filter(mod => hasPlugins(api, mod));
}

/** A start hook: FarCry2.exe on its own never loads plugins, so offer FCSE in its place. */
export async function checkLaunch(
  api: types.IExtensionApi, call: types.IRunParameters,
): Promise<types.IRunParameters> {
  const gameRoot = gamePath(api);
  const needing = path.basename(call.executable).toLowerCase() === 'farcry2.exe'
    ? enabledPluginMods(api) : [];
  if (gameRoot === undefined || needing.length === 0) {
    return call;
  }

  const fcse = path.join(gameRoot, 'bin', FCSE_LOADER);
  const installed = nodeFs.existsSync(fcse);
  const result = await ask(api, 'question', 'These mods\' plugins won\'t load', {
    text: 'FarCry2.exe on its own doesn\'t load FCSE plugins, which these enabled mods contain. '
      + (installed ? 'Start the game with FCSE to use them.' : 'FCSE isn\'t installed yet.'),
    message: needing.map(mod => util.renderModName(mod)).join('\n'),
  }, [
    { label: 'Cancel' },
    { label: 'Play without plugins' },
    { label: installed ? 'Play with FCSE' : 'Get FCSE', default: true },
  ]);

  if (result.action === 'Play without plugins') {
    return call;
  }
  if (result.action === 'Play with FCSE') {
    return { ...call, executable: fcse };
  }
  if (result.action === 'Get FCSE') {
    void installFcse(api, util.renderModName(needing[0]));
  }
  throw new util.UserCanceled();
}

/** A warning icon in the mods table on each plugin mod while FCSE is missing; a click gets it. */
export function fcseColumn(api: types.IExtensionApi): types.ITableAttribute<types.IMod> {
  const lacksFcse = (mod: types.IMod) => hasPlugins(api, mod) && !isFcseAvailable(api);
  return {
    id: 'farcry2-fcse',
    name: 'FCSE',
    description: 'Warns about a mod whose plugin needs FCSE, which is not installed',
    placement: 'table',
    isToggleable: true,
    isDefaultVisible: true,
    isSortable: false,
    edit: {},
    condition: () => selectors.activeGameId(api.getState()) === GAME_ID,
    // The plugin mod's own row doesn't change when FCSE is installed.
    externalData: onChanged => api.onStateChange?.(['persistent', 'mods', GAME_ID], onChanged),
    calc: lacksFcse,
    customRenderer: mod => (Array.isArray(mod) || !lacksFcse(mod)
      ? React.createElement('span')
      : React.createElement(tooltip.IconButton, {
        icon: 'feedback-warning',
        tooltip: 'This mod\'s plugin only loads with FCSE, which isn\'t installed. Click to get it.',
        onClick: () => void installFcse(api, util.renderModName(mod)),
      })),
  };
}

/** Premium accounts get FCSE installed; Nexus serves anyone else its page only, so that's offered. */
async function installFcse(api: types.IExtensionApi, requiredBy: string): Promise<void> {
  if (installing || isFcseAvailable(api)) {
    return;
  }
  installing = true;
  try {
    const premium = util.getSafe<boolean>(
      api.getState(), ['persistent', 'nexus', 'userInfo', 'isPremium'], false);
    if (!premium || !await downloadFcse(api, requiredBy)) {
      await offerFcsePage(api, requiredBy);
    }
  } finally {
    installing = false;
  }
}

/** Downloads FCSE's newest main file, installs and enables it; false when that failed. */
async function downloadFcse(api: types.IExtensionApi, requiredBy: string): Promise<boolean> {
  notify(api, {
    id: NOTIFICATION_ID,
    type: 'activity',
    title: 'Installing FCSE',
    message: `"${requiredBy}" contains an FCSE plugin, which needs the Far Cry Script Extender.`,
    noDismiss: true,
  });
  try {
    const files = await api.ext.nexusGetModFiles!(GAME_ID, FCSE_NEXUS_ID) as INexusFile[];
    const fileId = files
      .filter(file => file.category_id === MAIN_FILE_CATEGORY)
      .sort((lhs, rhs) => rhs.uploaded_timestamp - lhs.uploaded_timestamp)[0]?.file_id;

    // Undefined on failure, which nexusDownload has already reported.
    const downloadId: string | undefined = fileId === undefined ? undefined
      : await api.ext.nexusDownload!(GAME_ID, FCSE_NEXUS_ID, fileId, undefined, false);
    if (downloadId === undefined) {
      return false;
    }
    const modId = await util.toPromise<string>(cb => api.events.emit(
      'start-install-download', downloadId, { allowAutoEnable: false }, cb));
    const profile = activeProfile(api);
    if (profile !== undefined) {
      await actions.setModsEnabled(api, profile.id, [modId], true,
        { allowAutoDeploy: true, installed: true });
    }
    return true;
  } catch (err) {
    log('warn', 'Far Cry 2: FCSE download failed', { error: (err as Error).message });
    return false;
  } finally {
    dismiss(api, NOTIFICATION_ID);
  }
}

async function offerFcsePage(api: types.IExtensionApi, requiredBy: string): Promise<void> {
  const result = await ask(api, 'question', 'FCSE is required', {
    text: `"${requiredBy}" contains an FCSE plugin, which the game only loads with the Far Cry `
      + 'Script Extender (FCSE) installed.\n\n'
      + 'Open FCSE\'s Nexus Mods page and use "Mod Manager Download" there to install it.',
  }, [
    { label: 'Cancel' },
    { label: 'Open FCSE page' },
  ]);
  if (result.action === 'Open FCSE page') {
    await util.opn(`https://www.nexusmods.com/${GAME_ID}/mods/${FCSE_NEXUS_ID}?tab=files`)
      .catch(() => undefined);
  }
}
