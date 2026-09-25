import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TopProgressBar } from '@core/layout/top-progress-bar/top-progress-bar';
import { ThemeService } from '@core/theme/theme.service';
import { ToastModule } from 'primeng/toast';

/**
 * Root component: the router plus app-wide overlays (loading bar, toasts).
 * Layout lives in core/layout (Shell), pages in features/.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastModule, TopProgressBar],
  template: `
    <app-top-progress-bar />
    <router-outlet />
    <p-toast position="top-right" [breakpoints]="toastBreakpoints" />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  // Created here so the theme is applied on every page, including ones without the shell (login).
  private readonly theme = inject(ThemeService);

  // On phones the default 25rem-wide toast would overflow the screen: span the width instead.
  protected readonly toastBreakpoints = {
    '640px': { width: 'calc(100vw - 2rem)', left: '1rem', right: '1rem' },
  };
}
