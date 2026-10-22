import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PosApi } from '../../core/api/pos-api';
import { apiError } from '../../core/http/api-error';
import { InvoiceService } from '../../core/invoice/invoice.service';
import { SaleDto, money } from '../../core/models';
import { Toast } from '../../core/ui/toast';

@Component({
  selector: 'app-sales',
  imports: [FormsModule, DatePipe],
  templateUrl: './sales.html',
  styleUrl: './sales.scss',
})
export class Sales implements OnInit {
  private readonly api = inject(PosApi);
  private readonly toast = inject(Toast);
  private readonly invoice = inject(InvoiceService);
  readonly money = money;
  from = '';
  to = '';
  readonly sales = signal<SaleDto[]>([]);
  readonly total = signal(0);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.api.listSales(this.from || undefined, this.to || undefined, 1, 50).subscribe({
      next: (p) => {
        this.sales.set(p.items);
        this.total.set(p.totalCount);
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  print(s: SaleDto): void {
    this.invoice.print(s);
  }
}
