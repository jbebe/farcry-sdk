/**
 * Loads the built bundle against a stub vortex-api and checks it registers what it should.
 *
 * This can't test behaviour - that needs a real Vortex, a real game and real mods. What it catches is
 * the whole class of "the extension doesn't even load": a bad webpack external, an import of something
 * the API doesn't export, a typo in an id, main() not exported the way Vortex looks for it.
 */
const assert = require('assert');
const path = require('path');
const Module = require('module');

const GAME_ID = 'farcry2';

// Vortex injects this module at runtime, which is why the bundle leaves it external.
const stub = makeStub();
const originalLoad = Module._load;
Module._load = function load(request, parent, isMain) {
  return request === 'vortex-api' ? stub : originalLoad(request, parent, isMain);
};

const extension = require(path.join(__dirname, '..', 'dist', 'index.js'));
const main = extension.default ?? extension;

assert.strictEqual(typeof main, 'function', 'Vortex calls the default export; the bundle needs one.');

const registered = {
  games: [], modTypes: [], installers: [], loadOrders: [], actions: [], onceCallbacks: [],
};
const api = makeApi();

assert.strictEqual(main(makeContext(registered, api)), true, 'main() must return true.');

// --- what got registered ---------------------------------------------------

assert.strictEqual(registered.games.length, 1);
const game = registered.games[0];
assert.strictEqual(game.id, GAME_ID);
assert.strictEqual(game.queryModPath(), 'vortex-staging',
  'Layers must deploy outside Data_Win32, or JackAll mounts a legacy patch.fat as a game archive.');
assert.strictEqual(typeof game.mergeMods, 'function',
  'mergeMods must be the function form: the deploy step reconstructs each layer folder by name.');
assert.strictEqual(game.mergeMods({ id: 'some-mod-id' }), 'some-mod-id');
assert.deepStrictEqual(game.requiredFiles.map(f => f.replace(/\\/g, '/')),
  ['bin/FarCry2.exe', 'Data_Win32/patch.fat', 'Data_Win32/patch.dat']);
assert.strictEqual(typeof game.setup, 'function');
assert.strictEqual(game.supportedTools.length, 1);
assert.strictEqual(game.supportedTools[0].id, 'fcse');

assert.deepStrictEqual(registered.modTypes.map(t => t.id), ['farcry2-fcse-loader'],
  'FCSE is the only non-layer mod type; a plugin ships inside a layer');
registered.modTypes.forEach(modType => assert.strictEqual(modType.options.mergeMods, true,
  `${modType.id} must opt out of the game's per-mod-folder mergeMods, or FCSE's files land in a `
  + 'subdirectory it never looks in'));

assert.strictEqual(registered.installers.length, 1);
const installer = registered.installers[0];
assert.strictEqual(installer.id, 'farcry2-jackall');
assert.ok(installer.priority < 100, 'must beat Vortex\'s generic fallback installer');

assert.strictEqual(registered.loadOrders.length, 1);
const loadOrder = registered.loadOrders[0];
assert.strictEqual(loadOrder.gameId, GAME_ID);
assert.strictEqual(loadOrder.toggleableEntries, false);
assert.match(loadOrder.usageInstructions, /BOTTOM/, 'users have to be told which end wins');

assert.strictEqual(registered.actions.length, 2, 'rebuild + restore toolbar actions');
assert.strictEqual(registered.onceCallbacks.length, 1, 'event handlers belong in context.once');

// --- the load order page ---------------------------------------------------

(async () => {
  assert.strictEqual(await loadOrder.validate([], []), undefined,
    'Vortex checks whether validate returned anything at all, so { invalid: [] } fails validation '
    + 'with no reasons and the page reads "load order failed validation".');

  // No profile in the stub state, and the page is reachable before any mod is installed.
  assert.deepStrictEqual(await loadOrder.deserializeLoadOrder(), []);
  await loadOrder.serializeLoadOrder([], []);

  // --- classification, without touching jackall-cli -------------------------

  assert.strictEqual(
    (await installer.testSupported(['worlds/world1/foo.xml'], 'skyrimse')).supported, false,
    'the installer must keep its hands off other games\' archives');
  assert.strictEqual(
    (await installer.testSupported(['worlds/world1/foo.xml'], GAME_ID)).supported, true);

  const install = (files, name) =>
    installer.install(files, `C:\\staging\\${name}.installing`, GAME_ID);
  const rejects = (files, name, type, why, pattern) => assert.rejects(
    () => install(files, name),
    err => err instanceof type && (pattern === undefined || pattern.test(err.message)), why);
  const noModType = result => assert.ok(
    !result.instructions.some(i => i.type === 'setmodtype'),
    'plugins\\/mods\\ archives are plain layer mods - JackAll deploys both halves at build time');
  const copies = result => result.instructions.filter(i => i.type === 'copy').map(i => i.destination);

  const loader = await install(['FCSE.exe', 'readme.txt'], 'fcse');
  assert.ok(loader.instructions.some(i =>
    i.type === 'setmodtype' && i.value === 'farcry2-fcse-loader'));

  // A plugin needs no dll directly under plugins\ - FCSE loads .dll and .lua at any depth.
  const plugin = await install([path.join('plugins', 'my_mod', 'my.dll')], 'plug');
  noModType(plugin);
  assert.deepStrictEqual(copies(plugin), [path.join('plugins', 'my_mod', 'my.dll')]);

  const luaPlugin = await install([path.join('plugins', 'coolmod.lua')], 'lua');
  noModType(luaPlugin);
  assert.deepStrictEqual(copies(luaPlugin), [path.join('plugins', 'coolmod.lua')]);

  const asset = await install([path.join('mods', 'worlds', 'world1', 'foo.xml')], 'asset');
  noModType(asset);
  assert.deepStrictEqual(copies(asset), [path.join('mods', 'worlds', 'world1', 'foo.xml')],
    'the mods\\ folder itself is part of the staged path - JackAll reads the layer by it');

  // One archive carrying both halves, staged as-is; the readme beside them is dropped.
  const combined = await install([
    path.join('plugins', 'my_mod', 'my.dll'),
    path.join('plugins', 'data', 'config.ini'),
    path.join('mods', 'generated', 'entitylibrary.fcb', 'vehicle', 'Land', 'Jeep.xml'),
    'readme.txt',
  ], 'combined');
  noModType(combined);
  assert.deepStrictEqual(copies(combined),
    [
      // A fragment id is a path of its own under the container, and the leaf's name prefix is only
      // cosmetic to JackAll - but its exact spelling still has to survive installation untouched.
      path.join('mods', 'generated', 'entitylibrary.fcb', 'vehicle', 'Land', 'Jeep.xml'),
      path.join('plugins', 'my_mod', 'my.dll'),
      path.join('plugins', 'data', 'config.ini'),
    ]);
  // A plugins\ folder with no .dll or .lua anywhere inside it is not a plugin payload.
  const modsOnly = await install(
    [path.join('plugins', 'readme.txt'), path.join('mods', 'worlds', 'a.xml')], 'modsonly');
  noModType(modsOnly);
  assert.deepStrictEqual(copies(modsOnly), [path.join('mods', 'worlds', 'a.xml')]);

  // Only the top level is reserved, so a "plugins" folder below mods\ is ordinary game content.
  const nestedName = await install([path.join('mods', 'plugins', 'x.dll')], 'nestedname');
  assert.deepStrictEqual(copies(nestedName), [path.join('mods', 'plugins', 'x.dll')]);

  await rejects(['patch.dat'], 'lonedat', stub.util.DataInvalid,
    'a lone patch.dat is a legacy mod missing its index, not something to misread as another shape',
    /patch\.fat/);

  await rejects(['patch.dat', 'patch.fat', path.join('plugins', 'x.dll')], 'legacyplus',
    stub.util.UserCanceled,
    'a patch.dat pair wins over everything else: the extras dialog must fire (the stub cancels it)');

  await rejects([path.join('MyMod', 'mods', 'worlds', 'world1', 'foo.xml')], 'wrapped',
    stub.util.DataInvalid,
    'a wrapper above mods\\ makes the archive unrecognized, and the reason must name that folder '
    + 'as packaged - it is what someone has to go and move',
    /MyMod/);

  await rejects([path.join('Data_Win32', 'worlds', 'world1', 'foo.xml')], 'datawin32',
    stub.util.DataInvalid,
    'a Data_Win32-rooted archive is not a layer; the error must spell out what one looks like',
    /mods/);

  await rejects(['readme.txt', 'shot.png'], 'junk', stub.util.DataInvalid,
    'an archive matching no recognized shape must be rejected, not installed as one');

  console.log('smoke: ok');
})().catch(err => {
  console.error('smoke: FAILED');
  console.error(err);
  process.exit(1);
});

// --- stubs -----------------------------------------------------------------

function makeStub() {
  return {
    types: {},
    log: () => undefined,
    fs: {
      ensureDirWritableAsync: async () => undefined,
      readFileAsync: async () => Buffer.alloc(0),
      writeFileAsync: async () => undefined,
      removeAsync: async () => undefined,
    },
    selectors: {
      activeGameId: () => GAME_ID,
      activeProfile: () => undefined,
      profileById: () => undefined,
    },
    util: {
      GameStoreHelper: {
        findByAppId: () => Promise.reject(new Error('not installed')),
      },
      getSafe: (obj, keyPath, fallback) =>
        keyPath.reduce((acc, key) => (acc?.[key] !== undefined ? acc[key] : undefined), obj)
          ?? fallback,
      getVortexPath: id => path.join('C:\\vortex', id),
      renderModName: mod => mod.id,
      SetupError: class SetupError extends Error {},
      DataInvalid: class DataInvalid extends Error {},
      UserCanceled: class UserCanceled extends Error {},
    },
  };
}

function makeApi() {
  return {
    getState: () => ({ settings: { gameMode: { discovered: {} } }, persistent: { mods: {} } }),
    sendNotification: () => undefined,
    dismissNotification: () => undefined,
    showErrorNotification: () => undefined,
    showDialog: async () => ({ action: 'cancel' }),
    onAsync: () => undefined,
    events: { on: () => undefined },
  };
}

function makeContext(registered, api) {
  return {
    api,
    registerGame: game => registered.games.push(game),
    registerModType: (id, priority, isSupported, getPath, test, options) =>
      registered.modTypes.push({ id, priority, isSupported, getPath, test, options }),
    registerInstaller: (id, priority, testSupported, install) =>
      registered.installers.push({ id, priority, testSupported, install }),
    registerLoadOrder: info => registered.loadOrders.push(info),
    registerAction: (group, position, icon, options, title, action, condition) =>
      registered.actions.push({ group, position, icon, options, title, action, condition }),
    once: cb => registered.onceCallbacks.push(cb),
  };
}
