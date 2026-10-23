import { Injectable } from '@angular/core';
import { SaleDto, money } from '../models';
import { jsPDF } from 'jspdf';

@Injectable({ providedIn: 'root' })
export class InvoiceService {
  print(sale: SaleDto): void {
    const url = URL.createObjectURL(this.createPdf(sale).output('blob'));
    const link = document.createElement('a');
    link.href = url;
    link.target = '_blank';
    link.rel = 'noopener noreferrer';
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  }

  download(sale: SaleDto): void {
    const url = URL.createObjectURL(this.createPdf(sale).output('blob'));
    const link = document.createElement('a');
    link.href = url;
    link.download = `${sale.invoiceNumber}.pdf`;
    link.rel = 'noopener';
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  }

  private createPdf(sale: SaleDto): jsPDF {
    const doc = new jsPDF();
    let y = 20;
    doc.setFontSize(18);
    doc.text('POS System - Invoice', 14, y);
    y += 10;
    doc.setFontSize(10);
    doc.text(`Invoice: ${sale.invoiceNumber}`, 14, y);
    y += 6;
    doc.text(`Date: ${new Date(sale.saleDate).toLocaleString()}`, 14, y);
    y += 6;
    doc.text(`Payment recorded: ${sale.paymentMethod} (not a payment gateway receipt)`, 14, y);
    y += 10;
    doc.setFontSize(9);
    doc.text('SKU', 14, y);
    doc.text('Product', 42, y);
    doc.text('Qty', 120, y, { align: 'right' });
    doc.text('Price', 145, y, { align: 'right' });
    doc.text('Total', 195, y, { align: 'right' });
    y += 2;
    doc.line(14, y, 196, y);
    y += 6;

    for (const item of sale.items) {
      if (y > 270) {
        doc.addPage();
        y = 20;
      }
      const productLines = doc.splitTextToSize(item.productName, 70) as string[];
      doc.text(item.sku, 14, y);
      doc.text(productLines, 42, y);
      doc.text(String(item.quantity), 120, y, { align: 'right' });
      doc.text(money(item.unitPrice), 145, y, { align: 'right' });
      doc.text(money(item.lineTotal), 195, y, { align: 'right' });
      y += Math.max(6, productLines.length * 4.5);
    }
    y += 4;
    doc.line(125, y, 196, y);
    y += 7;
    for (const [label, value] of [
      ['Subtotal', sale.subtotal],
      ['Discount', sale.discountAmount],
      ['Tax', sale.taxAmount],
      ['Total', sale.totalAmount],
    ] as const) {
      doc.text(label, 130, y);
      doc.text(money(value), 195, y, { align: 'right' });
      y += 6;
    }
    return doc;
  }
}
