import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';

/** Placeholder — replaced by the real page later. */
@Component({
  selector: 'app-settings-page',
  imports: [PageHeader, EmptyState],
  template: `
    <app-page-header title="Settings" subtitle="Your account and workspace." />
    <app-empty-state
      icon="pi-cog"
      title="Nothing to configure yet"
      message="Account settings arrive with login."
    />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsPage {}
