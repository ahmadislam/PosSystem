import { InvoiceService } from './invoice.service';
import { SaleDto } from '../models';

const sale: SaleDto = {
  id: 1,
  invoiceNumber: 'INV-20261023-000001',
  saleDate: '2026-10-23T00:00:00Z',
  subtotal: 20,
  discountAmount: 2,
  taxAmount: 1.35,
  totalAmount: 19.35,
  paymentMethod: 'Cash',
  status: 'Completed',
  customerId: null,
  customerName: null,
  couponCode: null,
  loyaltyPointsEarned: 0,
  loyaltyPointsRedeemed: 0,
  items: [{
    saleItemId: 1,
    productId: 1,
    sku: 'SKU-1',
    productName: 'Product',
    quantity: 2,
    unitPrice: 10,
    discountAmount: 2,
    taxAmount: 1.35,
    lineTotal: 19.35,
  }],
};

describe('InvoiceService', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('opens a generated PDF without giving the new browsing context an opener', async () => {
    const service = new InvoiceService();
    const pdfBlobs: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      pdfBlobs.push(blob as Blob);
      return 'blob:invoice';
    });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    let printAnchor: HTMLAnchorElement | undefined;
    const createElement = document.createElement.bind(document);
    vi.spyOn(document, 'createElement').mockImplementation(((tagName: string, options?: ElementCreationOptions) => {
      const element = createElement(tagName, options);
      if (tagName === 'a') printAnchor = element as HTMLAnchorElement;
      return element;
    }) as typeof document.createElement);

    service.print(sale);

    expect(pdfBlobs[0]?.type).toBe('application/pdf');
    expect(click).toHaveBeenCalledOnce();
    expect(printAnchor?.target).toBe('_blank');
    expect(printAnchor?.rel).toContain('noopener');
    const pdfBytes = new Uint8Array(await pdfBlobs[0].arrayBuffer());
    expect(new TextDecoder().decode(pdfBytes.slice(0, 5))).toBe('%PDF-');
  });

  it('downloads a generated PDF using a safe object URL and invoice filename', () => {
    const service = new InvoiceService();
    vi.useFakeTimers();
    const pdfBlobs: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      pdfBlobs.push(blob as Blob);
      return 'blob:invoice';
    });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const createElement = document.createElement.bind(document);
    let downloadAnchor: HTMLAnchorElement | undefined;
    vi.spyOn(document, 'createElement').mockImplementation(((tagName: string, options?: ElementCreationOptions) => {
      const element = createElement(tagName, options);
      if (tagName === 'a') downloadAnchor = element as HTMLAnchorElement;
      return element;
    }) as typeof document.createElement);

    service.download(sale);

    expect(pdfBlobs[0]?.type).toBe('application/pdf');
    expect(downloadAnchor?.download).toBe('INV-20261023-000001.pdf');
    expect(downloadAnchor?.rel).toContain('noopener');
    expect(click).toHaveBeenCalledOnce();
    vi.runOnlyPendingTimers();
    vi.useRealTimers();
  });
});
