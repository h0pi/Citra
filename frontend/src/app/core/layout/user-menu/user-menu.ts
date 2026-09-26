import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { AuthService } from '@core/auth/auth.service';
import { MenuItem } from 'primeng/api';
import { AvatarModule } from 'primeng/avatar';
import { MenuModule } from 'primeng/menu';
import { TagModule } from 'primeng/tag';

/** Top-right user button: initials + name; opens a menu with account info, Settings and Log out. */
@Component({
  selector: 'app-user-menu',
  imports: [MenuModule, AvatarModule, TagModule],
  templateUrl: './user-menu.html',
  styleUrl: './user-menu.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserMenu {
  private readonly auth = inject(AuthService);

  protected readonly user = this.auth.currentUser;

  /** "Ammar Hadžić" → "AH", "ammar" → "A". */
  protected readonly initials = computed(() => {
    const name = this.user()?.displayName.trim() ?? '';
    const parts = name.split(/\s+/).filter(Boolean);
    const letters =
      parts.length > 1 ? parts[0][0] + parts[parts.length - 1][0] : (parts[0]?.[0] ?? '?');
    return letters.toUpperCase();
  });

  protected readonly items: MenuItem[] = [
    { label: 'Settings', icon: 'pi pi-cog', routerLink: '/settings' },
    { separator: true },
    { label: 'Log out', icon: 'pi pi-sign-out', command: () => this.auth.logout() },
  ];
}
