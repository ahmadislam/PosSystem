import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CategoryDto,
  DashboardSummaryDto,
  PagedResult,
  PaymentMethod,
  ProductDto,
  ProductRequest,
  SaleDto,
  ShiftDto,
} from '../models';

@Injectable({ providedIn: 'root' })
export class PosApi {
  private readonly http = inject(HttpClient);
  private readonly root = environment.apiUrl;

  categories(): Observable<CategoryDto[]> {
    return this.http.get<CategoryDto[]>(`${this.root}/api/categories`);
  }

  createCategory(name: string, description?: string): Observable<CategoryDto> {
    return this.http.post<CategoryDto>(`${this.root}/api/categories`, { name, description });
  }

  products(opts: {
    search?: string;
    categoryId?: number | null;
    lowStockOnly?: boolean;
    page?: number;
    pageSize?: number;
  }): Observable<PagedResult<ProductDto>> {
    let params = new HttpParams()
      .set('page', String(opts.page ?? 1))
      .set('pageSize', String(opts.pageSize ?? 20));
    if (opts.search) params = params.set('search', opts.search);
    if (opts.categoryId) params = params.set('categoryId', String(opts.categoryId));
    if (opts.lowStockOnly) params = params.set('lowStockOnly', 'true');
    return this.http.get<PagedResult<ProductDto>>(`${this.root}/api/products`, { params });
  }

  createProduct(body: ProductRequest): Observable<ProductDto> {
    return this.http.post<ProductDto>(`${this.root}/api/products`, body);
  }

  updateProduct(id: number, body: ProductRequest): Observable<ProductDto> {
    return this.http.put<ProductDto>(`${this.root}/api/products/${id}`, body);
  }

  adjustStock(id: number, quantityChange: number, note: string): Observable<ProductDto> {
    return this.http.post<ProductDto>(`${this.root}/api/products/${id}/adjust-stock`, {
      quantityChange,
      note,
    });
  }

  deleteProduct(id: number): Observable<void> {
    return this.http.delete<void>(`${this.root}/api/products/${id}`);
  }

  currentShift(): Observable<ShiftDto | null> {
    return this.http
      .get<ShiftDto>(`${this.root}/api/shifts/current`, { observe: 'response' })
      .pipe(map((res) => (res.status === 204 || !res.body ? null : res.body)));
  }

  openShift(openingCash: number): Observable<ShiftDto> {
    return this.http.post<ShiftDto>(`${this.root}/api/shifts/open`, { openingCash });
  }

  endShift(): Observable<ShiftDto> {
    return this.http.post<ShiftDto>(`${this.root}/api/shifts/end`, {});
  }

  closeShift(closingCash: number): Observable<ShiftDto> {
    return this.http.post<ShiftDto>(`${this.root}/api/shifts/close`, { closingCash });
  }

  createSale(paymentMethod: PaymentMethod, items: { productId: number; quantity: number; discountAmount: number }[]): Observable<SaleDto> {
    return this.http.post<SaleDto>(`${this.root}/api/sales`, { paymentMethod, items });
  }

  getSale(id: number): Observable<SaleDto> {
    return this.http.get<SaleDto>(`${this.root}/api/sales/${id}`);
  }

  listSales(from?: string, to?: string, page = 1, pageSize = 20): Observable<PagedResult<SaleDto>> {
    let params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<PagedResult<SaleDto>>(`${this.root}/api/sales`, { params });
  }

  dashboardSummary(from: string, to: string): Observable<DashboardSummaryDto> {
    const params = new HttpParams().set('from', from).set('to', to);
    return this.http.get<DashboardSummaryDto>(`${this.root}/api/dashboard`, { params });
  }
}
