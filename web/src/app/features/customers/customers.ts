import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PosApi } from '../../core/api/pos-api';
import { apiError } from '../../core/http/api-error';
import { CustomerDto, CustomerRequest, LoyaltyTransactionDto } from '../../core/models';
import { Toast } from '../../core/ui/toast';

const emptyForm = (): CustomerRequest => ({ name: '', email: '', phone: '', address: '', isActive: true });

@Component({
  selector: 'app-customers',
  imports: [FormsModule, DatePipe],
  templateUrl: './customers.html',
  styleUrl: './customers.scss',
})
export class Customers implements OnInit {
  private readonly api = inject(PosApi);
  private readonly toast = inject(Toast);
  readonly customers = signal<CustomerDto[]>([]);
  readonly editingId = signal<number | null>(null);
  readonly historyCustomer = signal<CustomerDto | null>(null);
  readonly loyaltyHistory = signal<LoyaltyTransactionDto[]>([]);
  search = '';
  formOpen = false;
  form: CustomerRequest = emptyForm();

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.customers(this.search.trim() || undefined).subscribe({
      next: (customers) => this.customers.set(customers),
      error: (error) => this.toast.show(apiError(error), 'err'),
    });
  }

  create(): void {
    this.editingId.set(null);
    this.form = emptyForm();
    this.formOpen = true;
  }

  edit(customer: CustomerDto): void {
    this.editingId.set(customer.id);
    this.form = {
      name: customer.name, email: customer.email, phone: customer.phone,
      address: customer.address, isActive: customer.isActive,
    };
    this.formOpen = true;
  }

  save(): void {
    const id = this.editingId();
    const request = { ...this.form, name: this.form.name.trim() };
    const operation = id ? this.api.updateCustomer(id, request) : this.api.createCustomer(request);
    operation.subscribe({
      next: () => {
        this.formOpen = false;
        this.toast.show(id ? 'Customer updated.' : 'Customer created.');
        this.load();
      },
      error: (error) => this.toast.show(apiError(error), 'err'),
    });
  }

  remove(customer: CustomerDto): void {
    if (!confirm(`Soft-delete ${customer.name}? Their sales and loyalty history will remain.`)) return;
    this.api.deleteCustomer(customer.id).subscribe({
      next: () => { this.toast.show('Customer archived.'); this.load(); },
      error: (error) => this.toast.show(apiError(error), 'err'),
    });
  }

  showLoyalty(customer: CustomerDto): void {
    this.historyCustomer.set(customer);
    this.api.customerLoyalty(customer.id).subscribe({
      next: (history) => this.loyaltyHistory.set(history),
      error: (error) => this.toast.show(apiError(error), 'err'),
    });
  }

  closeHistory(): void {
    this.historyCustomer.set(null);
    this.loyaltyHistory.set([]);
  }
}
