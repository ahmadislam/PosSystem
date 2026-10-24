import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PosApi } from '../../core/api/pos-api';
import { AuthService } from '../../core/auth/auth.service';
import { apiError } from '../../core/http/api-error';
import { BackgroundJobStatusDto, CouponDto, EmailSettingsDto, LowStockAlertDto, LoyaltyRulesDto, StaffDto, UserRole } from '../../core/models';
import { Toast } from '../../core/ui/toast';

@Component({
  selector: 'app-settings',
  imports: [FormsModule, DatePipe],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings implements OnInit {
  private readonly api = inject(PosApi);
  private readonly toast = inject(Toast);
  readonly auth = inject(AuthService);
  readonly staff = signal<StaffDto[]>([]);
  readonly email = signal<EmailSettingsDto | null>(null);
  readonly jobStatus = signal<BackgroundJobStatusDto | null>(null);
  readonly lowStockAlerts = signal<LowStockAlertDto[]>([]);
  readonly coupons = signal<CouponDto[]>([]);
  readonly loyaltyRules = signal<LoyaltyRulesDto>({ earnPointsPerCurrencyUnit: 1, redemptionPointsPerCurrencyUnit: 100 });
  readonly loading = signal(false);
  readonly savingEmail = signal(false);
  readonly testingEmail = signal(false);

  username = '';
  staffEmail = '';
  password = '';
  role: UserRole = 'Cashier';
  managerEmail = '';
  alertsEnabled = true;
  couponCode = '';
  couponName = '';
  couponIsPercentage = false;
  couponValue = 0;
  couponStartsAt = new Date().toISOString().slice(0, 16);
  couponEndsAt = new Date(Date.now() + 30 * 86400000).toISOString().slice(0, 16);
  readonly passwordDrafts: Record<number, string> = {};

  ngOnInit(): void {
    this.loadEmailSettings();
    this.api.backgroundJobStatus().subscribe({
      next: (status) => this.jobStatus.set(status),
      error: (error) => this.fail(error),
    });
    this.api.lowStockAlerts().subscribe({
      next: (alerts) => this.lowStockAlerts.set(alerts),
      error: (error) => this.fail(error),
    });
    this.api.couponManagementList().subscribe({
      next: (coupons) => this.coupons.set(coupons),
      error: (error) => this.fail(error),
    });
    this.api.loyaltyRules().subscribe({
      next: (rules) => this.loyaltyRules.set(rules),
      error: (error) => this.fail(error),
    });
    if (this.auth.role() === 'Admin') this.loadStaff();
  }

  loadStaff(): void {
    this.loading.set(true);
    this.api.staff().subscribe({
      next: (staff) => {
        this.staff.set(staff);
        this.loading.set(false);
      },
      error: (error) => this.fail(error),
    });
  }

  createStaff(): void {
    if (this.demoLocked()) return;
    this.api.createStaff({
      username: this.username.trim(),
      email: this.staffEmail.trim(),
      password: this.password,
      role: this.role,
    }).subscribe({
      next: () => {
        this.toast.show('Staff account created.');
        this.username = '';
        this.staffEmail = '';
        this.password = '';
        this.role = 'Cashier';
        this.loadStaff();
      },
      error: (error) => this.fail(error),
    });
  }

  updateStaff(user: StaffDto, role: UserRole, isActive: boolean): void {
    if (this.demoLocked()) return;
    this.api.updateStaff(user.id, role, isActive).subscribe({
      next: (updated) => {
        this.staff.update((current) => current.map((entry) => entry.id === updated.id ? updated : entry));
        this.toast.show('Staff account updated. Their refresh session was revoked; their current access token expires shortly.');
      },
      error: (error) => {
        this.fail(error);
        this.loadStaff();
      },
    });
  }

  resetPassword(user: StaffDto): void {
    if (this.demoLocked()) return;
    const password = this.passwordDrafts[user.id] ?? '';
    if (password.length < 12) {
      this.toast.show('Use a password with at least 12 characters.', 'warn');
      return;
    }
    this.api.resetStaffPassword(user.id, password).subscribe({
      next: () => {
        this.passwordDrafts[user.id] = '';
        this.toast.show('Password reset. Their refresh session was revoked; their current access token expires shortly.');
      },
      error: (error) => this.fail(error),
    });
  }

  loadEmailSettings(): void {
    this.api.emailSettings().subscribe({
      next: (settings) => {
        this.email.set(settings);
        this.managerEmail = settings.managerEmail ?? '';
        this.alertsEnabled = settings.alertsEnabled;
      },
      error: (error) => this.fail(error),
    });
  }

  saveEmailSettings(): void {
    if (this.demoLocked()) return;
    this.savingEmail.set(true);
    this.api.updateEmailSettings(this.managerEmail.trim(), this.alertsEnabled).subscribe({
      next: (settings) => {
        this.email.set(settings);
        this.managerEmail = settings.managerEmail ?? '';
        this.savingEmail.set(false);
        this.toast.show('Email recipient and alert settings saved.');
      },
      error: (error) => {
        this.savingEmail.set(false);
        this.fail(error);
      },
    });
  }

  sendTestEmail(): void {
    if (this.demoLocked()) return;
    this.testingEmail.set(true);
    this.api.testEmail().subscribe({
      next: ({ message }) => {
        this.testingEmail.set(false);
        this.toast.show(message);
      },
      error: (error) => {
        this.testingEmail.set(false);
        this.fail(error);
      },
    });
  }

  createCoupon(): void {
    if (this.demoLocked()) return;
    const request = {
      code: this.couponCode.trim().toUpperCase(),
      name: this.couponName.trim(),
      isPercentage: this.couponIsPercentage,
      value: this.couponValue,
      startsAt: new Date(this.couponStartsAt).toISOString(),
      endsAt: new Date(this.couponEndsAt).toISOString(),
      isActive: true,
      maxRedemptions: null,
    };
    this.api.createCoupon(request).subscribe({
      next: (coupon) => {
        this.coupons.update((items) => [...items, coupon].sort((a, b) => a.code.localeCompare(b.code)));
        this.couponCode = '';
        this.couponName = '';
        this.couponValue = 0;
        this.toast.show(`Coupon ${coupon.code} created.`);
      },
      error: (error) => this.fail(error),
    });
  }

  toggleCoupon(coupon: CouponDto): void {
    if (this.demoLocked()) return;
    this.api.updateCoupon(coupon.id, {
      code: coupon.code,
      name: coupon.name,
      isPercentage: coupon.isPercentage,
      value: coupon.value,
      startsAt: coupon.startsAt,
      endsAt: coupon.endsAt,
      isActive: !coupon.isActive,
      maxRedemptions: coupon.maxRedemptions,
    }).subscribe({
      next: (updated) => {
        this.coupons.update((items) => items.map((item) => item.id === updated.id ? updated : item));
        this.toast.show(`Coupon ${updated.code} ${updated.isActive ? 'activated' : 'deactivated'}.`);
      },
      error: (error) => this.fail(error),
    });
  }

  saveLoyaltyRules(): void {
    if (this.demoLocked()) return;
    this.api.saveLoyaltyRules(this.loyaltyRules()).subscribe({
      next: (rules) => {
        this.loyaltyRules.set(rules);
        this.toast.show('Loyalty rules saved.');
      },
      error: (error) => this.fail(error),
    });
  }

  private fail(error: unknown): void {
    this.loading.set(false);
    this.toast.show(apiError(error), 'err');
  }

  private demoLocked(): boolean {
    if (!this.auth.isDemo()) return false;
    this.toast.show('This administrative action is read-only in the public portfolio demo.', 'warn');
    return true;
  }
}
