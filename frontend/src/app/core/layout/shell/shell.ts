import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthService } from '@core/auth/auth.service';

import { Sidebar } from '../sidebar/sidebar';
import { Topbar } from '../topbar/topbar';

/**
 * App shell for logged-in pages: sidebar on the left, top bar above the content.
 * On small screens the sidebar becomes a slide-in drawer opened from the top bar.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, Sidebar, Topbar],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Shell {
  /** Mobile drawer state (ignored on desktop, where the sidebar is always visible). */
  protected readonly sidebarOpen = signal(false);
  protected readonly isDemo = inject(AuthService).isDemo;

  protected toggleSidebar(): void {
    this.sidebarOpen.update((open) => !open);
  }

  protected closeSidebar(): void {
    this.sidebarOpen.set(false);
  }
}
