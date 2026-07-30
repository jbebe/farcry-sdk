import { types } from 'vortex-api';

// These four are optional in IExtensionApi because it's shared with contexts that have no UI. A game
// extension only ever runs in the renderer, where they're always present - so the assertion is made
// once, here, instead of as a `!` at every call site.

export function notify(api: types.IExtensionApi, notification: types.INotification): void {
  api.sendNotification!(notification);
}

export function dismiss(api: types.IExtensionApi, id: string): void {
  api.dismissNotification!(id);
}

export function notifyError(
  api: types.IExtensionApi, title: string, detail: string | Error, options?: types.IErrorOptions,
): void {
  api.showErrorNotification!(title, detail, options);
}

/** PromiseLike, not Promise: Vortex hands back a Bluebird, and `await` doesn't care. */
export function ask(
  api: types.IExtensionApi,
  type: types.DialogType,
  title: string,
  content: types.IDialogContent,
  actions: types.DialogActions,
): PromiseLike<types.IDialogResult> {
  return api.showDialog!(type, title, content, actions);
}
