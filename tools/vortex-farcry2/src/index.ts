import { selectors, types } from 'vortex-api';

import { GAME_ID } from './constants';
import { rebuild, registerEvents, restore } from './deploy';
import { gameDefinition } from './game';
import { makeInstaller, registerModTypes, testSupported } from './installers';
import { loadOrderInfo } from './loadOrder';

/**
 * Far Cry 2 support for Vortex.
 *
 * The engine has no loose-file loading and no plugin list: everything a mod changes has to be
 * compiled into `Data_Win32\patch.dat`. So this extension is a front-end for JackAll, which already
 * does that correctly, rather than an implementation of Far Cry 2 modding in its own right — Vortex
 * handles downloading, staging, enabling and ordering, and `jackall-cli` handles every question
 * about what a file means to the game.
 *
 * The shape that falls out of that:
 *
 *   - each mod deploys into its own folder under `vortex-staging\` (one layer per mod),
 *   - the load order decides which layer wins a conflict (bottom wins, as in JackAll),
 *   - `did-deploy` compiles those layers into patch.dat, always starting from a pristine backup,
 *   - `did-purge` puts that backup back.
 */
function main(context: types.IExtensionContext): boolean {
  context.registerGame(gameDefinition(context.api));

  registerModTypes(context);

  // Priority 25 puts this ahead of Vortex's generic fallback installer, which would otherwise copy
  // a mod's wrapper folder into the layer verbatim and produce a mod that applies nothing.
  context.registerInstaller('farcry2-jackall', 25, testSupported, makeInstaller(context.api));

  context.registerLoadOrder(loadOrderInfo(context.api));

  context.registerAction('mod-icons', 300, 'refresh', {}, 'Rebuild patch.dat',
    () => { void rebuild(context.api, 'manual'); },
    () => isActive(context.api));

  context.registerAction('mod-icons', 301, 'remove', {}, 'Restore vanilla patch.dat',
    () => { void restore(context.api); },
    () => isActive(context.api));

  // Event handlers go in `once` so every other extension has finished registering first.
  context.once(() => registerEvents(context.api));

  return true;
}

function isActive(api: types.IExtensionApi): boolean {
  return selectors.activeGameId(api.getState()) === GAME_ID;
}

export default main;
