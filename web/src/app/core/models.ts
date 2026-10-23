export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  username: string;
  role: string;
}

export type UserRole = 'Admin' | 'Manager' | 'Cashier';

export interface StaffDto {
  id: number;
  username: string;
  email: string;
  role: UserRole;
  isActive: boolean;
  createdAt: string;
}

export interface EmailSettingsDto {
  smtpEnabled: boolean;
  managerEmail: string | null;
  alertsEnabled: boolean;
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
export type StockMovementType = 'Purchase' | 'Sale' | 'Return' | 'Adjustment';
export type AuditAction = 'Create' | 'Update' | 'Delete';

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
  saleItemId: number;
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

export interface SaleReturnItemDto {
  saleItemId: number;
  productId: number;
  sku: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
  discountAmount: number;
  taxAmount: number;
  lineTotal: number;
}

export interface SaleReturnDto {
  id: number;
  saleId: number;
  shiftId: number;
  processedByUserId: number;
  refundDate: string;
  refundMethod: PaymentMethod;
  subtotal: number;
  discountAmount: number;
  taxAmount: number;
  totalAmount: number;
  items: SaleReturnItemDto[];
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
  monthlyRevenue: MonthlyRevenueDto[];
  topSellingProducts: TopSellingProductDto[];
  salesByCategory: CategorySalesDto[];
  recentSales: SaleDto[];
  lowStockProducts: ProductDto[];
}

export interface MonthlyRevenueDto {
  month: string;
  revenue: number;
}

export interface TopSellingProductDto {
  sku: string;
  productName: string;
  quantitySold: number;
  revenue: number;
}

export interface CategorySalesDto {
  categoryId: number;
  categoryName: string;
  quantitySold: number;
  revenue: number;
}

export interface AuditLogDto {
  id: number;
  timestamp: string;
  userId: number | null;
  username: string | null;
  action: AuditAction;
  entityName: string;
  entityId: string;
  oldValues: string | null;
  newValues: string | null;
}

export interface StockMovementDto {
  id: number;
  createdAt: string;
  productId: number;
  sku: string;
  productName: string;
  createdByUserId: number;
  username: string | null;
  type: StockMovementType;
  quantityChange: number;
  referenceType: string | null;
  referenceId: number | null;
  note: string | null;
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
