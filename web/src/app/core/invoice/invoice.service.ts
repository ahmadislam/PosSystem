import { Injectable } from '@angular/core';
import { SaleDto, money } from '../models';

@Injectable({ providedIn: 'root' })
export class InvoiceService {
  print(sale: SaleDto): void {
    const rows = sale.items
      .map(
        (i) => `<tr>
          <td>${escapeHtml(i.sku)}</td>
          <td>${escapeHtml(i.productName)}</td>
          <td class="n">${i.quantity}</td>
          <td class="n">${money(i.unitPrice)}</td>
          <td class="n">${money(i.discountAmount)}</td>
          <td class="n">${money(i.lineTotal)}</td>
        </tr>`,
      )
      .join('');

    const html = `<!doctype html>
<html><head><meta charset="utf-8"><title>${escapeHtml(sale.invoiceNumber)}</title>
<style>
  body { font-family: "IBM Plex Sans", system-ui, sans-serif; color: #142018; margin: 32px; }
  h1 { font-size: 20px; margin: 0 0 4px; }
  .muted { color: #5b6b62; font-size: 13px; }
  table { width: 100%; border-collapse: collapse; margin-top: 20px; font-size: 13px; }
  th, td { text-align: left; padding: 8px 6px; border-bottom: 1px solid #d7e0da; }
  .n { text-align: right; font-variant-numeric: tabular-nums; }
  .tot { margin-top: 16px; width: 260px; margin-left: auto; font-size: 14px; }
  .tot div { display: flex; justify-content: space-between; padding: 3px 0; }
  .grand { font-weight: 700; font-size: 16px; border-top: 2px solid #142018; margin-top: 6px; padding-top: 8px; }
</style></head><body>
  <h1>POS System</h1>
  <div class="muted">Invoice ${escapeHtml(sale.invoiceNumber)} · ${new Date(sale.saleDate).toLocaleString()}</div>
  <div class="muted">Payment: ${escapeHtml(sale.paymentMethod)} · ${escapeHtml(sale.status)}</div>
  <table>
    <thead><tr><th>SKU</th><th>Item</th><th class="n">Qty</th><th class="n">Price</th><th class="n">Disc</th><th class="n">Total</th></tr></thead>
    <tbody>${rows}</tbody>
  </table>
  <div class="tot">
    <div><span>Subtotal</span><span>${money(sale.subtotal)}</span></div>
    <div><span>Discount</span><span>${money(sale.discountAmount)}</span></div>
    <div><span>Tax</span><span>${money(sale.taxAmount)}</span></div>
    <div class="grand"><span>Total</span><span>${money(sale.totalAmount)}</span></div>
  </div>
  <script>window.onload = () => { window.print(); }</script>
</body></html>`;

    const w = window.open('', '_blank', 'noopener,width=720,height=900');
    if (!w) return;
    w.document.open();
    w.document.write(html);
    w.document.close();
  }
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);
}
