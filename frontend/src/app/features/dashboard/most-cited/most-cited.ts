import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { CitedDocument } from '@core/models/analytics.models';

/** The documents the period's answers cited most: what the bots actually rely on. */
@Component({
  selector: 'app-most-cited',
  templateUrl: './most-cited.html',
  styleUrl: './most-cited.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MostCited {
  readonly documents = input.required<CitedDocument[]>();
}
