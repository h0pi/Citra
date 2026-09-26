import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

import { HasUnsavedChanges, unsavedChangesGuard } from './unsaved-changes.guard';

describe('unsavedChangesGuard', () => {
  const run = (dirty: boolean) =>
    unsavedChangesGuard(
      { hasUnsavedChanges: () => dirty } satisfies HasUnsavedChanges,
      {} as ActivatedRouteSnapshot,
      {} as RouterStateSnapshot,
      {} as RouterStateSnapshot,
    );

  afterEach(() => vi.restoreAllMocks());

  it('lets the user leave a page without changes, without asking', () => {
    const confirm = vi.spyOn(window, 'confirm');
    expect(run(false)).toBe(true);
    expect(confirm).not.toHaveBeenCalled();
  });

  it('asks before leaving unsaved changes and respects the answer', () => {
    vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    expect(run(true)).toBe(false);
    expect(run(true)).toBe(true);
  });
});
