import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';

/** Placeholder — replaced by the real page later. */
@Component({
  selector: 'app-documents-page',
  imports: [PageHeader, EmptyState],
  template: `
    <app-page-header title="Documents" subtitle="Files and web pages your bot answers from." />
    <app-empty-state
      icon="pi-file"
      title="No documents yet"
      message="Upload PDFs, Word files or add a web page URL."
    />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocumentsPage {}
