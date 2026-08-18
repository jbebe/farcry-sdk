import * as path from 'path';
import { fs, types, util } from 'vortex-api';

import { GAME_ID, MODTYPE_LAYER } from './constants';
import { activeProfile } from './game';

const USAGE_INSTRUCTIONS =
  'Drag to set which mod wins when two of them change the same file. '
  + 'Layers are applied top to bottom, so the mod at the BOTTOM overrides the ones above it — the '
  + 'same order the JackAll app uses.\n\n'
  + 'Two mods changing different archetypes or placed entities of the same .fcb never collide at all, '
  + 'and different parts of the same one are merged, so order only decides genuine conflicts.\n\n'
  + 'Use Vortex\'s normal enable/disable to leave a mod out; only enabled mods appear here.';

function loadOrderFile(profileId: string): string {
  return path.join(util.getVortexPath('userData'), GAME_ID, `loadorder.${profileId}.json`);
}

async function readSavedOrder(profileId: string): Promise<string[]> {
  try {
    const raw = await fs.readFileAsync(loadOrderFile(profileId), { encoding: 'utf8' });
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === 'string') : [];
  } catch {
    // No file yet, or an unreadable one. Installation order is a fine starting point.
    return [];
  }
}

function enabledLayerMods(api: types.IExtensionApi): types.IMod[] {
  const profile = activeProfile(api);
  if (profile === undefined) {
    return [];
  }

  const mods = util.getSafe(
    api.getState(), ['persistent', 'mods', GAME_ID], {}) as { [id: string]: types.IMod };

  return Object.values(mods).filter(mod =>
    (mod.type ?? MODTYPE_LAYER) === MODTYPE_LAYER
    && util.getSafe<boolean>(profile, ['modState', mod.id, 'enabled'], false));
}

/**
 * The layer mods in apply order - the single source of truth for both this page and the --layer list.
 * Mods the saved order has never seen go last, where they win, which is what "just installed" means.
 */
export async function orderedLayerMods(api: types.IExtensionApi): Promise<types.IMod[]> {
  const profile = activeProfile(api);
  if (profile === undefined) {
    return [];
  }

  const mods = enabledLayerMods(api);
  const saved = await readSavedOrder(profile.id);
  const rank = new Map(saved.map((id, index) => [id, index]));

  return mods.sort((lhs, rhs) =>
    (rank.get(lhs.id) ?? Number.MAX_SAFE_INTEGER) - (rank.get(rhs.id) ?? Number.MAX_SAFE_INTEGER));
}

export function loadOrderInfo(api: types.IExtensionApi): types.ILoadOrderGameInfo {
  return {
    gameId: GAME_ID,
    // Vortex's own enable/disable already decides which mods deploy; a second switch here could only
    // ever disagree with it.
    toggleableEntries: false,
    usageInstructions: USAGE_INSTRUCTIONS,

    deserializeLoadOrder: async (): Promise<types.LoadOrder> => {
      const ordered = await orderedLayerMods(api);
      return ordered.map(mod => ({
        id: mod.id,
        modId: mod.id,
        name: util.renderModName(mod),
        enabled: true,
      }));
    },

    serializeLoadOrder: async (loadOrder: types.LoadOrder): Promise<void> => {
      const profile = activeProfile(api);
      if (profile === undefined) {
        return;
      }
      const target = loadOrderFile(profile.id);
      await fs.ensureDirWritableAsync(path.dirname(target));
      await fs.writeFileAsync(target, JSON.stringify(loadOrder.map(entry => entry.id), null, 2));
    },

    // No order can be invalid: layers are independent files and any permutation builds. It has to be
    // undefined rather than { invalid: [] } - Vortex checks whether a value came back at all, so an
    // empty result object fails validation with no reasons and the page reads "failed validation".
    validate: async () => undefined as unknown as types.IValidationResult,
  };
}
