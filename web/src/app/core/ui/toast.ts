import { Injectable, signal } from '@angular/core';

export type ToastKind = 'ok' | 'err' | 'warn';

@Injectable({ providedIn: 'root' })
export class Toast {
  readonly message = signal<string | null>(null);
  readonly kind = signal<ToastKind>('ok');
  private timer: ReturnType<typeof setTimeout> | null = null;

  show(message: string, kind: ToastKind = 'ok'): void {
    this.message.set(message);
    this.kind.set(kind);
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.message.set(null), 4200);
  }

  clear(): void {
    this.message.set(null);
  }
}
