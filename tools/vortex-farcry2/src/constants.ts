/** The Nexus Mods domain for Far Cry 2 — also the id Vortex knows this game by everywhere. */
export const GAME_ID = 'farcry2';

/**
 * Steam sells exactly one Far Cry 2 SKU, "Fortune's Edition", under this id. GOG and Ubisoft
 * Connect ids are deliberately absent rather than guessed — {@link findGame} falls back to a
 * by-name store lookup, which finds those installs without hard-coding an id that might be wrong.
 */
export const STEAM_APP_ID = '19900';

export const GOG_APP_ID = '1207659042';

/**
 * Where Vortex deploys mod layers, relative to the game root.
 *
 * Deliberately *not* under `Data_Win32`: JackAll enumerates `*.fat` recursively under that folder
 * to find the game's archives, so a legacy mod's `patch.fat` deployed there would be mounted as if
 * it were a shipped game archive. One folder up, that can't happen.
 */
export const LAYERS_FOLDER = 'vortex-staging';

/**
 * Vortex's default mod type is the empty string, and that's what an ordinary JackAll layer is —
 * it goes to the game's own {@link LAYERS_FOLDER}, so it needs no mod type of its own.
 */
export const MODTYPE_LAYER = '';

/** An FCSE plugin DLL: goes next to the loader in `bin\plugins\`. */
export const MODTYPE_FCSE_PLUGIN = 'farcry2-fcse-plugin';

/** FCSE itself: an extra launcher beside FarCry2.exe. */
export const MODTYPE_FCSE_LOADER = 'farcry2-fcse-loader';
