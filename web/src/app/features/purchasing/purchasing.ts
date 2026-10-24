import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PosApi } from '../../core/api/pos-api';
import { apiError } from '../../core/http/api-error';
import {
  ProductDto,
  PurchaseOrderDto,
  PurchaseOrderLineRequest,
  PurchaseOrderStatus,
  SupplierDto,
  SupplierRequest,
  money,
} from '../../core/models';
import { Toast } from '../../core/ui/toast';

const emptySupplier = (): SupplierRequest => ({
  name: '',
  contactName: '',
  email: '',
  phone: '',
  address: '',
  notes: '',
  isActive: true,
});

const emptyLine = (): PurchaseOrderLineRequest => ({ productId: 0, quantity: 1, unitCost: 0 });

@Component({
  selector: 'app-purchasing',
  imports: [FormsModule, DatePipe],
  templateUrl: './purchasing.html',
  styleUrl: './purchasing.scss',
})
export class Purchasing implements OnInit {
  private readonly api = inject(PosApi);
  private readonly toast = inject(Toast);

  readonly money = money;
  readonly suppliers = signal<SupplierDto[]>([]);
  readonly products = signal<ProductDto[]>([]);
  readonly orders = signal<PurchaseOrderDto[]>([]);
  readonly loading = signal(false);
  readonly editingSupplierId = signal<number | null>(null);
  readonly editingOrderId = signal<number | null>(null);
  supplierForm = emptySupplier();
  supplierFormOpen = false;
  orderFormOpen = false;
  supplierId = 0;
  lines: PurchaseOrderLineRequest[] = [emptyLine()];
  statusFilter: PurchaseOrderStatus | '' = '';

  ngOnInit(): void {
    this.loadSuppliers();
    this.api.products({ page: 1, pageSize: 100 }).subscribe({
      next: (result) => this.products.set(result.items.filter((product) => product.isActive)),
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
    this.loadOrders();
  }

  loadSuppliers(): void {
    this.api.suppliers().subscribe({
      next: (list) => this.suppliers.set(list),
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  loadOrders(): void {
    this.loading.set(true);
    this.api.purchaseOrders(this.statusFilter || undefined).subscribe({
      next: (result) => {
        this.orders.set(result.items);
        this.loading.set(false);
      },
      error: (e) => {
        this.loading.set(false);
        this.toast.show(apiError(e), 'err');
      },
    });
  }

  startSupplier(): void {
    this.editingSupplierId.set(null);
    this.supplierForm = emptySupplier();
    this.supplierFormOpen = true;
  }

  editSupplier(supplier: SupplierDto): void {
    this.editingSupplierId.set(supplier.id);
    this.supplierForm = {
      name: supplier.name,
      contactName: supplier.contactName,
      email: supplier.email,
      phone: supplier.phone,
      address: supplier.address,
      notes: supplier.notes,
      isActive: supplier.isActive,
    };
    this.supplierFormOpen = true;
  }

  saveSupplier(): void {
    const id = this.editingSupplierId();
    const request = id
      ? this.api.updateSupplier(id, this.supplierForm)
      : this.api.createSupplier(this.supplierForm);
    request.subscribe({
      next: () => {
        this.supplierFormOpen = false;
        this.loadSuppliers();
        this.toast.show(id ? 'Supplier updated.' : 'Supplier created.');
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  removeSupplier(supplier: SupplierDto): void {
    if (!confirm(`Soft-delete ${supplier.name}? Existing purchase history will remain available.`)) return;
    this.api.deleteSupplier(supplier.id).subscribe({
      next: () => {
        this.loadSuppliers();
        this.toast.show('Supplier deleted.');
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  startOrder(): void {
    this.editingOrderId.set(null);
    this.supplierId = this.suppliers().find((supplier) => supplier.isActive)?.id ?? 0;
    this.lines = [emptyLine()];
    this.orderFormOpen = true;
  }

  editOrder(order: PurchaseOrderDto): void {
    this.editingOrderId.set(order.id);
    this.supplierId = order.supplierId;
    this.lines = order.items.map(({ productId, quantity, unitCost }) => ({ productId, quantity, unitCost }));
    this.orderFormOpen = true;
  }

  addLine(): void {
    this.lines = [...this.lines, emptyLine()];
  }

  removeLine(index: number): void {
    if (this.lines.length > 1) this.lines = this.lines.filter((_, lineIndex) => lineIndex !== index);
  }

  saveOrder(): void {
    const id = this.editingOrderId();
    const items = this.lines.map((line) => ({
      productId: Number(line.productId),
      quantity: Number(line.quantity),
      unitCost: Number(line.unitCost),
    }));
    if (!this.supplierId || items.some((item) => !item.productId || item.quantity < 1 || item.unitCost < 0)) {
      this.toast.show('Choose a supplier and valid products, quantities, and unit costs.', 'warn');
      return;
    }
    const request = id
      ? this.api.updatePurchaseOrder(id, this.supplierId, items)
      : this.api.createPurchaseOrder(this.supplierId, items);
    request.subscribe({
      next: () => {
        this.orderFormOpen = false;
        this.loadOrders();
        this.toast.show(id ? 'Draft purchase order updated.' : 'Draft purchase order created.');
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  transition(order: PurchaseOrderDto, action: 'order' | 'receive' | 'cancel'): void {
    const prompts = {
      order: `Mark purchase order #${order.id} as ordered?`,
      receive: `Receive all ${order.items.length} lines on purchase order #${order.id} into stock?`,
      cancel: `Cancel purchase order #${order.id}?`,
    };
    if (!confirm(prompts[action])) return;
    const request = action === 'order'
      ? this.api.orderPurchaseOrder(order.id)
      : action === 'receive'
        ? this.api.receivePurchaseOrder(order.id)
        : this.api.cancelPurchaseOrder(order.id);
    request.subscribe({
      next: () => {
        this.loadOrders();
        this.toast.show(action === 'receive' ? 'Purchase received and stock updated.' : `Purchase order ${action === 'order' ? 'ordered' : 'cancelled'}.`);
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  productName(productId: number): string {
    return this.products().find((product) => product.id === Number(productId))?.name ?? '';
  }

  hasActiveSuppliers(): boolean {
    return this.suppliers().some((supplier) => supplier.isActive);
  }

  activeSupplierCount(): number {
    return this.suppliers().filter((supplier) => supplier.isActive).length;
  }
}
