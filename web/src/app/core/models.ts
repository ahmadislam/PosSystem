export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  username: string;
  role: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface CategoryDto {
  id: number;
  name: string;
  description: string | null;
}

export interface ProductDto {
  id: number;
  sku: string;
  name: string;
  description: string | null;
  categoryId: number;
  categoryName: string;
  costPrice: number;
  salePrice: number;
  taxRate: number;
  stockQuantity: number;
  minStockThreshold: number;
  isActive: boolean;
  isLowStock: boolean;
}

export interface ProductRequest {
  sku: string;
  name: string;
  description?: string | null;
  categoryId: number;
  costPrice: number;
  salePrice: number;
  taxRate: number;
  stockQuantity: number;
  minStockThreshold: number;
  isActive: boolean;
}

export type PaymentMethod = 'Cash' | 'Card' | 'Online';
export type SaleStatus = 'Completed' | 'PartiallyReturned' | 'Refunded';
export type ShiftStatus = 'Open' | 'PendingClose' | 'Closed';

export interface ShiftDto {
  id: number;
  cashierId: number;
  startTime: string;
  endTime: string | null;
  openingCash: number;
  expectedCash: number;
  closingCash: number | null;
  cashVariance: number | null;
  status: ShiftStatus;
}

export interface SaleItemDto {
  productId: number;
  sku: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  taxAmount: number;
  lineTotal: number;
}

export interface SaleDto {
  id: number;
  invoiceNumber: string;
  saleDate: string;
  subtotal: number;
  discountAmount: number;
  taxAmount: number;
  totalAmount: number;
  paymentMethod: PaymentMethod;
  status: SaleStatus;
  items: SaleItemDto[];
}

export interface DailySalesDto {
  date: string;
  revenue: number;
}

export interface DashboardSummaryDto {
  totalSales: number;
  revenue: number;
  lowStockCount: number;
  dailySales: DailySalesDto[];
  recentSales: SaleDto[];
  lowStockProducts: ProductDto[];
}

export interface CartLine {
  product: ProductDto;
  quantity: number;
  discountAmount: number;
}

/** Matches backend PricingCalculator (tax on gross − discount). */
export function lineTotals(unitPrice: number, quantity: number, discountAmount: number, taxRatePercent: number) {
  const gross = unitPrice * quantity;
  const net = gross - discountAmount;
  const tax = Math.round((net * taxRatePercent) / 100 * 100) / 100;
  return { gross, tax, total: net + tax };
}

export function money(n: number): string {
  return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
