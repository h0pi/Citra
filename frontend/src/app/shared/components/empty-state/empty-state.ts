import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Friendly placeholder for "nothing here yet" situations (no documents, no chats, ...).
 * Projected content (e.g. a call-to-action button) is shown below the text.
 */
@Component({
  selector: 'app-empty-state',
  template: `
    <div class="empty-state">
      <span class="empty-state-icon" aria-hidden="true"><i [class]="'pi ' + icon()"></i></span>
      <h3>{{ title() }}</h3>
      @if (message()) {
        <p class="text-muted">{{ message() }}</p>
      }
      <ng-content />
    </div>
  `,
  styles: `
    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      text-align: center;
      padding: var(--citra-space-8) var(--citra-space-5);
      background: var(--citra-surface);
      border: 1px dashed var(--citra-border);
      border-radius: var(--citra-radius-lg);
    }
    .empty-state-icon {
      display: grid;
      place-items: center;
      width: 56px;
      height: 56px;
      margin-bottom: var(--citra-space-4);
      border-radius: 50%;
      background: var(--citra-primary-soft);
      color: var(--citra-primary);
      font-size: 1.5rem;
    }
    h3 {
      margin-bottom: var(--citra-space-2);
    }
    p {
      max-width: 420px;
      margin-bottom: var(--citra-space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmptyState {
  /** PrimeIcons name without the "pi " prefix, e.g. "pi-file". */
  readonly icon = input('pi-inbox');
  readonly title = input.required<string>();
  readonly message = input<string>();
}
