import { Injectable, inject } from '@angular/core';
import { MessageService } from 'primeng/api';

/**
 * App-wide toast notifications (rendered by the <p-toast> in the root component).
 * Use this instead of PrimeNG's MessageService directly, so every toast looks and behaves the same.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly messages = inject(MessageService);

  success(summary: string, detail?: string): void {
    this.messages.add({ severity: 'success', summary, detail, life: 3000 });
  }

  info(summary: string, detail?: string): void {
    this.messages.add({ severity: 'info', summary, detail, life: 4000 });
  }

  warn(summary: string, detail?: string): void {
    this.messages.add({ severity: 'warn', summary, detail, life: 5000 });
  }

  /** Errors stay a bit longer so there is time to read (and copy a reference). */
  error(summary: string, detail?: string): void {
    this.messages.add({ severity: 'error', summary, detail, life: 7000 });
  }
}
