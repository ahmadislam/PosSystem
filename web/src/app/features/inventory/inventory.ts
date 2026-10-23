import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PosApi } from '../../core/api/pos-api';
import { apiError } from '../../core/http/api-error';
import { CategoryDto, ProductDto, ProductImageDto, ProductRequest, SupplierDto, money } from '../../core/models';
import { Toast } from '../../core/ui/toast';

const emptyForm = (): ProductRequest => ({
  sku: '',
  name: '',
  description: '',
  categoryId: 0,
  defaultSupplierId: null,
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
  readonly api = inject(PosApi);
  private readonly toast = inject(Toast);

  readonly money = money;
  search = '';
  lowStockOnly = false;
  readonly categories = signal<CategoryDto[]>([]);
  readonly suppliers = signal<SupplierDto[]>([]);
  readonly products = signal<ProductDto[]>([]);
  readonly imageProduct = signal<ProductDto | null>(null);
  readonly productImages = signal<ProductImageDto[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly totalPages = signal(1);
  readonly downloadingReport = signal(false);
  readonly editingId = signal<number | null>(null);
  readonly formOpen = signal(false);
  form: ProductRequest = emptyForm();
  newCategory = '';
  adjustId: number | null = null;
  adjustQty = 0;
  adjustNote = '';

  ngOnInit(): void {
    this.api.categories().subscribe({ next: (c) => this.categories.set(c) });
    this.api.suppliers().subscribe({
      next: (s) => this.suppliers.set(s.filter((supplier) => supplier.isActive)),
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
    this.load();
  }

  load(): void {
    this.api.products({
      search: this.search.trim() || undefined,
      lowStockOnly: this.lowStockOnly,
      page: this.page(),
      pageSize: 50,
    }).subscribe({
      next: (p) => {
        const pages = Math.max(p.totalPages, 1);
        if (this.page() > pages) {
          this.page.set(pages);
          this.load();
          return;
        }
        this.products.set(p.items);
        this.total.set(p.totalCount);
        this.totalPages.set(pages);
      },
      error: (e) => this.toast.show(apiError(e), 'err'),
    });
  }

  filter(): void {
    this.page.set(1);
    this.load();
  }

  movePage(delta: number): void {
    this.page.update((page) => Math.max(1, Math.min(this.totalPages(), page + delta)));
    this.load();
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
      defaultSupplierId: p.defaultSupplierId,
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

  uploadImage(product: ProductDto, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > 4 * 1024 * 1024) {
      this.toast.show('Choose a PNG, JPEG, or WebP image no larger than 4 MB.', 'warn');
      return;
    }
    this.api.uploadProductImage(product.id, file).subscribe({
      next: () => {
        this.toast.show(`Image saved for ${product.sku}.`);
        this.load();
        if (this.imageProduct()?.id === product.id) this.loadProductImages(product.id);
      },
      error: (error) => this.toast.show(apiError(error), 'err'),
    });
  }

  manageImages(product: ProductDto): void {
    this.imageProduct.set(product);
    this.loadProductImages(product.id);
  }

  selectPrimaryImage(image: ProductImageDto): void {
    const product = this.imageProduct();
    if (!product || image.isPrimary) return;
    this.api.setPrimaryProductImage(product.id, image.id).subscribe({
      next: () => {
        this.toast.show('Primary product image updated.');
        this.loadProductImages(product.id);
        this.load();
      },
      error: (error) => this.toast.show(apiError(error), 'err'),
    });
  }

  removeImage(image: ProductImageDto): void {
    const product = this.imageProduct();
    if (!product || !confirm(`Delete this image for ${product.sku}?`)) return;
    this.api.deleteProductImage(product.id, image.id).subscribe({
      next: () => {
        this.toast.show('Product image deleted.');
        this.loadProductImages(product.id);
        this.load();
      },
      error: (error) => this.toast.show(apiError(error), 'err'),
    });
  }

  closeImageManager(): void {
    this.imageProduct.set(null);
    this.productImages.set([]);
  }

  downloadReport(): void {
    this.downloadingReport.set(true);
    this.api.inventoryReport().subscribe({
      next: (file) => {
        const url = URL.createObjectURL(file);
        const link = document.createElement('a');
        link.href = url;
        link.download = `inventory-report-${new Date().toISOString().slice(0, 10)}.xlsx`;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        this.downloadingReport.set(false);
      },
      error: (error) => {
        this.downloadingReport.set(false);
        this.toast.show(apiError(error), 'err');
      },
    });
  }

  private loadProductImages(productId: number): void {
    this.api.productImages(productId).subscribe({
      next: (images) => this.productImages.set(images),
      error: (error) => this.toast.show(apiError(error), 'err'),
    });
  }
}
