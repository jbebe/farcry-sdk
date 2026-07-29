import Bluebird from 'bluebird';
import * as path from 'path';
import { fs, log, selectors, types, util } from 'vortex-api';

import { GAME_ID, GOG_APP_ID, LAYERS_FOLDER, STEAM_APP_ID } from './constants';
import * as jackall from './jackall';
import { ask, notify } from './ui';

/**
 * Finds the install through whichever store has it.
 *
 * Steam's id is the only one hard-coded, because it's the only one that's certain. GOG, Ubisoft
 * Connect and Epic ids are found by name instead: a wrong hard-coded id fails silently and leaves
 * the user thinking the game isn't installed, whereas the name lookup degrades to "not found" only
 * when it genuinely isn't there.
 */
export function findGame(): Bluebird<string> {
  return util.GameStoreHelper.findByAppId([STEAM_APP_ID, GOG_APP_ID])
    .then((game: types.IGameStoreEntry) => game.gamePath);
}

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

/**
 * Called before every activation of this game.
 *
 * Two jobs: make sure the layers folder exists so deployment has somewhere to go, and get the
 * "already-modded patch.dat" situation resolved *now*, while there's a user looking, rather than at
 * deploy time. See {@link confirmVanillaBaseline} for why that one matters so much.
 */
export function prepareForModding(api: types.IExtensionApi) {
  return (discovery: types.IDiscoveryResult): Bluebird<void> => Bluebird.resolve(prepare(api, discovery));
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
    // Never block activation on this: a failed check is worth logging, but the deploy path
    // re-checks anyway and refuses there, so nothing unsafe gets through by carrying on here.
    log('warn', 'Far Cry 2: could not check patch.dat state', { error: (err as Error).message });
  }
}

/**
 * Asks what to do about a `patch.dat` that already looks modded while no `patch.dat.vanilla` backup
 * exists.
 *
 * This is the one genuinely destructive situation in the whole pipeline. JackAll rebuilds the patch
 * from its backup every time, so whatever gets captured as "vanilla" is baked into every future
 * build — capture someone else's mod and there is no way back short of reinstalling the game.
 *
 * The answer isn't remembered anywhere. Choosing to accept the current patch acts immediately, by
 * running a zero-layer forced build that creates the backup then and there; anything else leaves
 * the install untouched and the deploy path keeps refusing until it's resolved. Nothing to persist,
 * nothing to get out of sync.
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

  // A forced build with no layers writes the patch back out exactly as it is, and creates the
  // backup on the way - so the user's decision takes effect immediately rather than being stored.
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
    // The same three files GameInstall.TryOpen checks, so Vortex and JackAll can never disagree
    // about whether a folder is a usable install.
    requiredFiles: [
      path.join('bin', 'FarCry2.exe'),
      path.join('Data_Win32', 'patch.fat'),
      path.join('Data_Win32', 'patch.dat'),
    ],
    setup: prepareForModding(api),
    // One folder per mod, named by mod id. The function form rather than `false` on purpose: it
    // pins the deployed folder name to something this extension can reconstruct exactly when it
    // builds the --layer list, instead of having to work out what Vortex chose.
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

/** The active profile's id, or undefined when no profile is active for this game. */
export function activeProfile(api: types.IExtensionApi): types.IProfile | undefined {
  const profile = selectors.activeProfile(api.getState());
  return profile?.gameId === GAME_ID ? profile : undefined;
}
