const path = require('path');

/**
 * Vortex loads an extension as a single CommonJS file, so everything under src/ is bundled into
 * dist/index.js. 'vortex-api' is deliberately *not* bundled: at runtime Vortex injects its own
 * module of that name (along with the React/Redux/Bluebird instances it owns), and bundling a
 * second copy of any of those into the process is the classic way to break an extension in ways
 * that only show up at runtime.
 */
module.exports = {
  target: 'node',
  entry: './src/index.ts',
  output: {
    path: path.resolve(__dirname, 'dist'),
    filename: 'index.js',
    libraryTarget: 'commonjs2',
  },
  module: {
    rules: [{ test: /\.ts$/, use: 'ts-loader', exclude: /node_modules/ }],
  },
  resolve: {
    extensions: ['.ts', '.js'],
    alias: {
      // The published typings live under a scoped name, but the module Vortex injects at runtime
      // is the bare 'vortex-api' - so source imports that name and this maps it for the compiler.
      'vortex-api': path.resolve(__dirname, 'node_modules/@nexusmods/vortex-api/lib/api.d.ts'),
    },
  },
  externals: {
    // Only this one. Bluebird is bundled instead of externalised even though Vortex has its own:
    // it's small, holds no cross-module state, and two bluebirds interoperate through `then` just
    // fine - whereas relying on the host to resolve it would trade a few KB for a runtime failure
    // mode. It's only needed at all because several Vortex API signatures are typed as returning
    // Bluebird rather than a plain promise.
    'vortex-api': 'commonjs2 vortex-api',
  },
  devtool: 'source-map',
  // Node built-ins are external automatically with target: 'node'.
  node: false,
};
