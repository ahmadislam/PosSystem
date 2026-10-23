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

export interface BackgroundJobStatusDto {
  failed: number;
  enqueued: number;
  scheduled: number;
}

export interface LowStockAlertDto {
  id: number;
  sku: string;
  productName: string;
  stockQuantity: number;
  minStockThreshold: number;
  detectedAt: string;
  sentAt: string | null;
  resolvedAt: string | null;
  lastAttemptAt: string | null;
  deliveryAttempts: number;
  lastError: string | null;
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
  defaultSupplierId: number | null;
  costPrice: number;
  salePrice: number;
  taxRate: number;
  stockQuantity: number;
  minStockThreshold: number;
  isActive: boolean;
  isLowStock: boolean;
  primaryImageUrl: string | null;
}

export interface CustomerDto {
  id: number;
  name: string;
  email: string | null;
  phone: string | null;
  address: string | null;
  loyaltyPoints: number;
  isActive: boolean;
}
export interface CustomerRequest {
  name: string;
  email?: string | null;
  phone?: string | null;
  address?: string | null;
  isActive: boolean;
}
export interface ProductImageDto {
  id: number;
  contentType: string;
  isPrimary: boolean;
  imageUrl: string;
}
export interface LoyaltyTransactionDto {
  id: number;
  createdAt: string;
  pointsChange: number;
  balanceAfter: number;
  type: string;
  note: string | null;
}
export interface CouponDto {
  id: number;
  code: string;
  name: string;
  isPercentage: boolean;
  value: number;
  startsAt: string;
  endsAt: string;
  isActive: boolean;
  maxRedemptions: number | null;
  redemptionCount: number;
}
export interface LoyaltyRulesDto {
  earnPointsPerCurrencyUnit: number;
  redemptionPointsPerCurrencyUnit: number;
}

export interface ProductRequest {
  sku: string;
  name: string;
  description?: string | null;
  categoryId: number;
  defaultSupplierId?: number | null;
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
export type PurchaseOrderStatus = 'Draft' | 'Ordered' | 'Received' | 'Cancelled';

export interface SupplierDto {
  id: number;
  name: string;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  notes: string | null;
  isActive: boolean;
}

export interface SupplierRequest {
  name: string;
  contactName?: string | null;
  email?: string | null;
  phone?: string | null;
  address?: string | null;
  notes?: string | null;
  isActive: boolean;
}

export interface PurchaseOrderLineRequest {
  productId: number;
  quantity: number;
  unitCost: number;
}

export interface PurchaseOrderItemDto extends PurchaseOrderLineRequest {
  id: number;
  sku: string;
  productName: string;
  lineTotal: number;
}

export interface PurchaseOrderDto {
  id: number;
  supplierId: number;
  supplierName: string;
  createdByUserId: number;
  status: PurchaseOrderStatus;
  createdAt: string;
  orderedAt: string | null;
  receivedAt: string | null;
  totalCost: number;
  items: PurchaseOrderItemDto[];
}


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
  customerId: number | null;
  customerName: string | null;
  couponCode: string | null;
  loyaltyPointsEarned: number;
  loyaltyPointsRedeemed: number;
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
  reason: string | null;
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
  grossSales: number;
  refunds: number;
  netRevenue: number;
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
  const gross = Math.round(unitPrice * quantity * 100) / 100;
  const discount = Math.round(Math.max(0, Math.min(discountAmount, gross)) * 100) / 100;
  const net = gross - discount;
  const tax = Math.round(((net * taxRatePercent) / 100 + Number.EPSILON) * 100) / 100;
  return { gross, tax, total: net + tax };
}

export function money(n: number): string {
  return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
