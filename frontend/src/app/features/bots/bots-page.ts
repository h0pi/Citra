import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';

/** Placeholder — replaced by the real page later. */
@Component({
  selector: 'app-bots-page',
  imports: [PageHeader, EmptyState],
  template: `
    <app-page-header
      title="Bots"
      subtitle="Each bot has its own documents, instructions and website widget."
    />
    <app-empty-state
      icon="pi-microchip-ai"
      title="No bots yet"
      message="Create your first bot to start adding documents."
    />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BotsPage {}
