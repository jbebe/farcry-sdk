import { types } from 'vortex-api';

/**
 * Thin wrappers over the notification/dialog side of `IExtensionApi`.
 *
 * All of these are declared optional in the typings because `IExtensionApi` is shared with contexts
 * that have no UI at all. A game extension only ever runs in the renderer, where they are always
 * present — so the assertion is made once, here, with the reason written down, rather than sprayed
 * as `!` across every call site.
 */

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

export function ask(
  api: types.IExtensionApi,
  type: types.DialogType,
  title: string,
  content: types.IDialogContent,
  actions: types.DialogActions,
): PromiseLike<types.IDialogResult> {
  // PromiseLike, not Promise: Vortex hands back a Bluebird, and `await` doesn't care.
  return api.showDialog!(type, title, content, actions);
}
