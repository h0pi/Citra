import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Standard page heading: title, optional subtitle, and an actions area on the right.
 * Usage: <app-page-header title="Documents" subtitle="..."><p-button label="Upload" /></app-page-header>
 */
@Component({
  selector: 'app-page-header',
  template: `
    <header class="page-header">
      <div class="page-header-text">
        <h1>{{ title() }}</h1>
        @if (subtitle()) {
          <p class="text-muted">{{ subtitle() }}</p>
        }
      </div>
      <div class="page-header-actions">
        <ng-content />
      </div>
    </header>
  `,
  styles: `
    .page-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--citra-space-4);
      flex-wrap: wrap;
      margin-bottom: var(--citra-space-5);
    }
    h1 {
      margin-bottom: var(--citra-space-1);
    }
    p {
      margin: 0;
    }
    .page-header-actions {
      display: flex;
      gap: var(--citra-space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
}
