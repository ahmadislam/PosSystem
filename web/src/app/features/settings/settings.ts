import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PosApi } from '../../core/api/pos-api';
import { AuthService } from '../../core/auth/auth.service';
import { apiError } from '../../core/http/api-error';
import { BackgroundJobStatusDto, EmailSettingsDto, LowStockAlertDto, StaffDto, UserRole } from '../../core/models';
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
  readonly loading = signal(false);
  readonly savingEmail = signal(false);
  readonly testingEmail = signal(false);

  username = '';
  staffEmail = '';
  password = '';
  role: UserRole = 'Cashier';
  managerEmail = '';
  alertsEnabled = true;
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

  private fail(error: unknown): void {
    this.loading.set(false);
    this.toast.show(apiError(error), 'err');
  }
}
