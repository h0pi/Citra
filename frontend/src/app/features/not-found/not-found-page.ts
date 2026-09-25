import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';
import { ButtonModule } from 'primeng/button';

/** Shown for any unknown URL inside the app. */
@Component({
  selector: 'app-not-found-page',
  imports: [PageHeader, EmptyState, ButtonModule, RouterLink],
  template: `
    <app-page-header
      title="Page not found"
      subtitle="The page you are looking for does not exist."
    />
    <app-empty-state
      icon="pi-compass"
      title="Nothing here"
      message="Check the address or go back to the dashboard."
    >
      <p-button label="Go to dashboard" icon="pi pi-arrow-left" routerLink="/dashboard" />
    </app-empty-state>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFoundPage {}
