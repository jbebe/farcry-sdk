/** The Nexus Mods domain for Far Cry 2, and the id Vortex knows the game by. */
export const GAME_ID = 'farcry2';

/** Steam sells exactly one Far Cry 2 SKU, "Fortune's Edition", under this id. */
export const STEAM_APP_ID = '19900';

export const GOG_APP_ID = '1207659042';

/**
 * Where mods deploy, relative to the game root. Deliberately not under Data_Win32: JackAll scans
 * that folder for *.fat, so a legacy mod's patch.fat deployed there would be mounted as if it were
 * one of the game's own archives.
 */
export const LAYERS_FOLDER = 'vortex-staging';

/** An ordinary mod. Vortex's default mod type is the empty string, and that's what a layer is. */
export const MODTYPE_LAYER = '';

/** An FCSE plugin DLL, deployed to bin\plugins\. */
export const MODTYPE_FCSE_PLUGIN = 'farcry2-fcse-plugin';

/** FCSE itself, deployed beside FarCry2.exe. */
export const MODTYPE_FCSE_LOADER = 'farcry2-fcse-loader';
