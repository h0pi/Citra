import { CanDeactivateFn } from '@angular/router';

/** A page with a form that can be left half-edited (bot settings, later document and widget settings). */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/** Asks before leaving a page with unsaved edits, so a stray click on the sidebar does not throw work away. */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (page) =>
  !page.hasUnsavedChanges() || window.confirm('You have unsaved changes. Leave without saving?');
