import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { LoadingService } from '@core/services/loading.service';

/**
 * Thin animated bar at the very top of the page while API requests are running.
 * It fades in only after 150 ms, so quick requests don't cause a flicker.
 */
@Component({
  selector: 'app-top-progress-bar',
  template: `
    @if (loading.isLoading()) {
      <div class="progress" role="progressbar" aria-label="Loading" aria-busy="true">
        <div class="progress-indicator"></div>
      </div>
    }
  `,
  styles: `
    .progress {
      position: fixed;
      inset: 0 0 auto 0;
      z-index: 2000;
      height: 3px;
      overflow: hidden;
      background: var(--citra-primary-soft);
      opacity: 0;
      animation: appear 0.2s ease 150ms forwards;
    }

    .progress-indicator {
      width: 40%;
      height: 100%;
      background: var(--citra-primary);
      border-radius: 3px;
      animation: slide 1.1s ease-in-out infinite;
    }

    @keyframes appear {
      to {
        opacity: 1;
      }
    }

    @keyframes slide {
      from {
        transform: translateX(-100%);
      }
      to {
        transform: translateX(250%);
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .progress-indicator {
        width: 100%;
        animation: none;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TopProgressBar {
  protected readonly loading = inject(LoadingService);
}
