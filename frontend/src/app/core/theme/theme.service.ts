import { DOCUMENT } from '@angular/common';
import { Injectable, effect, inject, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark';

const STORAGE_KEY = 'citra-theme';
const DARK_CLASS = 'app-dark';

/**
 * Light/dark mode. The choice is saved in localStorage; on first visit it follows the OS setting.
 * Dark mode = the `app-dark` class on <html>, which both PrimeNG and our own CSS variables react to.
 * (index.html applies the saved class before Angular starts, so there is no light "flash" on load.)
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  readonly mode = signal<ThemeMode>(this.readInitialMode());

  constructor() {
    effect(() => {
      const mode = this.mode();
      this.document.documentElement.classList.toggle(DARK_CLASS, mode === 'dark');
      try {
        localStorage.setItem(STORAGE_KEY, mode);
      } catch {
        // Storage can be unavailable (private mode, blocked cookies): theme still works for this visit.
      }
    });
  }

  toggle(): void {
    this.mode.update((mode) => (mode === 'dark' ? 'light' : 'dark'));
  }

  private readInitialMode(): ThemeMode {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved === 'light' || saved === 'dark') {
        return saved;
      }
    } catch {
      // Ignore and fall back to the OS preference.
    }
    // matchMedia is missing in some environments (unit tests, very old browsers): default to light.
    const view = this.document.defaultView;
    const prefersDark =
      typeof view?.matchMedia === 'function' &&
      view.matchMedia('(prefers-color-scheme: dark)').matches;
    return prefersDark ? 'dark' : 'light';
  }
}
