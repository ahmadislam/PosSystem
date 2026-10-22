import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PosApi } from '../../core/api/pos-api';
import { apiError } from '../../core/http/api-error';
import { CategoryDto, ProductDto, ProductRequest, money } from '../../core/models';
import { Toast } from '../../core/ui/toast';

const emptyForm = (): ProductRequest => ({
  sku: '',
  name: '',
  description: '',
  categoryId: 0,
  costPrice: 0,
  salePrice: 0,
  taxRate: 0,
  stockQuantity: 0,
  minStockThreshold: 0,
  isActive: true,
});

@Component({
  selector: 'app-inventory',
  imports: [FormsModule],
  templateUrl: './inventory.html',
  styleUrl: './inventory.scss',
})
export class Inventory implements OnInit {
  private readonly api = inject(PosApi);
  private readonly toast = inject(Toast);

  readonly money = money;
  search = '';
  lowStockOnly = false;
  readonly categories = signal<CategoryDto[]>([]);
  readonly products = signal<ProductDto[]>([]);
  readonly total = signal(0);
  readonly editingId = signal<number | null>(null);
  readonly formOpen = signal(false);
  form: ProductRequest = emptyForm();
  newCategory = '';
  adjustId: number | null = null;
  adjustQty = 0;
  adjustNote = '';

  ngOnInit(): void {
    this.api.categories().subscribe({ next: (c) => this.categories.set(c) });
    this.load();
  }

  load(): void {
    this.api.products({ search: this.search.trim() || undefined, lowStockOnly: this.lowStockOnly, pageSize: 100 }).subscribe({
      next: (p) => {
        this.products.set(p.items);
        this.total.set(p.totalCount);
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  addCategory(): void {
    const name = this.newCategory.trim();
    if (!name) return;
    this.api.createCategory(name).subscribe({
      next: (c) => {
        this.categories.update((list) => [...list, c]);
        this.newCategory = '';
        this.toast.show(`Category ${c.name} added.`);
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  startCreate(): void {
    this.editingId.set(null);
    this.form = emptyForm();
    this.form.categoryId = this.categories()[0]?.id ?? 0;
    this.formOpen.set(true);
  }

  startEdit(p: ProductDto): void {
    this.editingId.set(p.id);
    this.form = {
      sku: p.sku,
      name: p.name,
      description: p.description,
      categoryId: p.categoryId,
      costPrice: p.costPrice,
      salePrice: p.salePrice,
      taxRate: p.taxRate,
      stockQuantity: p.stockQuantity,
      minStockThreshold: p.minStockThreshold,
      isActive: p.isActive,
    };
    this.formOpen.set(true);
  }

  save(): void {
    const id = this.editingId();
    const req$ = id
      ? this.api.updateProduct(id, this.form)
      : this.api.createProduct(this.form);
    req$.subscribe({
      next: () => {
        this.formOpen.set(false);
        this.toast.show(id ? 'Product updated.' : 'Product created.');
        this.load();
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  startAdjust(p: ProductDto): void {
    this.adjustId = p.id;
    this.adjustQty = 0;
    this.adjustNote = '';
  }

  applyAdjust(): void {
    if (!this.adjustId || this.adjustQty === 0 || !this.adjustNote.trim()) {
      this.toast.show('Quantity and note are required.', 'warn');
      return;
    }
    this.api.adjustStock(this.adjustId, this.adjustQty, this.adjustNote.trim()).subscribe({
      next: () => {
        this.adjustId = null;
        this.toast.show('Stock updated.');
        this.load();
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  remove(p: ProductDto): void {
    if (!confirm(`Soft-delete ${p.sku}?`)) return;
    this.api.deleteProduct(p.id).subscribe({
      next: () => {
        this.toast.show('Product deleted.');
        this.load();
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }
}
