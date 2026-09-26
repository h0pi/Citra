import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';
import { ThemeService } from '@core/theme/theme.service';
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';

import { UserMenu } from '../user-menu/user-menu';

/** Top bar: mobile menu button, theme toggle and the user menu. */
@Component({
  selector: 'app-topbar',
  imports: [ButtonModule, TooltipModule, UserMenu],
  templateUrl: './topbar.html',
  styleUrl: './topbar.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Topbar {
  private readonly theme = inject(ThemeService);

  /** Opens/closes the sidebar drawer on small screens. */
  readonly menuToggle = output<void>();

  protected readonly isDark = computed(() => this.theme.mode() === 'dark');

  protected toggleTheme(): void {
    this.theme.toggle();
  }
}
