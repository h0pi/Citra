import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CurrentBot } from '@core/bots/current-bot';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';

/** Top-bar dropdown for the bot that Documents, Chat and Evaluation work on. */
@Component({
  selector: 'app-bot-selector',
  imports: [FormsModule, RouterLink, ButtonModule, SelectModule],
  templateUrl: './bot-selector.html',
  styleUrl: './bot-selector.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BotSelector {
  protected readonly current = inject(CurrentBot);

  protected onChange(id: string | null): void {
    if (id) {
      this.current.select(id);
    }
  }
}
