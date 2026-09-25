import { ChangeDetectionStrategy, Component, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

import { NAV_SECTIONS } from './nav-items';

/** Left navigation: Citra brand on top, grouped navigation links below. */
@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Sidebar {
  protected readonly sections = NAV_SECTIONS;

  /** Emitted when a link is clicked, so the shell can close the mobile drawer. */
  readonly navigate = output<void>();
}
