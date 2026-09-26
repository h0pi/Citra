import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { ThemeService } from '@core/theme/theme.service';
import { ButtonModule } from 'primeng/button';

/**
 * Centered card layout for logged-out pages (login, register): brand on top, form content inside,
 * theme toggle in the corner. The pages project their form into it.
 */
@Component({
  selector: 'app-auth-layout',
  imports: [ButtonModule],
  templateUrl: './auth-layout.html',
  styleUrl: './auth-layout.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthLayout {
  private readonly theme = inject(ThemeService);

  readonly title = input.required<string>();
  readonly subtitle = input<string>();

  protected readonly isDark = computed(() => this.theme.mode() === 'dark');

  protected toggleTheme(): void {
    this.theme.toggle();
  }
}
