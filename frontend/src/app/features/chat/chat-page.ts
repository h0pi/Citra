import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { PageHeader } from '@shared/components/page-header/page-header';

/** Placeholder — replaced by the real page later. */
@Component({
  selector: 'app-chat-page',
  imports: [PageHeader, EmptyState],
  template: `
    <app-page-header title="Chat" subtitle="Ask questions and get answers with exact citations." />
    <app-empty-state
      icon="pi-comments"
      title="No conversations yet"
      message="Start a chat once your bot has documents."
    />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChatPage {}
