import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';

/** Placeholder — replaced by the real page later. */
@Component({
  selector: 'app-dashboard-page',
  imports: [PageHeader, EmptyState],
  template: `
    <app-page-header title="Dashboard" subtitle="Usage, answer quality and costs at a glance." />
    <app-empty-state
      icon="pi-chart-bar"
      title="No activity yet"
      message="Stats appear here once your bots start answering questions."
    />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardPage {}
