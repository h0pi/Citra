import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';
import { CurrentUser } from '@core/models/auth.models';

import { UserMenu } from './user-menu';

describe('UserMenu', () => {
  const currentUser = signal<CurrentUser | null>(null);
  const logout = vi.fn();

  beforeEach(async () => {
    logout.mockReset();
    currentUser.set({
      id: 'u1',
      email: 'ammar@acme.com',
      displayName: 'Ammar Hadzic',
      role: 'Owner',
      tenantId: 't1',
      tenantName: 'Acme Logistics',
    });
    await TestBed.configureTestingModule({
      imports: [UserMenu],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: currentUser.asReadonly(), logout } },
      ],
    }).compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(UserMenu);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('shows the initials and name of the logged-in user', async () => {
    const { el } = await render();
    expect(el.querySelector('.p-avatar')?.textContent?.trim()).toBe('AH');
    expect(el.textContent).toContain('Ammar Hadzic');
  });

  it('shows nothing when nobody is logged in', async () => {
    currentUser.set(null);
    const { el } = await render();
    expect(el.querySelector('.user-button')).toBeNull();
  });

  it('opens a menu with account details, and Log out logs out', async () => {
    const { fixture, el } = await render();
    el.querySelector<HTMLButtonElement>('.user-button')!.click();
    await fixture.whenStable();

    const popup = document.body.querySelector('.user-menu-popup')!;
    expect(popup.textContent).toContain('ammar@acme.com');
    expect(popup.textContent).toContain('Acme Logistics');
    expect(popup.textContent).toContain('Owner');

    // PrimeNG handles item clicks on the item's inner content element.
    const logoutItem = [...popup.querySelectorAll<HTMLElement>('.p-menu-item-content')].find((e) =>
      e.textContent?.includes('Log out'),
    )!;
    logoutItem.click();
    expect(logout).toHaveBeenCalledTimes(1);
  });
});
