import { selectors, types } from 'vortex-api';

import { GAME_ID } from './constants';
import { rebuild, registerEvents, restore } from './deploy';
import { gameDefinition } from './game';
import { makeInstaller, registerModTypes, testSupported } from './installers';
import { loadOrderInfo } from './loadOrder';

/**
 * Far Cry 2 has no loose-file loading and no plugin list - everything a mod changes has to be
 * compiled into Data_Win32\patch.dat. So Vortex handles downloading, staging, enabling and ordering,
 * and jackall-mi answers every question about what a file means to the game.
 */
function main(context: types.IExtensionContext): boolean {
  context.registerGame(gameDefinition(context.api));
  registerModTypes(context);

  // Priority 25 beats Vortex's fallback installer, which would copy a mod's wrapper folder into the
  // layer verbatim and produce a mod that applies nothing.
  context.registerInstaller('farcry2-jackall', 25, testSupported, makeInstaller(context.api));

  context.registerLoadOrder(loadOrderInfo(context.api));

  context.registerAction('mod-icons', 300, 'refresh', {}, 'Rebuild patch.dat',
    () => { void rebuild(context.api, 'manual'); },
    () => isActive(context.api));

  context.registerAction('mod-icons', 301, 'remove', {}, 'Restore vanilla patch.dat',
    () => { void restore(context.api); },
    () => isActive(context.api));

  // In once() so every other extension has finished registering first.
  context.once(() => registerEvents(context.api));

  return true;
}

function isActive(api: types.IExtensionApi): boolean {
  return selectors.activeGameId(api.getState()) === GAME_ID;
}

export default main;
