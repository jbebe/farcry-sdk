import Bluebird from 'bluebird';
import * as path from 'path';
import { fs, log, selectors, types, util } from 'vortex-api';

import { GAME_ID, GOG_APP_ID, LAYERS_FOLDER, STEAM_APP_ID } from './constants';
import * as jackall from './jackall';
import { ask, notify } from './ui';

/** The discovered install path, or undefined if Vortex hasn't found the game. */
export function gamePath(api: types.IExtensionApi): string | undefined {
  const discovery = util.getSafe(
    api.getState(), ['settings', 'gameMode', 'discovered', GAME_ID], undefined) as
    types.IDiscoveryResult | undefined;
  return discovery?.path;
}

export function layersPath(gameRoot: string): string {
  return path.join(gameRoot, LAYERS_FOLDER);
}

/** The active profile, or undefined when the active one belongs to another game. */
export function activeProfile(api: types.IExtensionApi): types.IProfile | undefined {
  const profile = selectors.activeProfile(api.getState());
  return profile?.gameId === GAME_ID ? profile : undefined;
}

function findGame(): Bluebird<string> {
  return util.GameStoreHelper.findByAppId([STEAM_APP_ID, GOG_APP_ID])
    .then((game: types.IGameStoreEntry) => game.gamePath);
}

/** Vortex's setup hook - runs before every activation of this game. */
function prepareForModding(api: types.IExtensionApi) {
  return (discovery: types.IDiscoveryResult): Bluebird<void> =>
    Bluebird.resolve(prepare(api, discovery));
}

async function prepare(api: types.IExtensionApi, discovery: types.IDiscoveryResult): Promise<void> {
  if (discovery.path === undefined) {
    return;
  }
  await fs.ensureDirWritableAsync(layersPath(discovery.path));

  try {
    const status = await jackall.status(discovery.path);
    if (status.valid && status.needsVanillaConfirmation === true) {
      await confirmVanillaBaseline(api, discovery.path);
    }
  } catch (err) {
    // Never block activation on this - the deploy path re-checks and refuses there anyway.
    log('warn', 'Far Cry 2: could not check patch.dat state', { error: (err as Error).message });
  }
}

/**
 * Every build starts from patch.dat.vanilla, so capturing someone else's mod as the baseline bakes it
 * into all of them and the only way back is reinstalling. Hence asking at activation, not mid-deploy.
 */
async function confirmVanillaBaseline(api: types.IExtensionApi, gameRoot: string): Promise<void> {
  const result = await ask(api, 'question', 'Far Cry 2: patch.dat already looks modded', {
    text:
      'Vortex compiles every Far Cry 2 mod into Data_Win32\\patch.dat, always starting from a '
      + 'pristine backup of that file. This install has no backup yet, and the patch.dat currently '
      + 'there looks like it already contains a mod.\n\n'
      + 'If that mod is captured as the baseline it becomes part of every build from now on, and '
      + 'the only way to undo it is to reinstall the game.\n\n'
      + 'The safe route is to restore the original files first — in Steam, right-click the game and '
      + 'use Verify integrity of game files — then come back.',
  }, [
    { label: "I'll restore the game files first" },
    { label: 'Use the current patch.dat as the baseline' },
  ]);

  if (result.action !== 'Use the current patch.dat as the baseline') {
    return;
  }

  // A forced build with no layers writes the patch back out as-is and creates the backup on the way,
  // so the answer takes effect immediately and there's nothing to persist.
  await jackall.build(gameRoot, [], { force: true });
  notify(api, {
    type: 'info',
    title: 'Far Cry 2 baseline captured',
    message: 'The current patch.dat is now this install\'s vanilla backup.',
    displayMS: 8000,
  });
}

export function gameDefinition(api: types.IExtensionApi): types.IGame {
  return {
    id: GAME_ID,
    name: 'Far Cry 2',
    logo: 'gameart.jpg',
    queryPath: findGame,
    queryModPath: () => LAYERS_FOLDER,
    executable: () => path.join('bin', 'FarCry2.exe'),
    // The same three files JackAll checks, so the two can never disagree about whether a folder is
    // a usable install.
    requiredFiles: [
      path.join('bin', 'FarCry2.exe'),
      path.join('Data_Win32', 'patch.fat'),
      path.join('Data_Win32', 'patch.dat'),
    ],
    setup: prepareForModding(api),
    // One folder per mod, named by mod id. The function form rather than `false` so the deploy step
    // can reconstruct each layer path exactly instead of guessing what Vortex chose.
    mergeMods: (mod: types.IMod) => mod.id,
    requiresCleanup: true,
    supportedTools: [
      {
        id: 'fcse',
        name: 'Far Cry Script Extender (FCSE)',
        shortName: 'FCSE',
        executable: () => 'FCSE.exe',
        requiredFiles: ['FCSE.exe'],
        queryPath: (gameRoot: string) => path.join(gameRoot, 'bin'),
        relative: true,
        // FCSE launches the game alongside FarCry2.exe rather than replacing it, so it must not
        // take over as *the* way to start the game.
        exclusive: false,
      } as types.ITool,
    ],
    environment: { SteamAPPId: STEAM_APP_ID },
    details: { steamAppId: parseInt(STEAM_APP_ID, 10) },
  };
}
