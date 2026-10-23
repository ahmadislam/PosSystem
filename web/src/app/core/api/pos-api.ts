import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CategoryDto,
  CouponDto,
  CustomerDto,
  CustomerRequest,
  AuditAction,
  AuditLogDto,
  BackgroundJobStatusDto,
  DashboardSummaryDto,
  EmailSettingsDto,
  LowStockAlertDto,
  LoyaltyRulesDto,
  LoyaltyTransactionDto,
  PagedResult,
  PaymentMethod,
  ProductDto,
  ProductImageDto,
  ProductRequest,
  PurchaseOrderDto,
  PurchaseOrderLineRequest,
  PurchaseOrderStatus,
  SaleDto,
  SaleReturnDto,
  ShiftDto,
  StaffDto,
  SupplierDto,
  SupplierRequest,
  StockMovementDto,
  UserRole,
} from '../models';

@Injectable({ providedIn: 'root' })
export class PosApi {
  private readonly http = inject(HttpClient);
  private readonly root = environment.apiUrl;

  assetUrl(path: string): string {
    if (!path.startsWith('/api/products/')) throw new Error('Invalid product image URL.');
    return new URL(path, this.root || window.location.origin).toString();
  }

  categories(): Observable<CategoryDto[]> {
    return this.http.get<CategoryDto[]>(`${this.root}/api/categories`);
  }

  customers(search?: string): Observable<CustomerDto[]> {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    return this.http.get<CustomerDto[]>(`${this.root}/api/customers`, { params });
  }

  createCustomer(body: CustomerRequest): Observable<CustomerDto> {
    return this.http.post<CustomerDto>(`${this.root}/api/customers`, body);
  }

  updateCustomer(id: number, body: CustomerRequest): Observable<CustomerDto> {
    return this.http.put<CustomerDto>(`${this.root}/api/customers/${id}`, body);
  }

  deleteCustomer(id: number): Observable<void> {
    return this.http.delete<void>(`${this.root}/api/customers/${id}`);
  }

  customerLoyalty(id: number): Observable<LoyaltyTransactionDto[]> {
    return this.http.get<LoyaltyTransactionDto[]>(`${this.root}/api/customers/${id}/loyalty`);
  }

  coupons(): Observable<CouponDto[]> {
    return this.http.get<CouponDto[]>(`${this.root}/api/coupons/available`);
  }

  couponManagementList(): Observable<CouponDto[]> {
    return this.http.get<CouponDto[]>(`${this.root}/api/coupons`);
  }

  createCoupon(coupon: Omit<CouponDto, 'id' | 'redemptionCount'>): Observable<CouponDto> {
    return this.http.post<CouponDto>(`${this.root}/api/coupons`, coupon);
  }

  updateCoupon(id: number, coupon: Omit<CouponDto, 'id' | 'redemptionCount'>): Observable<CouponDto> {
    return this.http.put<CouponDto>(`${this.root}/api/coupons/${id}`, coupon);
  }

  loyaltyRules(): Observable<LoyaltyRulesDto> {
    return this.http.get<LoyaltyRulesDto>(`${this.root}/api/coupons/loyalty-rules`);
  }

  saveLoyaltyRules(rules: LoyaltyRulesDto): Observable<LoyaltyRulesDto> {
    return this.http.put<LoyaltyRulesDto>(`${this.root}/api/coupons/loyalty-rules`, rules);
  }

  uploadProductImage(productId: number, file: File): Observable<unknown> {
    const body = new FormData();
    body.set('file', file);
    return this.http.post(`${this.root}/api/products/${productId}/images`, body);
  }

  productImages(productId: number): Observable<ProductImageDto[]> {
    return this.http.get<ProductImageDto[]>(`${this.root}/api/products/${productId}/images`);
  }

  setPrimaryProductImage(productId: number, imageId: number): Observable<void> {
    return this.http.put<void>(`${this.root}/api/products/${productId}/images/${imageId}/primary`, {});
  }

  deleteProductImage(productId: number, imageId: number): Observable<void> {
    return this.http.delete<void>(`${this.root}/api/products/${productId}/images/${imageId}`);
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

  suppliers(): Observable<SupplierDto[]> {
    return this.http.get<SupplierDto[]>(`${this.root}/api/suppliers`);
  }

  createSupplier(body: SupplierRequest): Observable<SupplierDto> {
    return this.http.post<SupplierDto>(`${this.root}/api/suppliers`, body);
  }

  updateSupplier(id: number, body: SupplierRequest): Observable<SupplierDto> {
    return this.http.put<SupplierDto>(`${this.root}/api/suppliers/${id}`, body);
  }

  deleteSupplier(id: number): Observable<void> {
    return this.http.delete<void>(`${this.root}/api/suppliers/${id}`);
  }

  purchaseOrders(status?: PurchaseOrderStatus): Observable<PagedResult<PurchaseOrderDto>> {
    let params = new HttpParams().set('page', '1').set('pageSize', '100');
    if (status) params = params.set('status', status);
    return this.http.get<PagedResult<PurchaseOrderDto>>(`${this.root}/api/purchase-orders`, { params });
  }

  createPurchaseOrder(supplierId: number, items: PurchaseOrderLineRequest[]): Observable<PurchaseOrderDto> {
    return this.http.post<PurchaseOrderDto>(`${this.root}/api/purchase-orders`, { supplierId, items });
  }

  updatePurchaseOrder(id: number, supplierId: number, items: PurchaseOrderLineRequest[]): Observable<PurchaseOrderDto> {
    return this.http.put<PurchaseOrderDto>(`${this.root}/api/purchase-orders/${id}`, { supplierId, items });
  }

  orderPurchaseOrder(id: number): Observable<PurchaseOrderDto> {
    return this.http.post<PurchaseOrderDto>(`${this.root}/api/purchase-orders/${id}/order`, {});
  }

  receivePurchaseOrder(id: number): Observable<PurchaseOrderDto> {
    return this.http.post<PurchaseOrderDto>(`${this.root}/api/purchase-orders/${id}/receive`, {});
  }

  cancelPurchaseOrder(id: number): Observable<PurchaseOrderDto> {
    return this.http.post<PurchaseOrderDto>(`${this.root}/api/purchase-orders/${id}/cancel`, {});
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

  createSale(
    paymentMethod: PaymentMethod,
    items: { productId: number; quantity: number; discountAmount: number }[],
    idempotencyKey: string,
    options: { customerId: number | null; couponCode: string | null; loyaltyPointsToRedeem: number },
  ): Observable<SaleDto> {
    return this.http.post<SaleDto>(
      `${this.root}/api/sales`,
      { paymentMethod, items, ...options },
      { headers: { 'Idempotency-Key': idempotencyKey } },
    );
  }

  getSale(id: number): Observable<SaleDto> {
    return this.http.get<SaleDto>(`${this.root}/api/sales/${id}`);
  }

  saleReturns(saleId: number): Observable<SaleReturnDto[]> {
    return this.http.get<SaleReturnDto[]>(`${this.root}/api/sales/${saleId}/returns`);
  }

  returnSale(
    saleId: number,
    refundMethod: PaymentMethod,
    items: { saleItemId: number; quantity: number }[],
    reason?: string,
  ): Observable<SaleReturnDto> {
    return this.http.post<SaleReturnDto>(`${this.root}/api/sales/${saleId}/returns`, {
      refundMethod,
      reason,
      items,
    });
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

  auditHistory(opts: {
    from?: string;
    to?: string;
    entityName?: string;
    action?: AuditAction | '';
    page?: number;
  }): Observable<PagedResult<AuditLogDto>> {
    let params = new HttpParams().set('page', String(opts.page ?? 1)).set('pageSize', '50');
    if (opts.from) params = params.set('from', opts.from);
    if (opts.to) params = params.set('to', opts.to);
    if (opts.entityName) params = params.set('entityName', opts.entityName.trim());
    if (opts.action) params = params.set('action', opts.action);
    return this.http.get<PagedResult<AuditLogDto>>(`${this.root}/api/history/audit`, { params });
  }

  stockMovementHistory(opts: {
    productId?: number | null;
    from?: string;
    to?: string;
    page?: number;
  }): Observable<PagedResult<StockMovementDto>> {
    let params = new HttpParams().set('page', String(opts.page ?? 1)).set('pageSize', '50');
    if (opts.productId) params = params.set('productId', String(opts.productId));
    if (opts.from) params = params.set('from', opts.from);
    if (opts.to) params = params.set('to', opts.to);
    return this.http.get<PagedResult<StockMovementDto>>(`${this.root}/api/history/stock-movements`, { params });
  }

  salesReport(from?: string, to?: string): Observable<Blob> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get(`${this.root}/api/reports/sales.xlsx`, { params, responseType: 'blob' });
  }

  inventoryReport(): Observable<Blob> {
    return this.http.get(`${this.root}/api/reports/inventory.xlsx`, { responseType: 'blob' });
  }

  staff(): Observable<StaffDto[]> {
    return this.http.get<StaffDto[]>(`${this.root}/api/staff`);
  }

  createStaff(body: { username: string; email: string; password: string; role: UserRole }): Observable<StaffDto> {
    return this.http.post<StaffDto>(`${this.root}/api/staff`, body);
  }

  updateStaff(id: number, role: UserRole, isActive: boolean): Observable<StaffDto> {
    return this.http.put<StaffDto>(`${this.root}/api/staff/${id}`, { role, isActive });
  }

  resetStaffPassword(id: number, password: string): Observable<void> {
    return this.http.put<void>(`${this.root}/api/staff/${id}/password`, { password });
  }

  emailSettings(): Observable<EmailSettingsDto> {
    return this.http.get<EmailSettingsDto>(`${this.root}/api/email-settings`);
  }

  updateEmailSettings(managerEmail: string, alertsEnabled: boolean): Observable<EmailSettingsDto> {
    return this.http.put<EmailSettingsDto>(`${this.root}/api/email-settings`, { managerEmail, alertsEnabled });
  }

  testEmail(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.root}/api/email-settings/test`, {});
  }

  backgroundJobStatus(): Observable<BackgroundJobStatusDto> {
    return this.http.get<BackgroundJobStatusDto>(`${this.root}/api/operations/jobs`);
  }

  lowStockAlerts(): Observable<LowStockAlertDto[]> {
    return this.http.get<LowStockAlertDto[]>(`${this.root}/api/operations/low-stock-alerts`);
  }
}
