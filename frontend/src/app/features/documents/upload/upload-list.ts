import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { ProgressBarModule } from 'primeng/progressbar';

import { UploadItem } from './document-uploads';

/** The files being uploaded: a progress bar each, then "Uploaded" or the reason it failed. */
@Component({
  selector: 'app-upload-list',
  imports: [ButtonModule, ProgressBarModule],
  template: `
    @if (items().length > 0) {
      <ul class="upload-list" aria-label="Uploads">
        @for (item of items(); track item.id) {
          <li class="upload-item" [attr.data-state]="item.state">
            <div class="upload-head">
              <i [class]="'pi ' + icon(item)" aria-hidden="true"></i>
              <span class="upload-name" [title]="item.fileName">{{ item.fileName }}</span>
              <span class="upload-state text-muted" aria-live="polite">{{ stateText(item) }}</span>
              <p-button
                icon="pi pi-times"
                [text]="true"
                [rounded]="true"
                severity="secondary"
                size="small"
                [ariaLabel]="
                  (item.state === 'uploading' || item.state === 'waiting'
                    ? 'Cancel '
                    : 'Dismiss ') + item.fileName
                "
                (onClick)="dismiss.emit(item.id)"
              />
            </div>
            @if (item.state === 'uploading' || item.state === 'waiting') {
              <p-progressbar
                [value]="item.progress"
                [showValue]="false"
                [attr.aria-label]="'Uploading ' + item.fileName"
              />
            }
            @if (item.error) {
              <small class="field-error">{{ item.error }}</small>
            }
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .upload-list {
      display: flex;
      flex-direction: column;
      gap: var(--citra-space-2);
      margin: 0 0 var(--citra-space-4);
      padding: 0;
      list-style: none;
    }
    .upload-item {
      display: flex;
      flex-direction: column;
      gap: var(--citra-space-2);
      padding: var(--citra-space-3) var(--citra-space-4);
      background: var(--citra-surface);
      border: 1px solid var(--citra-border);
      border-radius: var(--citra-radius-md);
    }
    .upload-item[data-state='failed'] {
      border-color: var(--p-red-300);
    }
    .upload-head {
      display: flex;
      align-items: center;
      gap: var(--citra-space-3);
      min-width: 0;
    }
    .upload-name {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-weight: 500;
    }
    .upload-state {
      flex-shrink: 0;
      font-size: var(--citra-text-sm);
    }
    [data-state='done'] .pi {
      color: var(--p-green-500);
    }
    [data-state='failed'] .pi {
      color: var(--p-red-500);
    }
    p-progressbar {
      --p-progressbar-height: 0.375rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UploadList {
  readonly items = input.required<UploadItem[]>();

  /** Cancel (while uploading or waiting) or remove from the list. */
  readonly dismiss = output<number>();

  protected stateText(item: UploadItem): string {
    switch (item.state) {
      case 'waiting':
        return 'Waiting…';
      case 'uploading':
        return `${item.progress}%`;
      case 'done':
        return 'Uploaded';
      case 'failed':
        return 'Failed';
    }
  }

  protected icon(item: UploadItem): string {
    switch (item.state) {
      case 'done':
        return 'pi-check-circle';
      case 'failed':
        return 'pi-exclamation-circle';
      default:
        return 'pi-file';
    }
  }
}
