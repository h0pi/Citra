import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { Bot } from '@core/models/bot.models';
import { NotificationService } from '@core/services/notification.service';
import { environment } from '@env/environment';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';

/** The website widget is served by the API (/widget.js). */
const WIDGET_SCRIPT = `${environment.apiUrl.replace(/\/+$/, '')}/widget.js`;

/**
 * Embed tab (owners and admins): the snippet that adds the bot's chat to a website, and a live preview of the
 * widget on a sample page.
 */
@Component({
  selector: 'app-embed-panel',
  imports: [ButtonModule, MessageModule],
  templateUrl: './embed-panel.html',
  styleUrl: './embed-panel.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmbedPanel {
  private readonly notifications = inject(NotificationService);
  private readonly sanitizer = inject(DomSanitizer);

  readonly bot = input.required<Bot>();

  protected readonly snippet = computed(
    () =>
      `<script src="${WIDGET_SCRIPT}" data-bot="${this.bot().tenantSlug}/${this.bot().slug}" defer></script>`,
  );

  /** A sample page with the snippet; our own markup (slugs are letters, digits and dashes). */
  protected readonly previewPage = computed(() =>
    this.sanitizer.bypassSecurityTrustHtml(
      `<!doctype html><body style="font-family: sans-serif"><h1>Your website</h1>${this.snippet()}</body>`,
    ),
  );

  protected copy(): void {
    navigator.clipboard.writeText(this.snippet()).then(
      () => this.notifications.success('Snippet copied'),
      () => this.notifications.error("Couldn't copy the snippet"),
    );
  }
}
