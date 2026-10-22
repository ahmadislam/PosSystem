import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PosApi } from '../../core/api/pos-api';
import { apiError } from '../../core/http/api-error';
import { ShiftDto, money } from '../../core/models';
import { Toast } from '../../core/ui/toast';

@Component({
  selector: 'app-shifts',
  imports: [FormsModule, DatePipe],
  templateUrl: './shifts.html',
  styleUrl: './shifts.scss',
})
export class Shifts implements OnInit {
  private readonly api = inject(PosApi);
  private readonly toast = inject(Toast);
  readonly money = money;
  readonly shift = signal<ShiftDto | null>(null);
  openingCash = 0;
  closingCash = 0;

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.api.currentShift().subscribe({
      next: (s) => {
        this.shift.set(s);
        if (s?.status === 'PendingClose') this.closingCash = s.expectedCash;
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  open(): void {
    this.api.openShift(this.openingCash).subscribe({
      next: (s) => {
        this.shift.set(s);
        this.toast.show('Shift opened.');
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  end(): void {
    this.api.endShift().subscribe({
      next: (s) => {
        this.shift.set(s);
        this.closingCash = s.expectedCash;
        this.toast.show(`Expected cash ${money(s.expectedCash)}. Count the drawer, then close.`);
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  close(): void {
    this.api.closeShift(this.closingCash).subscribe({
      next: (s) => {
        this.shift.set(s);
        this.toast.show(`Shift closed. Variance ${money(s.cashVariance ?? 0)}.`);
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }
}
