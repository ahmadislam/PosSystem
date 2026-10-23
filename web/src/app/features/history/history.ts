import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PosApi } from '../../core/api/pos-api';
import { apiError } from '../../core/http/api-error';
import { AuditAction, AuditLogDto, StockMovementDto } from '../../core/models';
import { Toast } from '../../core/ui/toast';

type HistoryTab = 'audit' | 'stock';

@Component({
  selector: 'app-history',
  imports: [FormsModule, DatePipe],
  templateUrl: './history.html',
  styleUrl: './history.scss',
})
export class History implements OnInit {
  private readonly api = inject(PosApi);
  private readonly toast = inject(Toast);
  readonly tab = signal<HistoryTab>('audit');
  readonly auditRows = signal<AuditLogDto[]>([]);
  readonly stockRows = signal<StockMovementDto[]>([]);
  readonly auditPage = signal(1);
  readonly auditPages = signal(1);
  readonly stockPage = signal(1);
  readonly stockPages = signal(1);
  readonly loading = signal(false);

  from = '';
  to = '';
  entityName = '';
  action: AuditAction | '' = '';
  productId = '';

  ngOnInit(): void {
    this.load();
  }

  selectTab(tab: HistoryTab): void {
    this.tab.set(tab);
    this.load();
  }

  load(): void {
    this.loading.set(true);
    if (this.tab() === 'audit') {
      this.api.auditHistory({
        from: this.from || undefined,
        to: this.to || undefined,
        entityName: this.entityName || undefined,
        action: this.action,
        page: this.auditPage(),
      }).subscribe({
        next: (result) => {
          this.auditRows.set(result.items);
          this.auditPages.set(Math.max(result.totalPages, 1));
          this.loading.set(false);
        },
        error: (error) => this.fail(error),
      });
      return;
    }

    const parsedProductId = this.productId.trim() ? Number(this.productId) : null;
    if (parsedProductId !== null && (!Number.isInteger(parsedProductId) || parsedProductId < 1)) {
      this.loading.set(false);
      this.toast.show('Product ID must be a positive whole number.', 'warn');
      return;
    }
    this.api.stockMovementHistory({
      productId: parsedProductId,
      from: this.from || undefined,
      to: this.to || undefined,
      page: this.stockPage(),
    }).subscribe({
      next: (result) => {
        this.stockRows.set(result.items);
        this.stockPages.set(Math.max(result.totalPages, 1));
        this.loading.set(false);
      },
      error: (error) => this.fail(error),
    });
  }

  filter(): void {
    this.auditPage.set(1);
    this.stockPage.set(1);
    this.load();
  }

  movePage(delta: number): void {
    if (this.tab() === 'audit') {
      this.auditPage.update((page) => Math.max(1, Math.min(this.auditPages(), page + delta)));
    } else {
      this.stockPage.update((page) => Math.max(1, Math.min(this.stockPages(), page + delta)));
    }
    this.load();
  }

  private fail(error: unknown): void {
    this.loading.set(false);
    this.toast.show(apiError(error), 'err');
  }
}
