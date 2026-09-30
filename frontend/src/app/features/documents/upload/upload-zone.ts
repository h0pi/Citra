import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';

import { ACCEPT_ATTRIBUTE, MAX_UPLOAD_MB } from './upload-rules';

/**
 * Where files are dropped (or picked with a button) to upload them. Several files at once.
 * Emits the chosen files; checking and uploading them is up to the page.
 */
@Component({
  selector: 'app-upload-zone',
  imports: [ButtonModule],
  template: `
    <div
      class="upload-zone"
      [class.dragging]="dragging()"
      [class.compact]="compact()"
      (dragenter)="onDragOver($event)"
      (dragover)="onDragOver($event)"
      (dragleave)="onDragLeave($event)"
      (drop)="onDrop($event)"
    >
      <i class="pi pi-cloud-upload zone-icon" aria-hidden="true"></i>
      <div class="zone-text">
        <strong>Drop files here</strong>
        <span class="text-muted">PDF, Word, text or Markdown · up to {{ maxMb }} MB each</span>
      </div>
      <p-button
        label="Choose files"
        icon="pi pi-folder-open"
        [outlined]="true"
        (onClick)="picker.click()"
      />
      <input
        #picker
        type="file"
        multiple
        hidden
        [accept]="accept"
        aria-label="Choose files to upload"
        (change)="onPicked($event)"
      />
    </div>
  `,
  styles: `
    .upload-zone {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--citra-space-3);
      padding: var(--citra-space-6) var(--citra-space-5);
      text-align: center;
      border: 2px dashed var(--citra-border);
      border-radius: var(--citra-radius-lg);
      background: var(--citra-surface);
      transition:
        border-color 0.15s,
        background-color 0.15s;
    }
    .upload-zone.compact {
      flex-direction: row;
      flex-wrap: wrap;
      justify-content: center;
      padding: var(--citra-space-4) var(--citra-space-5);
      text-align: left;
    }
    .upload-zone.dragging {
      border-color: var(--citra-primary);
      background: var(--citra-primary-soft);
    }
    .zone-icon {
      font-size: 1.75rem;
      color: var(--citra-primary);
    }
    .zone-text {
      display: flex;
      flex-direction: column;
      gap: var(--citra-space-1);
    }
    .compact .zone-text {
      flex: 1;
      min-width: 12rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UploadZone {
  /** A slim bar above the table (true) or a big area in the empty page (false). */
  readonly compact = input(false);

  /** The files dropped or picked. */
  readonly files = output<File[]>();

  protected readonly dragging = signal(false);
  protected readonly accept = ACCEPT_ATTRIBUTE;
  protected readonly maxMb = MAX_UPLOAD_MB;

  protected onDragOver(event: DragEvent): void {
    if (!this.hasFiles(event)) {
      return;
    }
    event.preventDefault(); // allows dropping here instead of the browser opening the file
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'copy';
    }
    this.dragging.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    // Moving over a child element also fires dragleave: only react when the pointer really left the zone.
    const zone = event.currentTarget as HTMLElement;
    if (!(event.relatedTarget instanceof Node) || !zone.contains(event.relatedTarget)) {
      this.dragging.set(false);
    }
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const files = [...(event.dataTransfer?.files ?? [])];
    if (files.length > 0) {
      this.files.emit(files);
    }
  }

  protected onPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = [...(input.files ?? [])];
    input.value = ''; // picking the same file again later must fire "change" again
    if (files.length > 0) {
      this.files.emit(files);
    }
  }

  private hasFiles(event: DragEvent): boolean {
    return [...(event.dataTransfer?.types ?? [])].includes('Files');
  }
}
