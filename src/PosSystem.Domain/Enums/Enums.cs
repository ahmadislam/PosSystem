namespace PosSystem.Domain.Enums;

public enum UserRole { Admin, Manager, Cashier }
public enum PaymentMethod { Cash, Card, Online }
public enum SaleStatus { Completed, PartiallyReturned, Refunded }
public enum StockMovementType { Purchase, Sale, Return, Adjustment }
public enum ShiftStatus { Open, PendingClose, Closed }
public enum AuditAction { Create, Update, Delete }
