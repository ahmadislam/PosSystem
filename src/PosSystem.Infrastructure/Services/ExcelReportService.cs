using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Services;
using PosSystem.Infrastructure.Persistence;

namespace PosSystem.Infrastructure.Services;

public sealed class ExcelReportService(AppDbContext db) : IExcelReportService
{
    public async Task<byte[]> CreateInventoryReportAsync(CancellationToken ct = default)
    {
        var products = await (
                from product in db.Products.AsNoTracking()
                join category in db.Categories.IgnoreQueryFilters().AsNoTracking()
                    on product.CategoryId equals category.Id
                orderby category.Name, product.Name
                select new
                {
                    product.Sku,
                    product.Name,
                    Category = category.Name,
                    product.CostPrice,
                    product.SalePrice,
                    product.StockQuantity,
                    product.MinStockThreshold,
                    product.IsActive,
                })
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell(1, 1).Value = "Inventory report";
        summary.Cell(2, 1).Value = "Generated (UTC)";
        summary.Cell(2, 2).Value = DateTime.UtcNow;
        summary.Cell(3, 1).Value = "Products";
        summary.Cell(3, 2).Value = products.Count;
        summary.Cell(4, 1).Value = "Units in stock";
        summary.Cell(4, 2).Value = products.Sum(product => product.StockQuantity);
        summary.Cell(5, 1).Value = "Cost value";
        summary.Cell(5, 2).Value = products.Sum(product => product.CostPrice * product.StockQuantity);
        summary.Cell(6, 1).Value = "Retail value";
        summary.Cell(6, 2).Value = products.Sum(product => product.SalePrice * product.StockQuantity);
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(1, 1).Style.Font.FontSize = 16;
        summary.Cell(2, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        summary.Range(5, 2, 6, 2).Style.NumberFormat.Format = "#,##0.00";
        summary.Columns().AdjustToContents();

        var sheet = workbook.Worksheets.Add("Inventory");
        WriteHeader(sheet, ["SKU", "Product", "Category", "Cost price", "Sale price", "Stock", "Min stock", "Status", "Cost value"]);
        for (var index = 0; index < products.Count; index++)
        {
            var product = products[index];
            var row = index + 2;
            sheet.Cell(row, 1).Value = product.Sku;
            sheet.Cell(row, 2).Value = product.Name;
            sheet.Cell(row, 3).Value = product.Category;
            sheet.Cell(row, 4).Value = product.CostPrice;
            sheet.Cell(row, 5).Value = product.SalePrice;
            sheet.Cell(row, 6).Value = product.StockQuantity;
            sheet.Cell(row, 7).Value = product.MinStockThreshold;
            sheet.Cell(row, 8).Value = product.IsActive ? "Active" : "Inactive";
            sheet.Cell(row, 9).Value = product.CostPrice * product.StockQuantity;
        }
        FormatDataSheet(sheet, products.Count, 9, 4);
        if (products.Count > 0)
            sheet.Range(2, 6, products.Count + 1, 7).Style.NumberFormat.Format = "0";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> CreateSalesReportAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var start = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc);

        var sales = await db.Sales.AsNoTracking()
            .Where(s => s.SaleDate >= start && s.SaleDate < end)
            .OrderBy(s => s.SaleDate)
            .ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id,
                s.InvoiceNumber,
                s.SaleDate,
                s.PaymentMethod,
                s.Status,
                s.Subtotal,
                s.DiscountAmount,
                s.TaxAmount,
                s.TotalAmount,
            })
            .ToListAsync(ct);

        var returns = await db.SaleReturns.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.RefundDate >= start && r.RefundDate < end)
            .Include(r => r.Sale)
            .Include(r => r.Items)
            .OrderBy(r => r.RefundDate)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();

        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell(1, 1).Value = "Sales report";
        summary.Cell(2, 1).Value = "Period (UTC)";
        summary.Cell(2, 2).Value = $"{start:yyyy-MM-dd} to {to:yyyy-MM-dd}";
        summary.Cell(3, 1).Value = "Sales count";
        summary.Cell(3, 2).Value = sales.Count;
        summary.Cell(4, 1).Value = "Sales total";
        summary.Cell(4, 2).Value = sales.Sum(s => s.TotalAmount);
        summary.Cell(5, 1).Value = "Returns count";
        summary.Cell(5, 2).Value = returns.Count;
        summary.Cell(6, 1).Value = "Refund total";
        summary.Cell(6, 2).Value = returns.Sum(r => r.TotalAmount);
        summary.Cell(7, 1).Value = "Net total";
        summary.Cell(7, 2).Value = sales.Sum(s => s.TotalAmount) - returns.Sum(r => r.TotalAmount);
        summary.Range(1, 1, 1, 2).Merge();
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(1, 1).Style.Font.FontSize = 16;
        summary.Range(4, 2, 7, 2).Style.NumberFormat.Format = "#,##0.00";
        summary.Columns().AdjustToContents();

        var salesSheet = workbook.Worksheets.Add("Sales");
        var saleHeaders = new[] { "ID", "Invoice", "Date (UTC)", "Payment method", "Status", "Subtotal", "Discount", "Tax", "Total" };
        WriteHeader(salesSheet, saleHeaders);
        for (var index = 0; index < sales.Count; index++)
        {
            var sale = sales[index];
            var row = index + 2;
            salesSheet.Cell(row, 1).Value = sale.Id;
            salesSheet.Cell(row, 2).Value = sale.InvoiceNumber;
            salesSheet.Cell(row, 3).Value = sale.SaleDate;
            salesSheet.Cell(row, 4).Value = sale.PaymentMethod.ToString();
            salesSheet.Cell(row, 5).Value = sale.Status.ToString();
            salesSheet.Cell(row, 6).Value = sale.Subtotal;
            salesSheet.Cell(row, 7).Value = sale.DiscountAmount;
            salesSheet.Cell(row, 8).Value = sale.TaxAmount;
            salesSheet.Cell(row, 9).Value = sale.TotalAmount;
        }
        FormatDataSheet(salesSheet, sales.Count, 9);

        var returnsSheet = workbook.Worksheets.Add("Returns");
        var returnHeaders = new[] { "ID", "Invoice", "Date (UTC)", "Refund method", "Subtotal", "Discount", "Tax", "Total" };
        WriteHeader(returnsSheet, returnHeaders);
        for (var index = 0; index < returns.Count; index++)
        {
            var saleReturn = returns[index];
            var row = index + 2;
            returnsSheet.Cell(row, 1).Value = saleReturn.Id;
            returnsSheet.Cell(row, 2).Value = saleReturn.Sale.InvoiceNumber;
            returnsSheet.Cell(row, 3).Value = saleReturn.RefundDate;
            returnsSheet.Cell(row, 4).Value = saleReturn.RefundMethod.ToString();
            returnsSheet.Cell(row, 5).Value = saleReturn.Subtotal;
            returnsSheet.Cell(row, 6).Value = saleReturn.DiscountAmount;
            returnsSheet.Cell(row, 7).Value = saleReturn.TaxAmount;
            returnsSheet.Cell(row, 8).Value = saleReturn.TotalAmount;
        }
        FormatDataSheet(returnsSheet, returns.Count, 8);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteHeader(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var column = 0; column < headers.Count; column++)
            sheet.Cell(1, column + 1).Value = headers[column];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightBlue;
    }

    private static void FormatDataSheet(
        IXLWorksheet sheet, int rowCount, int amountColumn, int? firstAmountColumn = null)
    {
        if (rowCount > 0)
        {
            sheet.Range(2, firstAmountColumn ?? amountColumn - 3, rowCount + 1, amountColumn)
                .Style.NumberFormat.Format = "#,##0.00";
            sheet.Range(2, 3, rowCount + 1, 3)
                .Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        }
        sheet.SheetView.FreezeRows(1);
        sheet.RangeUsed()?.SetAutoFilter();
        sheet.Columns().AdjustToContents();
    }
}
